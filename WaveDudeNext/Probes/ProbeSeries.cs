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

namespace WaveDudeNext.Probes;

/// <summary>
/// The measured values of one point probe: a growable buffer with one value per simulation step, in step
/// order (value i belongs to step i + 1). Not thread-safe: each instance belongs to one
/// <see cref="ProbeTrace"/> and is only touched by that trace's owning thread.
/// </summary>
public sealed class ProbeSeries
{
	/// <summary>Number of valid values; only the first <see cref="Count"/> entries of <see cref="Values"/> are meaningful.</summary>
	public int Count => _count;

	/// <summary>
	/// Backing array, exposed without copying. Its length is the capacity, not the sample count, so read only
	/// indices below <see cref="Count"/>. The reference changes whenever the buffer grows. Do not change.
	/// </summary>
	public float[] Values => _values;

	/// <summary>Most recently added value, or NaN when the series is empty.</summary>
	public float LastValue => _count == 0 ? float.NaN : _values[_count - 1];

	/// <summary>Appends values in step order, at least doubling the backing array when it is full. Null or empty input is ignored.</summary>
	public void Add(float[]? items)
	{
		if (items == null || items.Length == 0)
		{
			return;
		}
		int newCount = _count + items.Length;
		if (newCount > _values.Length)
		{
			var grown = new float[Math.Max(newCount, _values.Length * 2)];
			Array.Copy(_values, grown, _count);
			_values = grown;
		}
		Array.Copy(items, 0, _values, _count, items.Length);
		_count = newCount;
	}

	/// <summary>Discards all values but keeps the allocated storage.</summary>
	public void Clear()
	{
		_count = 0;
	}

	/// <summary>Backing storage; starts at 1024 entries, grows by at least doubling and never shrinks.</summary>
	private float[] _values = new float[1024];

	/// <summary>Number of valid entries at the start of <see cref="_values"/>.</summary>
	private int _count;
}
