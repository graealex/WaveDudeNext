// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WaveDudeNext.Engine;
using WaveDudeNext.Export;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.WPF.Dialogs;
using WaveDudeNext.WPF.Properties;
using WaveDudeNext.WPF.Rendering;

namespace WaveDudeNext.WPF;

/// <summary>
/// Handlers of <see cref="Commands.ExportCommands"/> (2026-10-14): the Save dialog picks the format by its
/// extension (PNG or JPEG sequence, MPEG-1, AVI, MP4) and the name, pre-set to the project's name with
/// <c>_field</c> / <c>_intensity</c> in the project file's folder; "both" writes two outputs from one name;
/// a frame command writes the frame the preview shows as one PNG or JPEG (<c>_field_&lt;step&gt;</c>).
/// Every export renders from the run's store with the display settings as they are, the outline when opted
/// in and the probe graph boxes when <see cref="Display.ProbeGraphsInExport"/> is on; a still shows exactly
/// what a video export would show at that frame (the user's rule), never the scratch frame. There is always a
/// run to export: when there is none, it is stale, or it lacks the intensity plane, a run is started first
/// (like Play does) and the export waits for it (<see cref="IsExportWaiting"/>). The <see cref="ExportJob"/>
/// works on its own thread and reports to the status bar's busy indicator; Esc cancels it. Runs are not
/// started or replaced while an export reads the store. A probe's recording goes out as a CSV file the same
/// way (<see cref="Commands.ExportCommands.ProbeCsv"/>, 2026-10-18): the Save dialog, the full run, then the
/// run's trace of the probe written by <see cref="ProbeCsv"/> on the UI thread (a few megabytes at most).
/// </summary>
public partial class MainWindow
{
	/// <summary>Identifies the <see cref="Export"/> property.</summary>
	public static readonly DependencyProperty ExportProperty = DependencyProperty.Register(
		nameof(Export), typeof(ExportJob), typeof(MainWindow),
		new PropertyMetadata(null, (d, _) => ((MainWindow)d).OnExportChanged()));

	/// <summary>Identifies the <see cref="IsExportWaiting"/> property.</summary>
	public static readonly DependencyProperty IsExportWaitingProperty = DependencyProperty.Register(
		nameof(IsExportWaiting), typeof(bool), typeof(MainWindow),
		new PropertyMetadata(false, (d, _) => ((MainWindow)d).OnExportChanged()));

	/// <summary>The current export (running or finished), or null.</summary>
	public ExportJob? Export
	{
		get => (ExportJob?)GetValue(ExportProperty);
		set => SetValue(ExportProperty, value);
	}

	/// <summary>Whether an export is waiting for its run to complete before it starts writing.</summary>
	public bool IsExportWaiting
	{
		get => (bool)GetValue(IsExportWaitingProperty);
		set => SetValue(IsExportWaitingProperty, value);
	}

	/// <summary>Whether an export is running or waiting for its run.</summary>
	private bool IsExporting => Export?.IsActive == true || IsExportWaiting;

	private void OnExportChanged()
	{
		CommandManager.InvalidateRequerySuggested();
		UpdateBusy();
	}

	private void CanExport(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !IsExporting && Project.Simulation.Steps > 0;

	private void OnExportField(object sender, ExecutedRoutedEventArgs e) => BeginExport(field: true, intensity: false);

	private void OnExportIntensity(object sender, ExecutedRoutedEventArgs e) => BeginExport(field: false, intensity: true);

	private void OnExportBoth(object sender, ExecutedRoutedEventArgs e) => BeginExport(field: true, intensity: true);

	private void OnExportFieldFrame(object sender, ExecutedRoutedEventArgs e) => BeginExport(field: true, intensity: false, still: true);

	private void OnExportIntensityFrame(object sender, ExecutedRoutedEventArgs e) => BeginExport(field: false, intensity: true, still: true);

	/// <summary>An active probe only: a disabled one is not in any run.</summary>
	private void CanExportProbeCsv(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.Parameter is Probe { Active: true } && !IsExporting && Project.Simulation.Steps > 0;

	/// <summary>The Save dialog, the full run (started first when there is none or it is stale, like the pictures), the probe's trace as CSV.</summary>
	private async void OnExportProbeCsv(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is not Probe probe || IsExporting)
		{
			return;
		}
		string suggested = ProbeCsv.FileName(ProjectName(), probe);
		string chosen;
		string? scripted = Environment.GetEnvironmentVariable("WAVEDUDE_EXPORT_PATH");
		if (!string.IsNullOrEmpty(scripted))
		{
			// the harness hook of the picture exports: "{name}" stands for the suggested name
			chosen = scripted.Replace("{name}", Path.GetFileNameWithoutExtension(suggested), StringComparison.Ordinal);
			Logger.Debug("Export path taken from WAVEDUDE_EXPORT_PATH: {Path}", chosen);
		}
		else
		{
			var dialog = new SaveFileDialog
			{
				Title = (string)FindResource("ExportDialogTitleProbeCsv"),
				Filter = (string)FindResource("ExportDialogFilterCsv"),
				AddExtension = true,
				DefaultExt = ProbeCsv.Extension,
				FileName = suggested,
				InitialDirectory = ProjectFolder ?? (Directory.Exists(Settings.Default.LastExportFolder) ? Settings.Default.LastExportFolder : ""),
			};
			if (dialog.ShowDialog(this) != true)
			{
				Logger.Debug("Probe export cancelled in the Save dialog");
				return;
			}
			chosen = dialog.FileName;
			Settings.Default.LastExportFolder = Path.GetDirectoryName(chosen) ?? "";
		}
		Logger.Debug("Export: probe '{Probe}' ({Kind}, {Layer}, {Measurement}) as CSV to {Path}", probe.Name, probe.Kind, probe.Layer, probe.Measurement, chosen);
		SimulationRun? run = await EnsureRunForExport(intensity: probe.Layer == ProbeLayer.Intensity, needsHalves: false);
		if (run == null)
		{
			return;
		}
		ProbeTrace? trace = null;
		for (int i = 0; i < run.Spec.Probes.Length && i < run.Traces.Length; i++)
		{
			if (ReferenceEquals(run.Spec.Probes[i].Origin, probe))
			{
				trace = run.Traces[i];
			}
		}
		if (trace == null)
		{
			Logger.Error("Probe '{Probe}' is not in the run (removed or disabled while it was waited for)", probe.Name);
			MessageWindow.Show(this, (string)FindResource("ExportError"), MessageKind.Error, MessageButtons.Ok);
			return;
		}
		try
		{
			int rows = ProbeCsv.Write(chosen, probe, trace, run.Spec.VisualizeEvery, run.Spec.FrameStride);
			Logger.Info("Probe data written: {Rows} rows of '{Probe}' to {Path}", rows, probe.Name, chosen);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.Error(ex, "The probe data could not be written: {Message}", ex.Message);
			ShowError((string)FindResource("ExportError"), ex);
		}
	}

	/// <summary>The Save dialog, the run, the targets, the job.</summary>
	/// <param name="field">Export the field.</param>
	/// <param name="intensity">Export the intensity.</param>
	/// <param name="still">One frame, the one the preview shows, as a single image file.</param>
	private async void BeginExport(bool field, bool intensity, bool still = false)
	{
		if (IsExporting)
		{
			return;
		}
		int stillFrame = Math.Max(0, PreviewPosition);
		string suffix = field && intensity ? "" : field ? "_field" : "_intensity";
		if (still)
		{
			suffix += "_" + (stillFrame * Math.Max(1, Project.Simulation.VisualizeEvery)).ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		string chosen;
		// the still's filter lists the sequence formats, which lead the full list in the same order
		int filterIndex = still ? Math.Clamp(Settings.Default.ExportFilterIndex + 1, 1, ExportFormats.All.Count(ExportFormats.IsSequence)) : Math.Clamp(Settings.Default.ExportFilterIndex + 1, 1, ExportFormats.All.Length);
		string? scripted = Environment.GetEnvironmentVariable("WAVEDUDE_EXPORT_PATH");
		if (!string.IsNullOrEmpty(scripted))
		{
			// a UI harness cannot drive the common dialog by posted keys: it hands the path in instead, "{name}" standing for the suggested name
			chosen = scripted.Replace("{name}", ProjectName() + suffix, StringComparison.Ordinal);
			Logger.Debug("Export path taken from WAVEDUDE_EXPORT_PATH: {Path}", chosen);
		}
		else
		{
			var dialog = new SaveFileDialog
			{
				Title = (string)FindResource(still ? "ExportDialogTitleFrame" : field && intensity ? "ExportDialogTitleBoth" : field ? "ExportDialogTitleField" : "ExportDialogTitleIntensity"),
				Filter = (string)FindResource(still ? "ExportDialogFilterStill" : "ExportDialogFilter"),
				FilterIndex = filterIndex,
				AddExtension = true,
				DefaultExt = ExportFormats.Extension(ExportFormat.Png),
				FileName = ProjectName() + suffix,
				InitialDirectory = ProjectFolder ?? (Directory.Exists(Settings.Default.LastExportFolder) ? Settings.Default.LastExportFolder : ""),
			};
			if (dialog.ShowDialog(this) != true)
			{
				Logger.Debug("Export cancelled in the Save dialog");
				return;
			}
			chosen = dialog.FileName;
			filterIndex = dialog.FilterIndex;
			if (!still)
			{
				Settings.Default.ExportFilterIndex = filterIndex - 1;
			}
			Settings.Default.LastExportFolder = Path.GetDirectoryName(chosen) ?? "";
		}
		ExportFormat format = ExportFormats.FromPath(chosen) ?? ExportFormats.All[Math.Clamp(filterIndex - 1, 0, ExportFormats.All.Length - 1)];
		if (still && !ExportFormats.IsSequence(format))
		{
			format = ExportFormat.Png;
		}
		if (ExportFormats.FromPath(chosen) != format)
		{
			chosen = Path.ChangeExtension(chosen, ExportFormats.Extension(format));
		}
		Logger.Debug("Export: {What} as {Format} to {Path}", still ? "the shown frame " + stillFrame : field && intensity ? "field and intensity" : field ? "field" : "intensity", format, chosen);
		if (!ExportFormats.IsAvailable(format))
		{
			Logger.Error("MP4 export is not available: Media Foundation is missing on this computer (Windows N needs the Media Feature Pack)");
			MessageWindow.Show(this, (string)FindResource("ExportError"), MessageKind.Error, MessageButtons.Ok);
			return;
		}
		// the output paths: one per layer; "both" takes the chosen name without a layer suffix and appends its own
		var outputs = new List<(FrameLayer Layer, string Path)>();
		if (field && intensity)
		{
			string folder = Path.GetDirectoryName(chosen) ?? "";
			string name = Path.GetFileNameWithoutExtension(chosen);
			foreach (string known in new[] { "_field", "_intensity" })
			{
				if (name.EndsWith(known, StringComparison.OrdinalIgnoreCase))
				{
					name = name[..^known.Length];
				}
			}
			string extension = ExportFormats.Extension(format);
			outputs.Add((FrameLayer.Field, Path.Combine(folder, name + "_field" + extension)));
			outputs.Add((FrameLayer.Intensity, Path.Combine(folder, name + "_intensity" + extension)));
		}
		else
		{
			outputs.Add((field ? FrameLayer.Field : FrameLayer.Intensity, chosen));
		}
		// a sequence goes to a folder of its own; one that exists with files in it is replaced only on request
		if (!still && ExportFormats.IsSequence(format))
		{
			foreach ((_, string path) in outputs)
			{
				string folder = ExportFormats.SequenceFolder(path);
				if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()
					&& MessageWindow.Show(this, string.Format((string)FindResource("ExportFolderExistsFormat"), folder), MessageKind.Question, MessageButtons.YesNo) != MessageAnswer.Yes)
				{
					Logger.Debug("Export cancelled: {Folder} kept", folder);
					return;
				}
			}
		}
		SimulationRun? run = await EnsureRunForExport(intensity, ExportFormats.IsData(format));
		if (run == null)
		{
			return;
		}
		// the range in frames, from the model's steps like the preview's in/out points; a still is the shown frame
		Display display = Project.Display;
		int every = run.Spec.VisualizeEvery;
		int lastFrame = run.Spec.FrameCount - 1;
		int first;
		int last;
		if (still)
		{
			first = last = Math.Clamp(stillFrame, 0, lastFrame);
		}
		else
		{
			first = Math.Clamp(display.ExportFrom / every, 0, lastFrame);
			last = display.ExportTo <= 0 ? lastFrame : Math.Clamp(display.ExportTo / every, first, lastFrame);
		}
		int frameCount = last - first + 1;
		Logger.Debug("Export range: frames {First} to {Last} ({Count}) of the run's {Total}, {Width}×{Height}, store {Store}", first, last, frameCount, run.Spec.FrameCount, run.Spec.Width, run.Spec.Height, run.Store.Path);
		// the targets: the layer's colours and outline, the probe boxes, the sink
		var targets = new List<ExportTarget>();
		try
		{
			byte[]? edges = null;
			if (display.Field.OverlayInExport || display.Intensity.OverlayInExport)
			{
				ScenePixels? scene = LoadSceneImage();
				if (scene != null && scene.Width == run.Spec.Width && scene.Height == run.Spec.Height)
				{
					edges = GlowingEdges.Compute(scene.Bgra, scene.Width, scene.Height, scene.StrideBytes);
				}
				Logger.Debug("Export outline: {State}", edges != null ? "computed from the scene image" : "none (no image scene)");
			}
			ProbeGraphPainter? painter = display.ProbeGraphsInExport || display.ProbeMarkersInExport ? new ProbeGraphPainter(Project, run, display.ProbeGraphsInExport, display.ProbeMarkersInExport) : null;
			foreach ((FrameLayer layer, string path) in outputs)
			{
				DisplayLayer settings = layer == FrameLayer.Field ? display.Field : display.Intensity;
				int[]? lut = ColorSchemeCatalog.Instance.LutFor(Project, settings.ColorScheme, settings.BlackLevel);
				Action<int, Memory<int>>? decorate = painter?.For(layer == FrameLayer.Field ? ProbeLayer.Field : ProbeLayer.Intensity);
				var exportLayer = new ExportLayer(layer, lut, (float)settings.Brightness, settings.OverlayInExport ? edges : null, settings.OverlayGain, decorate);
				// the sink is opened by the job on its thread (a Media Foundation writer must stay on one thread)
				int width = run.Spec.Width;
				int height = run.Spec.Height;
				int fps = display.Video.Fps;
				int quality = display.Video.Quality;
				string output = !still && ExportFormats.IsSequence(format) ? ExportFormats.SequenceFolder(path) : path;
				Func<IFrameSink> open = still
					? () => ExportFormats.OpenStill(format, path, width, height, quality)
					: () => ExportFormats.Open(format, path, width, height, fps, quality, frameCount);
				targets.Add(new ExportTarget(exportLayer, output, open));
				if (ExportFormats.IsData(format))
				{
					Logger.Debug("Export target {Layer}: the stored 16-bit values as OpenEXR (no colours, outline or boxes), to {Output}", layer, output);
				}
				else
				{
					Logger.Debug("Export target {Layer}: scheme '{Scheme}' (black level {Black}), brightness {Brightness}, outline {Outline}, probe boxes {Boxes}, markers {Markers}, to {Output}",
						layer, settings.ColorScheme, settings.BlackLevel, settings.Brightness, settings.OverlayInExport && edges != null ? "gain " + settings.OverlayGain : "off", display.ProbeGraphsInExport ? "on" : "off", display.ProbeMarkersInExport ? "on" : "off", output);
				}
			}
			if (!still && format == ExportFormat.Mpeg1)
			{
				double used = Mpeg1Writer.FrameRates[Mpeg1Writer.NearestFrameRateCode(display.Video.Fps) - 1];
				if (Math.Abs(used - display.Video.Fps) > 0.01)
				{
					Logger.Warn("MPEG-1 has no {Wanted} fps: the video is written at {Used:0.###} fps", display.Video.Fps, used);
				}
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or RunSpecException)
		{
			Logger.Error(ex, "The export could not start: {Message}", ex.Message);
			ShowError((string)FindResource("ExportError"), ex);
			return;
		}
		var job = new ExportJob(run.Store, first, last, targets, SynchronizationContext.Current);
		job.Finished += (_, _) => OnExportFinished(job);
		WatchExport(job);
		Export = job;
		Logger.Info("Export started: frames {First} to {Last} ({Count}) of {Width}×{Height} to {Outputs}",
			first, last, frameCount, run.Spec.Width, run.Spec.Height, string.Join(", ", targets.Select(target => target.Output)));
		job.Start();
	}

	/// <summary>The project's name for a file: the project file's name, else "project".</summary>
	private string ProjectName() => Path.GetFileNameWithoutExtension(Filename ?? _suggestedFileName ?? "project");

	/// <summary>
	/// A completed run the export can read: the current one when it is fresh, finished or in progress, and
	/// holds the intensity plane if asked for; otherwise a run is started and awaited. Null when no run could
	/// be had (logged, and an error shown when the run could not start).
	/// </summary>
	/// <param name="intensity">The run must hold the intensity plane.</param>
	/// <param name="needsHalves">The run must hold 16-bit samples (a data export); otherwise the 8-bit index does.</param>
	private async Task<SimulationRun?> EnsureRunForExport(bool intensity, bool needsHalves)
	{
		IsExportWaiting = true;
		try
		{
			for (int attempt = 0; attempt < 3; attempt++)
			{
				SimulationRun? run = Run;
				// a reduced preview serves a picture export when only its samples are reduced: the 8-bit index gives the
				// same pixels (2026-10-18); a smaller picture or skipped frames never do, and a data export needs the halves
				PreviewQuality? quality = run?.Spec.Quality;
				bool reduced = quality != null && (quality.ResolutionDivisor != 1 || quality.FrameDivisor != 1 || (quality.Format != FrameFormat.Half && needsHalves));
				string? reason = run == null ? "no run" : IsRunStale ? "the run is stale" : IsProbesStale ? "the probes were edited since the run"
					: reduced ? (quality!.Format != FrameFormat.Half && needsHalves && quality.ResolutionDivisor == 1 && quality.FrameDivisor == 1 ? "a data export needs the 16-bit samples" : "the run is a reduced preview (View menu)")
					: intensity && !run.Store.Has(FrameLayer.Intensity) ? "the run has no intensity plane" : !run.IsActive && run.State != RunState.Completed ? "the run did not complete" : null;
				if (reason != null)
				{
					Logger.Debug("Export: a run is started first ({Reason})", reason);
					StartRun(automatic: true, trackIntensity: intensity, fullQuality: true);
					run = Run;
				}
				else if (run!.IsActive)
				{
					Logger.Debug("Export: waiting for the run in progress");
				}
				if (run == null)
				{
					return null;
				}
				await run.Completion;
				await WaitForState(run);
				if (run.State == RunState.Completed && ReferenceEquals(run, Run))
				{
					return run;
				}
				if (!ReferenceEquals(run, Run))
				{
					// replaced meanwhile (an edit started a fresh one): look at the new one
					Logger.Debug("Export: the run was replaced while waited for, looking again");
					continue;
				}
				Logger.Warn("Export abandoned: the run ended {State}", run.State);
				return null;
			}
			Logger.Warn("Export abandoned: the project kept changing while its run was computed");
			return null;
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			Logger.Error(ex, "The export's run could not start: {Message}", ex.Message);
			ShowError((string)FindResource("RunError"), ex);
			return null;
		}
		finally
		{
			IsExportWaiting = false;
		}
	}

	/// <summary>The run's final state is posted to the UI thread after its completion task completes; wait for that post.</summary>
	private static async Task WaitForState(SimulationRun run)
	{
		for (int i = 0; i < 50 && !run.IsFinished; i++)
		{
			await Task.Delay(20);
		}
	}

	private void OnExportFinished(ExportJob job)
	{
		if (!ReferenceEquals(job, Export))
		{
			return;
		}
		UpdateBusy();
		string outputs = string.Join(", ", job.Targets.Select(target => target.Output));
		switch (job.State)
		{
			case ExportState.Completed:
				Logger.Info("Exported {Frames} frames to {Outputs} in {Seconds:0.0} s", job.FramesDone, outputs, job.Elapsed.TotalSeconds);
				break;
			case ExportState.Cancelled:
				Logger.Warn("Export cancelled after {Frames} of {Total} frames; {Outputs} hold what was written", job.FramesDone, job.FrameCount, outputs);
				break;
			default:
				Logger.Error("Export failed after {Frames} frames: {Message}", job.FramesDone, job.Error?.Message);
				if (job.Error != null)
				{
					ShowError((string)FindResource("ExportError"), job.Error);
				}
				break;
		}
		// the auto re-run held back during the export may be due now
		OnRunFinished();
	}

	/// <summary>Cancels a running export and waits briefly for its files to close; for closing the window.</summary>
	private void ShutdownExport()
	{
		ExportJob? job = Export;
		if (job?.IsActive != true)
		{
			return;
		}
		job.Cancel();
		if (!job.Completion.Wait(TimeSpan.FromSeconds(10)))
		{
			Logger.Warn("The export did not stop in time; its last file may be incomplete");
		}
	}
}
