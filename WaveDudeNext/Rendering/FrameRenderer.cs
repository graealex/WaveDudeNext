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
using System.Numerics.Tensors;
using System.Runtime.Intrinsics;

namespace WaveDudeNext.Rendering;

/// <summary>
/// Static helpers that turn a stored frame (the field or the intensity of one simulation step, width × height
/// values without padding) into packed BGRA32 pixels, like <c>WaveVisualizer.render_field</c> and
/// <c>render_intensity</c> in the Python code: the value is converted to a table index (see
/// <see cref="ColorLut"/>) and the index to a colour. Without a colour table (null) the index itself is used
/// as grey level. Row loops run in parallel (each worker converts its row to floats with the vectorised
/// <see cref="TensorPrimitives.ConvertToSingle"/> first and colours four pixels at a time on 128-bit registers),
/// so the methods are CPU-bound but not thread-affine;
/// the caller owns the buffers and must not touch them while a call is in progress.
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
	/// Renders the wave field: every value is mapped by <see cref="ColorLut.FieldIndex"/>
	/// (<c>clip(value · brightness, -1, 1) · 127 + 127</c>) and coloured.
	/// </summary>
	/// <param name="values">Field values, row-major, <paramref name="width"/> × <paramref name="height"/>.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="lut">ARGB palette indexed 0..254 (127 = zero field), or null for greyscale.</param>
	/// <param name="brightness">Gain applied before the value is clamped to -1..1.</param>
	/// <param name="pixels">Destination, at least <paramref name="width"/> × <paramref name="height"/> packed BGRA32 pixels, row-major.</param>
	public static void RenderField(ReadOnlyMemory<Half> values, int width, int height, int[]? lut, float brightness, Memory<int> pixels)
	{
		Parallel.ForEach(Blocks(height), () => new float[width], (block, _, row) =>
		{
			for (int y = block.Item1; y < block.Item2; y++)
			{
				TensorPrimitives.ConvertToSingle(values.Span.Slice(y * width, width), row);
				FieldRow(row, lut, brightness, pixels.Span.Slice(y * width, width));
			}
			return row;
		}, _ => { });
	}

	/// <summary>
	/// Renders the accumulated intensity: each value is mapped as <c>sqrt(value) · brightness</c> (exponent 0.5,
	/// as in the Python code), clamped to 1, then scaled to palette index 0..254 (non-positive values map to 0).
	/// </summary>
	/// <param name="values">Intensity values, row-major, <paramref name="width"/> × <paramref name="height"/>.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="lut">ARGB palette indexed 0..254, or null for greyscale.</param>
	/// <param name="brightness">Gain applied after the square-root compression.</param>
	/// <param name="pixels">Destination, at least <paramref name="width"/> × <paramref name="height"/> packed BGRA32 pixels, row-major.</param>
	public static void RenderIntensity(ReadOnlyMemory<Half> values, int width, int height, int[]? lut, float brightness, Memory<int> pixels)
	{
		Parallel.ForEach(Blocks(height), () => new float[width], (block, _, row) =>
		{
			for (int y = block.Item1; y < block.Item2; y++)
			{
				TensorPrimitives.ConvertToSingle(values.Span.Slice(y * width, width), row);
				IntensityRow(row, lut, brightness, pixels.Span.Slice(y * width, width));
			}
			return row;
		}, _ => { });
	}

	/// <summary>
	/// One row of the field, four pixels at a time on 128-bit registers (2026-10-16, the width every x64 and ARM64
	/// machine accelerates): the same operations in the same order as <see cref="ColorLut.FieldIndex"/>, so the
	/// indices are identical bit for bit (the library harness checks it over awkward values); NaN lanes take 127
	/// through a mask, so the min/max semantics for NaN never matter. The tail and a machine without acceleration
	/// go through the scalar helper.
	/// </summary>
	internal static void FieldRow(ReadOnlySpan<float> row, int[]? lut, float brightness, Span<int> target)
	{
		int x = 0;
		if (Vector128.IsHardwareAccelerated)
		{
			Vector128<float> gain = Vector128.Create(brightness);
			Vector128<float> one = Vector128.Create(1f);
			Vector128<float> minusOne = Vector128.Create(-1f);
			Vector128<float> scale = Vector128.Create(127f);
			Vector128<int> nanIndex = Vector128.Create(127);
			for (; x <= row.Length - 4; x += 4)
			{
				Vector128<float> scaled = Vector128.Create(row.Slice(x, 4)) * gain;
				Vector128<int> isNumber = Vector128.Equals(scaled, scaled).AsInt32();
				Vector128<float> clamped = Vector128.Min(Vector128.Max(scaled, minusOne), one);
				Vector128<int> index = Vector128.ConvertToInt32(clamped * scale + scale);
				Store(Vector128.ConditionalSelect(isNumber, index, nanIndex), lut, target.Slice(x, 4));
			}
		}
		for (; x < row.Length; x++)
		{
			target[x] = Colorize(ColorLut.FieldIndex(row[x], brightness), lut);
		}
	}

	/// <summary>
	/// One row of the intensity, four pixels at a time: the operations of <see cref="ColorLut.IntensityIndex(float, float)"/>
	/// in the same single precision; lanes that are not positive (zero, negative, NaN) take 0 through a mask.
	/// </summary>
	internal static void IntensityRow(ReadOnlySpan<float> row, int[]? lut, float brightness, Span<int> target)
	{
		int x = 0;
		if (Vector128.IsHardwareAccelerated)
		{
			Vector128<float> gain = Vector128.Create(brightness);
			Vector128<float> one = Vector128.Create(1f);
			Vector128<float> scale = Vector128.Create(254f);
			Vector128<float> zero = Vector128<float>.Zero;
			for (; x <= row.Length - 4; x += 4)
			{
				Vector128<float> value = Vector128.Create(row.Slice(x, 4));
				Vector128<int> positive = Vector128.GreaterThan(value, zero).AsInt32();
				Vector128<float> scaled = Vector128.Sqrt(value) * gain;
				Vector128<int> index = Vector128.ConvertToInt32(Vector128.Min(scaled, one) * scale);
				Store(Vector128.ConditionalSelect(positive, index, Vector128<int>.Zero), lut, target.Slice(x, 4));
			}
		}
		for (; x < row.Length; x++)
		{
			target[x] = Colorize(ColorLut.IntensityIndex(row[x], brightness), lut);
		}
	}

	/// <summary>Four palette indices to four pixels: a lookup each with a palette, else the grey arithmetic on the register.</summary>
	private static void Store(Vector128<int> index, int[]? lut, Span<int> target)
	{
		if (lut != null)
		{
			target[0] = lut[index[0]];
			target[1] = lut[index[1]];
			target[2] = lut[index[2]];
			target[3] = lut[index[3]];
		}
		else
		{
			(index * Vector128.Create(0x010101) | Vector128.Create(unchecked((int)0xFF000000))).CopyTo(target);
		}
	}

	/// <summary>
	/// Renders a plane of palette indices (an <see cref="Engine.FrameFormat.Index"/> frame, the brightness already
	/// applied when it was captured): every byte is looked up in the palette, nothing else. The cheapest picture there
	/// is, a table lookup per pixel.
	/// </summary>
	/// <param name="indices">Palette indices 0..254, row-major, <paramref name="width"/> × <paramref name="height"/>.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="lut">ARGB palette indexed 0..254, or null for greyscale.</param>
	/// <param name="pixels">Destination, at least <paramref name="width"/> × <paramref name="height"/> packed BGRA32 pixels, row-major.</param>
	public static void RenderIndexed(ReadOnlyMemory<byte> indices, int width, int height, int[]? lut, Memory<int> pixels)
	{
		Parallel.For(0, height, y =>
		{
			ReadOnlySpan<byte> row = indices.Span.Slice(y * width, width);
			Span<int> target = pixels.Span.Slice(y * width, width);
			for (int x = 0; x < width; x++)
			{
				target[x] = Colorize(Math.Min((int)row[x], ColorLut.Size - 1), lut);
			}
		});
	}

	/// <summary>Looks up a palette index in <paramref name="lut"/>, or builds an opaque grey of that level when there is no LUT.</summary>
	public static int Colorize(int index, int[]? lut) =>
		lut != null ? lut[index] : unchecked((int)0xFF000000) | (index << 16) | (index << 8) | index;
}
