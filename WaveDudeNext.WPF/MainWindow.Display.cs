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
using WaveDudeNext.Engine;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.WPF.Properties;

namespace WaveDudeNext.WPF;

/// <summary>
/// The pictures' Auto brightness (2026-10-20, Jeroen's point 9; the user's choice of the variants: a one-shot fit to
/// the frame shown, the brightness then staying put, so a decaying wave still looks like one). A frame of halves
/// (a 16-bit run, or a scratch frame in 16-bit) is fitted at once; palette indices are clipped at the brightness
/// they were captured with, so for an 8-bit preview the frame is computed once more in 16-bit, on an engine of its
/// own, and fitted when it arrives. The fit is an ordinary brightness edit: in 8-bit it is a simulation edit as
/// any other. UI thread.
/// </summary>
public partial class MainWindow
{
	/// <summary>Computes the 16-bit frame of an 8-bit preview for a fit; null between fits (its engine is released).</summary>
	private ScratchRunner? _fitRunner;

	/// <summary>The layer and frame the pending fit is for.</summary>
	private (ProbeLayer Layer, int Frame) _fitRequest;

	/// <summary>The run paused while the fit's frame computes (the user's frame comes first, as for scratch frames).</summary>
	private SimulationRun? _pausedForFit;

	private void CanAutoBrightness(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = _fitRunner == null && !IsExporting && Project.Simulation.Steps > 0;

	private void OnAutoBrightness(object sender, ExecutedRoutedEventArgs e)
	{
		ProbeLayer layer = e.Parameter is ProbeLayer.Intensity ? ProbeLayer.Intensity : ProbeLayer.Field;
		FrameLayer plane = PlaneOf(layer);
		if (previewPane.ShownFrame is { Source: { Format: FrameFormat.Half } source, Frame: int shown } && source.Has(plane) && source.Count > 0)
		{
			var halves = new Half[source.Width * source.Height];
			if (source.CopyLayer(Math.Clamp(shown, 0, source.Count - 1), plane, halves))
			{
				ApplyFit(layer, BrightnessFit.For(halves, source.KindOf(plane)), PreviewPosition);
				return;
			}
		}
		RunSpec spec;
		try
		{
			spec = RunSpec.Create(Project, LoadSceneImage(), Settings.Default.UseGpu, trackIntensity: plane == FrameLayer.Intensity,
				quality: PreviewQualitySetting with { Format = FrameFormat.Half });
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			Logger.Warn("No brightness fit: {Message}", ex.Message);
			return;
		}
		int frame = Math.Clamp(PreviewPosition, 0, Math.Max(0, spec.FrameCount - 1));
		Logger.Info("Fitting the {Layer} brightness: frame {Frame} is computed in 16-bit", layer == ProbeLayer.Field ? "field" : "intensity", frame);
		if (Run is { IsActive: true } run && !run.IsPauseRequested)
		{
			run.Pause();
			_pausedForFit = run;
		}
		_fitRequest = (layer, frame);
		_fitRunner = new ScratchRunner(SynchronizationContext.Current);
		_fitRunner.Completed += OnFitFrameCompleted;
		_fitRunner.Request(spec, frame);
		CommandManager.InvalidateRequerySuggested();
	}

	private void OnFitFrameCompleted(object? sender, ScratchResult result)
	{
		ScratchRunner? runner = _fitRunner;
		_fitRunner = null;
		if (runner != null)
		{
			runner.Completed -= OnFitFrameCompleted;
		}
		if (_pausedForFit is { } paused)
		{
			_pausedForFit = null;
			if (ReferenceEquals(paused, Run) && paused.IsActive)
			{
				paused.Resume();
			}
		}
		FrameLayer plane = PlaneOf(_fitRequest.Layer);
		IFrameSource source = new SnapshotFrameSource(result.Snapshot);
		var halves = new Half[source.Width * source.Height];
		double? fit = source.Has(plane) && source.CopyLayer(0, plane, halves) ? BrightnessFit.For(halves, source.KindOf(plane)) : null;
		// the runner's engine is released once its result is read (the snapshot belongs to the runner)
		runner?.Dispose();
		ApplyFit(_fitRequest.Layer, fit, result.Frame);
		CommandManager.InvalidateRequerySuggested();
	}

	/// <summary>Sets the fitted brightness on the layer, or says there was nothing to fit.</summary>
	private void ApplyFit(ProbeLayer layer, double? fit, int frame)
	{
		string name = layer == ProbeLayer.Field ? "field" : "intensity";
		DisplayLayer settings = layer == ProbeLayer.Field ? Project.Display.Field : Project.Display.Intensity;
		if (fit is not { } brightness)
		{
			Logger.Info("No {Layer} brightness fitted: frame {Frame} is empty", name, frame);
			return;
		}
		Logger.Info("The {Layer} brightness is fitted to frame {Frame}: {Old:G4} becomes {New:G4}", name, frame, settings.Brightness, brightness);
		settings.Brightness = brightness;
	}

	private static FrameLayer PlaneOf(ProbeLayer layer) => layer == ProbeLayer.Intensity ? FrameLayer.Intensity : FrameLayer.Field;
}
