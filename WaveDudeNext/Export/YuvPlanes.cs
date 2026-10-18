// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace WaveDudeNext.Export;

/// <summary>
/// A frame as 8-bit Y, Cb and Cr planes, the chroma either at full resolution (4:4:4, the JPEG encoder since
/// 2026-10-18: a wave picture's information is in its colour) or sampled 2 × 2 (4:2:0, MPEG-1), padded to
/// whole 16 × 16 macroblocks by repeating the last column and row, which is what both encoders
/// consume. Converted with the BT.601 matrix: full range (JFIF, Y 0..255) for JPEG, studio range
/// (Y 16..235, chroma 16..240) for MPEG-1, which is what its decoders assume. Four pixels at a time on
/// <see cref="Vector128{T}"/>, the width every x64 and ARM64 machine accelerates (2026-10-14, the user's rule:
/// SIMD is everywhere now; this machine's .NET does not accelerate 256-bit vectors), rows in parallel. Reusable: <see cref="Convert"/> overwrites the planes. Any thread, one at a time.
/// </summary>
public sealed class YuvPlanes
{
	/// <param name="width">Picture width in pixels.</param>
	/// <param name="height">Picture height in pixels.</param>
	/// <param name="studioRange">Studio (MPEG) range instead of full (JFIF) range.</param>
	/// <param name="subsampled">Chroma sampled 2 × 2 (4:2:0) instead of at full resolution (4:4:4).</param>
	public YuvPlanes(int width, int height, bool studioRange, bool subsampled)
	{
		Width = width;
		Height = height;
		StudioRange = studioRange;
		Subsampled = subsampled;
		MacroblockColumns = (width + 15) / 16;
		MacroblockRows = (height + 15) / 16;
		PaddedWidth = MacroblockColumns * 16;
		PaddedHeight = MacroblockRows * 16;
		Y = new byte[PaddedWidth * PaddedHeight];
		Cb = new byte[subsampled ? PaddedWidth / 2 * (PaddedHeight / 2) : Y.Length];
		Cr = new byte[Cb.Length];
		if (studioRange)
		{
			_matrix = [16f, 0.257f, 0.504f, 0.098f, 128f, -0.148f, -0.291f, 0.439f, 128f, 0.439f, -0.368f, -0.071f];
		}
		else
		{
			_matrix = [0f, 0.299f, 0.587f, 0.114f, 128f, -0.168736f, -0.331264f, 0.5f, 128f, 0.5f, -0.418688f, -0.081312f];
		}
	}

	/// <summary>The conversion as (offset, r, g, b) for Y, Cb and Cr.</summary>
	private readonly float[] _matrix;

	/// <summary>Picture width in pixels.</summary>
	public int Width { get; }

	/// <summary>Picture height in pixels.</summary>
	public int Height { get; }

	/// <summary>Whether the planes use the studio range.</summary>
	public bool StudioRange { get; }

	/// <summary>Whether the chroma is sampled 2 × 2 (4:2:0); otherwise it has the luma's resolution (4:4:4).</summary>
	public bool Subsampled { get; }

	/// <summary>Macroblocks per row.</summary>
	public int MacroblockColumns { get; }

	/// <summary>Macroblock rows.</summary>
	public int MacroblockRows { get; }

	/// <summary>Width of the Y plane, a multiple of 16.</summary>
	public int PaddedWidth { get; }

	/// <summary>Height of the Y plane, a multiple of 16.</summary>
	public int PaddedHeight { get; }

	/// <summary>Width of the chroma planes: half the padded width when <see cref="Subsampled"/>, else all of it.</summary>
	public int ChromaWidth => Subsampled ? PaddedWidth / 2 : PaddedWidth;

	/// <summary>Luma plane, <see cref="PaddedWidth"/> × <see cref="PaddedHeight"/>, row-major.</summary>
	public byte[] Y { get; }

	/// <summary>Blue-difference plane, <see cref="ChromaWidth"/> wide, half the padded height when <see cref="Subsampled"/>, else all of it.</summary>
	public byte[] Cb { get; }

	/// <summary>Red-difference plane, like <see cref="Cb"/>.</summary>
	public byte[] Cr { get; }

	/// <summary>Converts a packed BGRA32 frame of <see cref="Width"/> × <see cref="Height"/> into the planes; macroblock rows run in parallel.</summary>
	public void Convert(ReadOnlyMemory<int> pixels)
	{
		Parallel.For(0, MacroblockRows, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, mbRow => ConvertRows(pixels.Span, mbRow * 16, mbRow * 16 + 16));
	}

	/// <summary>Converts picture rows <paramref name="fromY"/> (inclusive) to <paramref name="toY"/> (exclusive), an even count starting on an even row.</summary>
	private void ConvertRows(ReadOnlySpan<int> frame, int fromY, int toY)
	{
		int width = Width;
		int paddedWidth = PaddedWidth;
		int chromaWidth = ChromaWidth;
		Span<float> cb = stackalloc float[paddedWidth];
		Span<float> cr = stackalloc float[paddedWidth];
		Span<float> cbAbove = stackalloc float[paddedWidth];
		Span<float> crAbove = stackalloc float[paddedWidth];
		for (int y = fromY; y < toY; y++)
		{
			ReadOnlySpan<int> row = frame.Slice(Math.Min(y, Height - 1) * width, width);
			Span<byte> luma = Y.AsSpan(y * paddedWidth, paddedWidth);
			int x = 0;
			if (Vector128.IsHardwareAccelerated)
			{
				x = ConvertVector(row, luma, cb, cr);
			}
			for (; x < paddedWidth; x++)
			{
				int argb = row[Math.Min(x, width - 1)];
				float r = (argb >> 16) & 0xFF;
				float g = (argb >> 8) & 0xFF;
				float b = argb & 0xFF;
				luma[x] = ClampByte(_matrix[0] + _matrix[1] * r + _matrix[2] * g + _matrix[3] * b);
				cb[x] = _matrix[4] + _matrix[5] * r + _matrix[6] * g + _matrix[7] * b;
				cr[x] = _matrix[8] + _matrix[9] * r + _matrix[10] * g + _matrix[11] * b;
			}
			if (!Subsampled)
			{
				int chromaOffset = y * paddedWidth;
				for (int cx = 0; cx < paddedWidth; cx++)
				{
					Cb[chromaOffset + cx] = ClampByte(cb[cx]);
					Cr[chromaOffset + cx] = ClampByte(cr[cx]);
				}
			}
			else if ((y & 1) == 0)
			{
				cb.CopyTo(cbAbove);
				cr.CopyTo(crAbove);
			}
			else
			{
				// the 2 × 2 mean of this row and the one above
				int chromaOffset = (y >> 1) * chromaWidth;
				for (int cx = 0; cx < chromaWidth; cx++)
				{
					int px = cx * 2;
					Cb[chromaOffset + cx] = ClampByte((cb[px] + cb[px + 1] + cbAbove[px] + cbAbove[px + 1]) * 0.25f);
					Cr[chromaOffset + cx] = ClampByte((cr[px] + cr[px + 1] + crAbove[px] + crAbove[px + 1]) * 0.25f);
				}
			}
		}
	}

	/// <summary>Converts the pixels of one row four at a time; returns how many were done (the rest, fewer than four plus the padding, is scalar).</summary>
	private int ConvertVector(ReadOnlySpan<int> row, Span<byte> luma, Span<float> cb, Span<float> cr)
	{
		Vector128<int> mask = Vector128.Create(0xFF);
		Vector128<float> yOffset = Vector128.Create(_matrix[0]);
		Vector128<float> yR = Vector128.Create(_matrix[1]);
		Vector128<float> yG = Vector128.Create(_matrix[2]);
		Vector128<float> yB = Vector128.Create(_matrix[3]);
		Vector128<float> cbOffset = Vector128.Create(_matrix[4]);
		Vector128<float> cbR = Vector128.Create(_matrix[5]);
		Vector128<float> cbG = Vector128.Create(_matrix[6]);
		Vector128<float> cbB = Vector128.Create(_matrix[7]);
		Vector128<float> crOffset = Vector128.Create(_matrix[8]);
		Vector128<float> crR = Vector128.Create(_matrix[9]);
		Vector128<float> crG = Vector128.Create(_matrix[10]);
		Vector128<float> crB = Vector128.Create(_matrix[11]);
		Vector128<float> zero = Vector128<float>.Zero;
		Vector128<float> top = Vector128.Create(255f);
		Span<int> lumaInts = stackalloc int[4];
		int x = 0;
		for (; x + 4 <= row.Length; x += 4)
		{
			Vector128<int> argb = Vector128.Create(row.Slice(x, 4));
			Vector128<float> r = Vector128.ConvertToSingle(Vector128.ShiftRightLogical(argb, 16) & mask);
			Vector128<float> g = Vector128.ConvertToSingle(Vector128.ShiftRightLogical(argb, 8) & mask);
			Vector128<float> b = Vector128.ConvertToSingle(argb & mask);
			Vector128<float> yv = yB * b + yG * g + yR * r + yOffset;
			Vector128<float> cbv = cbB * b + cbG * g + cbR * r + cbOffset;
			Vector128<float> crv = crB * b + crG * g + crR * r + crOffset;
			Vector128.ConvertToInt32(Vector128.Round(Vector128.Clamp(yv, zero, top))).CopyTo(lumaInts);
			luma[x] = (byte)lumaInts[0];
			luma[x + 1] = (byte)lumaInts[1];
			luma[x + 2] = (byte)lumaInts[2];
			luma[x + 3] = (byte)lumaInts[3];
			cbv.CopyTo(cb.Slice(x, 4));
			crv.CopyTo(cr.Slice(x, 4));
		}
		return x;
	}

	private static byte ClampByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

	/// <summary>Copies the 8 × 8 block at (<paramref name="x"/>, <paramref name="y"/>) of a plane into <paramref name="block"/> as floats, minus <paramref name="offset"/>.</summary>
	/// <param name="plane">One of the planes.</param>
	/// <param name="planeWidth">The plane's row length.</param>
	/// <param name="x">Left column of the block.</param>
	/// <param name="y">Top row of the block.</param>
	/// <param name="offset">Subtracted from every sample (128 for JPEG's level shift, 0 for MPEG-1).</param>
	/// <param name="block">64 values, row-major.</param>
	public static void CopyBlock(byte[] plane, int planeWidth, int x, int y, float offset, Span<float> block)
	{
		for (int row = 0; row < 8; row++)
		{
			int source = (y + row) * planeWidth + x;
			for (int column = 0; column < 8; column++)
			{
				block[row * 8 + column] = plane[source + column] - offset;
			}
		}
	}
}

/// <summary>
/// The forward 8 × 8 DCT-II with the orthonormal scaling of JPEG and MPEG (<c>F(u,v) = ¼ C(u) C(v) ΣΣ f(x,y)
/// cos((2x+1)uπ/16) cos((2y+1)vπ/16)</c>, C(0) = 1/√2), computed separably: F = C · f · Cᵀ as two passes of
/// 64 multiply-adds on rows held as two <see cref="Vector128{T}"/> halves where the hardware has them, else in
/// scalar floats. Any thread.
/// </summary>
public static class Dct8x8
{
	private static readonly float[] Cosines = MakeCosines();
	private static readonly Vector128<float>[] CosineColumnsLow = MakeCosineColumns(0);
	private static readonly Vector128<float>[] CosineColumnsHigh = MakeCosineColumns(4);

	/// <summary>The zig-zag scan order shared by JPEG and MPEG-1: entry i is the row-major index of the i-th coefficient.</summary>
	public static readonly int[] ZigZag =
	[
		0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
		35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
	];

	/// <summary>
	/// The AAN output scaling per frequency index: <see cref="ForwardAan"/> gives F(u,v) × 8 × AanScale[u] × AanScale[v],
	/// which a quantiser folds into its divisors (the way the IJG float DCT is used).
	/// </summary>
	public static readonly double[] AanScale = [1.0, 1.387039845, 1.306562965, 1.175875602, 1.0, 0.785694958, 0.541196100, 0.275899379];

	/// <summary>The zig-zag order over the transposed layout of <see cref="ForwardAan"/> (index u × 8 + v): entry i is where the i-th coefficient sits.</summary>
	public static readonly int[] ZigZagAan = [.. ZigZag.Select(n => (n % 8) * 8 + n / 8)];

	/// <summary>
	/// The AAN factorisation (Arai, Agui, Nakajima; the IJG float DCT: 5 multiplications and 29 additions per
	/// 8-point transform) on SSE registers: a pass down the columns with the lanes along x, two in-register
	/// 4 × 4 transposes, the same pass again. The output is scaled (see <see cref="AanScale"/>) and lands
	/// transposed, <paramref name="coefficients"/>[u × 8 + v], so the caller indexes it with <see cref="ZigZagAan"/>.
	/// Without SSE the exact transform stands in, rearranged and scaled to match.
	/// </summary>
	public static void ForwardAan(ReadOnlySpan<float> block, Span<float> coefficients)
	{
		if (!Sse.IsSupported)
		{
			Span<float> exact = stackalloc float[64];
			Forward(block, exact);
			for (int v = 0; v < 8; v++)
			{
				for (int u = 0; u < 8; u++)
				{
					coefficients[u * 8 + v] = exact[v * 8 + u] * (float)(8.0 * AanScale[u] * AanScale[v]);
				}
			}
			return;
		}
		Span<Vector128<float>> lo = stackalloc Vector128<float>[8];
		Span<Vector128<float>> hi = stackalloc Vector128<float>[8];
		for (int y = 0; y < 8; y++)
		{
			lo[y] = Vector128.Create(block.Slice(y * 8, 4));
			hi[y] = Vector128.Create(block.Slice(y * 8 + 4, 4));
		}
		// pass 1: down the columns (the 8 rows are the 8 inputs), both halves of the columns
		Aan1D(lo);
		Aan1D(hi);
		// transpose: rows become columns; the quadrants swap across the diagonal
		Transpose4(lo, 0);
		Transpose4(hi, 4);
		Transpose4(lo, 4);
		Transpose4(hi, 0);
		for (int i = 0; i < 4; i++)
		{
			(lo[4 + i], hi[i]) = (hi[i], lo[4 + i]);
		}
		// pass 2: the former rows, now down the columns again
		Aan1D(lo);
		Aan1D(hi);
		for (int u = 0; u < 8; u++)
		{
			lo[u].CopyTo(coefficients.Slice(u * 8, 4));
			hi[u].CopyTo(coefficients.Slice(u * 8 + 4, 4));
		}
	}

	/// <summary>One 8-point AAN transform over the eight vectors (lane-wise), in place.</summary>
	private static void Aan1D(Span<Vector128<float>> d)
	{
		Vector128<float> tmp0 = d[0] + d[7];
		Vector128<float> tmp7 = d[0] - d[7];
		Vector128<float> tmp1 = d[1] + d[6];
		Vector128<float> tmp6 = d[1] - d[6];
		Vector128<float> tmp2 = d[2] + d[5];
		Vector128<float> tmp5 = d[2] - d[5];
		Vector128<float> tmp3 = d[3] + d[4];
		Vector128<float> tmp4 = d[3] - d[4];
		// even part
		Vector128<float> tmp10 = tmp0 + tmp3;
		Vector128<float> tmp13 = tmp0 - tmp3;
		Vector128<float> tmp11 = tmp1 + tmp2;
		Vector128<float> tmp12 = tmp1 - tmp2;
		d[0] = tmp10 + tmp11;
		d[4] = tmp10 - tmp11;
		Vector128<float> z1 = (tmp12 + tmp13) * Vector128.Create(0.707106781f);
		d[2] = tmp13 + z1;
		d[6] = tmp13 - z1;
		// odd part
		tmp10 = tmp4 + tmp5;
		tmp11 = tmp5 + tmp6;
		tmp12 = tmp6 + tmp7;
		Vector128<float> z5 = (tmp10 - tmp12) * Vector128.Create(0.382683433f);
		Vector128<float> z2 = Vector128.Create(0.541196100f) * tmp10 + z5;
		Vector128<float> z4 = Vector128.Create(1.306562965f) * tmp12 + z5;
		Vector128<float> z3 = tmp11 * Vector128.Create(0.707106781f);
		Vector128<float> z11 = tmp7 + z3;
		Vector128<float> z13 = tmp7 - z3;
		d[5] = z13 + z2;
		d[3] = z13 - z2;
		d[1] = z11 + z4;
		d[7] = z11 - z4;
	}

	/// <summary>Transposes the 4 × 4 block of rows <paramref name="first"/> .. +3 in place (the classic unpack/move sequence).</summary>
	private static void Transpose4(Span<Vector128<float>> rows, int first)
	{
		Vector128<float> r0 = rows[first];
		Vector128<float> r1 = rows[first + 1];
		Vector128<float> r2 = rows[first + 2];
		Vector128<float> r3 = rows[first + 3];
		Vector128<float> t0 = Sse.UnpackLow(r0, r1);
		Vector128<float> t1 = Sse.UnpackLow(r2, r3);
		Vector128<float> t2 = Sse.UnpackHigh(r0, r1);
		Vector128<float> t3 = Sse.UnpackHigh(r2, r3);
		rows[first] = Sse.MoveLowToHigh(t0, t1);
		rows[first + 1] = Sse.MoveHighToLow(t1, t0);
		rows[first + 2] = Sse.MoveLowToHigh(t2, t3);
		rows[first + 3] = Sse.MoveHighToLow(t3, t2);
	}

	/// <summary>Transforms <paramref name="block"/> (64 samples, row-major) into <paramref name="coefficients"/> (64, row-major, u along the row).</summary>
	public static void Forward(ReadOnlySpan<float> block, Span<float> coefficients)
	{
		if (Vector128.IsHardwareAccelerated)
		{
			ForwardVector(block, coefficients);
		}
		else
		{
			ForwardScalar(block, coefficients);
		}
	}

	/// <summary>
	/// Pass 1 down the columns with the lanes along x: T[v] = Σy C[v][y] · f[y]; pass 2 along the rows with the
	/// lanes along u: F[v] = Σx T[v][x] · C[·][x], the column x of C as a vector.
	/// </summary>
	private static void ForwardVector(ReadOnlySpan<float> block, Span<float> coefficients)
	{
		Span<Vector128<float>> low = stackalloc Vector128<float>[8];
		Span<Vector128<float>> high = stackalloc Vector128<float>[8];
		for (int y = 0; y < 8; y++)
		{
			low[y] = Vector128.Create(block.Slice(y * 8, 4));
			high[y] = Vector128.Create(block.Slice(y * 8 + 4, 4));
		}
		Span<float> t = stackalloc float[8];
		for (int v = 0; v < 8; v++)
		{
			Vector128<float> c0 = Vector128.Create(Cosines[v * 8]);
			Vector128<float> sumLow = c0 * low[0];
			Vector128<float> sumHigh = c0 * high[0];
			for (int y = 1; y < 8; y++)
			{
				Vector128<float> c = Vector128.Create(Cosines[v * 8 + y]);
				sumLow += c * low[y];
				sumHigh += c * high[y];
			}
			sumLow.CopyTo(t);
			sumHigh.CopyTo(t[4..]);
			Vector128<float> t0 = Vector128.Create(t[0]);
			Vector128<float> resultLow = t0 * CosineColumnsLow[0];
			Vector128<float> resultHigh = t0 * CosineColumnsHigh[0];
			for (int x = 1; x < 8; x++)
			{
				Vector128<float> tx = Vector128.Create(t[x]);
				resultLow += tx * CosineColumnsLow[x];
				resultHigh += tx * CosineColumnsHigh[x];
			}
			resultLow.CopyTo(coefficients.Slice(v * 8, 4));
			resultHigh.CopyTo(coefficients.Slice(v * 8 + 4, 4));
		}
	}

	private static void ForwardScalar(ReadOnlySpan<float> block, Span<float> coefficients)
	{
		Span<float> rows = stackalloc float[64];
		for (int y = 0; y < 8; y++)
		{
			for (int u = 0; u < 8; u++)
			{
				float sum = 0f;
				for (int x = 0; x < 8; x++)
				{
					sum += block[y * 8 + x] * Cosines[u * 8 + x];
				}
				rows[y * 8 + u] = sum;
			}
		}
		for (int u = 0; u < 8; u++)
		{
			for (int v = 0; v < 8; v++)
			{
				float sum = 0f;
				for (int y = 0; y < 8; y++)
				{
					sum += rows[y * 8 + u] * Cosines[v * 8 + y];
				}
				coefficients[v * 8 + u] = sum;
			}
		}
	}

	/// <summary><c>½ C(u) cos((2x+1)uπ/16)</c>, indexed u × 8 + x, so a row pass and a column pass together give the ¼ C(u) C(v) scaling.</summary>
	private static float[] MakeCosines()
	{
		var table = new float[64];
		for (int u = 0; u < 8; u++)
		{
			double scale = (u == 0 ? Math.Sqrt(0.5) : 1.0) * 0.5;
			for (int x = 0; x < 8; x++)
			{
				table[u * 8 + x] = (float)(scale * Math.Cos((2 * x + 1) * u * Math.PI / 16.0));
			}
		}
		return table;
	}

	/// <summary>Column x of the cosine table as a vector over u = <paramref name="firstU"/> .. +3.</summary>
	private static Vector128<float>[] MakeCosineColumns(int firstU)
	{
		var columns = new Vector128<float>[8];
		var column = new float[4];
		for (int x = 0; x < 8; x++)
		{
			for (int u = 0; u < 4; u++)
			{
				column[u] = Cosines[(firstU + u) * 8 + x];
			}
			columns[x] = Vector128.Create(column);
		}
		return columns;
	}
}
