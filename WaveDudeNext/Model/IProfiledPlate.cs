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

namespace WaveDudeNext.Model;

/// <summary>
/// A plate whose front face (−x') is a profile over y', one sample per pixel across the width (a <see cref="DiffuserPart"/>'s
/// roughness, a <see cref="FacettedGratingPart"/>'s facets): what the shared shape (<c>DiffuserShape</c>) paints.
/// </summary>
public interface IProfiledPlate
{
	/// <summary>Extent along the axis in pixels: the plate's thickness at the mean of its front face.</summary>
	int Length { get; }

	/// <summary>Extent across the axis in pixels: the width of the front face.</summary>
	int Width { get; }

	/// <summary>The front face's x' at every whole pixel across the face, from −width / 2 to width / 2 (width + 1 values), each at least a pixel before the back.</summary>
	double[] FrontFace();
}
