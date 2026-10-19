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

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// The simulation step a frame shows: frame index × visualisation interval (the two inputs in
/// that order), as text in the given culture. One way, for readouts: a multi-binding does not
/// convert an int to a string target on its own.
/// </summary>
public sealed class FrameToStepConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
		(values.Length == 2 && values[0] is int frame && values[1] is int every ? frame * every : 0).ToString(culture);

	/// <inheritdoc/>
	public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
