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
/// The Edit menu's own commands, bound on the main window: undo and redo of project edits (handlers in
/// <c>MainWindow.History.cs</c>), with the usual shortcuts, and Delete (<c>MainWindow.Project.cs</c>). A text box being typed
/// in keeps its own Ctrl+Z for its text; Cut, Copy and Paste are WPF's <see cref="ApplicationCommands"/>, which text boxes handle.
/// </summary>
public static class EditCommands
{
	/// <summary>
	/// Deletes the selected text in the text box that has the focus, else removes the selected sources, parts, probes or
	/// schemes as the tree's Remove does. No gesture of its own: Del is the text box's and the tree's.
	/// </summary>
	public static readonly RoutedUICommand Delete = new("Delete", nameof(Delete), typeof(EditCommands));

	/// <summary>Takes the project back one step. Ctrl+Z.</summary>
	public static readonly RoutedUICommand Undo = new("Undo", nameof(Undo), typeof(EditCommands),
		[new KeyGesture(Key.Z, ModifierKeys.Control)]);

	/// <summary>Takes the project forward again one undone step. Ctrl+Y or Ctrl+Shift+Z.</summary>
	public static readonly RoutedUICommand Redo = new("Redo", nameof(Redo), typeof(EditCommands),
		[new KeyGesture(Key.Y, ModifierKeys.Control), new KeyGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Shift)]);
}
