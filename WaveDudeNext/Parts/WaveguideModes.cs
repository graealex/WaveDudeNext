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

using WaveDudeNext.Engine;

namespace WaveDudeNext.Parts;

/// <summary>
/// The guided modes of a straight waveguide as the engines see it: the transverse index profile is one value per
/// pixel row, and the mode is a solution of the engine's own discrete equations, not of the continuous wave
/// equation, so a design built on it holds on the grid. A mode u(x, y, t) = φ(y)·e^{i(βx − ωt)} of the 4th-order
/// stencil (<see cref="Model.Simulation.FourthOrderLaplacian"/>) with the leapfrog's sub-steps satisfies
/// L_y φ + Ω n²(y) φ = μ(β) φ, where L_y is the stencil's transverse part, Ω = 4S² sin²(ω / 2S) the leapfrog's
/// frequency term for S sub-steps per step, and μ(β) = cos(2β) / 6 − 8 cos(β) / 3 + 5 / 2 the stencil's
/// longitudinal part. The largest eigenvalue μ of the given symmetry is the fundamental mode. Units: pixels, steps,
/// radians. Pure arithmetic; any thread.
/// </summary>
public static class WaveguideModes
{
	/// <summary>
	/// The leapfrog's frequency term Ω = 4S² sin²(ω / 2S) of a wave of <paramref name="wavelength"/> pixels in a medium
	/// of index 1 (ω = 2π / λ radians per step, the source form's nominal wavelength), with the sub-step count the
	/// engines use on a scene whose fastest pixel has index 1.
	/// </summary>
	public static double Omega(double wavelength)
	{
		int subSteps = WaveSimulator.ComputeSubSteps(1f);
		double s = Math.Sin(2.0 * Math.PI / wavelength / (2.0 * subSteps));
		return 4.0 * subSteps * subSteps * s * s;
	}

	/// <summary>
	/// The propagation constant β (radians per pixel) of the fundamental even (<paramref name="odd"/> false) or odd mode
	/// of the index profile <paramref name="index"/>, one value per pixel row, the field 0 beyond both ends; null when no
	/// mode of that symmetry is guided (its μ does not exceed the edge rows' Ω n²). The symmetry is about the middle of
	/// the array: row j mirrors row length − 1 − j, so the profile should be symmetric for an odd mode to mean anything.
	/// </summary>
	public static double? Beta(double[] index, double omega, bool odd)
	{
		int n = index.Length;
		double[] potential = new double[n];
		double top = 0.0;
		for (int j = 0; j < n; j++)
		{
			potential[j] = omega * index[j] * index[j];
			top = Math.Max(top, potential[j]);
		}
		// A = L_y + diag(Ω n²) is at most max Ω n² (L_y is negative semi-definite), so σI − A is positive definite
		// and inverse iteration with it converges on the largest eigenvalue of the chosen symmetry
		double sigma = top + 1e-9;
		(double[] l0, double[] l1, double[] l2) = Cholesky(potential, sigma);
		double[] x = new double[n];
		for (int j = 0; j < n; j++)
		{
			double t = (j + 0.5) / n - 0.5;
			x[j] = odd ? t : 1.0 - t * t;
		}
		double mu = double.NaN;
		for (int iteration = 0; iteration < 400; iteration++)
		{
			Symmetrise(x, odd);
			Normalise(x);
			double next = Rayleigh(x, potential);
			if (Math.Abs(next - mu) < 1e-15)
			{
				mu = next;
				break;
			}
			mu = next;
			Solve(l0, l1, l2, x);
		}
		double edge = Math.Max(potential[0], potential[n - 1]);
		if (!(mu > edge))
		{
			return null;
		}
		return BetaOf(mu);
	}

	/// <summary>The β in (0, π) whose longitudinal stencil term cos(2β) / 6 − 8 cos(β) / 3 + 5 / 2 is <paramref name="mu"/> (rising monotonically from 0 to 16/3).</summary>
	public static double BetaOf(double mu)
	{
		double low = 0.0;
		double high = Math.PI;
		for (int i = 0; i < 100; i++)
		{
			double mid = (low + high) / 2.0;
			if (Math.Cos(2.0 * mid) / 6.0 - 8.0 * Math.Cos(mid) / 3.0 + 2.5 < mu)
			{
				low = mid;
			}
			else
			{
				high = mid;
			}
		}
		return (low + high) / 2.0;
	}

	/// <summary>
	/// The index of each pixel row as the painter paints a horizontal band structure: <paramref name="profile"/> sampled
	/// at the four sub-rows of each row, averaged and rounded to 0.01 (red = index × 100). Row j spans
	/// [<paramref name="first"/> + j, <paramref name="first"/> + j + 1) in the part frame.
	/// </summary>
	public static double[] Rows(int first, int count, Func<double, double> profile)
	{
		double[] rows = new double[count];
		for (int j = 0; j < count; j++)
		{
			double sum = 0.0;
			for (int k = 0; k < PartPainter.Supersample; k++)
			{
				sum += profile(first + j + (k + 0.5) / PartPainter.Supersample);
			}
			rows[j] = Math.Round(sum / PartPainter.Supersample, 2);
		}
		return rows;
	}

	/// <summary>Banded Cholesky factor of σI − A (A = L_y + diag(potential)), the bands of L: the diagonal, the first and the second sub-diagonal.</summary>
	private static (double[] L0, double[] L1, double[] L2) Cholesky(double[] potential, double sigma)
	{
		int n = potential.Length;
		double[] l0 = new double[n];
		double[] l1 = new double[n];
		double[] l2 = new double[n];
		// σI − A: diagonal σ + 5/2 − Ω n², first off-diagonal −4/3, second +1/12
		const double near = -4.0 / 3.0;
		const double far = 1.0 / 12.0;
		for (int j = 0; j < n; j++)
		{
			if (j >= 2)
			{
				l2[j] = far / l0[j - 2];
			}
			if (j >= 1)
			{
				double sum = near - (j >= 2 ? l2[j] * l1[j - 1] : 0.0);
				l1[j] = sum / l0[j - 1];
			}
			double diagonal = sigma + 2.5 - potential[j] - l1[j] * l1[j] - l2[j] * l2[j];
			l0[j] = Math.Sqrt(diagonal);
		}
		return (l0, l1, l2);
	}

	/// <summary>Overwrites <paramref name="x"/> with (L Lᵀ)⁻¹ x.</summary>
	private static void Solve(double[] l0, double[] l1, double[] l2, double[] x)
	{
		int n = x.Length;
		for (int j = 0; j < n; j++)
		{
			double sum = x[j];
			if (j >= 1)
			{
				sum -= l1[j] * x[j - 1];
			}
			if (j >= 2)
			{
				sum -= l2[j] * x[j - 2];
			}
			x[j] = sum / l0[j];
		}
		for (int j = n - 1; j >= 0; j--)
		{
			double sum = x[j];
			if (j + 1 < n)
			{
				sum -= l1[j + 1] * x[j + 1];
			}
			if (j + 2 < n)
			{
				sum -= l2[j + 2] * x[j + 2];
			}
			x[j] = sum / l0[j];
		}
	}

	/// <summary>xᵀ A x for a unit x.</summary>
	private static double Rayleigh(double[] x, double[] potential)
	{
		int n = x.Length;
		double sum = 0.0;
		for (int j = 0; j < n; j++)
		{
			double at(int k) => k >= 0 && k < n ? x[k] : 0.0;
			double ax = -(at(j - 2) + at(j + 2)) / 12.0 + 4.0 * (at(j - 1) + at(j + 1)) / 3.0 - 2.5 * x[j] + potential[j] * x[j];
			sum += x[j] * ax;
		}
		return sum;
	}

	/// <summary>Projects <paramref name="x"/> onto the even or odd vectors about the middle of the array.</summary>
	private static void Symmetrise(double[] x, bool odd)
	{
		int n = x.Length;
		for (int j = 0; j < n / 2; j++)
		{
			double a = x[j];
			double b = x[n - 1 - j];
			x[j] = odd ? (a - b) / 2.0 : (a + b) / 2.0;
			x[n - 1 - j] = odd ? -x[j] : x[j];
		}
		if (n % 2 == 1 && odd)
		{
			x[n / 2] = 0.0;
		}
	}

	/// <summary>Scales <paramref name="x"/> to unit length.</summary>
	private static void Normalise(double[] x)
	{
		double sum = 0.0;
		foreach (double v in x)
		{
			sum += v * v;
		}
		double scale = 1.0 / Math.Sqrt(sum);
		for (int j = 0; j < x.Length; j++)
		{
			x[j] *= scale;
		}
	}
}
