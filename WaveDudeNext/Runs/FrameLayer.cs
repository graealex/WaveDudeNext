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

/// <summary>One plane of a stored frame.</summary>
public enum FrameLayer
{
	/// <summary>The wave field, signed amplitude.</summary>
	Field,
	/// <summary>The averaged intensity, non-negative.</summary>
	Intensity,
}

/// <summary>The set of planes a <see cref="FrameStore"/> holds per frame.</summary>
[Flags]
public enum FrameLayers
{
	/// <summary>No plane.</summary>
	None = 0,
	/// <summary><see cref="FrameLayer.Field"/>.</summary>
	Field = 1,
	/// <summary><see cref="FrameLayer.Intensity"/>.</summary>
	Intensity = 2,
}
