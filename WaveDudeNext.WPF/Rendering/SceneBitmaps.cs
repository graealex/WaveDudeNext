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

using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.WPF.Rendering;

/// <summary>
/// The preview's pictures made from a scene image (2026-10-13): the image itself, shown where there is
/// no frame yet, and the glowing-edges overlay (<see cref="GlowingEdges"/>) as a translucent layer. WPF
/// composites source-over, not additively like the author's export, so the overlay's alpha carries the
/// edge strength times the gain and its colour is the edge colour at full brightness: a black (no edge)
/// pixel leaves the frame alone, a faint edge tints it a little, a hard edge paints its colour over it.
/// All bitmaps are frozen. UI thread (or any thread; the results are shareable).
/// </summary>
public static class SceneBitmaps
{
	/// <summary>The scene image as a bitmap (alpha ignored).</summary>
	public static BitmapSource Image(ScenePixels pixels)
	{
		BitmapSource bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgr32, null, pixels.Bgra, pixels.StrideBytes);
		bitmap.Freeze();
		return bitmap;
	}

	/// <summary>
	/// The overlay layer for one picture: alpha = min(255, strongest channel × <paramref name="gain"/>),
	/// colour = the edge colour scaled so its strongest channel is 255. Null when the gain is 0.
	/// </summary>
	/// <param name="edgesBgra">The picture from <see cref="GlowingEdges.Compute"/>.</param>
	/// <param name="width">Width in pixels.</param>
	/// <param name="height">Height in pixels.</param>
	/// <param name="gain">The layer's overlay gain (<c>DisplayLayer.OverlayGain</c>).</param>
	public static BitmapSource? Overlay(byte[] edgesBgra, int width, int height, double gain)
	{
		if (gain <= 0.0)
		{
			return null;
		}
		var layer = new byte[edgesBgra.Length];
		for (int i = 0; i < layer.Length; i += 4)
		{
			int b = edgesBgra[i];
			int g = edgesBgra[i + 1];
			int r = edgesBgra[i + 2];
			int max = Math.Max(b, Math.Max(g, r));
			if (max == 0)
			{
				continue; // transparent black
			}
			layer[i] = (byte)(b * 255 / max);
			layer[i + 1] = (byte)(g * 255 / max);
			layer[i + 2] = (byte)(r * 255 / max);
			layer[i + 3] = (byte)Math.Min(255.0, max * gain);
		}
		BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, layer, width * 4);
		bitmap.Freeze();
		return bitmap;
	}
}
