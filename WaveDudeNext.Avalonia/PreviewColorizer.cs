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

// Contributors: Vinicius Miguel

using System.Runtime.InteropServices;
using NLog;
using WaveDudeNext.Engine;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>Colours preview planes on Vulkan when requested, falling back to the CPU. One caller at a time.</summary>
/// <remarks>The Vulkan path currently stages pixels through host memory before Avalonia presents them.</remarks>
internal sealed class PreviewColorizer : IDisposable
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private VulkanFrameRenderer? _gpu;
	private int _width, _height;
	private bool _gpuFailed;
	private int _gpuVersion;

	/// <summary>Colours both planes of a frame, or returns null if either is unavailable.</summary>
	internal (int[] Field, int[] Intensity)? Render(PresentationJob job)
	{
		IFrameSource source = job.Source;
		int width = source.Width, height = source.Height;
		if (_gpuVersion != job.GpuVersion) { _gpuVersion = job.GpuVersion; _gpuFailed = false; }
		if (!job.PreferGpu)
		{
			ReleaseGpu();
			_gpuFailed = false;
		}
		else if (!_gpuFailed && (_gpu == null || width != _width || height != _height))
		{
			ReleaseGpu();
			try
			{
				_gpu = new VulkanFrameRenderer(width, height);
				_width = width; _height = height;
				Logger.Info("Vulkan preview colouring on {Device} ({Width}×{Height}, host staging)", _gpu.DeviceName, width, height);
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				_gpuFailed = true;
				Logger.Warn(ex, "Vulkan preview colouring unavailable; using the CPU");
			}
		}
		int[]? Plane(FrameLayer layer, ColorLut lut, float brightness)
		{
			var pixels = new int[checked(width * height)];
			if (source.Format == FrameFormat.Index)
			{
				var indices = new byte[pixels.Length];
				if (!source.CopyLayer(job.Frame, layer, indices)) return null;
				if (!TryGpu(indices, FrameFormat.Index, lut, source.KindOf(layer), brightness, pixels))
					FrameRenderer.RenderIndexed(indices, width, height, lut, pixels);
			}
			else
			{
				var samples = new Half[pixels.Length];
				if (!source.CopyLayer(job.Frame, layer, samples)) return null;
				if (!TryGpu(MemoryMarshal.AsBytes(samples.AsSpan()), FrameFormat.Half, lut, source.KindOf(layer), brightness, pixels))
					FrameRenderer.Render(samples, width, height, lut, source.KindOf(layer), brightness, pixels);
			}
			return pixels;
		}
		int[]? field = Plane(FrameLayer.Field, job.FieldLut, job.FieldBrightness);
		int[]? intensity = Plane(FrameLayer.Intensity, job.IntensityLut, job.IntensityBrightness);
		return field == null || intensity == null ? null : (field, intensity);
	}

	private bool TryGpu(ReadOnlySpan<byte> samples, FrameFormat format, ColorLut lut, SampleKind kind, float brightness, Span<int> pixels)
	{
		if (_gpu == null) return false;
		try { _gpu.Render(samples, format, lut, kind, brightness, pixels); return true; }
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			Logger.Warn(ex, "Vulkan preview colouring failed; using the CPU");
			ReleaseGpu();
			_gpuFailed = true;
			return false;
		}
	}
	private void ReleaseGpu() { _gpu?.Dispose(); _gpu = null; }
	/// <summary>Releases Vulkan resources after any in-flight render has returned.</summary>
	public void Dispose() => ReleaseGpu();
}
