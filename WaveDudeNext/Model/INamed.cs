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
/// A project object with a name of its own: the items of the project's lists (a source, a part, a probe, a colour scheme),
/// which the tree shows by name, Paste renames on a clash and an undo step finds again by name. A name is unique within
/// its list, compared without case.
/// </summary>
public interface INamed
{
	/// <summary>The name, unique within the object's list.</summary>
	string Name { get; set; }
}
