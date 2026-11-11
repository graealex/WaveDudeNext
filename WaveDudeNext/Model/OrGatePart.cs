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
/// An OR gate: wave B leads by 30°, so the waves meet at 120° in the upper output, a + e^{i120°}·b over √2, and one input or
/// two give the same half an input's power (1 + 1 + 2 cos 120° = 1); the lower output, where they meet at 60°, gives ½, ½ and 1½.
/// </summary>
public sealed class OrGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "OR gate";

	/// <inheritdoc/>
	public override double PhaseShift => 30.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.Or;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.Remainder;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<OrGatePart>();
}
