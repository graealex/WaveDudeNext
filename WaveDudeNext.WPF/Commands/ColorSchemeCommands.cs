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

using System.Windows.Input;

namespace WaveDudeNext.WPF.Commands;

/// <summary>Commands of the colour scheme editor (<c>Views/ColorSchemeView</c>), bound on the view itself.</summary>
public static class ColorSchemeCommands
{
	/// <summary>Adds a stop in the widest gap (<see cref="WaveDudeNext.Rendering.ColorScheme.AddStop"/>).</summary>
	public static readonly RoutedUICommand AddStop = new("Add stop", nameof(AddStop), typeof(ColorSchemeCommands));

	/// <summary>Removes the stop given as parameter; the last stop stays.</summary>
	public static readonly RoutedUICommand RemoveStop = new("Remove stop", nameof(RemoveStop), typeof(ColorSchemeCommands));
}
