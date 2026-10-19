// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Buffers.Binary;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes an uncompressed AVI (24-bit BGR, bottom-up rows padded to four bytes, one <c>00db</c> chunk per
/// frame) with the OpenDML extensions, so the file may be as large as the disk allows: the frames are split
/// over RIFF segments of at most <see cref="SegmentLimit"/> bytes (the first <c>AVI </c>, the rest
/// <c>AVIX</c>), each segment ends with a standard index (<c>ix00</c>) and the header carries the super
/// index (<c>indx</c>) pointing at them, plus the legacy <c>idx1</c> for the first segment and the total
/// frame count in <c>dmlh</c>. Every frame has the same size, so the layout is arithmetic; the header is
/// written at <see cref="Close"/> with the counts that were reached, so a cancelled export is still a
/// valid file. In-house, no dependencies. One thread at a time.
/// </summary>
/// <remarks>
/// Layout: <c>RIFF 'AVI ' [LIST hdrl: avih, LIST strl: strh strf indx] [LIST odml: dmlh] [LIST movi: 00db… ix00] idx1</c>,
/// then per further segment <c>RIFF 'AVIX' [LIST movi: 00db… ix00]</c>. The first segment's RIFF head and
/// the hdrl/odml lists occupy the first <see cref="_headerBytes"/> bytes, reserved at open and filled at close.
/// </remarks>
public sealed class AviWriter : IFrameSink
{
	/// <summary>Largest RIFF segment: 1 GB, the size every player handles for the first segment.</summary>
	public const long DefaultSegmentLimit = 1L << 30;

	private const uint AvifHasIndex = 0x10;
	private const uint AvifTrustChunkType = 0x800;
	private const uint AviifKeyframe = 0x10;
	private const int DmlhSize = 248;

	private readonly FileStream _stream;
	private readonly int _rowBytes;
	private readonly int _frameBytes;
	private readonly int _chunkBytes;
	private readonly int _headerBytes;
	private readonly int _firstSegmentFrames;
	private readonly int _laterSegmentFrames;
	private readonly int _superEntries;
	private readonly byte[] _row;
	private readonly List<Segment> _segments = [];
	private bool _closed;

	/// <summary>One RIFF segment: where its RIFF head and its movi LIST head are, how many frames it holds, and its index once closed.</summary>
	private sealed class Segment(long riffStart, long moviStart)
	{
		public long RiffStart { get; } = riffStart;
		public long MoviStart { get; } = moviStart;
		public int Frames { get; set; }
		public long End { get; set; }
		public long IndexPosition { get; set; }
	}

	/// <param name="path">Output file; an existing file is replaced.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="framesPerSecond">Playback rate.</param>
	/// <param name="plannedFrames">How many frames will come at most (sizes the super index); more are refused.</param>
	/// <param name="segmentLimit">Largest RIFF segment in bytes; the default is 1 GB (a harness uses small values to force several segments).</param>
	public AviWriter(string path, int width, int height, int framesPerSecond, int plannedFrames, long segmentLimit = DefaultSegmentLimit)
	{
		Width = width;
		Height = height;
		FramesPerSecond = Math.Max(1, framesPerSecond);
		Output = path;
		SegmentLimit = segmentLimit;
		PlannedFrames = Math.Max(1, plannedFrames);
		_rowBytes = (width * 3 + 3) & ~3;
		_frameBytes = _rowBytes * height;
		_chunkBytes = 8 + _frameBytes; // the row padding keeps the chunk even
		_row = new byte[_rowBytes];
		// a later segment: RIFF head, LIST movi head, the frames, ix00 (32 + 8 per frame)
		_laterSegmentFrames = (int)Math.Max(1, (segmentLimit - 12 - 12 - 32) / (_chunkBytes + 8));
		// the first segment also holds the header (which grows with the number of segments) and idx1 (16 per frame)
		int entries = 1;
		for (int pass = 0; pass < 3; pass++)
		{
			_headerBytes = HeaderBytes(entries);
			_firstSegmentFrames = (int)Math.Max(1, (segmentLimit - _headerBytes - 12 - 32 - 8) / (_chunkBytes + 8 + 16));
			int rest = Math.Max(0, PlannedFrames - _firstSegmentFrames);
			int needed = 1 + (rest + _laterSegmentFrames - 1) / _laterSegmentFrames;
			if (needed <= entries)
			{
				break;
			}
			entries = needed;
		}
		_superEntries = entries;
		_stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_stream.SetLength(_headerBytes);
		_stream.Position = _headerBytes;
		BeginSegment();
	}

	/// <inheritdoc/>
	public int Width { get; }

	/// <inheritdoc/>
	public int Height { get; }

	/// <inheritdoc/>
	public int FrameCount { get; private set; }

	/// <inheritdoc/>
	public string Output { get; }

	/// <summary>Frames per second written to the headers.</summary>
	public int FramesPerSecond { get; }

	/// <summary>The frame count the writer was sized for.</summary>
	public int PlannedFrames { get; }

	/// <summary>Largest RIFF segment in bytes.</summary>
	public long SegmentLimit { get; }

	/// <summary>Bytes of one frame chunk's data (padded rows × height).</summary>
	public int FrameBytes => _frameBytes;

	/// <summary>Segments begun so far (at least one).</summary>
	public int SegmentCount => _segments.Count;

	/// <summary>The size of the file a given frame count produces, header, indexes and padding included.</summary>
	public long SizeOf(int frames)
	{
		int inFirst = Math.Min(frames, _firstSegmentFrames);
		long size = _headerBytes + 12 + (long)inFirst * _chunkBytes + 32 + 8L * inFirst + 8 + 16L * inFirst;
		int remaining = frames - inFirst;
		while (remaining > 0)
		{
			int inSegment = Math.Min(remaining, _laterSegmentFrames);
			size += 12 + 12 + (long)inSegment * _chunkBytes + 32 + 8L * inSegment;
			remaining -= inSegment;
		}
		return size;
	}

	/// <summary>The size of the file for a frame count, without opening a file.</summary>
	public static long EstimateSize(int width, int height, int frames, long segmentLimit = DefaultSegmentLimit)
	{
		int rowBytes = (width * 3 + 3) & ~3;
		long chunk = 8L + rowBytes * height;
		return frames * (chunk + 8 + 16) + 2048 + (frames * chunk / segmentLimit + 1) * 64;
	}

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<int> pixels)
	{
		if (_closed)
		{
			throw new InvalidOperationException("The video file is already closed.");
		}
		if (FrameCount >= PlannedFrames)
		{
			throw new InvalidOperationException("More frames than the writer was sized for.");
		}
		if (pixels.Length < Width * Height)
		{
			throw new ArgumentException("The frame is smaller than the video.", nameof(pixels));
		}
		Segment segment = _segments[^1];
		int limit = _segments.Count == 1 ? _firstSegmentFrames : _laterSegmentFrames;
		if (segment.Frames >= limit)
		{
			EndSegment();
			segment = BeginSegment();
		}
		Span<byte> head = stackalloc byte[8];
		"00db"u8.CopyTo(head);
		BinaryPrimitives.WriteInt32LittleEndian(head[4..], _frameBytes);
		_stream.Write(head);
		// bottom-up BGR rows
		for (int y = Height - 1; y >= 0; y--)
		{
			ReadOnlySpan<int> row = pixels.Slice(y * Width, Width);
			for (int x = 0; x < Width; x++)
			{
				int argb = row[x];
				_row[x * 3] = (byte)argb;
				_row[x * 3 + 1] = (byte)(argb >> 8);
				_row[x * 3 + 2] = (byte)(argb >> 16);
			}
			_stream.Write(_row);
		}
		segment.Frames++;
		FrameCount++;
	}

	/// <inheritdoc/>
	public void Close()
	{
		if (_closed)
		{
			return;
		}
		_closed = true;
		try
		{
			EndSegment();
			_stream.Position = 0;
			_stream.Write(BuildHeader());
			_stream.Flush();
		}
		finally
		{
			_stream.Dispose();
		}
	}

	/// <inheritdoc/>
	public void Dispose() => Close();

	// ---------------------------------------------------------------- segments

	/// <summary>Opens a segment at the current position: for the first only its 'LIST movi' head (its RIFF head is part of the header), for later ones 'RIFF … AVIX' and 'LIST … movi'; the sizes are patched by <see cref="EndSegment"/>.</summary>
	private Segment BeginSegment()
	{
		Span<byte> head = stackalloc byte[24];
		Segment segment;
		if (_segments.Count == 0)
		{
			segment = new Segment(0, _stream.Position);
			"LIST"u8.CopyTo(head);
			"movi"u8.CopyTo(head[8..]);
			_stream.Write(head[..12]);
		}
		else
		{
			segment = new Segment(_stream.Position, _stream.Position + 12);
			"RIFF"u8.CopyTo(head);
			"AVIX"u8.CopyTo(head[8..]);
			"LIST"u8.CopyTo(head[12..]);
			"movi"u8.CopyTo(head[20..]);
			_stream.Write(head);
		}
		_segments.Add(segment);
		return segment;
	}

	/// <summary>Closes the current segment: its ix00 inside movi, the idx1 after the first segment's movi, and the patched sizes.</summary>
	private void EndSegment()
	{
		Segment segment = _segments[^1];
		int frames = segment.Frames;
		long moviStart = segment.MoviStart;
		long dataStart = moviStart + 12; // the first frame chunk's header
		// ix00: the standard index of this segment's chunks, offsets from the movi LIST head to each chunk's data
		var index = new byte[32 + 8 * frames];
		"ix00"u8.CopyTo(index);
		BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(4), 24 + 8 * frames);
		BinaryPrimitives.WriteUInt16LittleEndian(index.AsSpan(8), 2); // wLongsPerEntry
		index[10] = 0; // bIndexSubType
		index[11] = 1; // bIndexType: AVI_INDEX_OF_CHUNKS
		BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(12), frames); // nEntriesInUse
		"00db"u8.CopyTo(index.AsSpan(16)); // dwChunkId
		BinaryPrimitives.WriteInt64LittleEndian(index.AsSpan(20), moviStart); // qwBaseOffset
		for (int i = 0; i < frames; i++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(32 + 8 * i), (int)(dataStart + (long)i * _chunkBytes + 8 - moviStart));
			BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(36 + 8 * i), _frameBytes); // bit 31 clear: a key frame
		}
		segment.IndexPosition = _stream.Position;
		_stream.Write(index);
		long moviEnd = _stream.Position;
		if (_segments.Count == 1)
		{
			// idx1: the legacy index, chunk offsets relative to the 'movi' fourcc
			var legacy = new byte[8 + 16 * frames];
			"idx1"u8.CopyTo(legacy);
			BinaryPrimitives.WriteInt32LittleEndian(legacy.AsSpan(4), 16 * frames);
			for (int i = 0; i < frames; i++)
			{
				"00db"u8.CopyTo(legacy.AsSpan(8 + 16 * i));
				BinaryPrimitives.WriteUInt32LittleEndian(legacy.AsSpan(12 + 16 * i), AviifKeyframe);
				BinaryPrimitives.WriteInt32LittleEndian(legacy.AsSpan(16 + 16 * i), (int)(dataStart + (long)i * _chunkBytes - (moviStart + 8)));
				BinaryPrimitives.WriteInt32LittleEndian(legacy.AsSpan(20 + 16 * i), _frameBytes);
			}
			_stream.Write(legacy);
		}
		segment.End = _stream.Position;
		if (_segments.Count > 1)
		{
			Patch(segment.RiffStart + 4, (uint)(segment.End - segment.RiffStart - 8));
		}
		Patch(moviStart + 4, (uint)(moviEnd - moviStart - 8));
		_stream.Position = segment.End;
	}

	private void Patch(long position, uint value)
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
		_stream.Position = position;
		_stream.Write(bytes);
	}

	// ---------------------------------------------------------------- header

	/// <summary>Bytes before the first segment's movi LIST: the RIFF head, 'LIST hdrl' and 'LIST odml'.</summary>
	private static int HeaderBytes(int superEntries) => 12 + HdrlChunkBytes(superEntries) + (12 + 8 + DmlhSize);

	/// <summary>Bytes of the whole 'LIST hdrl' chunk with a super index of <paramref name="superEntries"/> entries.</summary>
	private static int HdrlChunkBytes(int superEntries) => 12 + (8 + 56) + StrlChunkBytes(superEntries);

	private static int StrlChunkBytes(int superEntries) => 12 + (8 + 56) + (8 + 40) + (8 + 24 + 16 * superEntries);

	/// <summary>The first <see cref="_headerBytes"/> bytes of the file, with the counts reached.</summary>
	private byte[] BuildHeader()
	{
		Segment first = _segments[0];
		var header = new byte[_headerBytes];
		Span<byte> h = header;
		"RIFF"u8.CopyTo(h);
		BinaryPrimitives.WriteUInt32LittleEndian(h[4..], (uint)(first.End - 8));
		"AVI "u8.CopyTo(h[8..]);
		int p = 12;
		// LIST hdrl
		"LIST"u8.CopyTo(h[p..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 4)..], HdrlChunkBytes(_superEntries) - 8);
		"hdrl"u8.CopyTo(h[(p + 8)..]);
		p += 12;
		// avih
		"avih"u8.CopyTo(h[p..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 4)..], 56);
		Span<byte> avih = h.Slice(p + 8, 56);
		BinaryPrimitives.WriteUInt32LittleEndian(avih, (uint)(1000000 / FramesPerSecond)); // dwMicroSecPerFrame
		BinaryPrimitives.WriteUInt32LittleEndian(avih[4..], (uint)Math.Min(uint.MaxValue, (long)_chunkBytes * FramesPerSecond)); // dwMaxBytesPerSec
		BinaryPrimitives.WriteUInt32LittleEndian(avih[8..], 0); // dwPaddingGranularity
		BinaryPrimitives.WriteUInt32LittleEndian(avih[12..], AvifHasIndex | AvifTrustChunkType);
		BinaryPrimitives.WriteUInt32LittleEndian(avih[16..], (uint)first.Frames); // dwTotalFrames: the first segment's, per OpenDML
		BinaryPrimitives.WriteUInt32LittleEndian(avih[20..], 0); // dwInitialFrames
		BinaryPrimitives.WriteUInt32LittleEndian(avih[24..], 1); // dwStreams
		BinaryPrimitives.WriteUInt32LittleEndian(avih[28..], (uint)_frameBytes); // dwSuggestedBufferSize
		BinaryPrimitives.WriteUInt32LittleEndian(avih[32..], (uint)Width);
		BinaryPrimitives.WriteUInt32LittleEndian(avih[36..], (uint)Height);
		p += 8 + 56;
		// LIST strl
		"LIST"u8.CopyTo(h[p..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 4)..], StrlChunkBytes(_superEntries) - 8);
		"strl"u8.CopyTo(h[(p + 8)..]);
		p += 12;
		// strh
		"strh"u8.CopyTo(h[p..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 4)..], 56);
		Span<byte> strh = h.Slice(p + 8, 56);
		"vids"u8.CopyTo(strh);
		"DIB "u8.CopyTo(strh[4..]);
		BinaryPrimitives.WriteUInt32LittleEndian(strh[8..], 0); // dwFlags
		BinaryPrimitives.WriteUInt16LittleEndian(strh[12..], 0); // wPriority
		BinaryPrimitives.WriteUInt16LittleEndian(strh[14..], 0); // wLanguage
		BinaryPrimitives.WriteUInt32LittleEndian(strh[16..], 0); // dwInitialFrames
		BinaryPrimitives.WriteUInt32LittleEndian(strh[20..], 1); // dwScale
		BinaryPrimitives.WriteUInt32LittleEndian(strh[24..], (uint)FramesPerSecond); // dwRate
		BinaryPrimitives.WriteUInt32LittleEndian(strh[28..], 0); // dwStart
		BinaryPrimitives.WriteUInt32LittleEndian(strh[32..], (uint)FrameCount); // dwLength: all segments
		BinaryPrimitives.WriteUInt32LittleEndian(strh[36..], (uint)_frameBytes); // dwSuggestedBufferSize
		BinaryPrimitives.WriteUInt32LittleEndian(strh[40..], 0xFFFFFFFF); // dwQuality: default
		BinaryPrimitives.WriteUInt32LittleEndian(strh[44..], 0); // dwSampleSize
		BinaryPrimitives.WriteUInt16LittleEndian(strh[48..], 0); // rcFrame
		BinaryPrimitives.WriteUInt16LittleEndian(strh[50..], 0);
		BinaryPrimitives.WriteUInt16LittleEndian(strh[52..], (ushort)Width);
		BinaryPrimitives.WriteUInt16LittleEndian(strh[54..], (ushort)Height);
		p += 8 + 56;
		// strf: BITMAPINFOHEADER
		"strf"u8.CopyTo(h[p..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 4)..], 40);
		Span<byte> strf = h.Slice(p + 8, 40);
		BinaryPrimitives.WriteInt32LittleEndian(strf, 40);
		BinaryPrimitives.WriteInt32LittleEndian(strf[4..], Width);
		BinaryPrimitives.WriteInt32LittleEndian(strf[8..], Height); // positive: bottom-up
		BinaryPrimitives.WriteUInt16LittleEndian(strf[12..], 1); // biPlanes
		BinaryPrimitives.WriteUInt16LittleEndian(strf[14..], 24); // biBitCount
		BinaryPrimitives.WriteUInt32LittleEndian(strf[16..], 0); // BI_RGB
		BinaryPrimitives.WriteUInt32LittleEndian(strf[20..], (uint)_frameBytes); // biSizeImage
		p += 8 + 40;
		// indx: the super index, one entry per segment's ix00
		"indx"u8.CopyTo(h[p..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 4)..], 24 + 16 * _superEntries);
		Span<byte> indx = h.Slice(p + 8, 24 + 16 * _superEntries);
		BinaryPrimitives.WriteUInt16LittleEndian(indx, 4); // wLongsPerEntry
		indx[2] = 0; // bIndexSubType
		indx[3] = 0; // bIndexType: AVI_INDEX_OF_INDEXES
		BinaryPrimitives.WriteInt32LittleEndian(indx[4..], Math.Min(_segments.Count, _superEntries)); // nEntriesInUse
		"00db"u8.CopyTo(indx[8..]);
		for (int i = 0; i < _segments.Count && i < _superEntries; i++)
		{
			Segment segment = _segments[i];
			BinaryPrimitives.WriteInt64LittleEndian(indx[(24 + 16 * i)..], segment.IndexPosition); // qwOffset: the ix00 chunk
			BinaryPrimitives.WriteInt32LittleEndian(indx[(32 + 16 * i)..], 32 + 8 * segment.Frames); // dwSize: the chunk with its head
			BinaryPrimitives.WriteInt32LittleEndian(indx[(36 + 16 * i)..], segment.Frames); // dwDuration
		}
		p += 8 + 24 + 16 * _superEntries;
		// LIST odml with dmlh: the total frame count
		"LIST"u8.CopyTo(h[p..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 4)..], 4 + 8 + DmlhSize);
		"odml"u8.CopyTo(h[(p + 8)..]);
		"dmlh"u8.CopyTo(h[(p + 12)..]);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 16)..], DmlhSize);
		BinaryPrimitives.WriteInt32LittleEndian(h[(p + 20)..], FrameCount);
		return header;
	}
}
