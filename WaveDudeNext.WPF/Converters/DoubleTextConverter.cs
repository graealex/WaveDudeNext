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
/// Binds the text of an editable field to a <see cref="double"/> without the binding engine's
/// exceptions: text that does not parse yet (empty, a lone minus, "1.") leaves the source alone
/// instead of logging a conversion error on every keystroke. Both directions use the culture
/// the binding passes in, which <c>App.OnStartup</c> makes the user's culture for every element,
/// so the text agrees with how the preset list shows the same values.
/// </summary>
public sealed class DoubleTextConverter : IValueConverter
{
	/// <inheritdoc/>
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is double d ? d.ToString(culture) : value?.ToString();

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		double.TryParse(value as string, NumberStyles.Float, culture, out double d) ? d : Binding.DoNothing;
}
