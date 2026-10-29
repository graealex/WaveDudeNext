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
/// Builds the window title from the application name, the open file, the dirty flag and the suggested file name:
/// the non-empty parts joined with " - ", the suggested name standing in for a missing file (an unsaved import or a
/// shipped example, 2026-10-29), "Untitled" when an unsaved project has no name at all (the word is looked up in the
/// current language's strings on every conversion; the window refreshes the binding on a language switch), and an
/// asterisk after it for unsaved edits.
/// </summary>
public sealed class TitleConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
	{
		string title = values.Length > 0 ? values[0]?.ToString() ?? "" : "";
		string file = values.Length > 1 ? values[1]?.ToString() ?? "" : "";
		bool dirty = values.Length > 2 && values[2] is true;
		if (file.Length == 0 && values.Length > 3)
		{
			// an unsaved project that came from somewhere, a shipped example or an import, is named after its file (2026-10-29, the user's request)
			file = values[3]?.ToString() ?? "";
		}
		if (file.Length == 0)
		{
			// a new project reads "Untitled" from the start (2026-10-29, the user's point: the bar was empty until an edit turned it into "Untitled*")
			file = Application.Current?.TryFindResource("Untitled") as string ?? "Untitled";
		}
		if (dirty)
		{
			file += "*";
		}
		return title + " - " + file;
	}

	/// <inheritdoc/>
	public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
