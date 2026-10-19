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

using NLog;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;

namespace WaveDudeNext.Parts;

/// <summary>
/// Paints the enabled <see cref="OpticalPart"/>s of a project into the red channel of the scene pixels, the
/// channel the engines read as the refractive index (red = index × 100; red 0 is a mirror, see
/// <see cref="Engine.SceneData.MirrorRefractiveIndex"/>). Green (image sources) and blue (damping) are never
/// touched. Parts are painted in list order, a later one over an earlier one. Refractive parts are supersampled
/// 4 × 4 per pixel and the sample indices averaged, a sample outside the part counting the pixel's existing index
/// (red 0 as 1); a mirror part tests the pixel centre alone and writes red 0, since an averaged mirror edge would
/// clamp to glass. Pure geometry, no UI; any thread. The per-kind tests are in <see cref="PartShape"/> so the
/// harness can check them.
/// </summary>
public static class PartPainter
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Samples per pixel edge for refractive parts.</summary>
	public const int Supersample = 4;

	/// <summary>
	/// The scene pixels with the enabled parts painted in: <paramref name="image"/> itself (the same reference, so
	/// callers comparing by reference see no change) when no part is enabled, else a copy of the image, or a blank
	/// canvas of <paramref name="width"/> × <paramref name="height"/> with red = round(<paramref name="backgroundIndex"/> × 100),
	/// green 0, blue 0, alpha 255 when there is no image, with each enabled part painted in list order.
	/// </summary>
	/// <param name="image">The decoded scene image, or null for a blank canvas; never modified.</param>
	/// <param name="width">Canvas width in pixels (the image's when there is one).</param>
	/// <param name="height">Canvas height in pixels.</param>
	/// <param name="backgroundIndex">Refractive index of a blank canvas (<see cref="Scene.RefractiveIndex"/>).</param>
	/// <param name="parts">The project's parts; disabled ones are skipped.</param>
	public static ScenePixels? Paint(ScenePixels? image, int width, int height, double backgroundIndex, IReadOnlyList<OpticalPart> parts)
	{
		if (!parts.Any(part => part.Enabled))
		{
			return image;
		}
		if (image != null)
		{
			width = image.Width;
			height = image.Height;
		}
		int stride = width * 4;
		byte[] bgra = new byte[stride * height];
		if (image != null)
		{
			for (int y = 0; y < height; y++)
			{
				Buffer.BlockCopy(image.Bgra, y * image.StrideBytes, bgra, y * stride, stride);
			}
		}
		else
		{
			byte red = (byte)Math.Clamp((int)Math.Round(backgroundIndex * 100.0), 0, 255);
			for (int i = 0; i < bgra.Length; i += 4)
			{
				bgra[i + 2] = red;
				bgra[i + 3] = 255;
			}
		}
		foreach (OpticalPart part in parts)
		{
			if (part.Enabled)
			{
				Paint(bgra, width, height, stride, part);
			}
		}
		return new ScenePixels(width, height, stride, bgra);
	}

	/// <summary>Paints one part into the red channel of <paramref name="bgra"/> (rows <paramref name="stride"/> bytes apart).</summary>
	public static void Paint(byte[] bgra, int width, int height, int stride, OpticalPart part)
	{
		PartShape? shape = PartShape.For(part);
		if (shape == null)
		{
			Logger.Debug("Part '{Name}' ({Kind}) has no shape to paint (too few points)", part.Name, part.Kind);
			return;
		}
		(int x0, int y0, int x1, int y1) = shape.Bounds(width, height);
		if (x0 > x1 || y0 > y1)
		{
			Logger.Debug("Part '{Name}' ({Kind}) lies outside the canvas", part.Name, part.Kind);
			return;
		}
		Logger.Debug("Painting part '{Name}' ({Kind}) at ({X}, {Y}), {Angle}°, rows {Y0}..{Y1}", part.Name, part.Kind, part.X, part.Y, part.Angle, y0, y1);
		if (shape.IsMirror)
		{
			Parallel.For(y0, y1 + 1, y =>
			{
				int row = y * stride;
				for (int x = x0; x <= x1; x++)
				{
					if (shape.IndexAt(x + 0.5, y + 0.5) is not null)
					{
						bgra[row + x * 4 + 2] = 0;
					}
				}
			});
			return;
		}
		const double step = 1.0 / Supersample;
		const double half = step / 2.0;
		Parallel.For(y0, y1 + 1, y =>
		{
			int row = y * stride;
			for (int x = x0; x <= x1; x++)
			{
				int offset = row + x * 4 + 2;
				int red = bgra[offset];
				// red 0 is a mirror the engine reads as index 1000; averaging it in as glass of index 1 is the user's rule
				double existing = red == 0 ? 1.0 : red / 100.0;
				double sum = 0.0;
				int inside = 0;
				for (int sy = 0; sy < Supersample; sy++)
				{
					for (int sx = 0; sx < Supersample; sx++)
					{
						double? index = shape.IndexAt(x + half + sx * step, y + half + sy * step);
						if (index is { } n)
						{
							sum += n;
							inside++;
						}
						else
						{
							sum += existing;
						}
					}
				}
				if (inside > 0)
				{
					bgra[offset] = (byte)Math.Clamp((int)Math.Round(sum / (Supersample * Supersample) * 100.0), 1, 255);
				}
			}
		});
	}
}
