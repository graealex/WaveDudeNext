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

using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.Engine;

/// <summary>How the samples of a captured frame are stored.</summary>
public enum FrameFormat
{
	/// <summary>One <see cref="System.Half"/> per sample: the raw amplitude or intensity, coloured at display time with any brightness.</summary>
	Half = 0,

	/// <summary>
	/// One byte per sample: the palette index (0..254) the renderer would compute with the brightness the frame was
	/// captured with (<see cref="FrameShape.FieldBrightness"/>, <see cref="FrameShape.IntensityBrightness"/>), so the
	/// display's own quantisation is what is stored, at half the size of <see cref="Half"/> and with a table lookup
	/// per pixel to show. The palette and the black level still recolour such a frame; the brightness is baked in.
	/// </summary>
	Index = 1,
}

/// <summary>
/// The geometry and format of the frames a run captures (2026-10-15, the reduced preview for weaker machines):
/// the canvas of the simulation, the divisor that shrinks it (every <see cref="Divisor"/> × <see cref="Divisor"/>
/// block of cells averaged into one sample), the sample format and, for <see cref="FrameFormat.Index"/>, the
/// brightness of each layer at capture time. The simulation itself always runs at the canvas size; only what is
/// captured, stored and shown shrinks. Immutable; compared by value.
/// </summary>
/// <param name="CanvasWidth">Width of the simulated canvas in cells.</param>
/// <param name="CanvasHeight">Height of the simulated canvas in cells.</param>
/// <param name="Divisor">Cells per sample along each axis, at least 1; the last block of a row or column may be smaller and is averaged over the cells it has.</param>
/// <param name="Format">The sample format.</param>
/// <param name="FieldBrightness">Gain applied to the field before the palette index is taken (<see cref="FrameFormat.Index"/> only).</param>
/// <param name="IntensityBrightness">Gain applied to the compressed intensity before the palette index is taken (<see cref="FrameFormat.Index"/> only).</param>
public sealed record FrameShape(int CanvasWidth, int CanvasHeight, int Divisor, FrameFormat Format, float FieldBrightness, float IntensityBrightness)
{
	/// <summary>The full-quality shape of a canvas: every cell as a <see cref="FrameFormat.Half"/>.</summary>
	public static FrameShape Full(int canvasWidth, int canvasHeight) => new(canvasWidth, canvasHeight, 1, FrameFormat.Half, 1f, 1f);

	/// <summary>Frame width in samples: ⌈<see cref="CanvasWidth"/> / <see cref="Divisor"/>⌉.</summary>
	public int Width => (CanvasWidth + Divisor - 1) / Divisor;

	/// <summary>Frame height in samples: ⌈<see cref="CanvasHeight"/> / <see cref="Divisor"/>⌉.</summary>
	public int Height => (CanvasHeight + Divisor - 1) / Divisor;

	/// <summary>Bytes per sample: 2 for <see cref="FrameFormat.Half"/>, 1 for <see cref="FrameFormat.Index"/>.</summary>
	public int SampleBytes => Format == FrameFormat.Half ? 2 : 1;

	/// <summary>Bytes of one plane of a frame.</summary>
	public long PlaneBytes => (long)Width * Height * SampleBytes;

	/// <summary>Whether this is the full-quality shape (no shrinking, <see cref="FrameFormat.Half"/>); the brightness does not count then.</summary>
	public bool IsFull => Divisor == 1 && Format == FrameFormat.Half;

	/// <summary>The brightness baked into a plane: the field's or the intensity's.</summary>
	public float BrightnessOf(bool intensity) => intensity ? IntensityBrightness : FieldBrightness;

	/// <summary>Whether frames of this shape and of <paramref name="other"/> have the same size and format (the brightness may differ).</summary>
	public bool SameLayoutAs(FrameShape other) => Width == other.Width && Height == other.Height && Format == other.Format;

	/// <summary>Validates the record: the canvas at least 1 × 1, the divisor at least 1.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A dimension or the divisor is below 1.</exception>
	public FrameShape Validate()
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(CanvasWidth, 1, nameof(CanvasWidth));
		ArgumentOutOfRangeException.ThrowIfLessThan(CanvasHeight, 1, nameof(CanvasHeight));
		ArgumentOutOfRangeException.ThrowIfLessThan(Divisor, 1, nameof(Divisor));
		return this;
	}
}

/// <summary>
/// Turns a float plane of the canvas into a frame plane of a <see cref="FrameShape"/>: the reference
/// implementation of the shrinking and of the <see cref="FrameFormat.Index"/> mapping, used by the CPU engine
/// (on the thread that awaits a capture) and by the checks that hold the GPU kernels to it. Rows run in
/// parallel; the caller owns the buffers.
/// </summary>
public static class FrameConversion
{
	/// <summary>
	/// Converts one plane. For a divisor of 1 a <see cref="FrameFormat.Half"/> plane is the vectorised
	/// float-to-half conversion; otherwise every block of <see cref="FrameShape.Divisor"/>² cells (fewer at the
	/// right and bottom edges) is averaged first, then the sample is taken as a half or as the palette index
	/// (<see cref="ColorLut.FieldIndex"/> or <see cref="ColorLut.IntensityIndex(float, float)"/> with the shape's brightness)
	/// of the sample rounded to a half, so an index frame holds exactly what a half frame renders to (2026-10-19).
	/// </summary>
	/// <param name="source">The float values; row y of the canvas starts at <paramref name="offset"/> + y × <paramref name="stride"/>.</param>
	/// <param name="offset">Index of the canvas's first cell in <paramref name="source"/> (stride + 1 for an engine's padded buffer, 0 for a packed plane).</param>
	/// <param name="stride">Row pitch of <paramref name="source"/>.</param>
	/// <param name="shape">The frame's shape; its canvas size is the plane's.</param>
	/// <param name="intensity">Whether the plane is the intensity (its brightness, its index mapping) rather than the field.</param>
	/// <param name="destination">Receives <see cref="FrameShape.PlaneBytes"/> bytes, row-major samples.</param>
	/// <exception cref="ArgumentOutOfRangeException">The destination is too small.</exception>
	public static void Convert(float[] source, int offset, int stride, FrameShape shape, bool intensity, Memory<byte> destination)
	{
		int width = shape.Width;
		int height = shape.Height;
		if (destination.Length < shape.PlaneBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(destination), "The destination is smaller than a plane.");
		}
		int divisor = shape.Divisor;
		float brightness = shape.BrightnessOf(intensity);
		if (divisor == 1 && shape.Format == FrameFormat.Half)
		{
			Parallel.For(0, height, y =>
				TensorPrimitives.ConvertToHalf(source.AsSpan(offset + y * stride, width), MemoryMarshal.Cast<byte, Half>(destination.Span.Slice(y * width * 2, width * 2))));
			return;
		}
		int canvasWidth = shape.CanvasWidth;
		int canvasHeight = shape.CanvasHeight;
		Parallel.For(0, height, () => new float[width], (y, _, row) =>
		{
			if (divisor == 1)
			{
				source.AsSpan(offset + y * stride, width).CopyTo(row);
			}
			else
			{
				Average(source, offset, stride, canvasWidth, canvasHeight, divisor, y, row);
			}
			if (shape.Format == FrameFormat.Half)
			{
				TensorPrimitives.ConvertToHalf(row, MemoryMarshal.Cast<byte, Half>(destination.Span.Slice(y * width * 2, width * 2)));
			}
			else
			{
				Span<byte> target = destination.Span.Slice(y * width, width);
				if (intensity)
				{
					for (int x = 0; x < width; x++)
					{
						target[x] = (byte)ColorLut.IntensityIndex((float)(Half)row[x], brightness);
					}
				}
				else
				{
					for (int x = 0; x < width; x++)
					{
						target[x] = (byte)ColorLut.FieldIndex((float)(Half)row[x], brightness);
					}
				}
			}
			return row;
		}, _ => { });
	}

	/// <summary>One output row of block averages: the mean of every divisor × divisor block of the canvas, clipped at its right and bottom edges.</summary>
	private static void Average(float[] source, int offset, int stride, int canvasWidth, int canvasHeight, int divisor, int y, float[] row)
	{
		int rowStart = y * divisor;
		int rowEnd = Math.Min(canvasHeight, rowStart + divisor);
		for (int x = 0; x < row.Length; x++)
		{
			int columnStart = x * divisor;
			int columnEnd = Math.Min(canvasWidth, columnStart + divisor);
			float sum = 0f;
			for (int cy = rowStart; cy < rowEnd; cy++)
			{
				int at = offset + cy * stride;
				for (int cx = columnStart; cx < columnEnd; cx++)
				{
					sum += source[at + cx];
				}
			}
			row[x] = sum / ((rowEnd - rowStart) * (columnEnd - columnStart));
		}
	}
}
