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

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// The status bar's busy text from (<see cref="BusyKind"/>, frames done, frame count, the frame being computed):
/// "Exporting 12 / 100 frames" from <c>ExportStatusFormat</c>, <c>ExportStatusWaiting</c> while an export waits for
/// its run, "Computing frame 655" from <c>StatusComputingFormat</c>; empty while idle. A fifth input, the current
/// language, makes a bound text follow a switch. The WPF head's converter, 2026-11-06.
/// </summary>
public sealed class BusyTextConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Count < 4 || values[0] is not BusyKind kind)
		{
			return "";
		}
		return kind switch
		{
			BusyKind.Exporting => string.Format(culture, Text("ExportStatusFormat", "Exporting {0} / {1} frames"), values[1], values[2]),
			BusyKind.WaitingForRun => Text("ExportStatusWaiting", "Export: waiting for the run"),
			BusyKind.ComputingFrame => string.Format(culture, Text("StatusComputingFormat", "Computing frame {0}"), values[3]),
			_ => "",
		};
	}

	private static string Text(string key, string fallback) =>
		Application.Current?.TryGetResource(key, null, out object? value) == true && value is string text ? text : fallback;
}
