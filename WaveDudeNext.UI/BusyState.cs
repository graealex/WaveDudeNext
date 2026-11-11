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

using System.ComponentModel;
using WaveDudeNext.Export;
using WaveDudeNext.Model;

namespace WaveDudeNext.UI;

/// <summary>What the application is busy with: what its status bar's indicator shows (<see cref="BusyState.Kind"/>).</summary>
public enum BusyKind
{
	/// <summary>Nothing the user waits for; no indicator.</summary>
	Idle,
	/// <summary>An export writes its frames; the frames done of their count are its measure.</summary>
	Exporting,
	/// <summary>An export waits for the run it needs, whose progress is shown with the run; the indicator has no measure of its own.</summary>
	WaitingForRun,
	/// <summary>A frame is being computed for the preview after a scrub or an edit; no measure.</summary>
	ComputingFrame,
}

/// <summary>
/// The application's busy indicator (2026-10-14, the user's rule: whenever the application computes something the
/// user waits for, a bar shows it; a model of this library since 2026-11-06, properties of the main window before):
/// what the application is busy with and how far it is, for the status bar's text and bar (the Windows taskbar button
/// showed it too until 2026-11-11, the user's decision: little benefit). Three things are told in: the export whose
/// progress is shown (<see cref="Export"/>, followed to its end),
/// whether an export waits for its run (<see cref="IsWaitingForRun"/>) and the frame being computed for the preview
/// (<see cref="ComputingFrame"/>). When several are so at once the export comes first, then its wait, then the
/// frame, which shows again once the export is over. The words are the application's: it binds the kind and the
/// numbers. UI thread only, the thread an export reports on.
/// </summary>
public sealed class BusyState : ObservableObject
{
	private ExportJob? _export;
	private bool _isWaitingForRun;
	private int _computingFrame = -1;
	private BusyKind _kind;
	private int _framesDone;
	private int _frameCount;

	/// <summary>
	/// The export whose progress the indicator shows while the job runs; null without one. Its progress and its end
	/// are followed through the job's own change notifications, which the job posts to the thread it was given.
	/// </summary>
	public ExportJob? Export
	{
		get => _export;
		set
		{
			if (ReferenceEquals(_export, value))
			{
				return;
			}
			if (_export != null)
			{
				_export.PropertyChanged -= OnExportProgress;
			}
			_export = value;
			if (value != null)
			{
				value.PropertyChanged += OnExportProgress;
			}
			OnPropertyChanged();
			Update();
		}
	}

	/// <summary>Whether an export waits for the run it needs before it writes.</summary>
	public bool IsWaitingForRun
	{
		get => _isWaitingForRun;
		set
		{
			if (SetProperty(ref _isWaitingForRun, value))
			{
				Update();
			}
		}
	}

	/// <summary>
	/// The frame being computed for the preview's position, from the request until its result (or a newer request's);
	/// -1 while none is.
	/// </summary>
	public int ComputingFrame
	{
		get => _computingFrame;
		set
		{
			if (SetProperty(ref _computingFrame, value))
			{
				OnPropertyChanged(nameof(IsComputingFrame));
				Update();
			}
		}
	}

	/// <summary>
	/// Whether a frame is being computed for the preview, whatever else the application is busy with. The preview
	/// dims its pictures and shows a spinner meanwhile, so the wait reads as work in progress rather than as a
	/// preview that does not update (the author's impression; the user's rule, 2026-10-14).
	/// </summary>
	public bool IsComputingFrame => _computingFrame >= 0;

	/// <summary>What the application is busy with; <see cref="BusyKind.Idle"/> when nothing.</summary>
	public BusyKind Kind
	{
		get => _kind;
		private set
		{
			if (SetProperty(ref _kind, value))
			{
				OnPropertyChanged(nameof(IsBusy));
				OnPropertyChanged(nameof(IsIndeterminate));
				OnPropertyChanged(nameof(IsExporting));
			}
		}
	}

	/// <summary>Whether an export is in progress, writing or waiting for its run: no run is started or discarded under it, and the automatic re-run waits.</summary>
	public bool IsExporting => _kind is BusyKind.Exporting or BusyKind.WaitingForRun;

	/// <summary>Whether the indicator shows: the application is busy with something.</summary>
	public bool IsBusy => _kind != BusyKind.Idle;

	/// <summary>Whether the indicator has no measure of progress (an export waiting for its run, a frame being computed).</summary>
	public bool IsIndeterminate => _kind is BusyKind.WaitingForRun or BusyKind.ComputingFrame;

	/// <summary>Frames the export has written, while one runs; 0 otherwise.</summary>
	public int FramesDone
	{
		get => _framesDone;
		private set
		{
			if (SetProperty(ref _framesDone, value))
			{
				OnPropertyChanged(nameof(Share));
			}
		}
	}

	/// <summary>Frames the export writes, while one runs; 0 otherwise.</summary>
	public int FrameCount
	{
		get => _frameCount;
		private set
		{
			if (SetProperty(ref _frameCount, value))
			{
				OnPropertyChanged(nameof(Share));
			}
		}
	}

	/// <summary>The share of the work done, 0 to 1: the export's frames done of their count; 0 without a measure.</summary>
	public double Share => _frameCount > 0 ? Math.Clamp((double)_framesDone / _frameCount, 0.0, 1.0) : 0.0;

	private void OnExportProgress(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ExportJob.FramesDone) or nameof(ExportJob.State))
		{
			Update();
		}
	}

	/// <summary>The kind and the numbers from the three inputs, in their order of precedence.</summary>
	private void Update()
	{
		ExportJob? running = _export is { IsActive: true } ? _export : null;
		Kind = running != null ? BusyKind.Exporting
			: _isWaitingForRun ? BusyKind.WaitingForRun
			: _computingFrame >= 0 ? BusyKind.ComputingFrame
			: BusyKind.Idle;
		// the count before the done, so a bound bar never sees more done than there is
		FrameCount = running?.FrameCount ?? 0;
		FramesDone = running?.FramesDone ?? 0;
	}
}
