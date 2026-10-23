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

namespace WaveDudeNext.Rendering;

/// <summary>
/// The display brightness that fits a frame (the preview's Auto, 2026-10-20, Jeroen's point 9): the gain that brings
/// the <see cref="Percentile"/> of the coloured magnitude to the top of the palette, |value| for the field and the
/// square root for the intensity (which the preview stores and the renderer colours, see <see cref="ColorLut"/>), so the brightest half percent
/// saturates and a source cell, far above the field around it, does not darken the picture. Pure arithmetic; any
/// thread.
/// </summary>
public static class BrightnessFit
{
	/// <summary>The share of the samples at or below the value brought to the top of the palette.</summary>
	public const double Percentile = 0.995;

	/// <summary>
	/// The brightness that fits <paramref name="samples"/> of that <paramref name="kind"/>, rounded to three significant
	/// digits and clamped to the model's range (1e-4 to 1e5); null when the frame has nothing to fit (every sample
	/// zero or not finite).
	/// </summary>
	/// <param name="samples">One plane of a frame, as stored.</param>
	/// <param name="kind">What the samples are: the field is coloured by its value, the intensity by its square root, which the preview's frames store.</param>
	public static double? For(ReadOnlySpan<Half> samples, SampleKind kind)
	{
		float[] magnitudes = new float[samples.Length];
		int count = 0;
		foreach (Half sample in samples)
		{
			float value = (float)sample;
			if (float.IsFinite(value))
			{
				magnitudes[count++] = kind switch
				{
					SampleKind.Field => MathF.Abs(value),
					SampleKind.Intensity => MathF.Sqrt(MathF.Max(0f, value)),
					_ => MathF.Max(0f, value),
				};
			}
		}
		if (count == 0)
		{
			return null;
		}
		int rank = Math.Min(count - 1, (int)Math.Floor(Percentile * (count - 1)));
		double top = Select(magnitudes.AsSpan(0, count), rank);
		if (!(top > 0.0))
		{
			return null;
		}
		double brightness = Math.Clamp(1.0 / top, 1e-4, 1e5);
		// rounded by decimal places, which gives the nearest double (33.3, not 333 × 0.1 = 33.300000000000004)
		int digits = 2 - (int)Math.Floor(Math.Log10(brightness));
		if (digits >= 0)
		{
			return Math.Round(brightness, digits);
		}
		double unit = Math.Pow(10.0, -digits);
		return Math.Round(brightness / unit) * unit;
	}

	/// <summary>The value of rank <paramref name="rank"/> (0-based, ascending) of <paramref name="values"/>, which it reorders: Hoare's quickselect with a median-of-three pivot.</summary>
	private static float Select(Span<float> values, int rank)
	{
		int lo = 0;
		int hi = values.Length - 1;
		while (lo < hi)
		{
			int mid = lo + (hi - lo) / 2;
			float a = values[lo], b = values[mid], c = values[hi];
			float pivot = a < b ? (b < c ? b : a < c ? c : a) : (a < c ? a : b < c ? c : b);
			int i = lo;
			int j = hi;
			while (i <= j)
			{
				while (values[i] < pivot)
				{
					i++;
				}
				while (values[j] > pivot)
				{
					j--;
				}
				if (i <= j)
				{
					(values[i], values[j]) = (values[j], values[i]);
					i++;
					j--;
				}
			}
			if (rank <= j)
			{
				hi = j;
			}
			else if (rank >= i)
			{
				lo = i;
			}
			else
			{
				return values[rank];
			}
		}
		return values[lo];
	}
}
