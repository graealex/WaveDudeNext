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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.WPF.Commands;

namespace WaveDudeNext.WPF;

/// <summary>
/// Cut, Copy and Paste of project objects (2026-10-23, the user's choices): WPF's commands bound on the window, so a
/// focused text box handles them for its text first and everywhere else they act on the selection (the tree, the handles
/// on the pictures, the Edit menu, a tree row's menu). The objects go to the Windows clipboard through
/// <see cref="ProjectClipboard"/> (its format and the same XML as plain text, which Paste takes back too, from a text editor
/// for instance); Paste puts them behind the current item and
/// selects them, a settings object pastes onto its own row or the project root; Cut is Copy and Remove, for list items.
/// Each is one undo step.
/// UI thread.
/// </summary>
public partial class MainWindow
{
	/// <summary>The clipboard text last looked at and what it held, so the menus' frequent "can paste" checks parse it once.</summary>
	private (string Text, ProjectClipboard.Clip? Clip)? _clipCache;

	/// <summary>The copyable objects the command acts on: the row given, or the selection it belongs to.</summary>
	private object[] CopyGroup(object? parameter) => [.. Group(parameter).Where(item => ProjectClipboard.CanCopy(Project, item))];

	/// <summary>The list items among them (sources, parts, probes, schemes): what Cut takes away.</summary>
	private object[] CutGroup(object? parameter) => [.. Group(parameter).Where(item => item is Source or OpticalPart or Probe or ColorScheme)];

	private void CanCopyObjects(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = CopyGroup(e.Parameter).Length > 0;

	private void CanCutObjects(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = CutGroup(e.Parameter).Length > 0;

	private void CanPasteObjects(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = ClipboardClip() is { } clip && clip.CanPaste(Project, Target(e.Parameter));

	private void OnCopyObjects(object sender, ExecutedRoutedEventArgs e) => CopyToClipboard(CopyGroup(e.Parameter));

	private void OnCutObjects(object sender, ExecutedRoutedEventArgs e)
	{
		object[] group = CutGroup(e.Parameter);
		if (group.Length > 0 && CopyToClipboard(group))
		{
			ProjectCommands.Remove.Execute(group.Length == 1 ? group[0] : e.Parameter, this);
		}
	}

	private void OnPasteObjects(object sender, ExecutedRoutedEventArgs e)
	{
		// read afresh: a paste takes the clip's objects into the project, so each paste needs its own
		if (ReadClipboard() is not { } text || ProjectClipboard.Read(text) is not { } clip)
		{
			return;
		}
		IReadOnlyList<object> pasted = ProjectClipboard.Paste(Project, clip, Target(e.Parameter));
		if (pasted.Count == 0)
		{
			return;
		}
		Logger.Debug("Pasted {Count} object(s): {Names}", pasted.Count, string.Join(", ", pasted.Select(NameOrKind)));
		SelectedItems.Clear();
		foreach (object item in pasted.Where(item => item is Source or OpticalPart or Probe or ColorScheme))
		{
			SelectedItems.Add(item);
		}
		SelectedItem = pasted[^1];
	}

	/// <summary>Puts <paramref name="items"/> on the clipboard in the app's format and as text; false when the clipboard refused.</summary>
	private bool CopyToClipboard(object[] items)
	{
		if (items.Length == 0)
		{
			return false;
		}
		string text = ProjectClipboard.Write(Project, items);
		var data = new DataObject();
		data.SetData(ProjectClipboard.Format, text);
		data.SetText(text);
		try
		{
			Clipboard.SetDataObject(data, copy: true);
		}
		catch (COMException ex)
		{
			// another program holds the clipboard open
			Logger.Warn(ex, "The clipboard is busy: nothing was copied");
			return false;
		}
		Logger.Debug("Copied {Count} object(s): {Names}", items.Length, string.Join(", ", items.Select(NameOrKind)));
		_clipCache = null;
		return true;
	}

	/// <summary>The objects on the clipboard as last parsed, for the "can paste" checks; null without any.</summary>
	private ProjectClipboard.Clip? ClipboardClip()
	{
		if (ReadClipboard() is not { } text)
		{
			return null;
		}
		if (_clipCache is not { } cache || cache.Text != text)
		{
			_clipCache = cache = (text, ProjectClipboard.Read(text));
		}
		return cache.Clip;
	}

	/// <summary>The objects on the clipboard as text (the app's format, or plain text that may hold them), or null.</summary>
	private static string? ReadClipboard()
	{
		try
		{
			return Clipboard.GetDataObject() is { } data ? Panes.ProjectTree.ObjectsText(data) : null;
		}
		catch (COMException)
		{
			// another program holds the clipboard open; the next check tries again
			return null;
		}
	}

	private static string NameOrKind(object item) => item switch
	{
		Source source => source.Name,
		OpticalPart part => part.Name,
		Probe probe => probe.Name,
		ColorScheme scheme => scheme.Name,
		_ => item.GetType().Name,
	};
}
