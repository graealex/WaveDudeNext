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

using WaveDudeNext.Model;

namespace WaveDudeNext.Engine;

/// <summary>
/// The physics settings a wave engine is created with, resolved from the model on the UI thread and read by
/// the engine constructors on the run's worker. Units: frequencies in radians per step, time in steps.
/// </summary>
/// <param name="SourceFrequencyScale"><see cref="Simulation.SourceFrequencyScale"/>: frequency of a source = level / 255 × 0.5 × this value.</param>
/// <param name="SourceAmplitude">Peak amplitude of the image (green-channel) sources, <see cref="Scene.ImageSourceScale"/>; object sources carry their own.</param>
/// <param name="TrackIntensity">Whether to allocate and update the intensity buffers (it costs extra time in every step); when false <see cref="IWaveEngine.IntensityData"/> stays null.</param>
/// <param name="FourthOrderLaplacian"><see cref="Simulation.FourthOrderLaplacian"/>: the 4th-order 5-point-per-axis stencil (with sub-stepping) instead of the 9-point one.</param>
/// <param name="TimeDecay"><see cref="Simulation.TimeDecay"/>: intensity averaging time in units of <see cref="WaveSimulator.NominalDecayLevel"/>/level steps.</param>
/// <param name="IntensityScale"><see cref="Simulation.IntensityScale"/>: multiplier applied to the intensity output.</param>
public sealed record EngineOptions(
	double SourceFrequencyScale,
	double SourceAmplitude,
	bool TrackIntensity,
	bool FourthOrderLaplacian,
	double TimeDecay,
	double IntensityScale)
{
	/// <summary>The options of a project's simulation and scene settings.</summary>
	/// <param name="simulation">Solver settings.</param>
	/// <param name="scene">Supplies the image source amplitude.</param>
	/// <param name="trackIntensity">Whether the run needs the intensity (an intensity preview, export or probe).</param>
	public static EngineOptions From(Simulation simulation, Scene scene, bool trackIntensity) => new(
		simulation.SourceFrequencyScale,
		scene.ImageSourceScale,
		trackIntensity,
		simulation.FourthOrderLaplacian,
		simulation.TimeDecay,
		simulation.IntensityScale);
}
