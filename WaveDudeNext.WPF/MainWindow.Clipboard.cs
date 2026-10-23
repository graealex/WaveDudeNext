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
	/// <summary>The clipboard's objects as last parsed and the clipboard's change count then, so the menus' frequent "can paste" checks read the clipboard once per change.</summary>
	private (uint Sequence, ProjectClipboard.Clip? Clip)? _clipCache;

	/// <summary>The copyable objects the command acts on: the row given, or the selection it belongs to.</summary>
	private object[] CopyGroup(object? parameter) => [.. Group(parameter).Where(item => ProjectClipboard.CanCopy(Project, item))];

	/// <summary>The list items among them (sources, parts, probes, schemes): what Cut takes away.</summary>
	private object[] CutGroup(object? parameter) => [.. Group(parameter).Where(item => item is INamed)];

	private void CanCopyObjects(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = CopyGroup(e.Parameter).Length > 0;

	private void CanCutObjects(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = CutGroup(e.Parameter).Length > 0;

	private void CanPasteObjects(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = ClipboardClip() is { } clip && clip.CanPaste(Project, Target(e.Parameter));

	private void OnCopyObjects(object sender, ExecutedRoutedEventArgs e) => CopyToClipboard(CopyGroup(e.Parameter));

	private void OnCutObjects(object sender, ExecutedRoutedEventArgs e)
	{
		if (CopyToClipboard(CutGroup(e.Parameter)))
		{
			// the same group: Remove takes the row given, or the selection it belongs to, as Cut did
			ProjectCommands.Remove.Execute(e.Parameter, this);
		}
	}

	private void OnPasteObjects(object sender, ExecutedRoutedEventArgs e)
	{
		// taken, not kept: a paste makes the clip's objects the project's, so the next paste parses the text afresh
		ProjectClipboard.Clip? clip = ClipboardClip();
		_clipCache = null;
		if (clip == null)
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
		foreach (object item in pasted.Where(item => item is INamed))
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
		catch (ExternalException ex)
		{
			// another program holds the clipboard open
			Logger.Warn(ex, "The clipboard is busy: nothing was copied");
			return false;
		}
		Logger.Debug("Copied {Count} object(s): {Names}", items.Length, string.Join(", ", items.Select(NameOrKind)));
		return true;
	}

	/// <summary>The objects on the clipboard, read and parsed once per change of the clipboard (Windows counts its changes); null without any.</summary>
	private ProjectClipboard.Clip? ClipboardClip()
	{
		uint sequence = GetClipboardSequenceNumber();
		if (sequence != 0 && _clipCache is { } cache && cache.Sequence == sequence)
		{
			return cache.Clip;
		}
		ProjectClipboard.Clip? clip;
		try
		{
			clip = Clipboard.GetDataObject() is { } data && ObjectsData.Text(data) is { } text ? ProjectClipboard.Read(text) : null;
		}
		catch (ExternalException)
		{
			// another program holds the clipboard open; the next check tries again
			return null;
		}
		_clipCache = (sequence, clip);
		return clip;
	}

	/// <summary>Windows' count of the clipboard's changes; 0 when it cannot be told (then the clipboard is read every time).</summary>
	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();

	private static string NameOrKind(object item) => (item as INamed)?.Name ?? item.GetType().Name;
}
