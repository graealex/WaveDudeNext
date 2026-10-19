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
using System.Text;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes a QuickTime movie (<c>.mov</c>) with the Animation codec (<c>rle </c>, 24-bit RGB, run-length
/// coded, mathematically lossless, every frame a key frame), the lossless RGB format Final Cut, Premiere,
/// After Effects and Resolve all decode without anything installed (2026-10-15, the user's choice: one
/// lossless single-file format per container, this one for the editors, the uncompressed AVI for the
/// Windows tools). In-house, no dependencies. The <c>moov</c> with the sample tables comes first: its size
/// follows from the planned frame count (a fixed part plus 12 bytes a frame), so that much is reserved
/// after <c>ftyp</c> as a <c>free</c> atom, the samples go into one <c>mdat</c> behind it as they come, and at
/// close the reservation is overwritten with the real <c>moov</c> and a <c>free</c> atom for the rest (the
/// user's rule, 2026-10-15: a player streaming the file should not read it all before the first frame). More
/// frames than planned, or none planned, put the <c>moov</c> at the end. The <c>mdat</c> size and the chunk
/// offsets are 64-bit from the start (<c>co64</c>), so the file may grow past 4 GB. One thread at a time.
/// </summary>
/// <remarks>
/// A frame: a 4-byte big-endian length of the whole frame data, a 2-byte header of 0 (a full frame), then
/// per row a skip code (1 = no skip), run codes (a positive count n: n literal RGB pixels follow; a negative
/// count −n: one RGB pixel follows, repeated n times; 0: another skip code follows) and −1 at the row's end.
/// That is what every <c>rle </c> decoder expects (ffmpeg's <c>qtrle</c>, QuickTime's own).
/// </remarks>
public sealed class MovAnimationWriter : IFrameSink
{
	/// <summary>The moov's bytes besides the two per-frame tables (measured on the atoms as written, with slack).</summary>
	private const int MoovFixedBytes = 1024;

	private readonly FileStream _stream;
	private readonly long _reservedStart;
	private readonly long _reservedBytes;
	private readonly long _mdatStart;
	private readonly List<long> _offsets = [];
	private readonly List<int> _sizes = [];
	private readonly byte[] _row;
	private byte[] _frame;
	private bool _closed;

	/// <param name="path">Output file; an existing file is replaced.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="framesPerSecond">Playback rate; the movie's time scale.</param>
	/// <param name="plannedFrames">How many frames will come at most, which sizes the reservation for the leading <c>moov</c>; 0 puts it at the end.</param>
	public MovAnimationWriter(string path, int width, int height, int framesPerSecond, int plannedFrames = 0)
	{
		Width = width;
		Height = height;
		FramesPerSecond = Math.Max(1, framesPerSecond);
		Output = path;
		PlannedFrames = Math.Max(0, plannedFrames);
		_row = new byte[width * 3];
		// the worst case of a row: every pixel a literal in runs of 127, plus the codes, plus the skip and the end
		_frame = new byte[6 + height * (width * 3 + width / 127 + 4)];
		_stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		// ftyp, then the mdat with a 64-bit size patched at Close
		_stream.Write(Atom("ftyp", [.. "qt  "u8, 0, 0, 2, 0, .. "qt  "u8]));
		_reservedStart = _stream.Position;
		if (PlannedFrames > 0)
		{
			// room for the moov of the planned frames, plus a free atom's head behind it, as one free atom for now
			_reservedBytes = MoovFixedBytes + 12L * PlannedFrames + 8;
			_stream.Write(FreeAtomHead(_reservedBytes));
			_stream.Position = _reservedStart + _reservedBytes;
		}
		_mdatStart = _stream.Position;
		Span<byte> mdat = stackalloc byte[16];
		BinaryPrimitives.WriteUInt32BigEndian(mdat, 1); // size 1: a 64-bit size follows the type
		"mdat"u8.CopyTo(mdat[4..]);
		BinaryPrimitives.WriteUInt64BigEndian(mdat[8..], 0);
		_stream.Write(mdat);
	}

	/// <inheritdoc/>
	public int Width { get; }

	/// <inheritdoc/>
	public int Height { get; }

	/// <inheritdoc/>
	public int FrameCount => _sizes.Count;

	/// <inheritdoc/>
	public string Output { get; }

	/// <summary>Frames per second, the movie's time scale.</summary>
	public int FramesPerSecond { get; }

	/// <summary>The frame count the leading moov was reserved for; 0 for a trailing moov.</summary>
	public int PlannedFrames { get; }

	/// <summary>Whether the moov was written at the front (the reservation held it).</summary>
	public bool MoovFirst { get; private set; }

	/// <summary>Bytes of frame data written so far (for the bench's ratio against width × height × 3 per frame).</summary>
	public long FrameBytes { get; private set; }

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<int> pixels)
	{
		if (_closed)
		{
			throw new InvalidOperationException("The movie file is already closed.");
		}
		if (pixels.Length < Width * Height)
		{
			throw new ArgumentException("The frame is smaller than the movie.", nameof(pixels));
		}
		int length = Encode(pixels, _frame);
		_offsets.Add(_stream.Position);
		_sizes.Add(length);
		_stream.Write(_frame, 0, length);
		FrameBytes += length;
	}

	/// <summary>Run-length codes one frame into <paramref name="target"/>; returns the byte count.</summary>
	public int Encode(ReadOnlySpan<int> pixels, byte[] target)
	{
		int p = 6; // the length and the header are filled in at the end
		int width = Width;
		for (int y = 0; y < Height; y++)
		{
			ReadOnlySpan<int> row = pixels.Slice(y * width, width);
			target[p++] = 1; // skip nothing
			int x = 0;
			while (x < width)
			{
				int pixel = row[x];
				// a run of identical pixels: a repeat code, up to 128
				int run = 1;
				while (x + run < width && run < 128 && row[x + run] == pixel)
				{
					run++;
				}
				if (run >= 2)
				{
					target[p++] = (byte)(-run);
					target[p++] = (byte)(pixel >> 16);
					target[p++] = (byte)(pixel >> 8);
					target[p++] = (byte)pixel;
					x += run;
					continue;
				}
				// literals until the next run of at least two, up to 127
				int start = x;
				int count = 0;
				while (x < width && count < 127)
				{
					if (x + 1 < width && row[x + 1] == row[x])
					{
						break;
					}
					x++;
					count++;
				}
				if (count == 0)
				{
					// x stands on the first of a run; taken by the next round
					continue;
				}
				target[p++] = (byte)count;
				for (int i = 0; i < count; i++)
				{
					int v = row[start + i];
					target[p++] = (byte)(v >> 16);
					target[p++] = (byte)(v >> 8);
					target[p++] = (byte)v;
				}
			}
			target[p++] = 0xFF; // −1: the row's end
		}
		BinaryPrimitives.WriteInt32BigEndian(target, p);
		BinaryPrimitives.WriteUInt16BigEndian(target.AsSpan(4), 0); // a full frame
		return p;
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
			long mdatEnd = _stream.Position;
			byte[] moov = BuildMoov();
			if (_reservedBytes > 0 && moov.Length + 8 <= _reservedBytes)
			{
				// the real moov over the reservation, a free atom over what is left of it
				_stream.Position = _reservedStart;
				_stream.Write(moov);
				_stream.Write(FreeAtomHead(_reservedBytes - moov.Length));
				MoovFirst = true;
			}
			else
			{
				_stream.Position = mdatEnd;
				_stream.Write(moov);
			}
			_stream.Position = _mdatStart + 8;
			Span<byte> size = stackalloc byte[8];
			BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)(mdatEnd - _mdatStart));
			_stream.Write(size);
			_stream.Flush();
		}
		finally
		{
			_stream.Dispose();
		}
	}

	/// <inheritdoc/>
	public void Dispose() => Close();

	// ---------------------------------------------------------------- the movie atoms

	private byte[] BuildMoov()
	{
		int frames = _sizes.Count;
		uint timescale = (uint)FramesPerSecond;
		uint duration = (uint)frames;
		return Atom("moov", [.. Mvhd(timescale, duration), .. Atom("trak", [.. Tkhd(duration), .. Atom("mdia", [.. Mdhd(timescale, duration), .. Hdlr("mhlr", "vide", "VideoHandler"), .. Atom("minf",
			[.. Atom("vmhd", [0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]), .. Hdlr("dhlr", "alis", "DataHandler"), .. Atom("dinf", Atom("dref", [0, 0, 0, 0, 0, 0, 0, 1, .. Atom("alis", [0, 0, 0, 1])])),
			.. Atom("stbl", [.. Stsd(), .. Stts(frames), .. Atom("stsc", [0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1]), .. Stsz(), .. Co64()])])])])]);
	}

	private static byte[] Mvhd(uint timescale, uint duration)
	{
		var b = new byte[100];
		// version 0, flags 0, creation and modification time 0 (Mac epoch), timescale, duration
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), timescale);
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), duration);
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), 0x00010000); // preferred rate 1.0
		BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(24), 0x0100); // preferred volume 1.0
		Matrix(b.AsSpan(36));
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(96), 2); // next track id
		return Atom("mvhd", b);
	}

	private byte[] Tkhd(uint duration)
	{
		var b = new byte[84];
		b[3] = 0x0F; // enabled, in movie, in preview, in poster
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), 1); // track id
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), duration);
		Matrix(b.AsSpan(40));
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(76), (uint)Width << 16);
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(80), (uint)Height << 16);
		return Atom("tkhd", b);
	}

	private static byte[] Mdhd(uint timescale, uint duration)
	{
		var b = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), timescale);
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), duration);
		BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20), 0x55C4); // language: undetermined
		return Atom("mdhd", b);
	}

	/// <summary>A handler reference the QuickTime way: component type and subtype, manufacturer 'appl', the name as a counted string.</summary>
	private static byte[] Hdlr(string type, string subtype, string name)
	{
		byte[] nameBytes = Encoding.ASCII.GetBytes(name);
		return Atom("hdlr", [0, 0, 0, 0, .. Encoding.ASCII.GetBytes(type), .. Encoding.ASCII.GetBytes(subtype), .. "appl"u8, 0, 0, 0, 0, 0, 0, 0, 0, (byte)nameBytes.Length, .. nameBytes]);
	}

	/// <summary>The sample description: one 'rle ' entry, 24-bit, spatial quality lossless, named "Animation".</summary>
	private byte[] Stsd()
	{
		var e = new byte[86];
		BinaryPrimitives.WriteUInt32BigEndian(e, 86);
		"rle "u8.CopyTo(e.AsSpan(4));
		BinaryPrimitives.WriteUInt16BigEndian(e.AsSpan(14), 1); // data reference index
		"appl"u8.CopyTo(e.AsSpan(20)); // vendor
		BinaryPrimitives.WriteUInt32BigEndian(e.AsSpan(28), 0x3FF); // spatial quality: lossless
		BinaryPrimitives.WriteUInt16BigEndian(e.AsSpan(32), (ushort)Width);
		BinaryPrimitives.WriteUInt16BigEndian(e.AsSpan(34), (ushort)Height);
		BinaryPrimitives.WriteUInt32BigEndian(e.AsSpan(36), 0x00480000); // 72 dpi
		BinaryPrimitives.WriteUInt32BigEndian(e.AsSpan(40), 0x00480000);
		BinaryPrimitives.WriteUInt16BigEndian(e.AsSpan(48), 1); // frames per sample
		byte[] name = Encoding.ASCII.GetBytes("Animation");
		e[50] = (byte)name.Length;
		name.CopyTo(e, 51);
		BinaryPrimitives.WriteUInt16BigEndian(e.AsSpan(82), 24); // depth
		BinaryPrimitives.WriteUInt16BigEndian(e.AsSpan(84), 0xFFFF); // no colour table
		return Atom("stsd", [0, 0, 0, 0, 0, 0, 0, 1, .. e]);
	}

	private static byte[] Stts(int frames)
	{
		var b = new byte[16];
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), 1);
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), (uint)frames);
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), 1); // every sample lasts one tick
		return Atom("stts", b);
	}

	private byte[] Stsz()
	{
		var b = new byte[12 + 4 * _sizes.Count];
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), (uint)_sizes.Count);
		for (int i = 0; i < _sizes.Count; i++)
		{
			BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12 + 4 * i), (uint)_sizes[i]);
		}
		return Atom("stsz", b);
	}

	private byte[] Co64()
	{
		var b = new byte[8 + 8 * _offsets.Count];
		BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), (uint)_offsets.Count);
		for (int i = 0; i < _offsets.Count; i++)
		{
			BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(8 + 8 * i), (ulong)_offsets[i]);
		}
		return Atom("co64", b);
	}

	/// <summary>The head of a <c>free</c> atom of <paramref name="bytes"/> bytes in all (the body is whatever lies there).</summary>
	private static byte[] FreeAtomHead(long bytes)
	{
		var head = new byte[8];
		BinaryPrimitives.WriteUInt32BigEndian(head, (uint)bytes);
		"free"u8.CopyTo(head.AsSpan(4));
		return head;
	}

	/// <summary>The identity transformation matrix (16.16 and 2.30 fixed point).</summary>
	private static void Matrix(Span<byte> b)
	{
		BinaryPrimitives.WriteUInt32BigEndian(b, 0x00010000);
		BinaryPrimitives.WriteUInt32BigEndian(b[16..], 0x00010000);
		BinaryPrimitives.WriteUInt32BigEndian(b[32..], 0x40000000);
	}

	private static byte[] Atom(string type, ReadOnlySpan<byte> payload)
	{
		var atom = new byte[8 + payload.Length];
		BinaryPrimitives.WriteUInt32BigEndian(atom, (uint)atom.Length);
		Encoding.ASCII.GetBytes(type).CopyTo(atom, 4);
		payload.CopyTo(atom.AsSpan(8));
		return atom;
	}
}
