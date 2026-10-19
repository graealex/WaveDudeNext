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

namespace WaveDudeNext.Runs;

/// <summary>Where a <see cref="SimulationRun"/> is in its life.</summary>
public enum RunState
{
	/// <summary>Constructed, <see cref="SimulationRun.Start"/> not called yet.</summary>
	Created,
	/// <summary>The worker is building the scene and the engine.</summary>
	Starting,
	/// <summary>Stepping.</summary>
	Running,
	/// <summary>Parked at the pause gate; <see cref="SimulationRun.Resume"/> continues.</summary>
	Paused,
	/// <summary>Finished all steps.</summary>
	Completed,
	/// <summary>Stopped by <see cref="SimulationRun.Stop"/> before the last step.</summary>
	Cancelled,
	/// <summary>Ended with an error (<see cref="SimulationRun.Error"/>).</summary>
	Failed,
}
