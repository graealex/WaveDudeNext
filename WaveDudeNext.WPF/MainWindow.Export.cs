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

	/// <summary>The layer suffixes of an export's file name, which an export of both layers appends itself.</summary>
	private static readonly string[] LayerSuffixes = ["_field", "_intensity"];

	/// <summary>A run's export as it was made: the layers, the format and the path, its extension the format's.</summary>
	private sealed record RunExport(bool Field, bool Intensity, ExportFormat Format, string Path);

	/// <summary>Projects whose export paths the settings keep (<see cref="Settings.ExportHints"/>), the oldest dropped beyond it.</summary>
	private const int ExportHintCount = 20;

	/// <summary>
	/// This session's last export of a run's pictures, which Export last (<see cref="ExportCommands.Last"/>) makes again as
	/// it was; null before the first since a project was opened, when the command is disabled (2026-10-28, the user's
	/// request; a still is no "last export", his choice). Never restored from the settings: a repeat as the first keystroke
	/// of a session would replace a file without a question.
	/// </summary>
	private RunExport? _lastRunExport;

	/// <summary>
	/// Where the last export of a run's pictures went, which the next such dialog proposes as folder and name, a layer
	/// suffix swapped for the export's own; null when the dialog starts in the project's folder under the project's name
	/// as it always did (2026-10-28, Jeroen's report: every dialog started in the project's folder, so exports elsewhere
	/// meant navigating there each time). Set by each export of the kind, kept per project file in the user settings
	/// (<see cref="ExportHint"/>, the user's choice, 2026-10-28: a path belongs to the machine, not to a project file
	/// that may be shared) and restored when the project is opened; a project without a file keeps the session's alone.
	/// </summary>
	private string? _runExportPath;

	/// <summary>Where the last still went: the next still's dialog starts in its folder, the name the frame's own.</summary>
	private string? _stillExportPath;

	/// <summary>Where the last probe data went: the next probe's dialog starts in its folder, the name the probe's own.</summary>
	private string? _probeExportPath;

	/// <summary>A project opened, imported or made new: nothing to repeat, and the dialogs start in the project's folder until <see cref="RestoreExportPaths"/> finds a hint.</summary>
	private void ForgetExportPaths()
	{
		_lastRunExport = null;
		_runExportPath = null;
		_stillExportPath = null;
		_probeExportPath = null;
	}

	/// <summary>A project opened from its file: the paths the settings keep for it, if any, so its dialogs start where its last exports went.</summary>
	private void RestoreExportPaths()
	{
		if (Filename is not { } file || Settings.Default.ExportHints.Find(h => string.Equals(h.Project, file, StringComparison.OrdinalIgnoreCase)) is not { } hint)
		{
			return;
		}
		_runExportPath = hint.Run.Length > 0 ? hint.Run : null;
		_stillExportPath = hint.Still.Length > 0 ? hint.Still : null;
		_probeExportPath = hint.Probe.Length > 0 ? hint.Probe : null;
		Logger.Debug("Export paths restored from the settings: run {Run}, still {Still}, probe {Probe}", hint.Run, hint.Still, hint.Probe);
	}

	/// <summary>
	/// Keeps the export paths for the project file in the user settings, this project first, the others after it up to
	/// <see cref="ExportHintCount"/> and only while their project files exist; a project without a file keeps nothing.
	/// </summary>
	private void RememberExportPaths()
	{
		if (Filename is not { } file)
		{
			return;
		}
		// a fresh list, as the recent files are kept
		var hints = new List<ExportHint> { new() { Project = file, Run = _runExportPath ?? "", Still = _stillExportPath ?? "", Probe = _probeExportPath ?? "" } };
		foreach (ExportHint entry in Settings.Default.ExportHints)
		{
			if (!string.Equals(entry.Project, file, StringComparison.OrdinalIgnoreCase) && hints.Count < ExportHintCount && File.Exists(entry.Project))
			{
				hints.Add(entry);
			}
		}
		Settings.Default.ExportHints = hints;
		SaveSettings();
	}

	/// <summary>
	/// Where an export dialog starts and the name it proposes: the folder of <paramref name="remembered"/>, this session's
	/// last export of the kind, while it exists, else the project's folder, else the settings' last export folder; the name
	/// <paramref name="suggested"/>, or with <paramref name="takeName"/> the remembered file's stem, a layer suffix at its
	/// end swapped for <paramref name="suffix"/> (the intensity exported after the field is not proposed the field's file).
	/// </summary>
	private (string Folder, string Name) ExportStart(string? remembered, bool takeName, string suggested, string suffix = "")
	{
		string? folder = Path.GetDirectoryName(remembered);
		if (folder == null || !Directory.Exists(folder))
		{
			return (ProjectFolder ?? (Directory.Exists(Settings.Default.LastExportFolder) ? Settings.Default.LastExportFolder : ""), suggested);
		}
		string name = suggested;
		if (takeName)
		{
			name = Path.GetFileNameWithoutExtension(remembered!);
			foreach (string known in LayerSuffixes)
			{
				if (name.EndsWith(known, StringComparison.OrdinalIgnoreCase))
				{
					name = name[..^known.Length] + suffix;
					break;
				}
			}
		}
		Logger.Debug("Export dialog starts where the session's last export went: {Folder}, as {Name}", folder, name);
		return (folder, name);
	}

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
			(string folder, string name) = ExportStart(_probeExportPath, takeName: false, suggested);
			var dialog = new SaveFileDialog
			{
				Title = (string)FindResource("ExportDialogTitleProbeCsv"),
				Filter = (string)FindResource("ExportDialogFilterCsv"),
				AddExtension = true,
				DefaultExt = ProbeCsv.Extension,
				FileName = name,
				InitialDirectory = folder,
			};
			if (dialog.ShowDialog(this) != true)
			{
				Logger.Debug("Probe export cancelled in the Save dialog");
				return;
			}
			chosen = dialog.FileName;
			Settings.Default.LastExportFolder = Path.GetDirectoryName(chosen) ?? "";
		}
		_probeExportPath = chosen;
		RememberExportPaths();
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
			// the frame number, padded like a sequence's files, so a still of frame 655 is named as the sequence names it
			// (2026-10-19, the user's rule; the step number stood here until then)
			suffix += "_" + stillFrame.ToString("D" + ImageSequenceSink.MinDigits, System.Globalization.CultureInfo.InvariantCulture);
		}
		string chosen;
		// the remembered filter is an index into the full list; the still's filter lists the sequence formats alone, so a
		// remembered video means PNG there
		ExportFormat[] formats = still ? ExportFormats.Stills : ExportFormats.All;
		ExportFormat remembered = ExportFormats.All[Math.Clamp(Settings.Default.ExportFilterIndex, 0, ExportFormats.All.Length - 1)];
		int filterIndex = Math.Max(0, Array.IndexOf(formats, remembered)) + 1;
		string? scripted = Environment.GetEnvironmentVariable("WAVEDUDE_EXPORT_PATH");
		if (!string.IsNullOrEmpty(scripted))
		{
			// a UI harness cannot drive the common dialog by posted keys: it hands the path in instead, "{name}" standing for the suggested name
			chosen = scripted.Replace("{name}", ProjectName() + suffix, StringComparison.Ordinal);
			Logger.Debug("Export path taken from WAVEDUDE_EXPORT_PATH: {Path}", chosen);
		}
		else
		{
			// a still keeps the frame's name and takes the last still's folder; a run's export takes the last one's folder and name
			(string folder, string name) = ExportStart(still ? _stillExportPath : _runExportPath, takeName: !still, ProjectName() + suffix, suffix);
			var dialog = new SaveFileDialog
			{
				Title = (string)FindResource(still ? "ExportDialogTitleFrame" : field && intensity ? "ExportDialogTitleBoth" : field ? "ExportDialogTitleField" : "ExportDialogTitleIntensity"),
				Filter = (string)FindResource(still ? "ExportDialogFilterStill" : "ExportDialogFilter"),
				FilterIndex = filterIndex,
				AddExtension = true,
				DefaultExt = ExportFormats.Extension(formats[filterIndex - 1]),
				FileName = name,
				InitialDirectory = folder,
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
		// the filter the user chose is the format, whatever extension was typed (the two MOV formats share theirs); a
		// scripted path has no filter and says its format by its extension, a .mov meaning the Animation codec
		ExportFormat format = formats[Math.Clamp(filterIndex - 1, 0, formats.Length - 1)];
		if (!string.IsNullOrEmpty(scripted))
		{
			format = ExportFormats.FromPath(chosen) ?? format;
		}
		if (still && !ExportFormats.IsSequence(format))
		{
			format = ExportFormat.Png;
		}
		if (!ExportFormats.Accepts(format, chosen))
		{
			chosen = Path.ChangeExtension(chosen, ExportFormats.Extension(format));
		}
		// remembered for the next dialog of the kind, in the settings too, and a run's for Export last
		if (still)
		{
			_stillExportPath = chosen;
		}
		else
		{
			_runExportPath = chosen;
			_lastRunExport = new RunExport(field, intensity, format, chosen);
		}
		RememberExportPaths();
		await StartExport(field, intensity, still, stillFrame, format, chosen, ask: true);
	}

	/// <summary>Export last (Ctrl+E, 2026-10-28, the user's request): the session's last run export again, the same layers, format and path, no dialog and no question about what is there.</summary>
	private async void OnExportLast(object sender, ExecutedRoutedEventArgs e)
	{
		if (IsExporting || _lastRunExport is not { } last)
		{
			return;
		}
		Logger.Debug("Export last: the last export made again");
		await StartExport(last.Field, last.Intensity, still: false, stillFrame: 0, last.Format, last.Path, ask: false);
	}

	/// <summary>Something to repeat: a run export since the project was opened.</summary>
	private void CanExportLast(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _lastRunExport != null && !IsExporting && Project.Simulation.Steps > 0;

	/// <summary>
	/// The run (started first when there is none or it is stale), the targets, the job: the pictures <paramref name="field"/>
	/// and <paramref name="intensity"/> as <paramref name="format"/> at <paramref name="chosen"/>, whose extension is the
	/// format's; a <paramref name="still"/> is the one frame <paramref name="stillFrame"/> as a single image file.
	/// </summary>
	/// <param name="ask">Whether a sequence folder that exists with files in it is replaced only on request; Export last replaces it without asking.</param>
	private async Task StartExport(bool field, bool intensity, bool still, int stillFrame, ExportFormat format, string chosen, bool ask)
	{
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
			foreach (string known in LayerSuffixes)
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
		// a sequence goes to a folder of its own; one that exists with files in it is replaced only on request (Export last asks nothing)
		if (!still && ExportFormats.IsSequence(format))
		{
			foreach ((_, string path) in outputs)
			{
				string folder = ExportFormats.SequenceFolder(path);
				if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
				{
					continue;
				}
				if (!ask)
				{
					Logger.Debug("Export last: {Folder} replaced without asking", folder);
				}
				else if (MessageWindow.Show(this, string.Format((string)FindResource("ExportFolderExistsFormat"), folder), MessageKind.Question, MessageButtons.YesNo) != MessageAnswer.Yes)
				{
					Logger.Debug("Export cancelled: {Folder} kept", folder);
					return;
				}
			}
		}
		SimulationRun? run = await EnsureRunForExport(intensity, needsHalves: true, intensityRoot: intensity ? ExportFormats.IntensityRootFor(format) : null);
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
			// the sources on both pictures, under the probes' markers and boxes (as in the preview, where the boxes lie over every handle)
			SourceMarkerPainter? sources = display.SourceMarkersInExport ? new SourceMarkerPainter(Project, run.Spec.Width, run.Spec.Height) : null;
			if (sources is { IsEmpty: true })
			{
				Logger.Debug("Export source markers: none (no enabled source)");
				sources = null;
			}
			foreach ((FrameLayer layer, string path) in outputs)
			{
				DisplayLayer settings = layer == FrameLayer.Field ? display.Field : display.Intensity;
				ColorLut lut = ColorSchemeCatalog.Instance.LutFor(Project, settings.ColorScheme, settings.BlackLevel);
				Action<int, Memory<int>>? probes = painter?.For(layer == FrameLayer.Field ? ProbeLayer.Field : ProbeLayer.Intensity);
				Action<int, Memory<int>>? decorate = sources == null ? probes : (frame, pixels) =>
				{
					sources.Paint(pixels);
					probes?.Invoke(frame, pixels);
				};
				var exportLayer = new ExportLayer(layer, lut, (float)settings.Brightness, settings.OverlayInExport ? edges : null, settings.OverlayGain, decorate);
				// the sink is opened by the job on its thread (a Media Foundation writer must stay on one thread)
				int width = run.Spec.Width;
				int height = run.Spec.Height;
				int fps = display.Video.Fps;
				int quality = display.Video.Quality;
				string output = !still && ExportFormats.IsSequence(format) ? ExportFormats.SequenceFolder(path) : path;
				Func<IFrameSink> open = still
					? () => ExportFormats.OpenStill(format, path, width, height, quality)
					: () => ExportFormats.Open(format, path, width, height, fps, quality, frameCount, first);
				targets.Add(new ExportTarget(exportLayer, output, open));
				if (ExportFormats.IsData(format))
				{
					Logger.Debug("Export target {Layer}: the stored 16-bit values as OpenEXR{Samples} (no colours, outline or boxes), to {Output}",
						layer, layer == FrameLayer.Intensity ? (run.Store.IntensityRoot ? ", the intensity's square root" : ", the intensity itself") : "", output);
				}
				else
				{
					Logger.Debug("Export target {Layer}: scheme '{Scheme}' (black level {Black}), brightness {Brightness}, outline {Outline}, probe boxes {Boxes}, markers {Markers}, sources {Sources}, to {Output}",
						layer, settings.ColorScheme, settings.BlackLevel, settings.Brightness, settings.OverlayInExport && edges != null ? "gain " + settings.OverlayGain : "off", display.ProbeGraphsInExport ? "on" : "off", display.ProbeMarkersInExport ? "on" : "off", sources != null ? "on" : "off", output);
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
	private string ProjectName() => Path.GetFileNameWithoutExtension(Filename ?? SuggestedFileName ?? "project");

	/// <summary>
	/// A completed run the export can read: the current one when it is fresh, finished or in progress, and
	/// holds the intensity plane if asked for; otherwise a run is started and awaited. Null when no run could
	/// be had (logged, and an error shown when the run could not start).
	/// </summary>
	/// <param name="intensity">The run must hold the intensity plane.</param>
	/// <param name="needsHalves">The run must hold 16-bit samples (every picture or data export); the probe data does with the 8-bit index.</param>
	/// <param name="intensityRoot">What the run's 16-bit intensity must hold for an OpenEXR export of it: its square root (true), the intensity (false), or either (null).</param>
	private async Task<SimulationRun?> EnsureRunForExport(bool intensity, bool needsHalves, bool? intensityRoot = null)
	{
		IsExportWaiting = true;
		try
		{
			for (int attempt = 0; attempt < 3; attempt++)
			{
				SimulationRun? run = Run;
				// a reduced preview never serves a picture or data export: a smaller picture or skipped frames are not the
				// run, and the 8-bit index has only the palette's 255 colours where the 16-bit samples give every level of the
				// gradient (2026-10-23, the user's choice; from 2026-10-18 a picture export had taken an 8-bit run as it was)
				PreviewQuality? quality = run?.Spec.Quality;
				bool reduced = quality != null && (quality.ResolutionDivisor != 1 || quality.FrameDivisor != 1 || (quality.Format != FrameFormat.Half && needsHalves));
				string? reason = run == null ? "no run" : IsRunStale ? "the run is stale" : IsProbesStale ? "the probes were edited since the run"
					: reduced ? (quality!.Format != FrameFormat.Half && needsHalves && quality.ResolutionDivisor == 1 && quality.FrameDivisor == 1 ? "the export needs the 16-bit samples" : "the run is a reduced preview (View menu)")
					: intensity && !run.Store.Has(FrameLayer.Intensity) ? "the run has no intensity plane"
					: intensityRoot is bool root && run.Store.IntensityRoot != root ? (root ? "the export needs the intensity as its square root" : "the export needs the intensity as I, which the preview stores as its square root")
					: !run.IsActive && run.State != RunState.Completed ? "the run did not complete" : null;
				if (reason != null)
				{
					Logger.Debug("Export: a run is started first ({Reason})", reason);
					StartRun(automatic: true, trackIntensity: intensity, fullQuality: true, intensityRoot: intensityRoot ?? true);
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
