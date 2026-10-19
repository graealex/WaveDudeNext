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

using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.WPF.Converters;
using WaveDudeNext.WPF.Rendering;

namespace WaveDudeNext.WPF;

/// <summary>
/// Handlers of <see cref="Commands.ProjectCommands"/>: structural edits of the open project.
/// A command parameter names the node to act on; without one the tree selection is used.
/// </summary>
public partial class MainWindow
{
	private object? Target(object? parameter) => parameter ?? SelectedItem;

	/// <summary>The project list a tree node stands for: a folder's list, or the list containing an item.</summary>
	private IList? ListOf(object? node) => node switch
	{
		NamedCollection folder when ReferenceEquals(folder.Items, Project.Sources) => Project.Sources,
		NamedCollection folder when ReferenceEquals(folder.Items, Project.Parts) => Project.Parts,
		NamedCollection folder when ReferenceEquals(folder.Items, Project.Probes) => Project.Probes,
		NamedCollection folder when ReferenceEquals(folder.Items, Project.ColorSchemes) => Project.ColorSchemes,
		Source => Project.Sources,
		OpticalPart => Project.Parts,
		Probe => Project.Probes,
		ColorScheme => Project.ColorSchemes,
		_ => null,
	};

	private void OnAddSource(object sender, ExecutedRoutedEventArgs e) => Added(Project.AddSource());

	/// <summary>
	/// Add part: with a <see cref="PartKind"/> as parameter (the menu entries) the part is appended; without one (the
	/// "+ Part" button, the Parts folder's plus) the invoking element's context menu, the menu of kinds, opens under it,
	/// so the kind is chosen there (keyboard: Space opens it, the arrows walk it, Enter picks).
	/// </summary>
	private void OnAddPart(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is PartKind kind)
		{
			Added(Project.AddPart(kind));
		}
		else
		{
			OpenPartMenu(e.OriginalSource);
		}
	}

	/// <summary>Opens the menu of part kinds attached to <paramref name="element"/> (its context menu), placed under it.</summary>
	private static void OpenPartMenu(object? element)
	{
		if (element is FrameworkElement { ContextMenu: { } menu } anchor)
		{
			menu.PlacementTarget = anchor;
			menu.Placement = PlacementMode.Bottom;
			menu.IsOpen = true;
		}
	}

	private void OnAddProbe(object sender, ExecutedRoutedEventArgs e) => Added(Project.AddProbe());

	private void CanAdd(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Target(e.Parameter) is NamedCollection folder && ListOf(folder) != null;

	private void OnAdd(object sender, ExecutedRoutedEventArgs e)
	{
		IList? list = ListOf(Target(e.Parameter) as NamedCollection);
		if (ReferenceEquals(list, Project.Sources))
		{
			Added(Project.AddSource());
		}
		else if (ReferenceEquals(list, Project.Parts))
		{
			OpenPartMenu(e.OriginalSource);
		}
		else if (ReferenceEquals(list, Project.Probes))
		{
			Added(Project.AddProbe());
		}
		else if (ReferenceEquals(list, Project.ColorSchemes))
		{
			Added(Project.AddColorScheme());
		}
	}

	private void CanEditItem(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Target(e.Parameter) is Source or OpticalPart or Probe or ColorScheme;

	private void CanToggleEnabled(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Target(e.Parameter) is Source or OpticalPart or Probe;

	/// <summary>The tree's check boxes by keyboard: Space on a selected source, part or probe flips it (the author's point 11, 2026-10-13).</summary>
	private void OnToggleEnabled(object sender, ExecutedRoutedEventArgs e)
	{
		switch (Target(e.Parameter))
		{
			case Source source:
				source.Enabled = !source.Enabled;
				break;
			case OpticalPart part:
				part.Enabled = !part.Enabled;
				break;
			case Probe probe:
				probe.Active = !probe.Active;
				break;
		}
	}

	private void OnDuplicate(object sender, ExecutedRoutedEventArgs e)
	{
		switch (Target(e.Parameter))
		{
			case Source source:
				Added(Project.Duplicate(source));
				break;
			case OpticalPart part:
				Added(Project.Duplicate(part));
				break;
			case Probe probe:
				Added(Project.Duplicate(probe));
				break;
			case ColorScheme scheme:
				Added(Project.Duplicate(scheme));
				break;
		}
	}

	private void OnRemove(object sender, ExecutedRoutedEventArgs e)
	{
		object? item = Target(e.Parameter);
		IList? list = ListOf(item);
		if (list == null)
		{
			return;
		}
		int index = list.IndexOf(item);
		switch (item)
		{
			case Source source:
				Project.Remove(source);
				break;
			case OpticalPart part:
				Project.Remove(part);
				break;
			case Probe probe:
				Project.Remove(probe);
				break;
			case ColorScheme scheme:
				Project.Remove(scheme);
				break;
		}
		// keep the user in place: the item that took the slot, else the folder
		if (list.Count > 0)
		{
			projectTree.Select(list[Math.Min(index, list.Count - 1)]!);
		}
		else
		{
			projectTree.SelectFolder(list);
		}
	}

	private void CanMoveUp(object sender, CanExecuteRoutedEventArgs e)
	{
		object? item = Target(e.Parameter);
		e.CanExecute = ListOf(item) is { } list && item is Source or OpticalPart or Probe or ColorScheme && list.IndexOf(item) > 0;
	}

	private void CanMoveDown(object sender, CanExecuteRoutedEventArgs e)
	{
		object? item = Target(e.Parameter);
		e.CanExecute = ListOf(item) is { } list && item is Source or OpticalPart or Probe or ColorScheme && list.IndexOf(item) < list.Count - 1;
	}

	private void OnMoveUp(object sender, ExecutedRoutedEventArgs e) => MoveBy(Target(e.Parameter), -1);

	private void OnMoveDown(object sender, ExecutedRoutedEventArgs e) => MoveBy(Target(e.Parameter), +1);

	private void MoveBy(object? item, int delta)
	{
		IList? list = ListOf(item);
		if (list == null)
		{
			return;
		}
		int index = list.IndexOf(item) + delta;
		bool moved = item switch
		{
			Source source => Project.Move(source, index),
			OpticalPart part => Project.Move(part, index),
			Probe probe => Project.Move(probe, index),
			ColorScheme scheme => Project.Move(scheme, index),
			_ => false,
		};
		if (moved)
		{
			projectTree.Select(item!);
		}
	}

	/// <summary>Selects a newly added item and puts the caret into its first editor (the name).</summary>
	private void Added(object item)
	{
		projectTree.Select(item);
		propertiesPane.FocusFirstEditor();
	}

	/// <summary>
	/// Scene > Browse: picks an image file and embeds its bytes in the project (2026-10-13; the file is
	/// decoded once to prove it is an image and to log its size). The scene switches to image mode and
	/// shows the file's name; the watcher treats the change like any scene edit.
	/// </summary>
	private void OnBrowseSceneImage(object sender, ExecutedRoutedEventArgs e)
	{
		var dialog = new OpenFileDialog
		{
			Filter = (string)FindResource("SceneImageFilter"),
			Title = (string)FindResource("TipBrowseSceneImage"),
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			byte[] bytes = File.ReadAllBytes(dialog.FileName);
			ScenePixels pixels = SceneImageLoader.Load(bytes);
			Project.Scene.ImageData = bytes;
			Project.Scene.Image = Path.GetFileName(dialog.FileName);
			Project.Scene.ImagePath = Scene.StoredImagePath(ProjectFolder, dialog.FileName);
			Project.Scene.Mode = SceneMode.Image;
			Logger.Info("Scene image '{Name}' embedded: {Width}×{Height}, {Size} KB", Project.Scene.Image, pixels.Width, pixels.Height, Math.Max(1, bytes.Length / 1024));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
		{
			Logger.Error(ex, "The image could not be loaded: {Message}", ex.Message);
		}
	}
}
