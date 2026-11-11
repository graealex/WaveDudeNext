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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace WaveDudeNext.App.Commands;

/// <summary>
/// The commands the panes, overlays and menus bind to, one object owned by the main window and handed to the panes
/// through their <c>Commands</c> property. Upstream spread these over RoutedUICommand classes (ProjectCommands,
/// SimulationCommands, ExportCommands, DisplayCommands, EditCommands, PreviewCommands) bound at the window; Avalonia
/// has no routing, so the window creates every command here with its CanExecute and calls <see cref="Requery"/>
/// after the state they depend on changed. Before the window wires them, every command is
/// <see cref="RelayCommand.Disabled"/>, so a pane in a test binds without error. Every member announces a replacement
/// (<see cref="INotifyPropertyChanged"/>), so a pane that binds to <c>Commands.X</c> or reads the members follows the
/// window's wiring whenever it happens, with no order to keep and no handle that re-reads at call time.
/// Parameters: a project command takes the tree node it acts on (a Source, OpticalPart, Probe, ColorScheme or
/// <see cref="NamedCollection"/>), or null for the window's current selection, as upstream's did.
/// </summary>
public sealed class AppCommands : INotifyPropertyChanged
{
	/// <inheritdoc />
	public event PropertyChangedEventHandler? PropertyChanged;

	// ---- project objects (upstream ProjectCommands)

	/// <summary>Adds a source at the canvas centre and selects it.</summary>
	public ICommand AddSource
	{
		get => _addSource;
		set => Set(ref _addSource, value);
	}

	private ICommand _addSource = RelayCommand.Disabled;

	/// <summary>Adds a part of the kind given as a <see cref="Type"/>; without one, opens the kinds menu of the invoking control.</summary>
	public ICommand AddPart
	{
		get => _addPart;
		set => Set(ref _addPart, value);
	}

	private ICommand _addPart = RelayCommand.Disabled;

	/// <summary>Adds a probe at the canvas centre and selects it.</summary>
	public ICommand AddProbe
	{
		get => _addProbe;
		set => Set(ref _addProbe, value);
	}

	private ICommand _addProbe = RelayCommand.Disabled;

	/// <summary>Adds to the folder (<see cref="NamedCollection"/>) given as the parameter: a source, part, probe or colour scheme.</summary>
	public ICommand Add
	{
		get => _add;
		set => Set(ref _add, value);
	}

	private ICommand _add = RelayCommand.Disabled;

	/// <summary>Duplicates the parameter or the selection.</summary>
	public ICommand Duplicate
	{
		get => _duplicate;
		set => Set(ref _duplicate, value);
	}

	private ICommand _duplicate = RelayCommand.Disabled;

	/// <summary>Removes the parameter or the selection.</summary>
	public ICommand Remove
	{
		get => _remove;
		set => Set(ref _remove, value);
	}

	private ICommand _remove = RelayCommand.Disabled;

	/// <summary>Moves the parameter one row up in its list.</summary>
	public ICommand MoveUp
	{
		get => _moveUp;
		set => Set(ref _moveUp, value);
	}

	private ICommand _moveUp = RelayCommand.Disabled;

	/// <summary>Moves the parameter one row down in its list.</summary>
	public ICommand MoveDown
	{
		get => _moveDown;
		set => Set(ref _moveDown, value);
	}

	private ICommand _moveDown = RelayCommand.Disabled;

	/// <summary>Toggles Enabled (source, part) or Active (probe) of the parameter or the selection.</summary>
	public ICommand ToggleEnabled
	{
		get => _toggleEnabled;
		set => Set(ref _toggleEnabled, value);
	}

	private ICommand _toggleEnabled = RelayCommand.Disabled;

	/// <summary>Adds the parameter to the multi-selection or takes it out.</summary>
	public ICommand ToggleSelected
	{
		get => _toggleSelected;
		set => Set(ref _toggleSelected, value);
	}

	private ICommand _toggleSelected = RelayCommand.Disabled;

	/// <summary>Selects every source, part and probe.</summary>
	public ICommand SelectAll
	{
		get => _selectAll;
		set => Set(ref _selectAll, value);
	}

	private ICommand _selectAll = RelayCommand.Disabled;

	/// <summary>Cut, copy and paste of project objects (upstream ApplicationCommands bound at the window).</summary>
	public ICommand Cut
	{
		get => _cut;
		set => Set(ref _cut, value);
	}

	private ICommand _cut = RelayCommand.Disabled;

	/// <inheritdoc cref="Cut"/>
	public ICommand Copy
	{
		get => _copy;
		set => Set(ref _copy, value);
	}

	private ICommand _copy = RelayCommand.Disabled;

	/// <inheritdoc cref="Cut"/>
	public ICommand Paste
	{
		get => _paste;
		set => Set(ref _paste, value);
	}

	private ICommand _paste = RelayCommand.Disabled;

	/// <summary>Writes the recording of the probe given as the parameter as CSV, through a save dialog.</summary>
	public ICommand ExportProbeCsv
	{
		get => _exportProbeCsv;
		set => Set(ref _exportProbeCsv, value);
	}

	private ICommand _exportProbeCsv = RelayCommand.Disabled;

	// ---- the scene image (upstream ProjectCommands)

	/// <summary>Picks an image file and embeds it as the scene.</summary>
	public ICommand BrowseSceneImage
	{
		get => _browseSceneImage;
		set => Set(ref _browseSceneImage, value);
	}

	private ICommand _browseSceneImage = RelayCommand.Disabled;

	/// <summary>Writes the embedded scene image out to a file.</summary>
	public ICommand SaveSceneImage
	{
		get => _saveSceneImage;
		set => Set(ref _saveSceneImage, value);
	}

	private ICommand _saveSceneImage = RelayCommand.Disabled;

	/// <summary>Drops the scene image, keeping the canvas at its size.</summary>
	public ICommand RemoveSceneImage
	{
		get => _removeSceneImage;
		set => Set(ref _removeSceneImage, value);
	}

	private ICommand _removeSceneImage = RelayCommand.Disabled;

	/// <summary>
	/// Removes the colour stop given as the parameter (a <c>ColorStop</c>) from its scheme; the last stop stays. Del on the
	/// scheme form's gradient bar: a command, so the removal is an undo step of its own (the WPF ColorSchemeCommands.RemoveStop).
	/// </summary>
	public ICommand RemoveStop
	{
		get => _removeStop;
		set => Set(ref _removeStop, value);
	}

	private ICommand _removeStop = RelayCommand.Disabled;

	// ---- the run (upstream SimulationCommands)

	/// <summary>Starts a run from the start (F5).</summary>
	public ICommand Run
	{
		get => _run;
		set => Set(ref _run, value);
	}

	private ICommand _run = RelayCommand.Disabled;

	/// <summary>Pauses or resumes the active run (F6).</summary>
	public ICommand TogglePause
	{
		get => _togglePause;
		set => Set(ref _togglePause, value);
	}

	private ICommand _togglePause = RelayCommand.Disabled;

	/// <summary>Stops the active run, or cancels an export (Shift+F5, Esc).</summary>
	public ICommand Stop
	{
		get => _stop;
		set => Set(ref _stop, value);
	}

	private ICommand _stop = RelayCommand.Disabled;

	/// <summary>Starts an automatic run when there is none or it is stale; issued by the preview's Play.</summary>
	public ICommand EnsureRun
	{
		get => _ensureRun;
		set => Set(ref _ensureRun, value);
	}

	private ICommand _ensureRun = RelayCommand.Disabled;

	// ---- display and export (upstream DisplayCommands, ExportCommands)

	/// <summary>Fits the brightness of the layer given as a ProbeLayer parameter to the shown frame.</summary>
	public ICommand AutoBrightness
	{
		get => _autoBrightness;
		set => Set(ref _autoBrightness, value);
	}

	private ICommand _autoBrightness = RelayCommand.Disabled;

	/// <summary>Exports the field pictures of the run (Ctrl+Shift+E).</summary>
	public ICommand ExportField
	{
		get => _exportField;
		set => Set(ref _exportField, value);
	}

	private ICommand _exportField = RelayCommand.Disabled;

	/// <summary>Exports the intensity pictures of the run (Ctrl+Shift+I).</summary>
	public ICommand ExportIntensity
	{
		get => _exportIntensity;
		set => Set(ref _exportIntensity, value);
	}

	private ICommand _exportIntensity = RelayCommand.Disabled;

	/// <summary>Exports both layers of the run (Ctrl+Shift+B).</summary>
	public ICommand ExportBoth
	{
		get => _exportBoth;
		set => Set(ref _exportBoth, value);
	}

	private ICommand _exportBoth = RelayCommand.Disabled;

	/// <summary>Exports the shown frame of the layer given as a ProbeLayer parameter as a still (Ctrl+Shift+F / G).</summary>
	public ICommand ExportFrame
	{
		get => _exportFrame;
		set => Set(ref _exportFrame, value);
	}

	private ICommand _exportFrame = RelayCommand.Disabled;

	/// <summary>Repeats the last run export without a dialog (Ctrl+E).</summary>
	public ICommand ExportLast
	{
		get => _exportLast;
		set => Set(ref _exportLast, value);
	}

	private ICommand _exportLast = RelayCommand.Disabled;

	// ---- editing (upstream EditCommands)

	/// <summary>Undo (Ctrl+Z).</summary>
	public ICommand Undo
	{
		get => _undo;
		set => Set(ref _undo, value);
	}

	private ICommand _undo = RelayCommand.Disabled;

	/// <summary>Redo (Ctrl+Y, Ctrl+Shift+Z).</summary>
	public ICommand Redo
	{
		get => _redo;
		set => Set(ref _redo, value);
	}

	private ICommand _redo = RelayCommand.Disabled;

	/// <summary>Deletes a text box's selection, else runs <see cref="Remove"/> (Del).</summary>
	public ICommand Delete
	{
		get => _delete;
		set => Set(ref _delete, value);
	}

	private ICommand _delete = RelayCommand.Disabled;

	// ---- the window's own (2026-11-09: the file menu, Play, Help; the port's window had wired these to its menu items in code)

	/// <summary>File > New: a new, empty project (after the save question).</summary>
	public ICommand New
	{
		get => _new;
		set => Set(ref _new, value);
	}

	private ICommand _new = RelayCommand.Disabled;

	/// <summary>File > Open: the open dialog for a project, a Wave Dude 2D configuration or a colour scheme.</summary>
	public ICommand Open
	{
		get => _open;
		set => Set(ref _open, value);
	}

	private ICommand _open = RelayCommand.Disabled;

	/// <summary>File > Save: writes the project to its file, or asks where.</summary>
	public ICommand Save
	{
		get => _save;
		set => Set(ref _save, value);
	}

	private ICommand _save = RelayCommand.Disabled;

	/// <summary>File > Save As: asks where, then writes.</summary>
	public ICommand SaveAs
	{
		get => _saveAs;
		set => Set(ref _saveAs, value);
	}

	private ICommand _saveAs = RelayCommand.Disabled;

	/// <summary>File > Close: a new, empty project in place of the one open (after the save question).</summary>
	public ICommand CloseProject
	{
		get => _closeProject;
		set => Set(ref _closeProject, value);
	}

	private ICommand _closeProject = RelayCommand.Disabled;

	/// <summary>File > Exit: closes the window.</summary>
	public ICommand Exit
	{
		get => _exit;
		set => Set(ref _exit, value);
	}

	private ICommand _exit = RelayCommand.Disabled;

	/// <summary>Simulation > Play (F7): the preview's own play or pause, from anywhere.</summary>
	public ICommand PlayPause
	{
		get => _playPause;
		set => Set(ref _playPause, value);
	}

	private ICommand _playPause = RelayCommand.Disabled;

	/// <summary>Help > About.</summary>
	public ICommand About
	{
		get => _about;
		set => Set(ref _about, value);
	}

	private ICommand _about = RelayCommand.Disabled;

	// ---- the settings menus (upstream ViewCommands and SimulationCommands)

	/// <summary>View > UI zoom: the factor as the parameter, text with a decimal point or a number.</summary>
	public ICommand SetUiZoom
	{
		get => _setUiZoom;
		set => Set(ref _setUiZoom, value);
	}

	private ICommand _setUiZoom = RelayCommand.Disabled;

	/// <summary>View > Preview resolution: the canvas divisor (1, 2, 4) as the parameter.</summary>
	public ICommand SetPreviewResolution
	{
		get => _setPreviewResolution;
		set => Set(ref _setPreviewResolution, value);
	}

	private ICommand _setPreviewResolution = RelayCommand.Disabled;

	/// <summary>View > Preview frames: the multiplier of the project's visualisation interval (1, 2, 4) as the parameter.</summary>
	public ICommand SetPreviewFrames
	{
		get => _setPreviewFrames;
		set => Set(ref _setPreviewFrames, value);
	}

	private ICommand _setPreviewFrames = RelayCommand.Disabled;

	/// <summary>View > Preview samples: the bytes per sample (2 halves, 1 palette indices) as the parameter.</summary>
	public ICommand SetPreviewSamples
	{
		get => _setPreviewSamples;
		set => Set(ref _setPreviewSamples, value);
	}

	private ICommand _setPreviewSamples = RelayCommand.Disabled;

	/// <summary>View > Preview dithering: the bits per channel (0 none, 10, 12, 14, 16) as the parameter.</summary>
	public ICommand SetPreviewDither
	{
		get => _setPreviewDither;
		set => Set(ref _setPreviewDither, value);
	}

	private ICommand _setPreviewDither = RelayCommand.Disabled;

	/// <summary>Simulation > RAM cache size: the share of the RAM in percent, or <see cref="WaveDudeNext.Runs.FrameMemory.Automatic"/>, as the parameter.</summary>
	public ICommand SetFrameMemory
	{
		get => _setFrameMemory;
		set => Set(ref _setFrameMemory, value);
	}

	private ICommand _setFrameMemory = RelayCommand.Disabled;

	/// <summary>Simulation > Use the GPU: flips the setting.</summary>
	public ICommand ToggleUseGpu
	{
		get => _toggleUseGpu;
		set => Set(ref _toggleUseGpu, value);
	}

	private ICommand _toggleUseGpu = RelayCommand.Disabled;

	/// <summary>Simulation > Re-run automatically after an edit: flips the setting.</summary>
	public ICommand ToggleAutoRerun
	{
		get => _toggleAutoRerun;
		set => Set(ref _toggleAutoRerun, value);
	}

	private ICommand _toggleAutoRerun = RelayCommand.Disabled;

	/// <summary>Simulation > Warn when a run does not fit in RAM: flips the setting.</summary>
	public ICommand ToggleMemoryWarning
	{
		get => _toggleMemoryWarning;
		set => Set(ref _toggleMemoryWarning, value);
	}

	private ICommand _toggleMemoryWarning = RelayCommand.Disabled;

	/// <summary>View > Panes: shows or hides the dock item given as the parameter (the dock host's own command).</summary>
	public ICommand TogglePane
	{
		get => _togglePane;
		set => Set(ref _togglePane, value);
	}

	private ICommand _togglePane = RelayCommand.Disabled;

	/// <summary>Help > Manual (F1).</summary>
	public ICommand Manual
	{
		get => _manual;
		set => Set(ref _manual, value);
	}

	private ICommand _manual = RelayCommand.Disabled;

	/// <summary>Every command, for <see cref="Requery"/>.</summary>
	public IEnumerable<ICommand> All
	{
		get
		{
			yield return AddSource;
			yield return AddPart;
			yield return AddProbe;
			yield return Add;
			yield return Duplicate;
			yield return Remove;
			yield return MoveUp;
			yield return MoveDown;
			yield return ToggleEnabled;
			yield return ToggleSelected;
			yield return SelectAll;
			yield return Cut;
			yield return Copy;
			yield return Paste;
			yield return ExportProbeCsv;
			yield return BrowseSceneImage;
			yield return SaveSceneImage;
			yield return RemoveSceneImage;
			yield return RemoveStop;
			yield return Run;
			yield return TogglePause;
			yield return Stop;
			yield return EnsureRun;
			yield return AutoBrightness;
			yield return ExportField;
			yield return ExportIntensity;
			yield return ExportBoth;
			yield return ExportFrame;
			yield return ExportLast;
			yield return Undo;
			yield return Redo;
			yield return Delete;
			yield return New;
			yield return Open;
			yield return Save;
			yield return SaveAs;
			yield return CloseProject;
			yield return Exit;
			yield return PlayPause;
			yield return About;
			yield return Manual;
			yield return SetUiZoom;
			yield return SetPreviewResolution;
			yield return SetPreviewFrames;
			yield return SetPreviewSamples;
			yield return SetPreviewDither;
			yield return SetFrameMemory;
			yield return ToggleUseGpu;
			yield return ToggleAutoRerun;
			yield return ToggleMemoryWarning;
			yield return TogglePane;
		}
	}

	/// <summary>Raises CanExecuteChanged on every <see cref="RelayCommand"/> here: the window calls it after a state change.</summary>
	public void Requery()
	{
		foreach (ICommand command in All)
		{
			switch (command)
			{
				case RelayCommand relay:
					relay.Requery();
					break;
				case RelayCommand<object> typed:
					typed.Requery();
					break;
			}
		}
	}

	/// <summary>Replaces a member and announces it when it is another command.</summary>
	private void Set(ref ICommand field, ICommand value, [CallerMemberName] string? name = null)
	{
		if (ReferenceEquals(field, value))
		{
			return;
		}
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
