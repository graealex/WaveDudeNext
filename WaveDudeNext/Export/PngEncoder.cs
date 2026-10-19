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
using System.IO.Compression;
using System.Text;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes 24-bit RGB PNG files from packed BGRA32 frames, in-house: the signature, IHDR, the zlib stream
/// (<see cref="ZLibStream"/>, which writes the zlib header and Adler-32 itself) as IDAT chunks of up to 64 KB
/// written as the compressor produces them (no whole-file buffer: that cost more in garbage than the
/// compression, 2026-10-15) and IEND, with a <see cref="Crc32"/> per chunk. Every row is Paeth-filtered
/// (filter type 4), which suits the smooth gradients of a wave picture. Lossless; the alpha byte is
/// dropped. Any thread.
/// </summary>
public static class PngEncoder
{
	private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>Writes one frame as a PNG to <paramref name="stream"/>.</summary>
	/// <param name="stream">Destination; left open.</param>
	/// <param name="pixels">Packed BGRA32 pixels, row-major, at least <paramref name="width"/> × <paramref name="height"/>.</param>
	/// <param name="width">Width in pixels.</param>
	/// <param name="height">Height in pixels.</param>
	public static void Write(Stream stream, ReadOnlySpan<int> pixels, int width, int height)
	{
		stream.Write(Signature);
		Span<byte> header = stackalloc byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header, width);
		BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
		header[8] = 8; // bit depth
		header[9] = 2; // colour type: truecolour
		header[10] = 0; // deflate
		header[11] = 0; // adaptive filtering
		header[12] = 0; // no interlace
		WriteChunk(stream, "IHDR", header);
		using (var idat = new IdatStream(stream))
		{
			Deflate(idat, pixels, width, height);
		}
		WriteChunk(stream, "IEND", default);
	}

	/// <summary>Encodes a frame to a byte array (for a harness or a clipboard).</summary>
	public static byte[] Encode(ReadOnlySpan<int> pixels, int width, int height)
	{
		using var stream = new MemoryStream();
		Write(stream, pixels, width, height);
		return stream.ToArray();
	}

	/// <summary>The filtered rows, zlib-compressed into <paramref name="output"/>.</summary>
	private static void Deflate(Stream output, ReadOnlySpan<int> pixels, int width, int height)
	{
		int rowBytes = width * 3;
		var previous = new byte[rowBytes];
		var current = new byte[rowBytes];
		var filtered = new byte[rowBytes + 1];
		using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
		{
			for (int y = 0; y < height; y++)
			{
				ReadOnlySpan<int> row = pixels.Slice(y * width, width);
				for (int x = 0; x < width; x++)
				{
					int argb = row[x];
					current[x * 3] = (byte)(argb >> 16);
					current[x * 3 + 1] = (byte)(argb >> 8);
					current[x * 3 + 2] = (byte)argb;
				}
				filtered[0] = 4; // Paeth
				for (int i = 0; i < rowBytes; i++)
				{
					int a = i >= 3 ? current[i - 3] : 0;
					int b = previous[i];
					int c = i >= 3 ? previous[i - 3] : 0;
					filtered[i + 1] = (byte)(current[i] - Paeth(a, b, c));
				}
				zlib.Write(filtered, 0, filtered.Length);
				(previous, current) = (current, previous);
			}
		}
	}

	/// <summary>Collects what the compressor writes and emits it as IDAT chunks of up to <see cref="ChunkSize"/> bytes.</summary>
	private sealed class IdatStream(Stream target) : Stream
	{
		private const int ChunkSize = 1 << 16;
		private readonly byte[] _buffer = new byte[ChunkSize];
		private int _filled;

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			while (buffer.Length > 0)
			{
				int take = Math.Min(buffer.Length, ChunkSize - _filled);
				buffer[..take].CopyTo(_buffer.AsSpan(_filled));
				_filled += take;
				buffer = buffer[take..];
				if (_filled == ChunkSize)
				{
					Flush();
				}
			}
		}

		public override void Flush()
		{
			if (_filled > 0)
			{
				WriteChunk(target, "IDAT", _buffer.AsSpan(0, _filled));
				_filled = 0;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Flush();
			}
			base.Dispose(disposing);
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
	}

	/// <summary>The PNG Paeth predictor: whichever of left, above and above-left is closest to left + above − above-left.</summary>
	private static int Paeth(int a, int b, int c)
	{
		int p = a + b - c;
		int pa = Math.Abs(p - a);
		int pb = Math.Abs(p - b);
		int pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}

	private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
	{
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
		stream.Write(length);
		byte[] typeBytes = Encoding.ASCII.GetBytes(type);
		stream.Write(typeBytes);
		stream.Write(data);
		uint crc = Crc32.Update(Crc32.Update(Crc32.Initial, typeBytes), data);
		Span<byte> crcBytes = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32.Finish(crc));
		stream.Write(crcBytes);
	}
}

/// <summary>The CRC-32 of PNG and zip (polynomial 0xEDB88320, reflected), table-driven.</summary>
public static class Crc32
{
	private static readonly uint[] Table = MakeTable();

	/// <summary>The running value before any byte.</summary>
	public const uint Initial = 0xFFFFFFFFu;

	/// <summary>Feeds bytes into a running value.</summary>
	public static uint Update(uint crc, ReadOnlySpan<byte> data)
	{
		foreach (byte b in data)
		{
			crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
		}
		return crc;
	}

	/// <summary>The final CRC of a running value.</summary>
	public static uint Finish(uint crc) => crc ^ 0xFFFFFFFFu;

	/// <summary>The CRC-32 of a whole buffer.</summary>
	public static uint Of(ReadOnlySpan<byte> data) => Finish(Update(Initial, data));

	private static uint[] MakeTable()
	{
		var table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++)
			{
				c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			}
			table[n] = c;
		}
		return table;
	}
}
