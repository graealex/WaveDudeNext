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
using NLog;
using WaveDudeNext.Model;

namespace WaveDudeNext.UI;

/// <summary>
/// Undo and redo (2026-10-22, the user's request; a model of this library since 2026-11-06): a
/// <see cref="ProjectHistory"/> per open project and the rules around it. <b>An edit becomes a step when it has
/// finished</b> (2026-11-06, the user's rule: only when it is clear that editing the value is over, and time has no say
/// in it; a quiet period of half a second had ended a step until then): the changes reported with
/// <see cref="EditMade"/> gather in a step in progress until the application says the editing is over
/// (<see cref="EndStep"/>: a drag's button released, the focus gone from the value's editor, Enter in a text box, a
/// command carried out), so a drag, a held or tapped arrow key or a turn of the wheel is one step however long it
/// takes. Undo and Redo end a step in progress first, so an edit can be undone while it is still going on. A step
/// written back is reported by whoever watches the project like any edit (the run goes stale, a scratch frame and the
/// re-run follow), but is no step of its own; the selection is carried over to the restored objects by name, else by
/// position. The document is dirty unless the current step is the saved one. The application tells it what happens to
/// the project (<see cref="Start"/>, <see cref="EditMade"/>, <see cref="EndStep"/>, <see cref="MarkSaved"/>) and binds
/// or asks <see cref="CanUndo"/> and <see cref="CanRedo"/>, whose changes raise
/// <see cref="ObservableObject.PropertyChanged"/>. UI thread only.
/// </summary>
/// <param name="document">The open project; its dirty flag follows the history.</param>
/// <param name="selection">The selection, carried over a step written back.</param>
public sealed class UndoHistory(ProjectDocument document, Selection selection) : ObservableObject
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

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

	private ProjectHistory? _history;
	/// <summary>Whether the project was edited since the last step: a step is in progress.</summary>
	private bool _stepInProgress;
	/// <summary>Whether a step is being written back, so its changes are not taken for an edit.</summary>
	private bool _restoring;
	private bool _canUndo;
	private bool _canRedo;

	/// <summary>Whether there is a step to go back to: an earlier one, or a step in progress (Undo ends it first).</summary>
	public bool CanUndo
	{
		get => _canUndo;
		private set => SetProperty(ref _canUndo, value);
	}

	/// <summary>Whether there is a later step to go forward to; not while a step is in progress, which will cut the later steps off.</summary>
	public bool CanRedo
	{
		get => _canRedo;
		private set => SetProperty(ref _canRedo, value);
	}

	/// <summary>
	/// Whether a step is being written back into the project right now: its lists are emptied and refilled one after the
	/// other, so a change reported meanwhile meets a project that is not whole. What looks something up in the project
	/// at a change (the preview's colours by a scheme's name) waits for <see cref="Restored"/> (2026-11-11, the user's
	/// choice of three ways out: the first Undo of a session in a project whose picture used a project scheme had logged
	/// "Colour scheme not found; showing greyscale", the colours having been asked for while the scheme list was empty).
	/// </summary>
	public bool IsRestoring => _restoring;

	/// <summary>A step has been written back by Undo or Redo and the project is whole again; raised before the selection is carried over.</summary>
	public event EventHandler? Restored;

	/// <summary>A new project starts a new history, its present state the first step; null ends the history.</summary>
	public void Start(Project? project)
	{
		_stepInProgress = false;
		_history = project != null ? new ProjectHistory(project) : null;
		Refresh();
	}

	/// <summary>The project was edited: the change belongs to the step in progress, which <see cref="EndStep"/> ends (a step being written back is not an edit).</summary>
	public void EditMade()
	{
		if (_restoring)
		{
			return;
		}
		_stepInProgress = true;
		Refresh();
	}

	/// <summary>
	/// The editing is over: the project's state becomes a step when it was edited since the last one and differs from
	/// it (a drag abandoned, a value put back, leaves none). Nothing happens without a step in progress, so the
	/// application calls this wherever an edit may have finished.
	/// </summary>
	public void EndStep()
	{
		if (!_stepInProgress || _history == null)
		{
			return;
		}
		_stepInProgress = false;
		if (_history.Record())
		{
			Logger.Debug("Undo step {Step} recorded", _history.Position);
		}
		document.IsDirty = !_history.IsAtSaved;
		Refresh();
	}

	/// <summary>The project was saved: a step in progress ends, and the saved state is the current step.</summary>
	public void MarkSaved()
	{
		EndStep();
		_history?.MarkSaved();
	}

	/// <summary>Ends a step in progress, then writes the step before the current one back into the project, keeping the selection.</summary>
	public void Undo() => Move(history => history.Undo(), "Undo");

	/// <summary>Ends a step in progress, then writes the step after the current one back into the project, keeping the selection.</summary>
	public void Redo() => Move(history => history.Redo(), "Redo");

	/// <summary>Ends a step in progress, then writes the step <paramref name="move"/> goes to back into the project, keeping the selection.</summary>
	private void Move(Func<ProjectHistory, bool> move, string what)
	{
		EndStep();
		if (_history == null)
		{
			return;
		}
		List<Place> places = [.. selection.Items.Select(Locate)];
		Place? currentPlace = selection.Current is { } current ? Locate(current) : null;
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
		Restored?.Invoke(this, EventArgs.Empty);
		Logger.Debug("{What}: step {Step} of {Count}", what, _history.Position, _history.Count - 1);
		List<object> found = [.. places.Select(Find).OfType<object>()];
		object? newCurrent = currentPlace is { } place ? Find(place) : null;
		selection.Set(found, newCurrent ?? found.FirstOrDefault());
		if (selection.Current == null)
		{
			// nothing of the selection is left: the user is put at the project's root
			selection.Reveal(document.Project);
		}
		document.IsDirty = !_history.IsAtSaved;
		Refresh();
	}

	/// <summary>Announces <see cref="CanUndo"/> and <see cref="CanRedo"/> as they are now.</summary>
	private void Refresh()
	{
		CanUndo = _history != null && (_history.CanUndo || _stepInProgress);
		CanRedo = _history != null && _history.CanRedo && !_stepInProgress;
	}

	/// <summary>A selected item's place: its list, name and index there; or, with no list, the index of its settings object in <see cref="SettingsObjects"/> (-1 for anything else).</summary>
	private readonly record struct Place(IList? List, string? Name, int Index);

	/// <summary>Where a selected item sits now.</summary>
	private Place Locate(object item) =>
		ProjectItems.ListOf(document.Project, item) is { } list && list.IndexOf(item) is >= 0 and var index
			? new Place(list, (item as INamed)?.Name, index)
			: new Place(null, null, Array.FindIndex(SettingsObjects, get => ReferenceEquals(get(document.Project), item)));

	/// <summary>The restored object at a place: the item of that name in the same list, else at the same index, or the settings object at the same place; null when it is gone.</summary>
	private object? Find(Place place)
	{
		if (place.List is { } list)
		{
			object? named = place.Name != null ? list.OfType<INamed>().FirstOrDefault(item => item.Name == place.Name) : null;
			return named ?? (place.Index < list.Count ? list[place.Index] : null);
		}
		return place.Index >= 0 ? SettingsObjects[place.Index](document.Project) : null;
	}
}
