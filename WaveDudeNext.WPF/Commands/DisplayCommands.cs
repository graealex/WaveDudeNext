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

/// <summary>
/// The pictures' brightness (2026-10-20, Jeroen's point 9: it sat with his previews, here it took a search through
/// the project tree): from the box in each picture's header and the picture's context menu. The parameter is the
/// picture's <see cref="Model.ProbeLayer"/> (the cell's <c>PreviewPane.Layer</c>).
/// </summary>
public static class DisplayCommands
{
	/// <summary>Fits the layer's brightness once to the frame shown (handled by the main window, <c>MainWindow.Display.cs</c>).</summary>
	public static readonly RoutedUICommand AutoBrightness = new("Auto brightness", nameof(AutoBrightness), typeof(DisplayCommands));

	/// <summary>Moves the keyboard focus to the brightness box in the picture's header (handled by the preview pane).</summary>
	public static readonly RoutedUICommand EditBrightness = new("Brightness", nameof(EditBrightness), typeof(DisplayCommands));
}
