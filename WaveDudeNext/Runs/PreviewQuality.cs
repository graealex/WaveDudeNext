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

namespace WaveDudeNext.Runs;

/// <summary>
/// How much of a run is kept for the preview (2026-10-15, the user's design for weaker machines and a first
/// step towards a browser build): the frames shrunk by <see cref="ResolutionDivisor"/> along each axis, only
/// every <see cref="FrameDivisor"/>-th frame of the timeline stored (the others are stood in for by the last
/// stored one; the timeline itself keeps the project's numbering), each sample a <see cref="FrameFormat.Half"/>
/// or a palette index byte. An application setting, not part of the project: the simulation itself is the
/// same, only what is captured, stored and shown changes, and an export always uses a <see cref="Full"/> run.
/// Immutable; compared by value.
/// </summary>
/// <param name="ResolutionDivisor">Cells per sample along each axis (1, 2, 4: full, half, quarter resolution).</param>
/// <param name="FrameDivisor">Timeline frames per stored frame (1, 2, 4: every frame, every second, every fourth); see <see cref="RunSpec.FrameStride"/>.</param>
/// <param name="Format">The sample format; <see cref="FrameFormat.Index"/> bakes the display brightness into the frames.</param>
public sealed record PreviewQuality(int ResolutionDivisor, int FrameDivisor, FrameFormat Format)
{
	/// <summary>Every cell of every frame as a <see cref="FrameFormat.Half"/>: what an export needs and the default.</summary>
	public static readonly PreviewQuality Full = new(1, 1, FrameFormat.Half);

	/// <summary>Whether this is <see cref="Full"/>.</summary>
	public bool IsFull => this == Full;

	/// <summary>Validates the record: both divisors at least 1.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A divisor is below 1.</exception>
	public PreviewQuality Validate()
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(ResolutionDivisor, 1, nameof(ResolutionDivisor));
		ArgumentOutOfRangeException.ThrowIfLessThan(FrameDivisor, 1, nameof(FrameDivisor));
		return this;
	}

	/// <inheritdoc/>
	public override string ToString() =>
		IsFull ? "full" : $"1/{ResolutionDivisor} resolution, every {FrameDivisor}. frame, {(Format == FrameFormat.Half ? "16-bit" : "8-bit")}";
}
