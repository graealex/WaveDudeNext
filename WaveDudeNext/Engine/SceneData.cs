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

namespace WaveDudeNext.Engine;

/// <summary>
/// Per-pixel simulation input derived from a scene image: squared wave speed (<see cref="C2"/>,
/// <c>(1 / refractive index)^2</c>; the time step is 1), damping factor (<see cref="D"/>, the factor on the
/// velocity: 1 = no damping, 0 = the wave is stopped) and the positions / levels of image-defined sources
/// (the value of the green channel sets the frequency).
/// The rules are those of the Python classes <c>StaticImageScene</c>, <c>StaticRefractiveIndex</c> and
/// <c>StaticDampening</c>: red channel = refractive index * 100 (clamped to 1..10 as in Wave Dude 2D, whose help says
/// values below 100 are set to 100; the Python clamped at 0.9, 2026-10-18; red 0 is a mirror, see
/// <see cref="MirrorRefractiveIndex"/>), green = every pixel
/// above 0 is a sinusoidal source whose level is the green value, blue = damping strength (0 = none,
/// 255 = full; stored as <c>D = 1 - blue / 255</c>), damping edge = a border of the given thickness with
/// damping factor <c>sqrt(i / thickness)</c> at i pixels from the edge.
/// Created by the run on its worker from the decoded scene pixels (<see cref="FromBgra"/>) or
/// <see cref="Empty"/> and then copied into the padded grids of <see cref="WaveSimulator"/> /
/// <see cref="GpuWaveSimulator"/>. The arrays are row-major (<c>y * Width + x</c>) without padding and are
/// not modified after construction, so any thread may read them.
/// </summary>
public sealed class SceneData
{
	/// <summary>
	/// The refractive index of a scene pixel with a red value of 0: Jeroen's rule from Wave Dude 2D V0.30 (2026-10-18). Such a
	/// pixel is a mirror, a wall no wave gets through (a source placed inside one crawls); a closed outline of them is a
	/// cavity that keeps a wave for ever. Red values below 90 were never usable, so nothing else changes.
	/// </summary>
	public const double MirrorRefractiveIndex = 1000.0;

	private SceneData(int w, int h, float[] c2, float[] d, int[] srcX, int[] srcY, byte[] srcLevel)
	{
		Width = w;
		Height = h;
		C2 = c2;
		D = d;
		SourceX = srcX;
		SourceY = srcY;
		SourceLevel = srcLevel;
	}

	/// <summary>Number of image-defined source pixels (length of <see cref="SourceX"/>, <see cref="SourceY"/> and <see cref="SourceLevel"/>).</summary>
	public int SourceCount => SourceX.Length;

	/// <summary>
	/// Decodes 32-bit BGRA pixels (byte order B, G, R, A per pixel, rows <paramref name="strideBytes"/> apart)
	/// into per-pixel squared speed and damping factor and, when <paramref name="useSources"/> is set, collects
	/// every pixel with green &gt; 0 as a source whose level is that green value, in row-major order. Finally
	/// fades the damping in a border of <paramref name="dampingEdge"/> pixels (see <see cref="ApplyBorder"/>).
	/// </summary>
	/// <param name="bgra">Pixel bytes; at least <paramref name="height"/> rows of <paramref name="strideBytes"/>.</param>
	/// <param name="width">Canvas width in pixels.</param>
	/// <param name="height">Canvas height in pixels.</param>
	/// <param name="strideBytes">Bytes per row (at least 4 × <paramref name="width"/>).</param>
	/// <param name="useSources">Collect green pixels as sources; otherwise the source arrays are empty.</param>
	/// <param name="dampingEdge">Thickness of the absorbing border in pixels; 0 or less for none.</param>
	public static SceneData FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int strideBytes, bool useSources, int dampingEdge)
	{
		float[] c2 = new float[width * height];
		float[] d = new float[width * height];
		var sourceX = new List<int>();
		var sourceY = new List<int>();
		var sourceLevel = new List<byte>();
		for (int y = 0; y < height; y++)
		{
			ReadOnlySpan<byte> row = bgra.Slice(y * strideBytes, width * 4);
			for (int x = 0; x < width; x++)
			{
				int offset = x * 4;
				int blue = row[offset];
				int green = row[offset + 1];
				int red = row[offset + 2];
				double refractiveIndex = red == 0 ? MirrorRefractiveIndex : Math.Clamp(red / 100.0, 1.0, 10.0);
				double speed = 1.0 / refractiveIndex;
				int index = y * width + x;
				c2[index] = (float)(speed * speed);
				d[index] = (float)(1.0 - blue / 255.0);
				if (useSources && green > 0)
				{
					sourceX.Add(x);
					sourceY.Add(y);
					sourceLevel.Add((byte)green);
				}
			}
		}
		ApplyBorder(d, width, height, dampingEdge);
		return new SceneData(width, height, c2, d, sourceX.ToArray(), sourceY.ToArray(), sourceLevel.ToArray());
	}

	/// <summary>
	/// Creates a uniform, user defined <paramref name="w"/> x <paramref name="h"/> scene without sources: the
	/// speed is derived from <paramref name="refractiveIndex"/> (clamped to 1..10) and the damping
	/// factor is 1 (none) everywhere except in the <paramref name="dampingEdge"/> border.
	/// </summary>
	/// <param name="w">Canvas width in pixels.</param>
	/// <param name="h">Canvas height in pixels.</param>
	/// <param name="dampingEdge">Thickness of the absorbing border in pixels; 0 or less for none.</param>
	/// <param name="refractiveIndex">Background refractive index (1.0 by default, the same rule as a scene image's red channel: index = red / 100).</param>
	public static SceneData Empty(int w, int h, int dampingEdge, double refractiveIndex = 1.0)
	{
		float speed = (float)(1.0 / Math.Clamp(refractiveIndex, 1.0, 10.0));
		float[] c2 = new float[w * h];
		float[] d = new float[w * h];
		Array.Fill(c2, speed * speed);
		Array.Fill(d, 1f);
		ApplyBorder(d, w, h, dampingEdge);
		return new SceneData(w, h, c2, d, [], [], []);
	}

	/// <summary>
	/// Counts the pixels with green &gt; 0, i.e. the number of sources <see cref="FromBgra"/> would collect.
	/// Used on the UI thread before a run to warn when image sources are enabled but the image contains none.
	/// </summary>
	/// <param name="bgra">Pixel bytes as for <see cref="FromBgra"/>.</param>
	/// <param name="width">Canvas width in pixels.</param>
	/// <param name="height">Canvas height in pixels.</param>
	/// <param name="strideBytes">Bytes per row.</param>
	public static int CountSources(ReadOnlySpan<byte> bgra, int width, int height, int strideBytes)
	{
		int count = 0;
		for (int y = 0; y < height; y++)
		{
			ReadOnlySpan<byte> row = bgra.Slice(y * strideBytes, width * 4);
			for (int x = 0; x < width; x++)
			{
				if (row[x * 4 + 1] > 0)
				{
					count++;
				}
			}
		}
		return count;
	}

	/// <summary>
	/// Damping edge, as in <c>StaticDampening</c>: overwrites the damping factor in a frame of
	/// <paramref name="thickness"/> pixels (capped at half the smaller side) around the edge of the
	/// <paramref name="w"/> x <paramref name="h"/> grid with <c>sqrt(i / thickness)</c>, where i is the
	/// distance from the edge, so the outermost ring stops the wave completely and the factor rises to 1
	/// inside. Does nothing for a thickness of 0 or less.
	/// </summary>
	/// <param name="d">Damping array, row-major, modified in place.</param>
	/// <param name="w">Grid width in pixels.</param>
	/// <param name="h">Grid height in pixels.</param>
	/// <param name="thickness">Requested border thickness in pixels.</param>
	internal static void ApplyBorder(float[] d, int w, int h, int thickness)
	{
		int border = Math.Min(thickness, Math.Min(w, h) / 2);
		if (border <= 0)
		{
			return;
		}
		for (int i = 0; i < border; i++)
		{
			float factor = (float)Math.Sqrt((double)i / border);
			for (int x = i; x <= w - 1 - i; x++)
			{
				d[i * w + x] = factor;
				d[(h - 1 - i) * w + x] = factor;
			}
			for (int y = i; y <= h - 1 - i; y++)
			{
				d[y * w + i] = factor;
				d[y * w + (w - 1 - i)] = factor;
			}
		}
	}

	/// <summary>Canvas width in pixels.</summary>
	public int Width { get; }

	/// <summary>Canvas height in pixels.</summary>
	public int Height { get; }

	/// <summary>Squared wave speed per pixel (<c>1 / n^2</c> for refractive index n, so 1 for n = 1), row-major.</summary>
	public float[] C2 { get; }

	/// <summary>Damping factor per pixel, 0..1 where 1 = no damping, row-major.</summary>
	public float[] D { get; }

	/// <summary>X canvas coordinate of each image-defined source pixel; parallel to <see cref="SourceY"/> and <see cref="SourceLevel"/>.</summary>
	public int[] SourceX { get; }

	/// <summary>Y canvas coordinate of each image-defined source pixel.</summary>
	public int[] SourceY { get; }

	/// <summary>Green channel value (1..255) of each image-defined source pixel; the engines treat it as the source's level.</summary>
	public byte[] SourceLevel { get; }
}
