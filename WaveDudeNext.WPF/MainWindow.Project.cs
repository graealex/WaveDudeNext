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
using WaveDudeNext.WPF.Commands;
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

	/// <summary>
	/// What a command acts on: the whole selection when its target is one of several selected items (Explorer's rule,
	/// 2026-10-19), else the target alone. A snapshot, since acting changes the selection.
	/// </summary>
	private object[] Group(object? parameter)
	{
		object? target = Target(parameter);
		if (target == null)
		{
			return [];
		}
		return SelectedItems.Count > 1 && SelectedItems.Contains(target) ? [.. SelectedItems] : [target];
	}

	/// <summary>Every source, part and probe in tree order; the range selections and Select all draw on it.</summary>
	private IEnumerable<object> Selectable() => Project.Sources.Cast<object>().Concat(Project.Parts).Concat(Project.Probes);

	private void CanSelectAll(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Selectable().Any();

	/// <summary>Ctrl+A: every source, part and probe; the last one becomes the current item.</summary>
	private void OnSelectAll(object sender, ExecutedRoutedEventArgs e)
	{
		object[] all = [.. Selectable()];
		SelectedItems.Clear();
		foreach (object item in all)
		{
			SelectedItems.Add(item);
		}
		SelectedItem = all.Length > 0 ? all[^1] : null;
	}

	private void CanToggleSelected(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Target(e.Parameter) is Source or OpticalPart or Probe;

	/// <summary>Insert or Ctrl+Space on a tree row: into the selection, or out of it (the current item then moves to the last one left).</summary>
	private void OnToggleSelected(object sender, ExecutedRoutedEventArgs e)
	{
		object? item = Target(e.Parameter);
		if (item is not (Source or OpticalPart or Probe))
		{
			return;
		}
		if (SelectedItems.Contains(item))
		{
			SelectedItems.Remove(item);
			if (ReferenceEquals(SelectedItem, item))
			{
				SelectedItem = SelectedItems.LastOrDefault();
			}
		}
		else
		{
			SelectedItems.Add(item);
			SelectedItem = item;
		}
	}

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
	/// Add part: with an <see cref="OpticalPart"/> subclass as parameter (the menu entries) the part is appended; without one (the
	/// "+ Part" button, the Parts folder's plus) the invoking element's context menu, the menu of kinds, opens under it,
	/// so the kind is chosen there (keyboard: Space opens it, the arrows walk it, Enter picks).
	/// </summary>
	private void OnAddPart(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is Type type)
		{
			Added(Project.AddPart(type));
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
		e.CanExecute = Target(e.Parameter) is INamed;

	private void CanToggleEnabled(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Target(e.Parameter) is Source or OpticalPart or Probe;

	/// <summary>The tree's check boxes by keyboard: Space on a selected source, part or probe flips it (the author's point 11, 2026-10-13); each of a selected group flips on its own.</summary>
	private void OnToggleEnabled(object sender, ExecutedRoutedEventArgs e)
	{
		foreach (object item in Group(e.Parameter))
		{
			switch (item)
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
	}

	/// <summary>A copy after each original; the copies become the selection, the last one the current item with the caret in its name.</summary>
	private void OnDuplicate(object sender, ExecutedRoutedEventArgs e)
	{
		var copies = new List<object>();
		foreach (object item in Group(e.Parameter))
		{
			object? copy = item switch
			{
				Source source => Project.Duplicate(source),
				OpticalPart part => Project.Duplicate(part),
				Probe probe => Project.Duplicate(probe),
				ColorScheme scheme => Project.Duplicate(scheme),
				_ => null,
			};
			if (copy != null)
			{
				copies.Add(copy);
			}
		}
		if (copies.Count == 0)
		{
			return;
		}
		SelectedItems.Clear();
		foreach (object copy in copies)
		{
			SelectedItems.Add(copy);
		}
		Added(copies[^1]);
	}

	/// <summary>Edit > Delete: in a text box (the menu's command starts from the element that had the focus) its selected text; else what Remove takes.</summary>
	private void CanDelete(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.OriginalSource is System.Windows.Controls.TextBox box
			? box is { IsReadOnly: false, SelectionLength: > 0 }
			: Target(null) is INamed;

	private void OnDelete(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.OriginalSource is System.Windows.Controls.TextBox box)
		{
			box.SelectedText = "";
		}
		else
		{
			ProjectCommands.Remove.Execute(null, this);
		}
	}

	/// <summary>Removes the target, or the whole selection it belongs to; the user is kept in place at the slot of the last one removed.</summary>
	private void OnRemove(object sender, ExecutedRoutedEventArgs e)
	{
		object[] group = Group(e.Parameter);
		if (group.Length == 0 || ListOf(group[^1]) is not { } list)
		{
			return;
		}
		int index = list.IndexOf(group[^1]);
		foreach (object item in group)
		{
			SelectedItems.Remove(item);
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
		}
		// keep the user in place: the item that took the slot, else the folder
		if (list.Count > 0)
		{
			projectTree.Select(list[Math.Clamp(index, 0, list.Count - 1)]!);
		}
		else
		{
			projectTree.SelectFolder(list);
		}
	}

	private void CanMoveUp(object sender, CanExecuteRoutedEventArgs e)
	{
		object? item = Target(e.Parameter);
		e.CanExecute = ListOf(item) is { } list && item is INamed && list.IndexOf(item) > 0;
	}

	private void CanMoveDown(object sender, CanExecuteRoutedEventArgs e)
	{
		object? item = Target(e.Parameter);
		e.CanExecute = ListOf(item) is { } list && item is INamed && list.IndexOf(item) < list.Count - 1;
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
