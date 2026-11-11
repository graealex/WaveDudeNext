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

using System.Collections.ObjectModel;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.UI;

namespace WaveDudeNext.App;

/// <summary>
/// The window's hub for the project's edits, as the WPF window has it: the document reports every change
/// (<see cref="ProjectDocument.Changed"/>) and the hub tells each model what the change means to it: the undo history, the
/// scene image's watch, the preview's colours, and the live-editing rules (<see cref="LivePreview"/>: stale run, scratch
/// frame, automatic re-run).
/// </summary>
public partial class MainWindow
{
	private void OnProjectChanged(object? sender, ProjectChange change)
	{
		_history.EditMade();
		bool sceneReplaced = change.Item is Project && change.Property == nameof(Project.Scene);
		if (change.Item is Scene && change.Property is nameof(Scene.ImagePath) or nameof(Scene.ImageData) || sceneReplaced)
		{
			// a new image file to watch (Browse, a reload, a path made relative, a scene written back by Undo or Redo)
			SceneImage.Watch();
		}
		if (change.Item is ColorScheme or ObservableCollection<ColorScheme>)
		{
			// a project scheme edited, added or removed: recolour the frame shown. Not while Undo or Redo writes a step
			// back, which empties the scheme list before it refills it: the colours are asked for once, when the project
			// is whole again (the history's Restored, wired in the constructor)
			if (!_history.IsRestoring)
			{
				Preview.RefreshColors();
			}
			return;
		}
		Live.Edited(change);
	}
}
