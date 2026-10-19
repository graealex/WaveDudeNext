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

namespace WaveDudeNext.Export;

/// <summary>
/// Writes bit fields most-significant-bit first into a byte buffer, as the JPEG and MPEG-1 syntaxes want
/// them. JPEG entropy-coded data needs a 0x00 after every 0xFF byte (<see cref="StuffJpeg"/>); MPEG-1
/// does not. Single thread; the caller takes the bytes with <see cref="WrittenBytes"/> or <see cref="WriteTo"/>.
/// </summary>
public sealed class BitWriter
{
	private byte[] _buffer;
	private int _count;
	private ulong _bits;
	private int _bitCount;

	/// <param name="capacity">Initial size of the byte buffer; it grows as needed.</param>
	/// <param name="stuffJpeg">Insert a 0x00 after every 0xFF written (JPEG byte stuffing).</param>
	public BitWriter(int capacity = 1 << 16, bool stuffJpeg = false)
	{
		_buffer = new byte[Math.Max(16, capacity)];
		StuffJpeg = stuffJpeg;
	}

	/// <summary>Whether a 0x00 follows every 0xFF (JPEG).</summary>
	public bool StuffJpeg { get; }

	/// <summary>Bytes completed so far (pending bits not included).</summary>
	public int ByteCount => _count;

	/// <summary>Bits waiting for a full byte, 0 to 7.</summary>
	public int PendingBits => _bitCount;

	/// <summary>Whole bits written so far, pending ones included.</summary>
	public long BitPosition => (long)_count * 8 + _bitCount;

	/// <summary>Appends the low <paramref name="bits"/> bits of <paramref name="value"/>, most significant first.</summary>
	/// <param name="value">The field's value; bits above <paramref name="bits"/> are ignored.</param>
	/// <param name="bits">Field width, 0 to 32.</param>
	public void Write(uint value, int bits)
	{
		if (bits == 0)
		{
			return;
		}
		_bits = (_bits << bits) | (value & (bits == 32 ? 0xFFFFFFFFu : (1u << bits) - 1));
		_bitCount += bits;
		while (_bitCount >= 8)
		{
			_bitCount -= 8;
			Put((byte)(_bits >> _bitCount));
		}
	}

	/// <summary>Appends a code given as a string of '0' and '1' characters.</summary>
	public void WriteCode(string code)
	{
		uint value = 0;
		foreach (char c in code)
		{
			value = (value << 1) | (c == '1' ? 1u : 0u);
		}
		Write(value, code.Length);
	}

	/// <summary>Appends a signed value in <paramref name="bits"/> bits two's complement.</summary>
	public void WriteSigned(int value, int bits) => Write(unchecked((uint)value), bits);

	/// <summary>Pads with zero bits to the next byte boundary (MPEG start codes, slice ends).</summary>
	public void AlignZero()
	{
		if (_bitCount > 0)
		{
			Write(0, 8 - _bitCount);
		}
	}

	/// <summary>Pads with one bits to the next byte boundary (the JPEG rule before a marker).</summary>
	public void AlignOne()
	{
		if (_bitCount > 0)
		{
			Write(0xFF, 8 - _bitCount);
		}
	}

	/// <summary>Appends whole bytes; the writer must be byte-aligned.</summary>
	/// <exception cref="InvalidOperationException">Bits are pending.</exception>
	public void WriteBytes(ReadOnlySpan<byte> bytes)
	{
		if (_bitCount != 0)
		{
			throw new InvalidOperationException("The bit writer is not byte-aligned.");
		}
		foreach (byte b in bytes)
		{
			Put(b);
		}
	}

	/// <summary>The completed bytes (pending bits are not included; align first).</summary>
	public ReadOnlySpan<byte> WrittenBytes => _buffer.AsSpan(0, _count);

	/// <summary>Writes the completed bytes to <paramref name="stream"/>.</summary>
	public void WriteTo(Stream stream) => stream.Write(_buffer, 0, _count);

	/// <summary>Forgets everything written; the buffer is kept.</summary>
	public void Reset()
	{
		_count = 0;
		_bits = 0;
		_bitCount = 0;
	}

	private void Put(byte b)
	{
		if (_count + 2 > _buffer.Length)
		{
			Array.Resize(ref _buffer, _buffer.Length * 2);
		}
		_buffer[_count++] = b;
		if (StuffJpeg && b == 0xFF)
		{
			_buffer[_count++] = 0;
		}
	}
}
