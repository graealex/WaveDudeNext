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

using WaveDudeNext.Engine;

namespace WaveDudeNext.Probes;

/// <summary>
/// Accumulates probe samples and line profiles from the engine into <see cref="ProbeTrace"/>s and hands
/// incremental batches to the UI. Lives entirely on the run's worker: <see cref="Collect"/> is called after
/// every recorded frame and about every 50 ms, <see cref="CollectProfiles"/> once per recorded frame (the
/// profiles are kept per frame); <see cref="TakeForUi"/> then packages what arrived since the previous batch
/// into a <see cref="ProbeUpdate"/>, which the run raises as <c>ProbesUpdated</c> and the UI applies to its own
/// traces. The UI thread never touches this object itself.
/// </summary>
public sealed class ProbeCollector
{
	/// <summary>Creates one empty <see cref="ProbeTrace"/> and one pending sample list per probe of <paramref name="sampler"/>.</summary>
	public ProbeCollector(ProbeSampler sampler)
	{
		_pending = [];
		_sampler = sampler;
		Traces = new ProbeTrace[sampler.Count];
		_totals = new double[sampler.Count];
		_pendingProfiles = [];
		for (int i = 0; i < sampler.Count; i++)
		{
			Traces[i] = new ProbeTrace();
			_pending.Add(new List<float>());
			_pendingProfiles.Add(new List<float[]>());
		}
	}

	/// <summary>
	/// Drains the engine's buffered point-probe samples (<see cref="IWaveEngine.TakeProbeSamples"/>, one value
	/// per step since the previous call) into the series traces and the pending UI batch. For cumulative
	/// probes (point + IntegratedValue) the per-step intensities are replaced in place by their running
	/// sum, which stays at 0 for steps before the probe's start step. Does nothing when there are no point
	/// probes.
	/// </summary>
	/// <param name="engine">Engine that buffered the samples since the previous call.</param>
	/// <param name="stepsDone">Steps completed so far; recorded as <see cref="ProbeTrace.StepsSampled"/>.</param>
	public void Collect(IWaveEngine engine, int stepsDone)
	{
		if (!_sampler.HasSeries)
		{
			return;
		}
		float[][] samples = engine.TakeProbeSamples();
		// the number of steps already processed before this batch, so the absolute step of values[j] can be worked
		// out below (one value per step, in order) - needed to purge a cumulative total from before "start after step"
		int batchStartStep = _lastStepsDone;
		for (int i = 0; i < _sampler.Count; i++)
		{
			if (_sampler.IsProfile[i] || i >= samples.Length)
			{
				continue;
			}
			float[] values = samples[i];
			if (values.Length == 0)
			{
				continue;
			}
			if (_sampler.Cumulative[i])
			{
				bool useStartAfterStep = _sampler.UseStartAfterStep[i];
				int startAfterStep = _sampler.StartAfterStep[i];
				for (int j = 0; j < values.Length; j++)
				{
					// sample j belongs to step batchStartStep + j + 1 (one sample per step)
					if (useStartAfterStep && batchStartStep + j + 1 < startAfterStep)
					{
						// before recording starts: keep the running total at zero, purging any intensity that
						// arrived earlier, so the total does not jump the moment recording starts
						_totals[i] = 0.0;
						values[j] = 0f;
					}
					else
					{
						_totals[i] += values[j];
						values[j] = (float)_totals[i];
					}
				}
			}
			Traces[i].Series.Add(values);
			Traces[i].StepsSampled = stepsDone;
			_pending[i].AddRange(values);
		}
		_lastStepsDone = stepsDone;
	}

	/// <summary>
	/// Reads the current line-probe profiles from the engine (<see cref="IWaveEngine.ReadProbeProfiles"/>),
	/// appends them to the traces as the profiles of the next frame and queues them for the next
	/// <see cref="TakeForUi"/>. Does nothing when there are no line probes. The run calls this exactly once
	/// per recorded frame, in frame order, so a trace's profile list is indexed by frame.
	/// </summary>
	/// <param name="engine">Engine holding the current profiles.</param>
	/// <param name="stepsDone">Steps completed so far; recorded as <see cref="ProbeTrace.StepsSampled"/>.</param>
	public void CollectProfiles(IWaveEngine engine, int stepsDone)
	{
		if (!_sampler.HasProfiles)
		{
			return;
		}
		float[]?[] profiles = engine.ReadProbeProfiles();
		for (int i = 0; i < _sampler.Count; i++)
		{
			if (!_sampler.IsProfile[i] || i >= profiles.Length || profiles[i] is not { } profile)
			{
				continue;
			}
			Traces[i].AddProfile(profile, _sampler.Lengths[i]);
			Traces[i].StepsSampled = stepsDone;
			_pendingProfiles[i].Add(profile);
		}
		_lastStepsDone = stepsDone;
	}

	/// <summary>
	/// Drains the pending series samples and the profiles recorded since the previous call into a
	/// <see cref="ProbeUpdate"/> for the UI thread. Sample arrays are copied; profile arrays are handed over by
	/// reference, which is safe because the engine allocates a new array on every read and nobody writes to it
	/// afterwards.
	/// </summary>
	/// <returns>A batch covering everything since the previous call; never null.</returns>
	public ProbeUpdate TakeForUi()
	{
		int count = _sampler.Count;
		float[][] samples = new float[count][];
		float[][][] profiles = new float[count][][];
		for (int i = 0; i < count; i++)
		{
			samples[i] = _pending[i].ToArray();
			_pending[i].Clear();
			profiles[i] = _pendingProfiles[i].ToArray();
			_pendingProfiles[i].Clear();
		}
		return new ProbeUpdate(samples, profiles, _sampler.Lengths, _lastStepsDone);
	}

	/// <summary>Worker-side trace per probe (index parallel to the sampler).</summary>
	public ProbeTrace[] Traces { get; }

	/// <summary>Per-probe pixel indices, flags and line lengths; immutable.</summary>
	private readonly ProbeSampler _sampler;

	/// <summary>Per probe: running sum for cumulative point probes, kept in double to limit drift; unused entries stay 0.</summary>
	private readonly double[] _totals;

	/// <summary>Per probe: series values collected since the last <see cref="TakeForUi"/>.</summary>
	private readonly List<List<float>> _pending;

	/// <summary>Per probe: the profiles recorded since the last <see cref="TakeForUi"/>, in frame order.</summary>
	private readonly List<List<float[]>> _pendingProfiles;

	/// <summary>Steps completed at the most recent collect call; numbers the samples of the next batch and is reported as <see cref="ProbeUpdate.StepsDone"/>.</summary>
	private int _lastStepsDone;
}
