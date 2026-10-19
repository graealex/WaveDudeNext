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

using System.ComponentModel;
using System.Globalization;

namespace WaveDudeNext.Rendering;

/// <summary>
/// One 8-bit RGB colour. Replaces <c>System.Drawing.Color</c> in the library so that nothing here
/// depends on GDI+. Written and read as <c>#RRGGBB</c> (the form every colour takes in XML and in
/// the editors, 2026-10-13); <see cref="TryParse"/> also accepts <c>RRGGBB</c>, <c>#RGB</c> and the
/// author's JSON triplet <c>[r,g,b]</c>. The <see cref="TypeConverter"/> lets WPF bind a text box
/// straight to an <see cref="Rgb"/>.
/// </summary>
[TypeConverter(typeof(RgbConverter))]
public readonly record struct Rgb(byte R, byte G, byte B)
{
	/// <summary>Packed <c>0xFFRRGGBB</c>, which is also BGRA32 memory order on a little-endian machine.</summary>
	public int ToArgb() => unchecked((int)0xFF000000) | (R << 16) | (G << 8) | B;

	/// <summary>The colour of a packed <c>0xAARRGGBB</c> value (alpha ignored).</summary>
	public static Rgb FromArgb(int argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

	/// <summary>Builds a colour from integer channels, each clamped to 0..255.</summary>
	public static Rgb Clamped(int r, int g, int b) => new((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));

	/// <summary><c>#RRGGBB</c>, upper case.</summary>
	public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

	/// <summary>
	/// Hue in degrees (0 to 360, 0 for a grey), saturation and value (0 to 1) of this colour: the HSV model the
	/// colour picker works in (2026-10-15). <see cref="FromHsv"/> gives the colour back exactly for every 8-bit colour.
	/// </summary>
	public (double Hue, double Saturation, double Value) ToHsv()
	{
		double r = R / 255.0, g = G / 255.0, b = B / 255.0;
		double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
		double hue = 0;
		if (delta > 0)
		{
			hue = 60 * (max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4);
			if (hue < 0)
			{
				hue += 360;
			}
		}
		return (hue, max > 0 ? delta / max : 0, max);
	}

	/// <summary>
	/// The colour of a hue in degrees (wrapped into 0 to 360), a saturation and a value (both clamped to 0 to 1),
	/// each channel rounded to 8 bits.
	/// </summary>
	public static Rgb FromHsv(double hue, double saturation, double value)
	{
		hue = (hue % 360 + 360) % 360;
		saturation = Math.Clamp(saturation, 0, 1);
		value = Math.Clamp(value, 0, 1);
		double c = value * saturation;
		double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
		double m = value - c;
		(double r, double g, double b) = hue switch
		{
			< 60 => (c, x, 0.0),
			< 120 => (x, c, 0.0),
			< 180 => (0.0, c, x),
			< 240 => (0.0, x, c),
			< 300 => (x, 0.0, c),
			_ => (c, 0.0, x),
		};
		return Clamped((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
	}

	/// <summary>Parses <c>#RRGGBB</c>, <c>RRGGBB</c>, <c>#RGB</c> or <c>[r,g,b]</c>.</summary>
	/// <exception cref="FormatException">The text is none of those.</exception>
	public static Rgb Parse(string text) =>
		TryParse(text, out Rgb color) ? color : throw new FormatException($"'{text}' is not a colour; #RRGGBB expected.");

	/// <summary>Tries to parse <c>#RRGGBB</c>, <c>RRGGBB</c>, <c>#RGB</c> or <c>[r,g,b]</c>; false for anything else.</summary>
	public static bool TryParse(string? text, out Rgb color)
	{
		color = default;
		if (text == null)
		{
			return false;
		}
		string s = text.Trim();
		if (s.StartsWith('['))
		{
			string[] parts = s.Trim('[', ']').Split(',');
			if (parts.Length == 3 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
				&& int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g)
				&& int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
			{
				color = Clamped(r, g, b);
				return true;
			}
			return false;
		}
		if (s.StartsWith('#'))
		{
			s = s[1..];
		}
		if (s.Length == 3)
		{
			s = new string([s[0], s[0], s[1], s[1], s[2], s[2]]);
		}
		if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packed))
		{
			return false;
		}
		color = FromArgb(packed);
		return true;
	}
}

/// <summary>Converts between <see cref="Rgb"/> and its <c>#RRGGBB</c> text for bindings and property editors.</summary>
public sealed class RgbConverter : TypeConverter
{
	/// <inheritdoc/>
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
		sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	/// <inheritdoc/>
	public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
		destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

	/// <inheritdoc/>
	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
		value is string text ? Rgb.Parse(text) : base.ConvertFrom(context, culture, value);

	/// <inheritdoc/>
	public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
		destinationType == typeof(string) && value is Rgb color ? color.ToString() : base.ConvertTo(context, culture, value, destinationType);
}
