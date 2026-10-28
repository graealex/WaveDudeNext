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
/// The export commands, bound on the main window (handlers in <c>MainWindow.Export.cs</c>) and offered in
/// File > Export and on the pictures' context menus. Each opens the Save dialog for the format and name,
/// then renders the export range of the current run (started first when there is none or it is stale)
/// with the display settings as they are; the frame commands write the frame the preview shows, from the
/// same run, as one PNG or JPEG. The shortcuts (2026-10-28, the user's choice, GIMP's pair for the first two: Ctrl+E
/// exports to the last file again without asking, Ctrl+Shift+E opens the dialog) are Ctrl+E for <see cref="Last"/>,
/// Ctrl+Shift+E, I and B for the field, the intensity and both, Ctrl+Shift+F and G for the two frames.
/// </summary>
public static class ExportCommands
{
	/// <summary>Exports the field pictures (Ctrl+Shift+E).</summary>
	public static readonly RoutedUICommand Field = new("Export field", nameof(Field), typeof(ExportCommands),
		[new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Shift)]);

	/// <summary>Exports the intensity pictures (Ctrl+Shift+I).</summary>
	public static readonly RoutedUICommand Intensity = new("Export intensity", nameof(Intensity), typeof(ExportCommands),
		[new KeyGesture(Key.I, ModifierKeys.Control | ModifierKeys.Shift)]);

	/// <summary>Exports both, as two outputs named <c>_field</c> and <c>_intensity</c> after the chosen name (Ctrl+Shift+B).</summary>
	public static readonly RoutedUICommand Both = new("Export both", nameof(Both), typeof(ExportCommands),
		[new KeyGesture(Key.B, ModifierKeys.Control | ModifierKeys.Shift)]);

	/// <summary>Exports the field frame the preview shows as one image (Ctrl+Shift+F).</summary>
	public static readonly RoutedUICommand FieldFrame = new("Export field frame", nameof(FieldFrame), typeof(ExportCommands),
		[new KeyGesture(Key.F, ModifierKeys.Control | ModifierKeys.Shift)]);

	/// <summary>Exports the intensity frame the preview shows as one image (Ctrl+Shift+G).</summary>
	public static readonly RoutedUICommand IntensityFrame = new("Export intensity frame", nameof(IntensityFrame), typeof(ExportCommands),
		[new KeyGesture(Key.G, ModifierKeys.Control | ModifierKeys.Shift)]);

	/// <summary>
	/// Repeats this session's last export of a run's pictures (Ctrl+E, 2026-10-28, the user's request): the same layers,
	/// format and path, no dialog, and what is there is replaced without a question. A still does not count (the user's
	/// choice); nothing to repeat until the first such export since a project was opened, when the command is disabled.
	/// </summary>
	public static readonly RoutedUICommand Last = new("Export last", nameof(Last), typeof(ExportCommands),
		[new KeyGesture(Key.E, ModifierKeys.Control)]);

	/// <summary>
	/// Writes a probe's recording as a CSV file (2026-10-18); the parameter is the <see cref="Model.Probe"/>.
	/// Offered on the probe's node in the project tree and on its graph box. Like the pictures it exports the
	/// full run, started first when there is none or it is stale.
	/// </summary>
	public static readonly RoutedUICommand ProbeCsv = new("Export probe data", nameof(ProbeCsv), typeof(ExportCommands));
}
