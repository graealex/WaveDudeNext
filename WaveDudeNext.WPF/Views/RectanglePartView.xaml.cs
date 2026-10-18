// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Windows.Controls;
using System.Windows.Input;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF.Views;

/// <summary>Editor for a <see cref="RectanglePart"/>: length, width and a Square button that copies the length into the width.</summary>
public partial class RectanglePartView : UserControl
{
	public RectanglePartView()
	{
		InitializeComponent();
	}

	private void CanMakeSquare(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = DataContext is RectanglePart rectangle && rectangle.Width != rectangle.Length;

	private void OnMakeSquare(object sender, ExecutedRoutedEventArgs e)
	{
		if (DataContext is RectanglePart rectangle)
		{
			rectangle.Width = rectangle.Length;
		}
	}
}
