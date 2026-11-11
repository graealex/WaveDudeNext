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
/// An XOR gate: wave B leads by 90°, so the upper output carries (a − b) / √2, dark when both inputs are on, and the lower
/// the in-phase sum (the 2026-10-21 design, measured: with both inputs on the upper output keeps about 1 % of one input's
/// power, the lower 1.9×).
/// </summary>
public sealed class XorGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "XOR gate";

	/// <inheritdoc/>
	public override double PhaseShift => 90.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.Xor;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.And;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<XorGatePart>();
}
