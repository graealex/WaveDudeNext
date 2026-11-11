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
using Avalonia.Controls;
using Avalonia.Data.Converters;
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// A tree row's name for UI Automation, the text the row shows (2026-11-11, so the harnesses read a row's text as they do
/// from the WPF head, whose rows take their header's text by themselves): a named item's name, a folder's label, the
/// string table's text for the project and its three settings rows. A multi-value converter whose inputs are the row's
/// item, its <c>Name</c> (unset for an item without one) and the current language, so the text follows a rename and a
/// language switch.
/// </summary>
public sealed class TreeNameConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Count == 0)
		{
			return null;
		}
		return values[0] switch
		{
			INamed named => values.Count > 1 && values[1] is string name ? name : named.Name,
			NamedCollection folder => folder.Label,
			Project => Text("TreeProject"),
			Scene => Text("TreeScene"),
			Simulation => Text("TreeSimulation"),
			Display => Text("TreeDisplay"),
			null => null,
			var other => other.GetType().Name,
		};
	}

	private static string Text(string key) =>
		Avalonia.Application.Current?.TryGetResource(key, null, out object? value) == true && value is string text ? text : key;
}
