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

using System.Globalization;
using System.Windows.Data;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// The icon drawing of a lens or mirror by its radii (2026-10-29, the user's design): <c>Lens</c> followed by the shape of each
/// surface, <c>Convex</c>, <c>Flat</c> or <c>Concave</c>, the name of the theme's <c>IconLens{left}{right}</c> geometry. The
/// inputs are <see cref="LensPart.R1"/> and <see cref="LensPart.R2"/>, signed as the model signs them: R1 above 0 and R2 below
/// 0 bulge outward, 0 is flat; the fit never changes a sign. The kind's own symbol, <c>Lens</c>, stands in until both arrive.
/// </summary>
public sealed class LensIconConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
		values is [double r1, double r2] ? "Lens" + Shape(r1) + Shape(-r2) : "Lens";

	/// <summary>The surface's shape for a radius signed so that positive bulges outward.</summary>
	private static string Shape(double outward) => outward > 0.0 ? "Convex" : outward < 0.0 ? "Concave" : "Flat";

	/// <inheritdoc/>
	public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
