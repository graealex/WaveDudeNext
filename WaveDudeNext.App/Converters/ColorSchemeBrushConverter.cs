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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// A colour scheme (or its stop list, or a plain colour list) as a horizontal gradient brush, the preview strip of
/// the picker, the tree and the editor: every stop becomes a gradient stop at its position when there are up to 64,
/// else 64 samples of the gradient. Null gives a transparent brush. The brush is immutable (WPF froze it).
/// </summary>
public sealed class ColorSchemeBrushConverter : IValueConverter
{
	private const int MaxStops = 64;

	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		IReadOnlyList<ColorStop>? stops = value switch
		{
			ColorScheme scheme => scheme.Stops,
			IReadOnlyList<ColorStop> list => list,
			IReadOnlyList<Rgb> colors => colors.Select((c, k) => new ColorStop(colors.Count == 1 ? 0.0 : k / (double)(colors.Count - 1), c)).ToList(),
			_ => null,
		};
		return stops == null || stops.Count == 0 ? Brushes.Transparent : BrushOf(stops);
	}

	/// <summary>The horizontal gradient brush of these stops (at least one, in any order), immutable; the editor's gradient bar draws with it.</summary>
	public static IBrush BrushOf(IReadOnlyList<ColorStop> stops)
	{
		var brush = new LinearGradientBrush
		{
			StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
			EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
		};
		if (stops.Count <= MaxStops)
		{
			foreach (ColorStop stop in stops.OrderBy(s => s.Position))
			{
				brush.GradientStops.Add(new GradientStop(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B), Math.Clamp(stop.Position, 0.0, 1.0)));
			}
		}
		else
		{
			Rgb[] samples = ColorScheme.Interpolate(stops, MaxStops);
			for (int i = 0; i < samples.Length; i++)
			{
				brush.GradientStops.Add(new GradientStop(Color.FromRgb(samples[i].R, samples[i].G, samples[i].B), i / (double)(samples.Length - 1)));
			}
		}
		return brush.ToImmutable();
	}

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
