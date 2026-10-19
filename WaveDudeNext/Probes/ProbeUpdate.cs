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

namespace WaveDudeNext.Probes;

/// <summary>
/// One batch of probe results handed from the run's worker to the UI. Created by
/// <see cref="ProbeCollector.TakeForUi"/> on the worker, raised through the run's <c>ProbesUpdated</c> event
/// and applied to the UI's own <see cref="ProbeTrace"/>s on the UI thread. Never modified after construction,
/// so no locking is needed.
/// </summary>
public sealed class ProbeUpdate
{
	/// <param name="samples">Per probe: series values since the previous batch (see <see cref="Samples"/>).</param>
	/// <param name="profiles">Per probe: the line profiles recorded since the previous batch (see <see cref="Profiles"/>).</param>
	/// <param name="lengths">Per probe: line length in canvas pixels (see <see cref="Lengths"/>).</param>
	/// <param name="stepsDone">Steps completed when the batch was taken.</param>
	public ProbeUpdate(float[][] samples, float[][][] profiles, double[] lengths, int stepsDone)
	{
		Samples = samples;
		Profiles = profiles;
		Lengths = lengths;
		StepsDone = stepsDone;
	}

	/// <summary>
	/// Per probe (index parallel to the run's probe list): the series values sampled since the previous batch,
	/// in step order. Freshly allocated per batch; empty (never null) for line probes or when nothing new arrived.
	/// </summary>
	public float[][] Samples { get; }

	/// <summary>
	/// Per probe: the line profiles recorded since the previous batch, one per frame in frame order; empty
	/// (never null) for point probes or when no frame was recorded. The arrays are the very instances the
	/// worker's trace holds; they are never written to after the engine produced them, so sharing them is safe.
	/// </summary>
	public float[][][] Profiles { get; }

	/// <summary>Per probe: line length in canvas pixels (0 for point probes). This is the <see cref="ProbeSampler.Lengths"/> array itself, shared read-only by all batches.</summary>
	public double[] Lengths { get; }

	/// <summary>Simulation steps completed when the batch was taken; becomes <see cref="ProbeTrace.StepsSampled"/> on the UI side.</summary>
	public int StepsDone { get; }
}
