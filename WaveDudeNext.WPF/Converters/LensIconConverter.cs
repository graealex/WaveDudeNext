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
/// The icon drawing of a lens or mirror by its profile (2026-10-29, the user's design): <c>Lens</c> followed by the shape of
/// each surface as <see cref="LensPart.ShapeOf"/> reads it, <c>Convex</c>, <c>Flat</c> or <c>Concave</c>, the name of the
/// theme's <c>IconLens{left}{right}</c> geometry. The inputs are <see cref="LensPart.R1"/>, <see cref="LensPart.R2"/> and
/// <see cref="LensPart.Diameter"/>: R1 above 0 and R2 below 0 bow outward, and a surface whose sag at the rim is under half a
/// pixel is flat whatever its sign (the user's report: Example 1's mirror of radii 14401 on 104 px showed as biconvex). The
/// biconvex profile stands in until all three arrive (the kind's own symbol, <c>Lens</c>, is the Add part menu's
/// lens-and-mirror pair, which a row never shows).
/// </summary>
public sealed class LensIconConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
		values is [double r1, double r2, int diameter] ? "Lens" + LensPart.ShapeOf(r1, diameter) + LensPart.ShapeOf(-r2, diameter) : "LensConvexConvex";

	/// <inheritdoc/>
	public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
