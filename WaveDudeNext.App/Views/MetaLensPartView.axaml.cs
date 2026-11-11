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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using Avalonia.Controls;

namespace WaveDudeNext.App.Views;

/// <summary>Editor for a <see cref="WaveDudeNext.Model.MetaLensPart"/>: the plate's material (its band on the pattern's tops and its back), its size, the lens's design and the pattern's cells, with the wavelength, the zones and the full-turn height read-only.</summary>
public partial class MetaLensPartView : UserControl
{
	/// <summary>Creates the view.</summary>
	public MetaLensPartView()
	{
		InitializeComponent();
	}
}
