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

using System.Windows.Input;

namespace WaveDudeNext.WPF.Commands;

/// <summary>Commands of the part editors, bound on the views themselves: the point list of an outline or fibre (<c>Views/PartPointsView</c>), nothing else since the chain toggle replaced the Square and Circle buttons (2026-10-20).</summary>
public static class PartCommands
{
	/// <summary>Appends a point after the last one.</summary>
	public static readonly RoutedUICommand AddPoint = new("Add point", nameof(AddPoint), typeof(PartCommands));

	/// <summary>Removes the point given as parameter.</summary>
	public static readonly RoutedUICommand RemovePoint = new("Remove point", nameof(RemovePoint), typeof(PartCommands));
}
