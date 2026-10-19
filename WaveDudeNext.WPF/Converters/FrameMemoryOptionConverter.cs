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
/// The header of a Simulation > RAM cache size entry: "Auto" (the string table's
/// <c>MenuSimulationFrameMemoryAuto</c>) for <see cref="FrameMemory.Automatic"/>, else the percentage of the
/// RAM, "10 %" to "100 %". A multi-value converter whose first input is the option and whose second, the
/// current language, only serves to re-read the resource on a language switch (2026-10-15).
/// </summary>
public sealed class FrameMemoryOptionConverter : IMultiValueConverter
{
	/// <inheritdoc/>
	public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
	{
		if (values.Length == 0 || values[0] is not int percent)
		{
			return null;
		}
		if (percent <= FrameMemory.Automatic)
		{
			return Application.Current?.TryFindResource("MenuSimulationFrameMemoryAuto") as string ?? "_Auto";
		}
		// the underscore of a mnemonic would be swallowed; a digit mnemonic is of no use anyway
		return string.Format(culture, "{0} %", percent);
	}

	/// <inheritdoc/>
	public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
