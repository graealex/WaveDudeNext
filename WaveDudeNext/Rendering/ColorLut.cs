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
/// The colours a renderer takes from a <see cref="ColorScheme"/>'s gradient, with the display options of the Python
/// function <c>get_colormap_lut</c> (invert, black level, make symmetric) applied, for the two kinds of stored frame
/// (2026-10-23, the user's design: the scheme is the gradient, how many colours are taken from it is the renderer's
/// business):
/// <list type="bullet">
/// <item>a frame of 8-bit palette indices: <see cref="Argb"/>, <see cref="Size"/> colours at i / 254 like the author's
/// table, looked up by the byte the capture computed (<see cref="FieldIndex"/>, <see cref="IntensityIndex(float, float)"/>,
/// the GPU's <c>capture_index</c>), channels truncated like the Python code;</item>
/// <item>a frame of 16-bit samples: <see cref="HalfTable"/>, a colour for each of the 65,536 values a half can hold, the
/// gradient evaluated at that value's own position and rounded to the nearest level, so a picture has every level the
/// gradient passes through (the 255 entries had left steps of two to five levels in dark gradients, seen as patches).</item>
/// </list>
/// Built on the UI thread from a copy of the stops, then immutable apart from the cache of half tables: read from the
/// presenter's and the export's threads without locking.
/// </summary>
public sealed class ColorLut
{
	/// <summary>Number of entries in the 8-bit palette, index 0..254: the author's table, and the range of the captured index bytes.</summary>
	public const int Size = 255;

	/// <summary>Number of values a 16-bit sample can hold: the length of a <see cref="HalfTable"/>.</summary>
	public const int HalfValues = 1 << 16;

	private readonly List<ColorStop> _stops;
	private readonly bool _invert;
	private readonly double _blackLevel;
	private readonly bool _makeSymmetric;
	private readonly bool _reverse;
	/// <summary>The half tables built, one per <see cref="SampleKind"/>.</summary>
	private readonly TableEntry<int>?[] _tables = new TableEntry<int>?[3];
	/// <summary>The 16-bit half tables built, one per <see cref="SampleKind"/>.</summary>
	private readonly TableEntry<ulong>?[] _deepTables = new TableEntry<ulong>?[3];

	/// <summary>A built half table and the brightness it was built for.</summary>
	private sealed record TableEntry<T>(float Brightness, T[] Table);

	private ColorLut(List<ColorStop> stops, bool invert, double blackLevel, bool makeSymmetric, bool reverse)
	{
		_stops = stops;
		_invert = invert;
		_blackLevel = blackLevel;
		_makeSymmetric = makeSymmetric;
		_reverse = reverse;
		Argb = BuildPalette();
	}

	/// <summary>
	/// The 8-bit palette as packed opaque ARGB ints (<c>0xFFRRGGBB</c>, the memory order of BGRA32 pixels), indices
	/// 0..<see cref="Size"/>-1. Returns the internal array, not a copy; callers must not modify it.
	/// </summary>
	public int[] Argb { get; }

	/// <summary>Returns palette entry <paramref name="index"/> (0..<see cref="Size"/>-1) as a colour.</summary>
	public Rgb ColorAt(int index) => Rgb.FromArgb(Argb[index]);

	/// <summary>
	/// The grey levels the Python code shows without a colour table: palette entry i is the level i, 0..254, and a
	/// 16-bit sample the same ramp at its own position. Used when a layer's scheme cannot be found.
	/// </summary>
	public static ColorLut Grey { get; } = new([new ColorStop(0.0, new Rgb(0, 0, 0)), new ColorStop(1.0, new Rgb(254, 254, 254))], false, 0.0, false, false);

	/// <summary>
	/// The colours of <paramref name="scheme"/>'s gradient with the display options, applied in the Python order:
	/// <paramref name="reverse"/> (new, optional; the gradient read from the far end), <paramref name="invert"/>
	/// (each channel becomes <c>1 - channel</c>), <paramref name="makeSymmetric"/> (the gradient squeezed into each half
	/// and mirrored, palette entry i read from entry <c>2 * min(i, Size-1-i)</c>), then <paramref name="blackLevel"/>,
	/// which maps every channel linearly as <c>c * (1 - b) + b</c>, so 0 becomes <paramref name="blackLevel"/> and 1
	/// stays 1. The palette's channels are converted to 8 bit by truncation, like the Python code; alpha is always 255.
	/// UI thread (the scheme's stops are copied).
	/// </summary>
	/// <param name="scheme">The gradient.</param>
	/// <param name="invert">Replaces every colour by its complement (1 - colour), like the Python option.</param>
	/// <param name="blackLevel">Lifts (positive) or lowers (negative) the darkest level; 0 = unchanged.</param>
	/// <param name="makeSymmetric">Squeezes the whole scheme into each half and mirrors it around the centre.</param>
	/// <param name="reverse">Reverses the order of the scheme (like matplotlib's <c>_r</c> maps).</param>
	public static ColorLut Build(ColorScheme scheme, bool invert = false, double blackLevel = 0.0, bool makeSymmetric = false, bool reverse = false) =>
		new(scheme.SortedStops(), invert, blackLevel, makeSymmetric, reverse);

	/// <summary>The 8-bit palette: the author's table of the gradient (<see cref="ColorScheme.Interpolate"/>, 255 entries at i / 254) through the display options.</summary>
	private int[] BuildPalette()
	{
		Rgb[] table = ColorScheme.Interpolate(_stops, Size);
		double[,] rgb = new double[Size, 3];
		for (int i = 0; i < Size; i++)
		{
			Rgb entry = table[_reverse ? (Size - 1 - i) : i];
			rgb[i, 0] = entry.R / 255.0;
			rgb[i, 1] = entry.G / 255.0;
			rgb[i, 2] = entry.B / 255.0;
		}
		if (_invert)
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
			int sourceIndex = _makeSymmetric ? 2 * Math.Min(i, Size - 1 - i) : i;
			int[] components = new int[3];
			for (int channel = 0; channel < 3; channel++)
			{
				double level = Math.Clamp(rgb[sourceIndex, channel] * (1.0 - _blackLevel) + _blackLevel, 0.0, 1.0);
				components[channel] = Math.Min(255, (int)(level * 255.0));
			}
			argb[i] = new Rgb((byte)components[0], (byte)components[1], (byte)components[2]).ToArgb();
		}
		return argb;
	}

	/// <summary>
	/// The colour of every value a 16-bit sample can hold, indexed by the half's bits (<see cref="BitConverter.HalfToUInt16Bits"/>):
	/// the value's position on the gradient by the sample kind's mapping at <paramref name="brightness"/>, the continuous
	/// forms of the palette indices (the field <c>(clamp(value · brightness, −1, 1) + 1) / 2</c>, a zero field and NaN in
	/// the middle; the intensity <c>min(√value · brightness, 1)</c>, its stored square root <c>min(value · brightness, 1)</c>,
	/// both 0 for NaN and for values not above zero), the gradient evaluated there through the display options, each
	/// channel rounded to the nearest level. Kept per kind until a call with another brightness; built on the calling
	/// thread (65,536 evaluations, spread over the pool). Any thread: two threads asking at once may both build it, to the
	/// same colours. The array must not be modified.
	/// </summary>
	/// <param name="kind">What the samples are: the signed field, the intensity, or its square root.</param>
	/// <param name="brightness">The layer's gain, applied as in <see cref="FieldIndex"/> and <see cref="IntensityIndex(float, float)"/>.</param>
	public int[] HalfTable(SampleKind kind, float brightness) =>
		Cached(ref _tables[(int)kind], kind, brightness,
			(r, g, b) => unchecked((int)0xFF000000) | (Round(r * 255.0) << 16) | (Round(g * 255.0) << 8) | Round(b * 255.0));

	/// <summary>
	/// The colours of <see cref="HalfTable"/> at 16 bits per channel, for the preview's dithering (2026-10-23): each channel
	/// as the level times 256, 0..65,280 (<c>round(level · 255 · 256)</c>: the 8-bit level in the high byte, the fraction of
	/// the next level in the low one), packed as <c>R &lt;&lt; 32 | G &lt;&lt; 16 | B</c>. Kept and built like the 8-bit table.
	/// </summary>
	/// <param name="kind">What the samples are: the signed field, the intensity, or its square root.</param>
	/// <param name="brightness">The layer's gain.</param>
	public ulong[] DeepHalfTable(SampleKind kind, float brightness) =>
		Cached(ref _deepTables[(int)kind], kind, brightness,
			(r, g, b) => ((ulong)(uint)Round(r * 65280.0) << 32) | ((ulong)(uint)Round(g * 65280.0) << 16) | (uint)Round(b * 65280.0));

	/// <summary>The table in <paramref name="slot"/> when it was built for this brightness, else a new one, kept there.</summary>
	private T[] Cached<T>(ref TableEntry<T>? slot, SampleKind kind, float brightness, Func<double, double, double, T> pack)
	{
		TableEntry<T>? cached = Volatile.Read(ref slot);
		if (cached != null && cached.Brightness.Equals(brightness))
		{
			return cached.Table;
		}
		var table = new T[HalfValues];
		const int Blocks = 16;
		Parallel.For(0, Blocks, block =>
		{
			for (int bits = block * (HalfValues / Blocks); bits < (block + 1) * (HalfValues / Blocks); bits++)
			{
				double value = (double)BitConverter.UInt16BitsToHalf((ushort)bits);
				(double r, double g, double b) = LevelsAt(kind switch
				{
					SampleKind.Field => FieldPosition(value, brightness),
					SampleKind.Intensity => IntensityPosition(value > 0.0 ? Math.Sqrt(value) : 0.0, brightness),
					_ => IntensityPosition(value, brightness),
				});
				table[bits] = pack(r, g, b);
			}
		});
		Volatile.Write(ref slot, new TableEntry<T>(brightness, table));
		return table;
	}

	private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

	/// <summary>A field sample's position on the gradient: <c>(clamp(value · brightness, −1, 1) + 1) / 2</c>, NaN in the middle.</summary>
	private static double FieldPosition(double value, float brightness)
	{
		double scaled = value * brightness;
		return double.IsNaN(scaled) ? 0.5 : (Math.Clamp(scaled, -1.0, 1.0) + 1.0) / 2.0;
	}

	/// <summary>An intensity's position on the gradient from its square root: <c>min(root · brightness, 1)</c>, 0 for NaN and for values not above zero.</summary>
	private static double IntensityPosition(double root, float brightness) =>
		root > 0.0 ? Math.Clamp(root * brightness, 0.0, 1.0) : 0.0;

	/// <summary>The gradient at a position through the display options (the order of <see cref="Build"/>): each channel 0..1, unrounded.</summary>
	private (double R, double G, double B) LevelsAt(double position)
	{
		if (_makeSymmetric)
		{
			position = 2.0 * Math.Min(position, 1.0 - position);
		}
		if (_reverse)
		{
			position = 1.0 - position;
		}
		(double r, double g, double b) = ColorScheme.Evaluate(_stops, position);
		return (Level(r), Level(g), Level(b));
	}

	/// <summary>One channel (0..255, unrounded) through invert and black level, as a fraction 0..1.</summary>
	private double Level(double channel)
	{
		double level = channel / 255.0;
		if (_invert)
		{
			level = 1.0 - level;
		}
		return Math.Clamp(level * (1.0 - _blackLevel) + _blackLevel, 0.0, 1.0);
	}

	/// <summary>
	/// Maps a signed field sample to a palette index, like <c>render_field</c>:
	/// <c>clip(value * brightness, -1, 1) * 127 + 127</c>, so the result is 0..254 and a zero
	/// field gives index 127 (the centre colour). NaN gives 127. The index an 8-bit frame stores: computed by the
	/// CPU's capture (<see cref="Engine.FrameShape"/>) and mirrored by the GPU's <c>capture_index</c> kernel.
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
	/// The palette index of an intensity's square root, the index an 8-bit intensity frame stores (2026-10-23, the user's
	/// choice: the index of the very half a 16-bit frame stores, so both previews start from one value, and the darkest
	/// indices stay right at a high brightness, where the intensity rounded to a half had merged them):
	/// <c>min(root · brightness, 1) · 254</c>, 0 for NaN or a root not above zero.
	/// </summary>
	/// <param name="root">The intensity's square root, rounded to a half by the caller.</param>
	/// <param name="brightness">Gain applied to the root.</param>
	public static int IntensityRootIndex(float root, float brightness) =>
		root > 0f ? (int)(MathF.Min(root * brightness, 1f) * 254f) : 0;

	/// <summary>
	/// The square-root case of <see cref="IntensityIndex(float, float, float)"/>: <c>min(sqrt(intensity) · brightness, 1) · 254</c>,
	/// 0 for NaN or a non-positive intensity; the index of a frame whose 16-bit intensity is I itself (<see cref="Engine.FrameShape.IntensityRoot"/>).
	/// </summary>
	/// <param name="intensity">Accumulated intensity from the engine.</param>
	/// <param name="brightness">Gain applied after the square root.</param>
	public static int IntensityIndex(float intensity, float brightness)
	{
		if (!(intensity > 0f))
		{
			return 0;
		}
		// single precision throughout (2026-10-16): what the GPU's capture_index kernel computes bit for bit; the
		// double arithmetic before differed in the last bit at most
		return (int)(MathF.Min(MathF.Sqrt(intensity) * brightness, 1f) * 254f);
	}

	/// <summary>
	/// Maps a non-negative intensity to a palette index 0..254, like <c>render_intensity</c>:
	/// <c>clip(intensity ^ exponent * brightness, 0, 1) * 254</c>. NaN or non-positive
	/// intensity gives 0. The captures use the square-root case, <see cref="IntensityIndex(float, float)"/>.
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
}
