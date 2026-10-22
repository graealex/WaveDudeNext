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
/// The icon drawing of an optical part (2026-10-23): its class name without "Part" (a <see cref="LensPart"/> is "Lens", an
/// <see cref="XorGatePart"/> "XorGate"), the name of the theme's <c>Icon{name}</c> geometry the Add part menu shows too.
/// </summary>
public sealed class PartIconConverter : IValueConverter
{
	/// <inheritdoc/>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is OpticalPart part ? part.GetType().Name.Replace("Part", "", StringComparison.Ordinal) : null;

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
