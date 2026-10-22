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
using System.Windows.Input;
using System.Windows.Threading;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.WPF;

/// <summary>
/// Undo and redo (2026-10-22, the user's request): a <see cref="ProjectHistory"/> per open project. An edit ends a step
/// after <see cref="RecordDelay"/> of quiet with no mouse button held, so a drag, a number box scrub or a held arrow key is
/// one step; Undo and Redo end a pending step first. A step written back is reported by the watcher like any edit (the
/// run goes stale, a scratch frame and the re-run follow), but not recorded again; the selection is carried over to the
/// restored objects by name, else by position. The project is dirty unless the current step is the saved one. UI thread.
/// </summary>
public partial class MainWindow
{
	/// <summary>How long after the last edit, with no mouse button held, the edit ends a step.</summary>
	private static readonly TimeSpan RecordDelay = TimeSpan.FromMilliseconds(500);

	private ProjectHistory? _history;
	private DispatcherTimer? _recordTimer;
	/// <summary>Whether an edit has not ended its step yet.</summary>
	private bool _recordPending;
	/// <summary>Whether a step is being written back, so its changes are not recorded as a new one.</summary>
	private bool _restoring;

	/// <summary>A new project starts a new history, its present state the first step.</summary>
	private void StartHistory(Project? project)
	{
		_recordTimer?.Stop();
		_recordPending = false;
		_history = project != null ? new ProjectHistory(project) : null;
	}

	/// <summary>An edit: its step ends after the quiet period (a step being written back is not an edit of its own).</summary>
	private void ScheduleRecord()
	{
		if (_restoring)
		{
			return;
		}
		_recordPending = true;
		_recordTimer ??= MakeTimer(RecordDelay, OnRecordTimer);
		_recordTimer.Stop();
		_recordTimer.Start();
	}

	private void OnRecordTimer()
	{
		if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed)
		{
			// a drag or a scrub goes on: its step ends when the button is let go and the quiet period has passed
			return;
		}
		RecordStep();
	}

	/// <summary>Ends the pending step, if any: the project's state becomes a step when it differs from the current one.</summary>
	private void RecordStep()
	{
		_recordTimer?.Stop();
		if (!_recordPending || _history == null)
		{
			return;
		}
		_recordPending = false;
		if (_history.Record())
		{
			Logger.Debug("Undo step {Step} recorded", _history.Position);
		}
		IsDirty = !_history.IsAtSaved;
		CommandManager.InvalidateRequerySuggested();
	}

	/// <summary>After a save: the saved state is the current step.</summary>
	private void MarkHistorySaved()
	{
		RecordStep();
		_history?.MarkSaved();
	}

	private void CanUndo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _history != null && (_history.CanUndo || _recordPending);

	private void CanRedo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _history != null && _history.CanRedo && !_recordPending;

	private void OnUndo(object sender, ExecutedRoutedEventArgs e) => MoveInHistory(history => history.Undo(), "Undo");

	private void OnRedo(object sender, ExecutedRoutedEventArgs e) => MoveInHistory(history => history.Redo(), "Redo");

	/// <summary>Ends a pending step, then writes the step <paramref name="move"/> goes to back into the project, keeping the selection.</summary>
	private void MoveInHistory(Func<ProjectHistory, bool> move, string what)
	{
		RecordStep();
		if (_history == null)
		{
			return;
		}
		object? current = SelectedItem;
		List<(IList? List, string? Name, int Index)> selection = [.. SelectedItems.Select(Locate)];
		(IList? List, string? Name, int Index)? currentPlace = current != null ? Locate(current) : null;
		bool moved;
		_restoring = true;
		try
		{
			moved = move(_history);
		}
		finally
		{
			_restoring = false;
		}
		if (!moved)
		{
			return;
		}
		Logger.Debug("{What}: step {Step} of {Count}", what, _history.Position, _history.Count - 1);
		List<object> found = [.. selection.Select(Find).OfType<object>()];
		object? newCurrent = currentPlace is { } place ? Find(place) : null;
		SelectedItems.Clear();
		foreach (object item in found)
		{
			SelectedItems.Add(item);
		}
		SelectedItem = newCurrent ?? found.FirstOrDefault();
		if (SelectedItem == null)
		{
			projectTree.SelectRoot();
		}
		IsDirty = !_history.IsAtSaved;
		CommandManager.InvalidateRequerySuggested();
	}

	/// <summary>The project's settings objects a selection can hold, by their place: replaced as a whole by a step written back.</summary>
	private static readonly Func<Project, object>[] SettingsObjects =
	[
		project => project,
		project => project.Scene,
		project => project.Simulation,
		project => project.Display,
		project => project.Display.Field,
		project => project.Display.Intensity,
		project => project.Display.Video,
	];

	/// <summary>Where a selected item sits: its list, name and index there; or, with no list, the index of its settings object (-1 for anything else).</summary>
	private (IList? List, string? Name, int Index) Locate(object item)
	{
		foreach (IList list in (IList[])[Project.Sources, Project.Parts, Project.Probes, Project.ColorSchemes])
		{
			int index = list.IndexOf(item);
			if (index >= 0)
			{
				return (list, NameOf(item), index);
			}
		}
		return (null, null, Array.FindIndex(SettingsObjects, get => ReferenceEquals(get(Project), item)));
	}

	/// <summary>The restored object at a place <see cref="Locate"/> gave: the item of that name in the same list, else at the same index, or the settings object at the same place; null when it is gone.</summary>
	private object? Find((IList? List, string? Name, int Index) place)
	{
		if (place.List is { } list)
		{
			object? named = place.Name != null ? list.Cast<object>().FirstOrDefault(item => NameOf(item) == place.Name) : null;
			return named ?? (place.Index < list.Count ? list[place.Index] : null);
		}
		return place.Index >= 0 ? SettingsObjects[place.Index](Project) : null;
	}

	private static string? NameOf(object item) => item switch
	{
		Source source => source.Name,
		OpticalPart part => part.Name,
		Probe probe => probe.Name,
		ColorScheme scheme => scheme.Name,
		_ => null,
	};
}
