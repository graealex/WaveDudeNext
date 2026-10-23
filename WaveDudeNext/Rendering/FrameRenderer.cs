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

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WaveDudeNext.Runs;

namespace WaveDudeNext.Rendering;

/// <summary>
/// Static helpers that turn a stored frame (the field or the intensity of one simulation step, width × height
/// values without padding) into packed BGRA32 pixels through a <see cref="ColorLut"/>: a frame of 16-bit samples
/// through the colour of every half value (<see cref="ColorLut.HalfTable"/>, the gradient at the sample's own
/// position), a frame of 8-bit palette indices through the 255-colour palette. Either is one table lookup per pixel,
/// rows in parallel blocks, so the methods are CPU-bound but not thread-affine; the caller owns the buffers and must
/// not touch them while a call is in progress.
/// </summary>
public static class FrameRenderer
{
	/// <summary>
	/// The rows in one contiguous block per core (2026-10-16): row by row, the parallel loop reached a speed-up of
	/// 1.4 on ten cores (per-row scheduling and pool threads parking between 6 ms bursts); in blocks each worker
	/// takes one range and the loop runs close to the core count.
	/// </summary>
	private static OrderablePartitioner<Tuple<int, int>> Blocks(int height) =>
		Partitioner.Create(0, height, 32);

	/// <summary>
	/// Renders a plane of 16-bit samples: every sample's bits index the layer's half table at
	/// <paramref name="brightness"/>, built first when the brightness is new to <paramref name="lut"/>.
	/// </summary>
	/// <param name="values">Samples, row-major, <paramref name="width"/> × <paramref name="height"/>.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="lut">The colours (<see cref="ColorLut.Grey"/> for grey levels).</param>
	/// <param name="layer">Which plane the samples are: the field is signed around the gradient's middle, the intensity runs from its start.</param>
	/// <param name="brightness">The layer's gain.</param>
	/// <param name="pixels">Destination, at least <paramref name="width"/> × <paramref name="height"/> packed BGRA32 pixels, row-major.</param>
	public static void Render(ReadOnlyMemory<Half> values, int width, int height, ColorLut lut, FrameLayer layer, float brightness, Memory<int> pixels)
	{
		int[] table = lut.HalfTable(layer, brightness);
		Parallel.ForEach(Blocks(height), block =>
		{
			int start = block.Item1 * width;
			int count = (block.Item2 - block.Item1) * width;
			ReadOnlySpan<ushort> bits = MemoryMarshal.Cast<Half, ushort>(values.Span.Slice(start, count));
			Span<int> target = pixels.Span.Slice(start, count);
			for (int i = 0; i < bits.Length; i++)
			{
				target[i] = table[bits[i]];
			}
		});
	}

	/// <summary>
	/// Renders a plane of palette indices (an <see cref="Engine.FrameFormat.Index"/> frame, the brightness already
	/// applied when it was captured): every byte is looked up in the 8-bit palette, nothing else.
	/// </summary>
	/// <param name="indices">Palette indices 0..254, row-major, <paramref name="width"/> × <paramref name="height"/>.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="lut">The colours (<see cref="ColorLut.Grey"/> for grey levels).</param>
	/// <param name="pixels">Destination, at least <paramref name="width"/> × <paramref name="height"/> packed BGRA32 pixels, row-major.</param>
	public static void RenderIndexed(ReadOnlyMemory<byte> indices, int width, int height, ColorLut lut, Memory<int> pixels)
	{
		int[] palette = lut.Argb;
		Parallel.For(0, height, y =>
		{
			ReadOnlySpan<byte> row = indices.Span.Slice(y * width, width);
			Span<int> target = pixels.Span.Slice(y * width, width);
			for (int x = 0; x < width; x++)
			{
				target[x] = palette[Math.Min((int)row[x], ColorLut.Size - 1)];
			}
		});
	}
}
