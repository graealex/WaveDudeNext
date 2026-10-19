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
using WaveDudeNext.Rendering;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// The automation id of a Project tree row from its node: "Tree:" plus the item's name (sources, parts, probes,
/// colour schemes), a folder's label, or the type name of the fixed nodes ("Tree:Scene"), so a harness
/// can reach a row with Tab and act on it.
/// </summary>
public sealed class TreeIdConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => "Tree:" + value switch
	{
		Source source => source.Name,
		OpticalPart part => part.Name,
		Probe probe => probe.Name,
		ColorScheme scheme => scheme.Name,
		NamedCollection folder => folder.Key,
		null => "",
		_ => value.GetType().Name,
	};

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
