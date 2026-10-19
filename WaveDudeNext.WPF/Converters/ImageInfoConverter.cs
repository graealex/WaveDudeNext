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
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace WaveDudeNext.WPF.Converters;

/// <summary>
/// The embedded scene image's bytes as "1920 × 1080, 312 KB" for the scene view; "" without an image,
/// "(unreadable)" when no decoder takes the bytes. Reads the header only (<see cref="BitmapCacheOption.None"/>).
/// </summary>
public sealed class ImageInfoConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not byte[] bytes)
		{
			return "";
		}
		try
		{
			using var stream = new MemoryStream(bytes, writable: false);
			BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
			BitmapFrame frame = decoder.Frames[0];
			return string.Format(culture, "{0} × {1}, {2:N0} KB", frame.PixelWidth, frame.PixelHeight, Math.Max(1, bytes.Length / 1024));
		}
		catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
		{
			return Application.Current?.TryFindResource("ImageUnreadable") as string ?? "(unreadable)";
		}
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
