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
using System.Windows;
using System.Windows.Data;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// A folder's item count as words with the right plural (2026-10-15, after "1 entries"): the string table's
/// <c>ItemCountZero</c> ("No entries"), <c>ItemCountOne</c> ("1 entry") or <c>ItemCountMany</c> ("{0} entries",
/// the count formatted in the current culture). A multi-value converter whose first input is the count and
/// whose second, the current language, only serves to re-read the words on a language switch.
/// </summary>
public sealed class ItemCountConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Length == 0 || values[0] is not int count)
		{
			return null;
		}
		string key = count switch
		{
			0 => "ItemCountZero",
			1 => "ItemCountOne",
			_ => "ItemCountMany",
		};
		string format = Application.Current?.TryFindResource(key) as string ?? "{0}";
		return string.Format(culture, format, count);
	}

	/// <inheritdoc/>
	public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
