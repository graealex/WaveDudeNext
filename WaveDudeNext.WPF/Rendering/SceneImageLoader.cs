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

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveDudeNext.Runs;

namespace WaveDudeNext.WPF.Rendering;

/// <summary>Decodes a scene image file into the BGRA bytes a run needs. UI thread (WPF imaging).</summary>
public static class SceneImageLoader
{
	/// <summary>Reads <paramref name="path"/> with the WPF decoders and converts the first frame to 32-bit BGRA.</summary>
	/// <exception cref="FileNotFoundException">The file does not exist.</exception>
	/// <exception cref="NotSupportedException">The file is not an image WPF can decode.</exception>
	public static ScenePixels Load(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Load(stream);
	}

	/// <summary>Decodes an image file's bytes (an embedded scene image).</summary>
	public static ScenePixels Load(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes, writable: false);
		return Load(stream);
	}

	/// <summary>Decodes an image stream (any format WPF has a decoder for) into BGRA pixels.</summary>
	public static ScenePixels Load(Stream stream)
	{
		BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
		int width = converted.PixelWidth;
		int height = converted.PixelHeight;
		int stride = width * 4;
		var bgra = new byte[stride * height];
		converted.CopyPixels(bgra, stride, 0);
		return new ScenePixels(width, height, stride, bgra);
	}
}
