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
/// An immutable palette of <see cref="Size"/> opaque ARGB entries built from a
/// <see cref="ColorScheme"/> by <see cref="Build"/> plus the display options of the Python
/// function <c>get_colormap_lut</c> (invert, black level, make symmetric), and the static
/// value-to-index mappings used when colouring frames, which convert field and intensity
/// values to table indices exactly like <c>WaveVisualizer.render_field</c> /
/// <c>render_intensity</c>. Built on the UI thread when the display settings change; the
/// <see cref="Argb"/> array is then read from <see cref="FrameRenderer"/>'s parallel loops without
/// locking (it is never modified after construction).
/// </summary>
/// <remarks>
/// The options belong to how a scheme is used (field or intensity), not to the scheme itself, so
/// they are not saved in the scheme file; they live in <see cref="Model.DisplayLayer"/>.
/// </remarks>
public sealed class ColorLut
{
	private ColorLut(int[] argb) => Argb = argb;

	/// <summary>
	/// The palette as packed opaque ARGB ints (<c>0xFFRRGGBB</c>, the memory order of BGRA32 pixels),
	/// indices 0..<see cref="Size"/>-1. Returns the internal array, not a copy; callers must not modify it.
	/// </summary>
	public int[] Argb { get; }

	/// <summary>Returns entry <paramref name="index"/> (0..<see cref="Size"/>-1) as a colour.</summary>
	public Rgb ColorAt(int index) => Rgb.FromArgb(Argb[index]);

	/// <summary>
	/// Builds a palette from the <see cref="Size"/> entries of <paramref name="scheme"/>.
	/// Transformations are applied in the same order as in Python: <paramref name="reverse"/>
	/// (new, optional; entry i is read from entry <c>Size-1-i</c>), <paramref name="invert"/>
	/// (each channel becomes <c>1 - channel</c>), <paramref name="makeSymmetric"/> (entry i is
	/// read from entry <c>2 * min(i, Size-1-i)</c>, so the palette runs from entry 0 at both
	/// ends up to entry 254 in the middle), then <paramref name="blackLevel"/> maps every
	/// channel linearly as <c>c * (1 - b) + b</c>, so 0 becomes <paramref name="blackLevel"/>
	/// and 1 stays 1. Values are limited to 0..1 and converted to 8 bit by truncation, like
	/// the Python code. Alpha is always 255.
	/// </summary>
	/// <param name="scheme">Source colours; must have at least <see cref="Size"/> entries.</param>
	/// <param name="invert">Replaces every colour by its complement (1 - colour), like the Python option.</param>
	/// <param name="blackLevel">Lifts (positive) or lowers (negative) the darkest level; 0 = unchanged.</param>
	/// <param name="makeSymmetric">Squeezes the whole scheme into each half and mirrors it around the centre.</param>
	/// <param name="reverse">Reverses the order of the scheme (like matplotlib's <c>_r</c> maps).</param>
	public static ColorLut Build(ColorScheme scheme, bool invert = false, double blackLevel = 0.0, bool makeSymmetric = false, bool reverse = false)
	{
		double[,] rgb = new double[Size, 3];
		for (int i = 0; i < Size; i++)
		{
			Rgb entry = scheme.GetEntry(reverse ? (Size - 1 - i) : i);
			rgb[i, 0] = entry.R / 255.0;
			rgb[i, 1] = entry.G / 255.0;
			rgb[i, 2] = entry.B / 255.0;
		}
		if (invert)
		{
			for (int i = 0; i < Size; i++)
			{
				for (int channel = 0; channel < 3; channel++)
				{
					rgb[i, channel] = 1.0 - rgb[i, channel];
				}
			}
		}
		int[] argb = new int[Size];
		for (int i = 0; i < Size; i++)
		{
			// Python: entries 0..127 = src[0::2] and entries 127..254 = the same list mirrored,
			// which is entry i = src[2 * min(i, 254 - i)]
			int sourceIndex = makeSymmetric ? 2 * Math.Min(i, Size - 1 - i) : i;
			int[] components = new int[3];
			for (int channel = 0; channel < 3; channel++)
			{
				double level = Math.Clamp(rgb[sourceIndex, channel] * (1.0 - blackLevel) + blackLevel, 0.0, 1.0);
				components[channel] = Math.Min(255, (int)(level * 255.0));
			}
			argb[i] = new Rgb((byte)components[0], (byte)components[1], (byte)components[2]).ToArgb();
		}
		return new ColorLut(argb);
	}

	/// <summary>
	/// Maps a signed field sample to a palette index, like <c>render_field</c>:
	/// <c>clip(value * brightness, -1, 1) * 127 + 127</c>, so the result is 0..254 and a zero
	/// field gives index 127 (the centre colour). NaN gives 127. Called per pixel from
	/// <see cref="FrameRenderer.RenderField"/> on worker threads.
	/// </summary>
	/// <param name="value">Raw field amplitude from the engine.</param>
	/// <param name="brightness">Gain applied before clamping.</param>
	public static int FieldIndex(float value, float brightness)
	{
		float scaled = value * brightness;
		if (float.IsNaN(scaled))
		{
			return 127;
		}
		return (int)(Math.Clamp(scaled, -1f, 1f) * 127f + 127f);
	}

	/// <summary>
	/// The square-root case of <see cref="IntensityIndex(float, float, float)"/>, the arithmetic
	/// <see cref="FrameRenderer.RenderIntensity"/> uses per pixel: <c>min(sqrt(intensity) · brightness, 1) · 254</c>,
	/// 0 for NaN or a non-positive intensity. Also the mapping baked into a <see cref="Engine.FrameFormat.Index"/> frame.
	/// </summary>
	/// <param name="intensity">Accumulated intensity from the engine.</param>
	/// <param name="brightness">Gain applied after the square root.</param>
	public static int IntensityIndex(float intensity, float brightness)
	{
		if (!(intensity > 0f))
		{
			return 0;
		}
		// single precision throughout (2026-10-16): what the GPU's capture_index kernel computes and what the
		// vectorised renderer reproduces bit for bit; the double arithmetic before differed in the last bit at most
		return (int)(MathF.Min(MathF.Sqrt(intensity) * brightness, 1f) * 254f);
	}

	/// <summary>
	/// Maps a non-negative intensity to a palette index 0..254, like <c>render_intensity</c>:
	/// <c>clip(intensity ^ exponent * brightness, 0, 1) * 254</c>. NaN or non-positive
	/// intensity gives 0. <see cref="FrameRenderer.RenderIntensity"/> inlines the square-root case.
	/// </summary>
	/// <param name="intensity">Accumulated intensity from the engine.</param>
	/// <param name="brightness">Gain applied after compression.</param>
	/// <param name="exponent">Compression exponent; 0.5 (square root) is the Python default.</param>
	public static int IntensityIndex(float intensity, float brightness, float exponent = 0.5f)
	{
		if (float.IsNaN(intensity) || intensity <= 0f)
		{
			return 0;
		}
		double scaled = Math.Pow(intensity, exponent) * brightness;
		if (double.IsNaN(scaled) || scaled < 0.0)
		{
			return 0;
		}
		return (int)(Math.Min(scaled, 1.0) * 254.0);
	}

	/// <summary>Number of entries in a palette: <see cref="ColorScheme.EntryCount"/> (255).</summary>
	public const int Size = ColorScheme.EntryCount;
}
