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
using Avalonia.Controls;
using Avalonia.Data.Converters;
using WaveDudeNext.App.Localization;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// A language entry's text for the View > Language menu: a string table's language in its own name, the "System"
/// entry's from the string table in use (<c>LanguageSystem</c>), so it follows a switch. A multi-value converter whose
/// inputs are the <see cref="LanguageOption"/> and the current language (2026-11-11: the entries had been a fresh list
/// per change, which re-created the open menu's items under the one being clicked, so Avalonia's handler, which closes
/// the menu by the clicked item's parent after the click, found none and the menu stayed open).
/// </summary>
public sealed class LanguageNameConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Count == 0 || values[0] is not LanguageOption option)
		{
			return null;
		}
		return option.Code.Length > 0 ? option.Name
			: Application.Current?.TryGetResource("LanguageSystem", null, out object? value) == true && value is string text ? text
			: option.Name;
	}
}
