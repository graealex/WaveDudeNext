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
/// An AND gate in the only sense a passive linear gate has one: wave A leads by 90°, so the upper output carries the in-phase
/// sum (a + b) / √2, half an input's power for one input and twice it for two, 4 : 1, to be read with a threshold between;
/// the lower output carries the XOR. The XOR gate with its outputs exchanged.
/// </summary>
public sealed class AndGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "AND gate";

	/// <inheritdoc/>
	public override double PhaseShift => -90.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.And;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.Xor;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<AndGatePart>();
}
