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


using System.Windows;
using System.Windows.Input;
using WaveDudeNext.WPF.Help;

namespace WaveDudeNext.WPF;

public partial class MainWindow
{
	/// <summary>The open manual window, or null; closed with this window (<see cref="OnClosed"/>).</summary>
	private ManualWindow? _manual;

	/// <summary>Help > About: the modal <see cref="AboutWindow"/> over this window.</summary>
	private void OnHelpAbout(object sender, ExecutedRoutedEventArgs e)
	{
		Logger.Debug("About dialog opened");
		new AboutWindow { Owner = this }.ShowDialog();
	}

	/// <summary>
	/// Help > Manual (F1): the <see cref="ManualWindow"/>, owned by this window (2026-10-30, the user's choice: not modal,
	/// but attached, so Windows minimises and restores it with this window and keeps it above); opened once, brought to
	/// the front after that.
	/// </summary>
	private void OnHelpManual(object sender, ExecutedRoutedEventArgs e)
	{
		if (_manual == null)
		{
			Logger.Debug("Manual opened");
			_manual = new ManualWindow { Owner = this };
			_manual.Closed += (_, _) => _manual = null;
			_manual.Show();
			return;
		}
		Logger.Debug("Manual brought to the front");
		if (_manual.WindowState == WindowState.Minimized)
		{
			_manual.WindowState = WindowState.Normal;
		}
		_manual.Activate();
	}
}
