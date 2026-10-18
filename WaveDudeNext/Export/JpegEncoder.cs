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

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes baseline JPEG files (JFIF, 4:4:4, the standard quantisation and Huffman tables of ITU-T T.81
/// Annex K) from packed BGRA32 frames, in-house. The quality 1..100 scales the quantisation tables the way
/// the Independent JPEG Group's library does (50 is the tables as printed, 100 nearly lossless). The chroma
/// is not subsampled (2026-10-18): a wave picture's information is in its colour, and 4:2:0 averaged it
/// over 2 × 2 pixels, blocky fringes even at quality 100; 4:4:4 at quality 90 beat that in every channel
/// at half the size. Every band of 16 pixel rows (two rows of 8 × 8 MCUs) is a restart interval (a DRI
/// segment, RSTn markers between bands), so the bands are
/// transformed, quantised and Huffman-coded in parallel, each into its own byte buffer, and concatenated.
/// The transform is the AAN factorisation on SSE registers (<see cref="Dct8x8.ForwardAan"/>, its scaling
/// folded into the quantiser divisors, which are kept in zig-zag order so the coefficients land there
/// directly); the entropy coder emits a Huffman code and its value bits as one field into a 64-bit
/// accumulator flushed four bytes at a time (2026-10-15; the first version took three times WIC's time per
/// thread). Reusable for frames of one size; one thread at a time.
/// </summary>
public sealed class JpegEncoder
{
	private readonly YuvPlanes _planes;
	private readonly byte[] _lumaQuant = new byte[64];
	private readonly byte[] _chromaQuant = new byte[64];
	private readonly float[] _lumaDivisorsZ = new float[64];
	private readonly float[] _chromaDivisorsZ = new float[64];
	private readonly RowBuffer[] _rows;
	private readonly byte[] _header;
	private readonly int _mcuColumns;
	private readonly int _mcuRows;

	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="quality">1 (smallest file) to 100 (best picture).</param>
	public JpegEncoder(int width, int height, int quality)
	{
		Width = width;
		Height = height;
		Quality = Math.Clamp(quality, 1, 100);
		_planes = new YuvPlanes(width, height, studioRange: false, subsampled: false);
		_mcuColumns = (width + 7) / 8;
		_mcuRows = (height + 7) / 8;
		ScaleTables(Quality, LumaBase, _lumaQuant, _lumaDivisorsZ);
		ScaleTables(Quality, ChromaBase, _chromaQuant, _chromaDivisorsZ);
		_rows = new RowBuffer[_planes.MacroblockRows];
		for (int i = 0; i < _rows.Length; i++)
		{
			_rows[i] = new RowBuffer(Math.Max(4096, _mcuColumns * 96));
		}
		_header = BuildHeader();
	}

	/// <summary>Frame width in pixels.</summary>
	public int Width { get; }

	/// <summary>Frame height in pixels.</summary>
	public int Height { get; }

	/// <summary>The quality the tables were scaled for.</summary>
	public int Quality { get; }

	/// <summary>Whether the rows are coded in parallel (the default); off, they go one after the other, for the bench's comparison.</summary>
	public bool ParallelRows { get; set; } = true;

	/// <summary>
	/// Threads the rows may use when <see cref="ParallelRows"/> is on: the core count. Left to the scheduler (-1)
	/// the pool oversubscribed and the frame took twice as long (13.8 against 5.8 ms at 1080p, 2026-10-15); the
	/// bench sets other values for its scaling curve.
	/// </summary>
	public int MaxThreads { get; set; } = Environment.ProcessorCount;

	/// <summary>Milliseconds the last frame spent in the colour conversion and in the rows' transform-and-code pass (for the bench).</summary>
	public (double Convert, double Rows) LastPhases { get; private set; }

	/// <summary>Writes one frame as a JPEG to <paramref name="stream"/> (left open).</summary>
	/// <param name="stream">Destination.</param>
	/// <param name="pixels">Packed BGRA32 pixels, row-major, at least width × height.</param>
	public void Write(Stream stream, ReadOnlyMemory<int> pixels)
	{
		long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		_planes.Convert(pixels);
		long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
		if (ParallelRows)
		{
			Parallel.For(0, _rows.Length, new ParallelOptions { MaxDegreeOfParallelism = MaxThreads }, EncodeRow);
		}
		else
		{
			for (int row = 0; row < _rows.Length; row++)
			{
				EncodeRow(row);
			}
		}
		long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
		LastPhases = (System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
		stream.Write(_header);
		for (int row = 0; row < _rows.Length; row++)
		{
			stream.Write(_rows[row].Bytes, 0, _rows[row].Count);
			if (row < _rows.Length - 1)
			{
				stream.Write([0xFF, (byte)(0xD0 + (row & 7))]); // RSTn
			}
		}
		stream.Write([0xFF, 0xD9]); // EOI
	}

	/// <summary>Encodes one frame to a byte array.</summary>
	public byte[] Encode(ReadOnlyMemory<int> pixels)
	{
		using var stream = new MemoryStream();
		Write(stream, pixels);
		return stream.ToArray();
	}

	/// <summary>
	/// One restart interval: the band's two MCU rows (the last band may have one), each MCU a Y, a Cb and a Cr
	/// block of the same 8 × 8 pixels, transformed, quantised and coded, the DC predictors starting at zero.
	/// </summary>
	private void EncodeRow(int mbRow)
	{
		YuvPlanes planes = _planes;
		RowBuffer row = _rows[mbRow];
		row.Count = 0;
		var bits = new BitSink(row);
		Span<float> block = stackalloc float[64];
		Span<float> dct = stackalloc float[64];
		Span<int> quantised = stackalloc int[64];
		int predictorY = 0;
		int predictorCb = 0;
		int predictorCr = 0;
		int lastMcuRow = Math.Min(mbRow * 2 + 1, _mcuRows - 1);
		for (int mcuRow = mbRow * 2; mcuRow <= lastMcuRow; mcuRow++)
		{
			for (int mcuColumn = 0; mcuColumn < _mcuColumns; mcuColumn++)
			{
				for (int component = 0; component < 3; component++)
				{
					byte[] plane = component == 0 ? planes.Y : component == 1 ? planes.Cb : planes.Cr;
					YuvPlanes.CopyBlock(plane, planes.PaddedWidth, mcuColumn * 8, mcuRow * 8, 128f, block);
					Dct8x8.ForwardAan(block, dct);
					if (component == 0)
					{
						QuantiseZigZag(dct, _lumaDivisorsZ, quantised);
						EncodeBlock(ref bits, quantised, LumaDcCodes, LumaDcLengths, LumaAcCodes, LumaAcLengths, ref predictorY);
					}
					else
					{
						QuantiseZigZag(dct, _chromaDivisorsZ, quantised);
						EncodeBlock(ref bits, quantised, ChromaDcCodes, ChromaDcLengths, ChromaAcCodes, ChromaAcLengths, ref component == 1 ? ref predictorCb : ref predictorCr);
					}
				}
			}
		}
		bits.Finish();
	}

	/// <summary>Rounds the AAN coefficients times their folded divisors, straight into zig-zag order.</summary>
	private static void QuantiseZigZag(ReadOnlySpan<float> dct, ReadOnlySpan<float> divisorsZ, Span<int> quantised)
	{
		ReadOnlySpan<int> order = Dct8x8.ZigZagAan;
		if (Vector128.IsHardwareAccelerated)
		{
			Span<float> gathered = stackalloc float[64];
			for (int i = 0; i < 64; i++)
			{
				gathered[i] = dct[order[i]];
			}
			for (int i = 0; i < 64; i += 4)
			{
				Vector128<float> scaled = Vector128.Create(gathered.Slice(i, 4)) * Vector128.Create(divisorsZ.Slice(i, 4));
				Vector128.ConvertToInt32(Vector128.Round(scaled)).CopyTo(quantised.Slice(i, 4));
			}
		}
		else
		{
			for (int i = 0; i < 64; i++)
			{
				quantised[i] = (int)MathF.Round(dct[order[i]] * divisorsZ[i]);
			}
		}
	}

	/// <summary>Huffman-codes one block already in zig-zag order: the DC difference, the AC run/size pairs with their value bits as one field each, ZRL and EOB.</summary>
	private static void EncodeBlock(ref BitSink bits, scoped ReadOnlySpan<int> quantised, ushort[] dcCodes, byte[] dcLengths, ushort[] acCodes, byte[] acLengths, ref int predictor)
	{
		int diff = quantised[0] - predictor;
		predictor = quantised[0];
		int size = Category(diff);
		bits.Write(((ulong)dcCodes[size] << size) | ValueBits(diff, size), dcLengths[size] + size);
		int run = 0;
		for (int i = 1; i < 64; i++)
		{
			int value = quantised[i];
			if (value == 0)
			{
				run++;
				continue;
			}
			while (run >= 16)
			{
				bits.Write(acCodes[0xF0], acLengths[0xF0]); // ZRL: sixteen zeros
				run -= 16;
			}
			int category = Category(value);
			int symbol = (run << 4) | category;
			bits.Write(((ulong)acCodes[symbol] << category) | ValueBits(value, category), acLengths[symbol] + category);
			run = 0;
		}
		if (run > 0)
		{
			bits.Write(acCodes[0x00], acLengths[0x00]); // EOB
		}
	}

	/// <summary>The magnitude category of a coefficient: the number of bits of its absolute value.</summary>
	private static int Category(int value) => value == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)Math.Abs(value));

	/// <summary>The additional bits of a value in a category: the value itself, or for a negative one its one's complement in that many bits.</summary>
	private static ulong ValueBits(int value, int category) => (ulong)(uint)(value < 0 ? value - 1 : value) & ((1UL << category) - 1);

	/// <summary>Everything before the entropy-coded data: SOI, APP0, two DQT, SOF0, four DHT, DRI, SOS.</summary>
	private byte[] BuildHeader()
	{
		using var stream = new MemoryStream();
		stream.Write([0xFF, 0xD8]); // SOI
		// APP0 JFIF 1.01, no density, no thumbnail
		Segment(stream, 0xE0, [(byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
		// DQT: 8-bit tables in zig-zag order, luma 0, chroma 1
		var quant = new byte[2 * 65];
		quant[0] = 0;
		quant[65] = 1;
		for (int i = 0; i < 64; i++)
		{
			quant[1 + i] = _lumaQuant[Dct8x8.ZigZag[i]];
			quant[66 + i] = _chromaQuant[Dct8x8.ZigZag[i]];
		}
		Segment(stream, 0xDB, quant);
		// SOF0: 8 bits, three components all sampled 1 × 1 (4:4:4), Y with table 0, Cb and Cr with table 1
		var frame = new byte[15];
		frame[0] = 8;
		BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), (ushort)Height);
		BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), (ushort)Width);
		frame[5] = 3;
		frame[6] = 1; frame[7] = 0x11; frame[8] = 0;
		frame[9] = 2; frame[10] = 0x11; frame[11] = 1;
		frame[12] = 3; frame[13] = 0x11; frame[14] = 1;
		Segment(stream, 0xC0, frame);
		// DHT: the four standard tables
		Segment(stream, 0xC4, [.. DhtSegment(0x00, LumaDcCounts, LumaDcSymbols), .. DhtSegment(0x10, LumaAcCounts, LumaAcSymbols), .. DhtSegment(0x01, ChromaDcCounts, ChromaDcSymbols), .. DhtSegment(0x11, ChromaAcCounts, ChromaAcSymbols)]);
		// DRI: a restart interval of one band, two MCU rows (the last interval may be shorter, which the standard allows)
		var interval = new byte[2];
		BinaryPrimitives.WriteUInt16BigEndian(interval, (ushort)(2 * _mcuColumns));
		Segment(stream, 0xDD, interval);
		// SOS: three components with their DC/AC table pairs, spectral selection 0..63, no successive approximation
		Segment(stream, 0xDA, [3, 1, 0x00, 2, 0x11, 3, 0x11, 0, 63, 0]);
		return stream.ToArray();
	}

	private static byte[] DhtSegment(byte classAndId, byte[] counts, byte[] symbols) => [classAndId, .. counts, .. symbols];

	private static void Segment(Stream stream, byte marker, ReadOnlySpan<byte> payload)
	{
		Span<byte> head = stackalloc byte[4];
		head[0] = 0xFF;
		head[1] = marker;
		BinaryPrimitives.WriteUInt16BigEndian(head[2..], (ushort)(payload.Length + 2));
		stream.Write(head);
		stream.Write(payload);
	}

	/// <summary>
	/// Scales a base table to a quality (the IJG rule) into byte entries, and into the reciprocals in zig-zag order
	/// with the AAN output scaling folded in (<see cref="Dct8x8.AanScale"/>): quantised = round(aanOut × divisor).
	/// </summary>
	private static void ScaleTables(int quality, ReadOnlySpan<byte> baseTable, byte[] table, float[] divisorsZ)
	{
		int scale = quality < 50 ? 5000 / quality : 200 - 2 * quality;
		for (int i = 0; i < 64; i++)
		{
			table[i] = (byte)Math.Clamp((baseTable[i] * scale + 50) / 100, 1, 255);
		}
		for (int i = 0; i < 64; i++)
		{
			int natural = Dct8x8.ZigZag[i];
			int v = natural / 8;
			int u = natural % 8;
			divisorsZ[i] = (float)(1.0 / (table[natural] * Dct8x8.AanScale[u] * Dct8x8.AanScale[v] * 8.0));
		}
	}

	/// <summary>A row's output bytes.</summary>
	private sealed class RowBuffer(int capacity)
	{
		public byte[] Bytes = new byte[capacity];
		public int Count;
	}

	/// <summary>
	/// The entropy coder's bit sink: fields of up to 32 bits go into a 64-bit accumulator, which is emptied four
	/// bytes at a time with the 0x00 after every 0xFF the JPEG syntax wants; the buffer grows as needed.
	/// </summary>
	private ref struct BitSink(RowBuffer row)
	{
		private readonly RowBuffer _row = row;
		private ulong _acc;
		private int _bits;

		public void Write(ulong field, int length)
		{
			_acc = (_acc << length) | field;
			_bits += length;
			if (_bits >= 32)
			{
				Flush();
			}
		}

		/// <summary>Moves whole bytes out of the accumulator.</summary>
		private void Flush()
		{
			byte[] bytes = _row.Bytes;
			int count = _row.Count;
			if (count + 16 > bytes.Length)
			{
				Array.Resize(ref _row.Bytes, bytes.Length * 2);
				bytes = _row.Bytes;
			}
			while (_bits >= 8)
			{
				_bits -= 8;
				byte b = (byte)(_acc >> _bits);
				bytes[count++] = b;
				if (b == 0xFF)
				{
					bytes[count++] = 0;
				}
			}
			_row.Count = count;
		}

		/// <summary>Pads to a byte with one bits (the rule before a marker) and empties the accumulator.</summary>
		public void Finish()
		{
			int pad = (8 - _bits % 8) % 8;
			if (pad > 0)
			{
				Write((1UL << pad) - 1, pad);
			}
			Flush();
		}
	}

	// Annex K.1: the luminance quantisation table, row-major
	private static ReadOnlySpan<byte> LumaBase =>
	[
		16, 11, 10, 16, 24, 40, 51, 61,
		12, 12, 14, 19, 26, 58, 60, 55,
		14, 13, 16, 24, 40, 57, 69, 56,
		14, 17, 22, 29, 51, 87, 80, 62,
		18, 22, 37, 56, 68, 109, 103, 77,
		24, 35, 55, 64, 81, 104, 113, 92,
		49, 64, 78, 87, 103, 121, 120, 101,
		72, 92, 95, 98, 112, 100, 103, 99,
	];

	// Annex K.2: the chrominance quantisation table, row-major
	private static ReadOnlySpan<byte> ChromaBase =>
	[
		17, 18, 24, 47, 99, 99, 99, 99,
		18, 21, 26, 66, 99, 99, 99, 99,
		24, 26, 56, 99, 99, 99, 99, 99,
		47, 66, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
		99, 99, 99, 99, 99, 99, 99, 99,
	];

	// Annex K.3 to K.6: the standard Huffman tables as (codes per length 1..16, symbols), expanded to code/length per symbol
	private static readonly byte[] LumaDcCounts = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
	private static readonly byte[] LumaDcSymbols = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
	private static readonly byte[] ChromaDcCounts = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
	private static readonly byte[] ChromaDcSymbols = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
	private static readonly byte[] LumaAcCounts = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];
	private static readonly byte[] LumaAcSymbols =
	[
		0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
		0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
		0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
		0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
		0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
		0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
		0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
		0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
		0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
		0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
		0xF9, 0xFA,
	];
	private static readonly byte[] ChromaAcCounts = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];
	private static readonly byte[] ChromaAcSymbols =
	[
		0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
		0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
		0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
		0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
		0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
		0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
		0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
		0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
		0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
		0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
		0xF9, 0xFA,
	];

	private static readonly ushort[] LumaDcCodes = new ushort[256];
	private static readonly byte[] LumaDcLengths = new byte[256];
	private static readonly ushort[] ChromaDcCodes = new ushort[256];
	private static readonly byte[] ChromaDcLengths = new byte[256];
	private static readonly ushort[] LumaAcCodes = new ushort[256];
	private static readonly byte[] LumaAcLengths = new byte[256];
	private static readonly ushort[] ChromaAcCodes = new ushort[256];
	private static readonly byte[] ChromaAcLengths = new byte[256];

	static JpegEncoder()
	{
		Expand(LumaDcCounts, LumaDcSymbols, LumaDcCodes, LumaDcLengths);
		Expand(ChromaDcCounts, ChromaDcSymbols, ChromaDcCodes, ChromaDcLengths);
		Expand(LumaAcCounts, LumaAcSymbols, LumaAcCodes, LumaAcLengths);
		Expand(ChromaAcCounts, ChromaAcSymbols, ChromaAcCodes, ChromaAcLengths);
	}

	/// <summary>A JPEG Huffman table from its DHT form (counts per code length, then the symbols) to code and length per symbol, per Annex C.</summary>
	private static void Expand(byte[] counts, byte[] symbols, ushort[] codes, byte[] lengths)
	{
		int code = 0;
		int k = 0;
		for (int length = 1; length <= 16; length++)
		{
			for (int i = 0; i < counts[length - 1]; i++)
			{
				codes[symbols[k]] = (ushort)code;
				lengths[symbols[k]] = (byte)length;
				k++;
				code++;
			}
			code <<= 1;
		}
	}
}
