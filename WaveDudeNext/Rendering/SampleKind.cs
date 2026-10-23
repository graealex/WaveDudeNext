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

namespace WaveDudeNext.Rendering;

/// <summary>
/// What a 16-bit sample holds, which decides its position on the gradient (<see cref="ColorLut.HalfTable"/>); a frame
/// source tells it per plane (<see cref="Runs.FrameSources.KindOf"/>).
/// </summary>
public enum SampleKind
{
	/// <summary>The signed field: <c>(clamp(v · brightness, −1, 1) + 1) / 2</c>, zero in the gradient's middle.</summary>
	Field,

	/// <summary>The intensity itself, as a run for an OpenEXR export of I stores it: <c>min(√v · brightness, 1)</c>.</summary>
	Intensity,

	/// <summary>The intensity's square root, as the preview stores it (2026-10-23): <c>min(v · brightness, 1)</c>.</summary>
	IntensityRoot,
}
