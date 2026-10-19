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

using System.Globalization;
using System.Windows.Data;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// Turns a simulation step into the index of the frame that shows it. Inputs, in this order:
/// the step, the visualisation interval (steps per frame) and the last frame index. The result
/// is clamped to 0..last frame. With the parameter <c>"end"</c> a step of 0 means the end of
/// the run and gives the last frame, which is how <c>Display.ExportTo</c> encodes "no out point".
/// One way: the pane writes steps back through its commands.
/// </summary>
public sealed class StepToFrameConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Length != 3 || values[0] is not int step || values[1] is not int every || values[2] is not int lastFrame)
		{
			return 0;
		}
		if (step <= 0)
		{
			return Equals(parameter, "end") ? lastFrame : 0;
		}
		return Math.Clamp(every > 0 ? step / every : 0, 0, lastFrame);
	}

	/// <inheritdoc/>
	public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
