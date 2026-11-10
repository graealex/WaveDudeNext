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
using Avalonia.Data.Converters;
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Converters;

/// <summary>
/// The icon drawing of a probe by what it is (<see cref="ProbeDetector"/>; the WPF head's converter of the same name,
/// 2026-11-07): "Probe" for a standard probe, "CountingDetector" or "ImagingDetector" for a detector, the names of the
/// <c>Icon…</c> geometries in Themes/Icons.axaml.
/// </summary>
public sealed class ProbeIconConverter : IValueConverter
{
	/// <inheritdoc />
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
	{
		ProbeDetector.Counting => "CountingDetector",
		ProbeDetector.Imaging => "ImagingDetector",
		_ => "Probe",
	};

	/// <inheritdoc />
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
