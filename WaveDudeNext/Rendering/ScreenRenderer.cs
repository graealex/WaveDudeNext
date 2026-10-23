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
using System.Runtime.InteropServices;
using WaveDudeNext.Runs;

namespace WaveDudeNext.Rendering;

/// <summary>
/// How one axis of a picture is resampled from the samples to the screen: for each target pixel the first sample it
/// takes and the weights of the samples from there on, which sum to 1. Shrinking averages the samples a target pixel
/// covers, each weighted by how much of it the pixel covers (a box over the pixel's footprint); enlarging blends the
/// two samples nearest the pixel's centre linearly; the same count copies. Immutable; any thread.
/// </summary>
public sealed class ResampleAxis
{
	private readonly int[] _first;
	private readonly int[] _offsets;
	private readonly float[] _weights;

	/// <param name="source">Samples along the axis, at least 1.</param>
	/// <param name="target">Pixels along the axis on screen, at least 1.</param>
	public ResampleAxis(int source, int target)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(source, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(target, 1);
		Source = source;
		Target = target;
		_first = new int[target];
		_offsets = new int[target + 1];
		var weights = new List<float>(target * 2);
		double scale = source / (double)target;
		for (int t = 0; t < target; t++)
		{
			_offsets[t] = weights.Count;
			if (target == source)
			{
				_first[t] = t;
				weights.Add(1f);
			}
			else if (target < source)
			{
				// the footprint [lo, hi) in samples; each sample weighs the part of it inside, the edges partly
				double lo = t * scale;
				double hi = (t + 1) * scale;
				int first = (int)Math.Floor(lo);
				int last = Math.Min(source - 1, (int)Math.Ceiling(hi) - 1);
				_first[t] = first;
				for (int i = first; i <= last; i++)
				{
					weights.Add((float)((Math.Min(hi, i + 1) - Math.Max(lo, i)) / scale));
				}
			}
			else
			{
				// the pixel's centre in sample coordinates (a sample's centre at i + 0.5); the ends hold the outer samples
				double u = (t + 0.5) * scale - 0.5;
				if (u <= 0.0 || source == 1)
				{
					_first[t] = 0;
					weights.Add(1f);
				}
				else if (u >= source - 1)
				{
					_first[t] = source - 1;
					weights.Add(1f);
				}
				else
				{
					int i = (int)Math.Floor(u);
					float f = (float)(u - i);
					_first[t] = i;
					weights.Add(1f - f);
					weights.Add(f);
				}
			}
		}
		_offsets[target] = weights.Count;
		_weights = [.. weights];
	}

	/// <summary>Samples along the axis.</summary>
	public int Source { get; }

	/// <summary>Pixels along the axis on screen.</summary>
	public int Target { get; }

	/// <summary>The weights of target pixel <paramref name="target"/>, for the samples from <paramref name="first"/> on.</summary>
	public ReadOnlySpan<float> WeightsOf(int target, out int first)
	{
		first = _first[target];
		return _weights.AsSpan(_offsets[target], _offsets[target + 1] - _offsets[target]);
	}
}

/// <summary>
/// Renders a plane of 16-bit samples at the size it is shown on screen, for the preview (2026-10-23, the user's design):
/// the samples are resampled in value space first (<see cref="ResampleAxis"/>, rows then columns), so every screen pixel
/// shows a value the field or intensity has there, in a colour of the gradient; the value is looked up in the layer's
/// half table and, with dithering, rounded to 8 bits per channel through a Bayer matrix fixed to the screen pixels. Only
/// for the screen: a dithered picture cannot be scaled again, and an export never is one. One instance per picture and
/// size, holding the axes and the rows between the passes; one render at a time, on any thread, the rows of each pass
/// in parallel blocks.
/// </summary>
public sealed class ScreenRenderer
{
	/// <summary>The dithering depths offered: 8 bits per channel plus an even number of bits a Bayer matrix of 2, 4, 8 or 16 cells a side carries.</summary>
	public static IReadOnlyList<int> DitherDepths { get; } = [10, 12, 14, 16];

	private readonly ResampleAxis _x;
	private readonly ResampleAxis _y;
	private readonly float[] _rows;

	/// <param name="sourceWidth">Samples per row of the plane.</param>
	/// <param name="sourceHeight">Rows of the plane.</param>
	/// <param name="targetWidth">The picture's width on screen, in device pixels.</param>
	/// <param name="targetHeight">The picture's height on screen, in device pixels.</param>
	public ScreenRenderer(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
	{
		_x = new ResampleAxis(sourceWidth, targetWidth);
		_y = new ResampleAxis(sourceHeight, targetHeight);
		_rows = new float[sourceHeight * targetWidth];
	}

	/// <summary>Samples per row of the plane.</summary>
	public int SourceWidth => _x.Source;

	/// <summary>Rows of the plane.</summary>
	public int SourceHeight => _y.Source;

	/// <summary>The picture's width on screen.</summary>
	public int TargetWidth => _x.Target;

	/// <summary>The picture's height on screen.</summary>
	public int TargetHeight => _y.Target;

	/// <summary>
	/// Renders <paramref name="values"/> (<see cref="SourceWidth"/> × <see cref="SourceHeight"/>, row-major) into
	/// <paramref name="pixels"/> (<see cref="TargetWidth"/> × <see cref="TargetHeight"/> packed BGRA32, row-major).
	/// </summary>
	/// <param name="values">The plane's samples.</param>
	/// <param name="lut">The colours.</param>
	/// <param name="layer">Which mapping the samples take: the signed field or the non-negative intensity.</param>
	/// <param name="brightness">The layer's gain.</param>
	/// <param name="ditherBits">0 for none (each value's colour rounded to 8 bits, <see cref="ColorLut.HalfTable"/>), else one of <see cref="DitherDepths"/>.</param>
	/// <param name="pixels">Destination.</param>
	public void Render(ReadOnlyMemory<Half> values, ColorLut lut, FrameLayer layer, float brightness, int ditherBits, Memory<int> pixels)
	{
		if (ditherBits != 0 && !DitherDepths.Contains(ditherBits))
		{
			throw new ArgumentOutOfRangeException(nameof(ditherBits), ditherBits, "Dithering takes 0 (none), 10, 12, 14 or 16 bits.");
		}
		int sourceWidth = _x.Source;
		int targetWidth = _x.Target;
		float[] rows = _rows;
		// rows: every sample row to the screen's width
		Parallel.ForEach(Partitioner.Create(0, _y.Source, 32), () => new float[sourceWidth], (block, _, line) =>
		{
			for (int y = block.Item1; y < block.Item2; y++)
			{
				TensorPrimitives.ConvertToSingle(values.Span.Slice(y * sourceWidth, sourceWidth), line);
				Span<float> target = rows.AsSpan(y * targetWidth, targetWidth);
				for (int t = 0; t < targetWidth; t++)
				{
					ReadOnlySpan<float> weights = _x.WeightsOf(t, out int first);
					float sum = 0f;
					for (int k = 0; k < weights.Length; k++)
					{
						sum += weights[k] * line[first + k];
					}
					target[t] = sum;
				}
			}
			return line;
		}, _ => { });
		// columns: every screen row from the rows it covers, then its colours
		int[]? table = ditherBits == 0 ? lut.HalfTable(layer, brightness) : null;
		ulong[]? deep = ditherBits == 0 ? null : lut.DeepHalfTable(layer, brightness);
		int[] thresholds = ditherBits == 0 ? [] : BayerThresholds(ditherBits);
		int side = ditherBits == 0 ? 1 : 1 << ((ditherBits - 8) / 2);
		Parallel.ForEach(Partitioner.Create(0, _y.Target, 32), () => (Values: new float[targetWidth], Halves: new Half[targetWidth]), (block, _, buffers) =>
		{
			for (int y = block.Item1; y < block.Item2; y++)
			{
				ReadOnlySpan<float> weights = _y.WeightsOf(y, out int first);
				TensorPrimitives.Multiply(rows.AsSpan(first * targetWidth, targetWidth), weights[0], buffers.Values);
				for (int k = 1; k < weights.Length; k++)
				{
					TensorPrimitives.MultiplyAdd(rows.AsSpan((first + k) * targetWidth, targetWidth), weights[k], buffers.Values, buffers.Values);
				}
				TensorPrimitives.ConvertToHalf(buffers.Values, buffers.Halves);
				ReadOnlySpan<ushort> bits = MemoryMarshal.Cast<Half, ushort>(buffers.Halves);
				Span<int> target = pixels.Span.Slice(y * targetWidth, targetWidth);
				if (table != null)
				{
					for (int x = 0; x < targetWidth; x++)
					{
						target[x] = table[bits[x]];
					}
				}
				else
				{
					ReadOnlySpan<int> row = thresholds.AsSpan((y & (side - 1)) * side, side);
					for (int x = 0; x < targetWidth; x++)
					{
						ulong color = deep![bits[x]];
						int threshold = row[x & (side - 1)];
						target[x] = unchecked((int)0xFF000000) | (Dither((int)(color >> 32) & 0xFFFF, threshold) << 16)
							| (Dither((int)(color >> 16) & 0xFFFF, threshold) << 8) | Dither((int)color & 0xFFFF, threshold);
					}
				}
			}
			return buffers;
		}, _ => { });
	}

	/// <summary>
	/// One channel to 8 bits: <paramref name="level"/> is the level times 256 (<see cref="ColorLut.DeepHalfTable"/>), its
	/// high byte the 8-bit level, which goes one up where the fraction in the low byte reaches the pixel's threshold.
	/// </summary>
	private static int Dither(int level, int threshold) => (level >> 8) + ((level & 0xFF) * 2 >= threshold ? 1 : 0);

	/// <summary>
	/// The thresholds of the Bayer matrix for <paramref name="bits"/>: the 2^e cells of a matrix 2^(e/2) a side (e the bits
	/// beyond 8) in the order of the recursive Bayer construction, cell value B as <c>(2B + 1) · 2^(8−e)</c> on the scale of
	/// twice the fraction (0..510), so a fraction f in 256ths goes up in round(f · 2^e / 256) of the 2^e cells: the
	/// fraction to e bits, spread over the matrix. Row-major.
	/// </summary>
	/// <param name="bits">One of <see cref="DitherDepths"/>.</param>
	public static int[] BayerThresholds(int bits)
	{
		int extra = bits - 8;
		int side = 1 << (extra / 2);
		int[] matrix = [0];
		for (int size = 1; size < side; size *= 2)
		{
			var next = new int[4 * size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					int value = 4 * matrix[y * size + x];
					next[y * 2 * size + x] = value;
					next[y * 2 * size + x + size] = value + 2;
					next[(y + size) * 2 * size + x] = value + 3;
					next[(y + size) * 2 * size + x + size] = value + 1;
				}
			}
			matrix = next;
		}
		return [.. matrix.Select(value => (2 * value + 1) << (8 - extra))];
	}
}
