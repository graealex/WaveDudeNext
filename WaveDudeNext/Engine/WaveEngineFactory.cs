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
/// Picks the wave engine for a run: the GPU (OpenCL) engine when requested and available, otherwise the CPU engine.
/// Called by the run on its worker once per run.
/// </summary>
public static class WaveEngineFactory
{
	/// <summary>
	/// Creates the engine for <paramref name="scene"/>. With <paramref name="preferGpu"/> set it first tries
	/// <see cref="GpuWaveSimulator.TryCreate"/>, translating the usual OpenCL loader failures (missing, wrong-bitness or
	/// incomplete OpenCL.dll) into a readable reason; any failure falls back to the CPU <see cref="WaveSimulator"/>.
	/// Never throws for GPU problems, but CPU engine construction may still throw (e.g. out of memory).
	/// Object sources run on either engine, the same as the image sources.
	/// </summary>
	/// <param name="scene">Per-pixel speed, damping and image sources of the canvas; the engine copies it into its padded grid.</param>
	/// <param name="options">The physics options passed to the engine (frequency scale, image source amplitude, intensity tracking, Laplacian order, time decay, intensity scale).</param>
	/// <param name="preferGpu">Try the OpenCL engine first (an application setting, not a project property).</param>
	/// <param name="emitters">The emitters of the object sources to drive in addition to the image sources; may be null.</param>
	/// <param name="note">
	/// Receives an empty string when the GPU was not requested or the GPU engine was created; "GPU not used: reason" when the GPU
	/// was requested but could not be used (the loader message above, or the per-device errors from <c>TryCreate</c>).
	/// </param>
	/// <returns>A ready engine; the caller owns and must dispose it.</returns>
	public static IWaveEngine Create(SceneData scene, EngineOptions options, bool preferGpu, IReadOnlyList<Emitter>? emitters, out string note)
	{
		note = "";
		if (preferGpu)
		{
			string gpuMessage;
			try
			{
				GpuWaveSimulator? gpu = GpuWaveSimulator.TryCreate(scene, options, emitters, out gpuMessage);
				if (gpu != null)
				{
					return gpu;
				}
			}
			catch (DllNotFoundException)
			{
				gpuMessage = "no OpenCL driver (OpenCL.dll) found; install the driver of the graphics card";
			}
			catch (BadImageFormatException)
			{
				gpuMessage = "the OpenCL driver does not match this 32/64-bit program";
			}
			catch (EntryPointNotFoundException)
			{
				gpuMessage = "the OpenCL driver is incomplete";
			}
			catch (Exception ex)
			{
				gpuMessage = ex.Message;
			}
			note = "GPU not used: " + gpuMessage;
		}
		return new WaveSimulator(scene, options, emitters);
	}
}
