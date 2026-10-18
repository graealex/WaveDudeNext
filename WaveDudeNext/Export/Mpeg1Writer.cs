// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Runtime.Intrinsics;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes an MPEG-1 video of intra-coded pictures only (ISO/IEC 11172-2, every picture an I-picture of one
/// slice per macroblock row, the default quantisation matrix, no custom tables) inside an MPEG-1 program
/// stream (11172-1: one pack per picture, PES packets of at most 64 KB, a PTS on each picture), the
/// <c>.mpg</c> file every player and editor reads without a codec. In-house, no dependencies. The quality
/// 1..100 becomes the quantiser scale 31..1; only the standard frame rates exist in MPEG-1, so the nearest
/// is taken (<see cref="FrameRate"/>). 4:2:0, studio range. Slices are independent bit streams (their DC
/// predictors reset), so the rows are transformed, quantised and coded in parallel and concatenated
/// (2026-10-14); the DCT and quantisation run on <see cref="Vector128{T}"/> where the hardware has it.
/// One thread at a time.
/// </summary>
public sealed class Mpeg1Writer : IFrameSink
{
	/// <summary>The frame rates MPEG-1 can express, by frame_rate_code 1..8.</summary>
	public static readonly double[] FrameRates = [24000.0 / 1001, 24, 25, 30000.0 / 1001, 30, 50, 60000.0 / 1001, 60];

	/// <summary>Pictures per group of pictures (each starts with a GOP header carrying the time code).</summary>
	private const int GopLength = 12;

	/// <summary>Largest PES payload: the packet length field is 16 bits and the header needs a few bytes.</summary>
	private const int PesPayloadLimit = 65000;

	private readonly FileStream _stream;
	private readonly YuvPlanes _planes;
	private readonly BitWriter _bits = new(1 << 20);
	private readonly BitWriter[] _slices;
	private readonly float[] _reciprocals = new float[64];
	private readonly int _frameRateCode;
	private readonly int _quantiserScale;
	private readonly byte[] _sequenceHeader;
	private bool _closed;

	/// <param name="path">Output file; an existing file is replaced.</param>
	/// <param name="width">Frame width in pixels, at most 4095.</param>
	/// <param name="height">Frame height in pixels, at most 2800 (175 slices of 16 rows).</param>
	/// <param name="framesPerSecond">Wanted frame rate; the nearest of <see cref="FrameRates"/> is used.</param>
	/// <param name="quality">1 (coarsest quantiser) to 100 (finest).</param>
	/// <exception cref="NotSupportedException">The size is outside what MPEG-1 can carry.</exception>
	public Mpeg1Writer(string path, int width, int height, double framesPerSecond, int quality)
	{
		if (width < 16 || height < 16 || width > 4095 || height > 175 * 16)
		{
			throw new NotSupportedException($"MPEG-1 cannot carry a {width} × {height} picture (16 × 16 up to 4095 × 2800).");
		}
		Width = width;
		Height = height;
		Output = path;
		_frameRateCode = NearestFrameRateCode(framesPerSecond);
		FrameRate = FrameRates[_frameRateCode - 1];
		_quantiserScale = QuantiserScaleFor(quality);
		_planes = new YuvPlanes(width, height, studioRange: true, subsampled: true);
		// the AC levels are 8 F / (scale × W), truncated: the decoder reconstructs 2 × level × scale × W / 16 (11172-2, 2.4.4.1)
		for (int i = 0; i < 64; i++)
		{
			_reciprocals[i] = 8f / (_quantiserScale * IntraMatrix[i]);
		}
		_slices = new BitWriter[_planes.MacroblockRows];
		for (int i = 0; i < _slices.Length; i++)
		{
			_slices[i] = new BitWriter(Math.Max(4096, _planes.MacroblockColumns * 128));
		}
		_sequenceHeader = BuildSequenceHeader();
		_stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
	}

	/// <inheritdoc/>
	public int Width { get; }

	/// <inheritdoc/>
	public int Height { get; }

	/// <inheritdoc/>
	public int FrameCount { get; private set; }

	/// <inheritdoc/>
	public string Output { get; }

	/// <summary>The frame rate actually written, one of <see cref="FrameRates"/>.</summary>
	public double FrameRate { get; }

	/// <summary>The quantiser scale 1..31 used for every macroblock.</summary>
	public int QuantiserScale => _quantiserScale;

	/// <summary>The frame_rate_code 1..8 whose rate is nearest to <paramref name="framesPerSecond"/>.</summary>
	public static int NearestFrameRateCode(double framesPerSecond)
	{
		int best = 1;
		for (int i = 1; i < FrameRates.Length; i++)
		{
			if (Math.Abs(FrameRates[i] - framesPerSecond) < Math.Abs(FrameRates[best - 1] - framesPerSecond))
			{
				best = i + 1;
			}
		}
		return best;
	}

	/// <summary>The quantiser scale for a quality: 1 at 100, growing faster towards the coarse end, 31 at 1 (quality 70 gives 6, 50 gives 12).</summary>
	public static int QuantiserScaleFor(int quality)
	{
		double t = (100 - Math.Clamp(quality, 1, 100)) / 99.0;
		return Math.Clamp(1 + (int)Math.Round(30.0 * Math.Pow(t, 1.5)), 1, 31);
	}

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<int> pixels)
	{
		if (_closed)
		{
			throw new InvalidOperationException("The video file is already closed.");
		}
		if (pixels.Length < Width * Height)
		{
			throw new ArgumentException("The frame is smaller than the video.", nameof(pixels));
		}
		// the planes read the frame while the rows convert in parallel, so it is copied into a heap buffer first
		int[] frame = pixels[..(Width * Height)].ToArray();
		_planes.Convert(frame);
		_bits.Reset();
		int inGop = FrameCount % GopLength;
		if (inGop == 0)
		{
			_bits.WriteBytes(_sequenceHeader);
			WriteGopHeader(FrameCount);
		}
		WritePicture(inGop);
		WritePack(FrameCount, withSystemHeader: FrameCount == 0);
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
			if (FrameCount > 0)
			{
				// sequence_end_code in its own PES, then the program end code
				_bits.Reset();
				_bits.WriteBytes([0, 0, 1, 0xB7]);
				WritePack(FrameCount, withSystemHeader: false, withPts: false);
			}
			_stream.Write([0, 0, 1, 0xB9]);
			_stream.Flush();
		}
		finally
		{
			_stream.Dispose();
		}
	}

	/// <inheritdoc/>
	public void Dispose() => Close();

	// ---------------------------------------------------------------- video elementary stream

	private byte[] BuildSequenceHeader()
	{
		var bits = new BitWriter(32);
		bits.WriteBytes([0, 0, 1, 0xB3]);
		bits.Write((uint)Width, 12);
		bits.Write((uint)Height, 12);
		bits.Write(1, 4); // aspect_ratio_information: square pixels
		bits.Write((uint)_frameRateCode, 4);
		bits.Write(0x3FFFF, 18); // bit_rate: variable
		bits.Write(1, 1); // marker
		bits.Write(0x3FF, 10); // vbv_buffer_size, the largest
		bits.Write(0, 1); // constrained_parameters_flag
		bits.Write(0, 1); // load_intra_quantiser_matrix: the default
		bits.Write(0, 1); // load_non_intra_quantiser_matrix
		bits.AlignZero();
		return bits.WrittenBytes.ToArray();
	}

	/// <summary>group_of_pictures header: the time code of the first picture, closed GOP.</summary>
	private void WriteGopHeader(int frame)
	{
		double seconds = frame / FrameRate;
		int hours = (int)(seconds / 3600) % 24;
		int minutes = (int)(seconds / 60) % 60;
		int wholeSeconds = (int)seconds % 60;
		int pictures = (int)Math.Round((seconds - Math.Floor(seconds)) * FrameRate) % 60;
		_bits.WriteBytes([0, 0, 1, 0xB8]);
		_bits.Write(0, 1); // drop_frame_flag
		_bits.Write((uint)hours, 5);
		_bits.Write((uint)minutes, 6);
		_bits.Write(1, 1); // marker
		_bits.Write((uint)wholeSeconds, 6);
		_bits.Write((uint)pictures, 6);
		_bits.Write(1, 1); // closed_gop
		_bits.Write(0, 1); // broken_link
		_bits.AlignZero();
	}

	/// <summary>One I-picture: the picture header, then the slices (one per macroblock row, coded in parallel) in order.</summary>
	private void WritePicture(int temporalReference)
	{
		_bits.WriteBytes([0, 0, 1, 0x00]);
		_bits.Write((uint)temporalReference, 10);
		_bits.Write(1, 3); // picture_coding_type: I
		_bits.Write(0xFFFF, 16); // vbv_delay: variable
		_bits.Write(0, 1); // extra_bit_picture
		_bits.AlignZero();
		Parallel.For(0, _slices.Length, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, EncodeSlice);
		foreach (BitWriter slice in _slices)
		{
			_bits.WriteBytes(slice.WrittenBytes);
		}
	}

	/// <summary>One slice: its start code with the row, the quantiser, every macroblock of the row as intra with its six blocks; byte-aligned at the end.</summary>
	private void EncodeSlice(int row)
	{
		YuvPlanes planes = _planes;
		BitWriter bits = _slices[row];
		bits.Reset();
		bits.WriteBytes([0, 0, 1, (byte)(row + 1)]); // slice_start_code with the vertical position
		bits.Write((uint)_quantiserScale, 5);
		bits.Write(0, 1); // extra_bit_slice
		Span<float> block = stackalloc float[64];
		Span<float> dct = stackalloc float[64];
		Span<int> quantised = stackalloc int[64];
		Span<int> predictors = stackalloc int[3];
		predictors[0] = predictors[1] = predictors[2] = 128; // the DC predictors reset at every slice
		for (int column = 0; column < planes.MacroblockColumns; column++)
		{
			bits.Write(1, 1); // macroblock_address_increment: 1
			bits.Write(1, 1); // macroblock_type: intra
			for (int blockIndex = 0; blockIndex < 6; blockIndex++)
			{
				if (blockIndex < 4)
				{
					YuvPlanes.CopyBlock(planes.Y, planes.PaddedWidth, column * 16 + (blockIndex & 1) * 8, row * 16 + (blockIndex >> 1) * 8, 0f, block);
				}
				else
				{
					YuvPlanes.CopyBlock(blockIndex == 4 ? planes.Cb : planes.Cr, planes.ChromaWidth, column * 8, row * 8, 0f, block);
				}
				Dct8x8.Forward(block, dct);
				Quantise(dct, quantised);
				int component = blockIndex < 4 ? 0 : blockIndex - 3;
				WriteBlock(bits, quantised, component, ref predictors[component]);
			}
		}
		bits.AlignZero();
	}

	/// <summary>The intra DC as F(0,0) / 8 (0..255) and the AC levels truncated and clamped to ±255, row-major.</summary>
	private void Quantise(ReadOnlySpan<float> dct, Span<int> quantised)
	{
		if (Vector128.IsHardwareAccelerated)
		{
			Vector128<int> low = Vector128.Create(-255);
			Vector128<int> high = Vector128.Create(255);
			for (int i = 0; i < 64; i += 4)
			{
				Vector128<float> scaled = Vector128.Create(dct.Slice(i, 4)) * Vector128.Create(_reciprocals.AsSpan(i, 4));
				Vector128.Clamp(Vector128.ConvertToInt32(scaled), low, high).CopyTo(quantised.Slice(i, 4)); // ConvertToInt32 truncates
			}
		}
		else
		{
			for (int i = 0; i < 64; i++)
			{
				quantised[i] = Math.Clamp((int)(dct[i] * _reciprocals[i]), -255, 255);
			}
		}
		quantised[0] = Math.Clamp((int)MathF.Round(dct[0] / 8f), 0, 255);
	}

	/// <summary>One intra block: the DC differential with its size code, the AC run/level codes in zig-zag order, the end of block.</summary>
	private static void WriteBlock(BitWriter bits, ReadOnlySpan<int> quantised, int component, ref int predictor)
	{
		int dc = quantised[0];
		int diff = dc - predictor;
		predictor = dc;
		int size = 0;
		for (int magnitude = Math.Abs(diff); magnitude > 0; magnitude >>= 1)
		{
			size++;
		}
		(ushort dcCode, byte dcLength) = component == 0 ? DcSizeLuma[size] : DcSizeChroma[size];
		bits.Write(dcCode, dcLength);
		if (size > 0)
		{
			bits.Write((uint)(diff < 0 ? diff + (1 << size) - 1 : diff), size);
		}
		int run = 0;
		for (int i = 1; i < 64; i++)
		{
			int level = quantised[Dct8x8.ZigZag[i]];
			if (level == 0)
			{
				run++;
				continue;
			}
			WriteRunLevel(bits, run, level);
			run = 0;
		}
		bits.Write(0b10, 2); // end_of_block
	}

	/// <summary>A run/level pair from table B.14 with its sign bit, or the escape form (6 + 6 + 8 or 16 bits) for pairs outside it.</summary>
	private static void WriteRunLevel(BitWriter bits, int run, int level)
	{
		int magnitude = Math.Abs(level);
		if (magnitude <= 40 && run <= 31)
		{
			(ushort code, byte length) = AcCodes[run, magnitude];
			if (length > 0)
			{
				bits.Write(code, length);
				bits.Write(level < 0 ? 1u : 0u, 1);
				return;
			}
		}
		bits.Write(0b000001, 6); // escape
		bits.Write((uint)run, 6);
		if (level >= -127 && level <= 127)
		{
			bits.WriteSigned(level, 8);
		}
		else
		{
			// 16-bit form: 1000 0000 for -255..-128, 0000 0000 for 128..255, then the low byte
			bits.Write(level < 0 ? 0x80u : 0x00u, 8);
			bits.Write((uint)(level & 0xFF), 8);
		}
	}

	// ---------------------------------------------------------------- program stream

	/// <summary>Wraps the bits written so far (a picture with its headers, or the end code) into one pack of PES packets.</summary>
	private void WritePack(int frame, bool withSystemHeader, bool withPts = true)
	{
		long clock = (long)Math.Round(frame * 90000.0 / FrameRate); // 90 kHz
		Span<byte> pack = stackalloc byte[12];
		pack[0] = 0; pack[1] = 0; pack[2] = 1; pack[3] = 0xBA;
		// '0010' SCR[32..30] '1' SCR[29..15] '1' SCR[14..0] '1' '1' mux_rate '1'
		var bits = new BitWriter(16);
		bits.Write(0b0010, 4);
		bits.Write((uint)((clock >> 30) & 7), 3);
		bits.Write(1, 1);
		bits.Write((uint)((clock >> 15) & 0x7FFF), 15);
		bits.Write(1, 1);
		bits.Write((uint)(clock & 0x7FFF), 15);
		bits.Write(1, 1);
		bits.Write(1, 1);
		bits.Write(0x3FFFFF, 22); // mux_rate: the largest, in 50 bytes/s
		bits.Write(1, 1);
		bits.WrittenBytes.CopyTo(pack[4..]);
		_stream.Write(pack);
		if (withSystemHeader)
		{
			// system_header: one video stream, bounds at their maxima
			var system = new BitWriter(32);
			system.WriteBytes([0, 0, 1, 0xBB, 0, 9]);
			system.Write(1, 1);
			system.Write(0x3FFFFF, 22); // rate_bound
			system.Write(1, 1);
			system.Write(0, 6); // audio_bound
			system.Write(0, 1); // fixed_flag
			system.Write(0, 1); // CSPS_flag
			system.Write(0, 1); // system_audio_lock_flag
			system.Write(0, 1); // system_video_lock_flag
			system.Write(1, 1); // marker
			system.Write(1, 5); // video_bound
			system.Write(0xFF, 8); // reserved
			system.Write(0xE0, 8); // stream_id: video stream 0
			system.Write(0b11, 2);
			system.Write(1, 1); // P-STD_buffer_bound_scale: 1024-byte units
			system.Write(0x1FFF, 13); // P-STD_buffer_size_bound
			_stream.Write(system.WrittenBytes);
		}
		ReadOnlySpan<byte> payload = _bits.WrittenBytes;
		bool first = true;
		Span<byte> header = stackalloc byte[6 + 5];
		while (payload.Length > 0 || first)
		{
			int take = Math.Min(payload.Length, PesPayloadLimit);
			ReadOnlySpan<byte> part = payload[..take];
			payload = payload[take..];
			int headerLength = first && withPts ? 5 : 1;
			header[0] = 0; header[1] = 0; header[2] = 1; header[3] = 0xE0;
			int length = headerLength + take;
			header[4] = (byte)(length >> 8);
			header[5] = (byte)length;
			if (first && withPts)
			{
				// '0010' PTS[32..30] '1' PTS[29..15] '1' PTS[14..0] '1'
				header[6] = (byte)(0x21 | (((clock >> 30) & 7) << 1));
				header[7] = (byte)((clock >> 22) & 0xFF);
				header[8] = (byte)(0x01 | (((clock >> 15) & 0x7F) << 1));
				header[9] = (byte)((clock >> 7) & 0xFF);
				header[10] = (byte)(0x01 | ((clock & 0x7F) << 1));
				_stream.Write(header);
			}
			else
			{
				header[6] = 0x0F; // no PTS, no DTS
				_stream.Write(header[..7]);
			}
			_stream.Write(part);
			first = false;
		}
	}

	// ---------------------------------------------------------------- tables

	/// <summary>The default intra quantiser matrix of 11172-2, row-major.</summary>
	private static readonly int[] IntraMatrix =
	[
		8, 16, 19, 22, 26, 27, 29, 34,
		16, 16, 22, 24, 27, 29, 34, 37,
		19, 22, 26, 27, 29, 34, 34, 38,
		22, 22, 26, 27, 29, 34, 37, 40,
		22, 26, 27, 29, 32, 35, 40, 48,
		26, 27, 29, 32, 35, 40, 48, 58,
		26, 27, 29, 34, 38, 46, 56, 69,
		27, 29, 35, 38, 46, 56, 69, 83,
	];

	/// <summary>dct_dc_size_luminance codes (table B.12), indexed by size 0..8.</summary>
	private static readonly (ushort Code, byte Length)[] DcSizeLuma = Codes(["100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110"]);

	/// <summary>dct_dc_size_chrominance codes (table B.13), indexed by size 0..8.</summary>
	private static readonly (ushort Code, byte Length)[] DcSizeChroma = Codes(["00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110"]);

	/// <summary>
	/// Table B.14, the run/level codes of up to 13 bits without the sign bit, indexed [run, level]; a zero length
	/// means "use the escape". The 0/1 entry is the "next coefficient" form (11), the only one an intra block uses.
	/// </summary>
	private static readonly (ushort Code, byte Length)[,] AcCodes = MakeAcCodes();

	private static (ushort, byte) Code(string code)
	{
		ushort value = 0;
		foreach (char c in code)
		{
			value = (ushort)((value << 1) | (c == '1' ? 1 : 0));
		}
		return (value, (byte)code.Length);
	}

	private static (ushort, byte)[] Codes(string[] codes) => [.. codes.Select(Code)];

	private static (ushort, byte)[,] MakeAcCodes()
	{
		var table = new (ushort, byte)[32, 41];
		(int Run, int Level, string Code)[] entries =
		[
			(0, 1, "11"), (1, 1, "011"), (0, 2, "0100"), (2, 1, "0101"), (0, 3, "00101"), (3, 1, "00111"), (4, 1, "00110"),
			(1, 2, "000110"), (5, 1, "000111"), (6, 1, "000101"), (7, 1, "000100"),
			(0, 4, "0000110"), (2, 2, "0000100"), (8, 1, "0000111"), (9, 1, "0000101"),
			(0, 5, "00100110"), (0, 6, "00100001"), (1, 3, "00100101"), (3, 2, "00100100"), (10, 1, "00100111"), (11, 1, "00100011"), (12, 1, "00100010"), (13, 1, "00100000"),
			(0, 7, "0000001010"), (1, 4, "0000001100"), (2, 3, "0000001011"), (4, 2, "0000001111"), (5, 2, "0000001001"), (14, 1, "0000001110"), (15, 1, "0000001101"), (16, 1, "0000001000"),
			(0, 8, "000000011101"), (0, 9, "000000011000"), (0, 10, "000000010011"), (0, 11, "000000010000"), (1, 5, "000000011011"), (2, 4, "000000010100"), (3, 3, "000000011100"),
			(4, 3, "000000010010"), (6, 2, "000000011110"), (7, 2, "000000010101"), (8, 2, "000000010001"), (17, 1, "000000011111"), (18, 1, "000000011010"), (19, 1, "000000011001"),
			(20, 1, "000000010111"), (21, 1, "000000010110"),
			(0, 12, "0000000011010"), (0, 13, "0000000011001"), (0, 14, "0000000011000"), (0, 15, "0000000010111"), (1, 6, "0000000010110"), (1, 7, "0000000010101"),
			(2, 5, "0000000010100"), (3, 4, "0000000010011"), (5, 3, "0000000010010"), (9, 2, "0000000010001"), (10, 2, "0000000010000"), (22, 1, "0000000011111"),
			(23, 1, "0000000011110"), (24, 1, "0000000011101"), (25, 1, "0000000011100"), (26, 1, "0000000011011"),
		];
		foreach ((int run, int level, string code) in entries)
		{
			table[run, level] = Code(code);
		}
		return table;
	}
}
