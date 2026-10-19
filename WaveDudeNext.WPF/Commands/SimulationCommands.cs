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

using System.Windows.Input;

namespace WaveDudeNext.WPF.Commands;

/// <summary>
/// Commands that control the simulation run, bound on the main window (handlers in
/// <c>MainWindow.Simulation.cs</c>) and offered in the Simulation menu.
/// </summary>
public static class SimulationCommands
{
	/// <summary>Starts a run of the open project, replacing the previous run and its frames.</summary>
	public static readonly RoutedUICommand Run = new("Run", nameof(Run), typeof(SimulationCommands),
		[new KeyGesture(Key.F5)]);

	/// <summary>Pauses the run, or resumes a paused one.</summary>
	public static readonly RoutedUICommand TogglePause = new("Pause", nameof(TogglePause), typeof(SimulationCommands),
		[new KeyGesture(Key.F6)]);

	/// <summary>Makes sure the frames are coming: starts a run (keeping the preview's position) when there is none or it is stale; no gesture, the preview's Play issues it.</summary>
	public static readonly RoutedUICommand EnsureRun = new("Run if needed", nameof(EnsureRun), typeof(SimulationCommands));

	/// <summary>Sets the share of the memory a run's frames may take (the parameter: a percentage as text); the Simulation menu's choices.</summary>
	public static readonly RoutedUICommand SetFrameMemory = new("RAM cache size", nameof(SetFrameMemory), typeof(SimulationCommands));

	/// <summary>Stops the run; the frames computed so far stay in the preview. Shift+F5, or Escape (the usual "stop what you are doing" key, and reachable without a modifier).</summary>
	public static readonly RoutedUICommand Stop = new("Stop", nameof(Stop), typeof(SimulationCommands),
		[new KeyGesture(Key.F5, ModifierKeys.Shift), new KeyGesture(Key.Escape)]);
}
