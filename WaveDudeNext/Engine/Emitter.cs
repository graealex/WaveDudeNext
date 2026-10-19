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
/// One point emitter as the wave engines consume it: a resolved copy of a <see cref="Source"/> and one of
/// its generated <see cref="SourceElement"/>s with all units already converted (phase in radians, amplitude
/// 0..1). Built on the UI thread by <see cref="FromSource"/> / <see cref="FromProject"/>, handed to
/// <see cref="WaveSimulator"/> / <see cref="GpuWaveSimulator"/> and read from the run's worker; immutable,
/// so sharing it between threads is safe. The engines keep their own per-emitter off-width state and never
/// write to it.
/// </summary>
/// <remarks>
/// Determinism: <see cref="RngSeed"/> is drawn from the project seed, so a run reproduces from the project
/// file alone; both engines advance that seed with the same xorshift32 generator when an emitter redraws
/// its off-width, so the CPU and the GPU engine agree on the gap sequence too.
/// </remarks>
public sealed record Emitter
{
	/// <summary>Steps over which a detached emitter fades out linearly before it stops driving the field (see <see cref="DetachAfterStep"/>).</summary>
	public const int DetachRampSteps = 30;

	/// <summary>
	/// Stream selector XOR-ed onto the name hash, so the gap seeds come from a different <see cref="Pcg32"/>
	/// stream than the position/frequency jitter of <see cref="Source.CreateRandom"/>.
	/// </summary>
	private const ulong GapStream = 0x47415053UL;

	/// <summary>X position in canvas pixels (0 = left edge). Emitters outside the canvas are dropped by the engines.</summary>
	public required int X { get; init; }

	/// <summary>Y position in canvas pixels (0 = top edge). Emitters outside the canvas are dropped by the engines.</summary>
	public required int Y { get; init; }

	/// <summary>
	/// Frequency as a source level 1..255, the same scale as the brightness of image-based sources. The
	/// engines map it to an angular frequency of <c>Level / 255 × 0.5 × SourceFrequencyScale</c> radians per step.
	/// </summary>
	public required int Level { get; init; }

	/// <summary>Peak field amplitude 0..1 (the source's 0..100 % divided by 100).</summary>
	public required double Amplitude { get; init; }

	/// <summary>Phase offset in radians added to the sine argument (<c>sin(PhaseRadians + ω·t)</c>); positive values lead.</summary>
	public required double PhaseRadians { get; init; }

	/// <summary>Continuous or pulsed emission.</summary>
	public required SourceMode Mode { get; init; }

	/// <summary>Envelope shape of each burst; only used when <see cref="Mode"/> is <see cref="SourceMode.Pulsed"/>.</summary>
	public required PulseShape PulseShape { get; init; }

	/// <summary>Length of the "on" burst in cycles of this emitter's own frequency (pulsed mode only).</summary>
	public required int OnWidthCycles { get; init; }

	/// <summary>
	/// Length of the "off" gap before each burst in cycles (pulsed mode only), as rolled once at generate time
	/// (the element's jittered value). The engines start every emitter with this value and, for emitters with
	/// <see cref="RandomizeOffWidthAmount"/> &gt; 0, redraw it per period from <see cref="OffWidthBase"/>.
	/// </summary>
	public required int OffWidthCycles { get; init; }

	/// <summary>The source's un-jittered off-width in cycles: the centre of every per-period redraw.</summary>
	public required int OffWidthBase { get; init; }

	/// <summary>
	/// Spread of the per-period off-width redraw (uniform in ±amount cycles around <see cref="OffWidthBase"/>);
	/// 0 keeps <see cref="OffWidthCycles"/> for the whole run.
	/// </summary>
	public required int RandomizeOffWidthAmount { get; init; }

	/// <summary>Step after which the emitter fades out over <see cref="DetachRampSteps"/> steps and then stops driving the field; -1 never.</summary>
	public required int DetachAfterStep { get; init; }

	/// <summary>Non-zero starting state of the emitter's xorshift32 generator for the off-width redraws.</summary>
	public required uint RngSeed { get; init; }

	/// <summary>
	/// The emitters of one source: one per generated element, with the source's shared settings resolved
	/// and a gap seed per element from the project seed and the source name. Returns an empty list for a
	/// disabled source. UI thread; call <see cref="Source.GenerateElements"/> first when the settings may
	/// have changed since the elements were generated.
	/// </summary>
	/// <param name="source">The source, with its <see cref="Source.Elements"/> generated.</param>
	/// <param name="projectSeed">The project's <see cref="Project.Seed"/>.</param>
	public static List<Emitter> FromSource(Source source, int projectSeed)
	{
		var emitters = new List<Emitter>();
		if (!source.Enabled)
		{
			return emitters;
		}
		var rng = new Pcg32((ulong)(uint)projectSeed, Pcg32.StableHash(source.Name) ^ GapStream);
		foreach (SourceElement element in source.Elements)
		{
			uint seed = rng.NextUInt32();
			emitters.Add(new Emitter
			{
				X = element.X,
				Y = element.Y,
				Level = element.Level,
				Amplitude = source.Amplitude / 100.0,
				PhaseRadians = element.Phase * Math.PI / 180.0,
				Mode = source.Mode,
				PulseShape = source.PulseShape,
				OnWidthCycles = source.PulseWidth,
				OffWidthCycles = element.OffWidth,
				OffWidthBase = source.OffWidth,
				RandomizeOffWidthAmount = source.Array?.RandomOffWidth ?? 0,
				DetachAfterStep = source.DetachAfter,
				// xorshift32 never leaves state 0, so a zero seed would make the off-width constant
				RngSeed = seed == 0 ? 1u : seed,
			});
		}
		return emitters;
	}

	/// <summary>
	/// The emitters of every enabled source of the project, or an empty list when the scene does not use
	/// object sources (<see cref="Scene.ObjectSources"/>). UI thread; regenerates the elements first.
	/// </summary>
	public static List<Emitter> FromProject(Project project)
	{
		var emitters = new List<Emitter>();
		if (!project.Scene.ObjectSources)
		{
			return emitters;
		}
		foreach (Source source in project.Sources)
		{
			source.GenerateElements(project.Seed);
			emitters.AddRange(FromSource(source, project.Seed));
		}
		return emitters;
	}

	/// <summary>
	/// One xorshift32 step, the generator both engines use for the per-period off-width redraw (OpenCL has no
	/// built-in RNG, so the GPU kernel carries the same state per emitter). Never returns 0 for a non-zero input.
	/// </summary>
	public static uint Xorshift32(uint x)
	{
		x ^= x << 13;
		x ^= x >> 17;
		x ^= x << 5;
		return x;
	}
}
