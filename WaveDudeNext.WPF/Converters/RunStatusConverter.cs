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
using System.Windows;
using System.Windows.Data;
using WaveDudeNext.Runs;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// The status bar's run text from (state, steps done, total steps, engine description), e.g.
/// "Running: 1234 / 2500 steps, CPU (16 threads)", with <c>RunStatusStale</c> appended when a fifth value
/// says the project was edited since. Empty while there is no run. The state's wording comes from the
/// <c>RunState*</c> string resources and the layout from <c>RunStatusFormat</c>.
/// </summary>
public sealed class RunStatusConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Length < 4 || values[0] is not RunState state)
		{
			return "";
		}
		string stateText = Application.Current.TryFindResource("RunState" + state) as string ?? state.ToString();
		string format = Application.Current.TryFindResource("RunStatusFormat") as string ?? "{0}: {1} / {2}, {3}";
		string text = string.Format(culture, format, stateText, values[1], values[2], values[3]);
		if (values.Length > 4 && values[4] is true)
		{
			text += Application.Current.TryFindResource("RunStatusStale") as string ?? " (edited since)";
		}
		return text;
	}

	/// <inheritdoc/>
	public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
