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

// Contributors: Vinicius Miguel

using WaveDudeNext.Engine;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>The memory-warning choices for one run. Immutable; usable on the UI thread.</summary>
internal sealed class MemoryWarningPlan(RunSpec spec, long budget, long autoBudget, PreviewQuality current)
{
	private const int MaxDivisor = 4;

	/// <summary>Whether changing the cache to Auto would fit at least one more frame.</summary>
	public bool CanUseAuto => autoBudget - budget >= FrameStore.MemoryOf(spec.Shape, spec.Layers, 1);
	/// <summary>Whether resolution can be halved.</summary>
	public bool CanReduceResolution => current.ResolutionDivisor < MaxDivisor;
	/// <summary>Whether the stored frame rate can be halved.</summary>
	public bool CanReduceFrames => current.FrameDivisor < MaxDivisor;
	/// <summary>Whether samples can change from halves to palette indices.</summary>
	public bool CanUseIndices => current.Format == FrameFormat.Half;

	/// <summary>The quality represented by the three quality choices.</summary>
	public PreviewQuality Quality(bool resolution, bool frames, bool samples) => current with
	{
		ResolutionDivisor = resolution ? Math.Min(current.ResolutionDivisor * 2, MaxDivisor) : current.ResolutionDivisor,
		FrameDivisor = frames ? Math.Min(current.FrameDivisor * 2, MaxDivisor) : current.FrameDivisor,
		Format = samples ? FrameFormat.Index : current.Format,
	};

	/// <summary>Bytes needed and the applicable RAM budget for a choice.</summary>
	public (long Bytes, long Budget) Estimate(bool cache, bool resolution, bool frames, bool samples) =>
		(spec.StoreBytesAt(Quality(resolution, frames, samples)), cache ? autoBudget : budget);

	/// <summary>The least intrusive available combination that fits, or all available choices if none fits.</summary>
	public (bool Cache, bool Resolution, bool Frames, bool Samples) Proposal()
	{
		var fitting = new List<(bool Cache, bool Resolution, bool Frames, bool Samples, int Ticks, long Bytes)>();
		for (int mask = 1; mask < 16; mask++)
		{
			bool samples = (mask & 1) != 0;
			bool frames = (mask & 2) != 0;
			bool resolution = (mask & 4) != 0;
			bool cache = (mask & 8) != 0;
			if ((cache && !CanUseAuto) || (resolution && !CanReduceResolution) || (frames && !CanReduceFrames) || (samples && !CanUseIndices))
			{
				continue;
			}
			(long bytes, long available) = Estimate(cache, resolution, frames, samples);
			if (bytes <= available)
			{
				fitting.Add((cache, resolution, frames, samples, (cache ? 1 : 0) + (resolution ? 1 : 0) + (frames ? 1 : 0) + (samples ? 1 : 0), bytes));
			}
		}
		if (fitting.Count == 0)
		{
			return (CanUseAuto, CanReduceResolution, CanReduceFrames, CanUseIndices);
		}
		var best = fitting.OrderBy(choice => choice.Ticks).ThenByDescending(choice => choice.Bytes).First();
		return (best.Cache, best.Resolution, best.Frames, best.Samples);
	}
}
