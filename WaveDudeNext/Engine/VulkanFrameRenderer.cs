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
using WaveDudeNext.Rendering;

namespace WaveDudeNext.Engine;

/// <summary>Colours stored frames with Vulkan 1.3 using the same lookup tables as <see cref="FrameRenderer"/>. One caller at a time.</summary>
/// <remarks>This staging implementation reads the coloured pixels back to the host; it does not yet share an image with the UI compositor.</remarks>
public sealed class VulkanFrameRenderer : IDisposable
{
	private readonly VulkanComputeContext _context;
	private readonly VulkanBuffer _samples;
	private readonly VulkanBuffer _colours;
	private readonly VulkanBuffer _pixels;
	private readonly VulkanPipeline _pipeline;
	private readonly int _count;
	private bool _disposed;

	/// <summary>Creates buffers for a frame of <paramref name="width"/> × <paramref name="height"/> samples.</summary>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="deviceUuid">Optional 16-byte Vulkan device UUID. A supplied UUID must match a hardware GPU.</param>
	public VulkanFrameRenderer(int width, int height, byte[]? deviceUuid = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		_count = checked(width * height);
		int sampleBytes = checked((checked(_count * 2) + 3) & ~3);
		int pixelBytes = checked(_count * sizeof(int));
		_context = new VulkanComputeContext(deviceUuid);
		VulkanBuffer? samples = null, colours = null, pixels = null;
		try
		{
			samples = new VulkanBuffer(_context, sampleBytes);
			colours = new VulkanBuffer(_context, 65536 * sizeof(int));
			pixels = new VulkanBuffer(_context, pixelBytes, preferHostCached: true);
			_pipeline = new VulkanPipeline(_context, "Colorize", [samples, colours, pixels], (uint)Marshal.SizeOf<Parameters>());
			_samples = samples; _colours = colours; _pixels = pixels;
		}
		catch
		{
			pixels?.Dispose(); colours?.Dispose(); samples?.Dispose(); _context.Dispose();
			throw;
		}
	}

	/// <summary>The Vulkan device chosen for colouring.</summary>
	public string DeviceName => _context.Name;

	/// <summary>Colours a packed frame into BGRA32 pixels. The samples and destination must contain the frame's full size.</summary>
	/// <param name="samples">Half-value bytes or palette-index bytes in row-major order.</param>
	/// <param name="format">The sample format.</param>
	/// <param name="lut">The colour lookup table.</param>
	/// <param name="kind">The plane's sample kind, for a half frame.</param>
	/// <param name="brightness">The display brightness, for a half frame.</param>
	/// <param name="destination">The output pixels in row-major order.</param>
	public void Render(ReadOnlySpan<byte> samples, FrameFormat format, ColorLut lut, SampleKind kind, float brightness, Span<int> destination)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(lut);
		if (format is not (FrameFormat.Half or FrameFormat.Index)) throw new ArgumentOutOfRangeException(nameof(format));
		int bytes = checked(_count * (format == FrameFormat.Half ? 2 : 1));
		if (samples.Length < bytes) throw new ArgumentException("The frame is shorter than its dimensions.", nameof(samples));
		if (destination.Length < _count) throw new ArgumentException("The output is shorter than the frame.", nameof(destination));
		samples[..bytes].CopyTo(_samples.Bytes);
		int[] table = format == FrameFormat.Index ? lut.Argb : lut.HalfTable(kind, brightness);
		MemoryMarshal.AsBytes(table.AsSpan()).CopyTo(_colours.Bytes);
		var parameters = new Parameters { Count = _count, IndexFormat = format == FrameFormat.Index ? 1 : 0 };
		_context.Execute(command => _pipeline.Record(command, in parameters, (uint)((_count + 63L) / 64)));
		MemoryMarshal.Cast<byte, int>(_pixels.Bytes)[.._count].CopyTo(destination);
	}

	/// <summary>Releases the renderer and its Vulkan resources after its last call has returned.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_pipeline.Dispose(); _pixels.Dispose(); _colours.Dispose(); _samples.Dispose(); _context.Dispose();
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Parameters { public int Count; public int IndexFormat; }
}
