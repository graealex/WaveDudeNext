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

using System.Windows.Shell;

namespace WaveDudeNext.WPF;

/// <summary>
/// Mirrors the status bar's busy indicator on the window's taskbar button (the Windows 7 taskbar progress,
/// which WPF exposes as <see cref="TaskbarItemInfo"/>; the user's rule, 2026-10-14): an export writing frames
/// fills the button as the frames go, an export waiting for its run or a scratch frame being computed shows
/// the indeterminate marquee, and an idle application shows nothing. Fed by <see cref="UpdateBusy"/>. UI thread.
/// </summary>
public partial class MainWindow
{
	/// <summary>Puts the busy state on the taskbar button; the element is declared in the window's XAML.</summary>
	private void UpdateTaskbar()
	{
		if (TaskbarItemInfo is not { } taskbar)
		{
			return;
		}
		if (!IsBusy)
		{
			taskbar.ProgressState = TaskbarItemProgressState.None;
			taskbar.ProgressValue = 0;
		}
		else if (IsBusyIndeterminate)
		{
			taskbar.ProgressState = TaskbarItemProgressState.Indeterminate;
		}
		else
		{
			int total = Export?.FrameCount ?? 0;
			taskbar.ProgressValue = total > 0 ? Math.Clamp((double)(Export?.FramesDone ?? 0) / total, 0.0, 1.0) : 0.0;
			taskbar.ProgressState = TaskbarItemProgressState.Normal;
		}
	}
}
