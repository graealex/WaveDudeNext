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

namespace WaveDudeNext;

/// <summary>
/// Small deterministic pseudo-random generator (PCG-XSH-RR, 64-bit state, 32-bit output).
/// Used wherever a project needs reproducible randomness, because sequences from a seeded
/// <see cref="Random"/> are not guaranteed to stay identical across .NET versions.
/// </summary>
public sealed class Pcg32
{
	private const ulong Multiplier = 6364136223846793005UL;
	private ulong _state;
	private readonly ulong _increment;

	/// <param name="seed">Starting point of the sequence.</param>
	/// <param name="stream">Selects one of 2^63 independent sequences for the same seed.</param>
	public Pcg32(ulong seed, ulong stream = 0)
	{
		_increment = (stream << 1) | 1;
		_state = 0;
		NextUInt32();
		_state += seed;
		NextUInt32();
	}

	/// <summary>Next value in the full 32-bit range.</summary>
	public uint NextUInt32()
	{
		ulong old = _state;
		_state = old * Multiplier + _increment;
		uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
		int rotation = (int)(old >> 59);
		return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
	}

	/// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxInclusive"/>].</summary>
	public int Next(int minInclusive, int maxInclusive)
	{
		if (maxInclusive < minInclusive)
		{
			throw new ArgumentOutOfRangeException(nameof(maxInclusive), "Upper bound is below the lower bound.");
		}
		ulong range = (ulong)((long)maxInclusive - minInclusive) + 1;
		// rejection sampling keeps the distribution exactly uniform
		ulong limit = (1UL << 32) - ((1UL << 32) % range);
		uint value;
		do
		{
			value = NextUInt32();
		}
		while (value >= limit);
		return (int)(minInclusive + (long)(value % range));
	}

	/// <summary>Uniform double in [0, 1).</summary>
	public double NextDouble() => (NextUInt32() >> 8) * (1.0 / (1 << 24));

	/// <summary>
	/// Stable 32-bit FNV-1a hash of <paramref name="text"/> over its UTF-16 code units, for
	/// deriving per-object seeds from names. Unlike <see cref="string.GetHashCode()"/> it does
	/// not change between processes.
	/// </summary>
	public static uint StableHash(string text)
	{
		uint hash = 2166136261;
		foreach (char c in text)
		{
			hash = (hash ^ c) * 16777619;
		}
		return hash;
	}
}
