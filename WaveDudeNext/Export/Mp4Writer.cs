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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NLog;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes H.264 MP4 through the Media Foundation sink writer that ships with Windows (<c>mfplat.dll</c>,
/// <c>mfreadwrite.dll</c>, talked to through the hand-written COM interfaces in <see cref="MediaFoundation"/>),
/// so no package or codec is installed: the only export format with a dependency outside the library, and
/// that one is the operating system's. Hardware encoders are allowed (this machine's NVENC), the encoder
/// is asked for quality-based rate control with the project's 1..100 (the software encoder honours it, the
/// bitrate from the author's bits-per-pixel rule stays the fallback), no B-frames and a one-second GOP so
/// the file scrubs cleanly. Odd sizes are padded by repeating the last row or column (H.264 needs even
/// sizes; the author cropped). A failing constructor means "no H.264 on this machine" (Windows N without
/// the Media Feature Pack, or a size the encoder refuses). Windows only; one thread at a time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Mp4Writer : IFrameSink
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Largest picture the software encoder of Windows takes (4096 × 2304); hardware encoders may do more, but this is the safe limit.</summary>
	public const long MaxPixels = 4096L * 2304L;

	private readonly int _encodedWidth;
	private readonly int _encodedHeight;
	private readonly int[]? _padded;
	private readonly int _fps;
	private readonly YuvPlanes _planes;
	private bool _nv12;
	private IntPtr _writerPtr;
	private (IntPtr Manager, IntPtr Device)? _d3d;
	private MediaFoundation.IMFSinkWriter? _writer;
	private bool _started;
	private bool _closed;

	/// <param name="path">Output .mp4 path; an existing file is replaced.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="framesPerSecond">Playback frame rate.</param>
	/// <param name="quality">1..100: the encoder's quality setting, and the bitrate fallback.</param>
	/// <exception cref="NotSupportedException">The size is below 16 × 16 or above <see cref="MaxPixels"/>.</exception>
	/// <exception cref="COMException">Media Foundation refused (not installed, no H.264 encoder, bad path).</exception>
	public Mp4Writer(string path, int width, int height, int framesPerSecond, int quality)
	{
		Width = width;
		Height = height;
		Output = path;
		_fps = Math.Max(1, framesPerSecond);
		_encodedWidth = (width + 1) & ~1;
		_encodedHeight = (height + 1) & ~1;
		if (_encodedWidth < 16 || _encodedHeight < 16)
		{
			throw new NotSupportedException("The picture is too small for H.264 video (16 × 16 at least).");
		}
		if ((long)_encodedWidth * _encodedHeight > MaxPixels)
		{
			throw new NotSupportedException("The picture is too large for the H.264 encoder of Windows (4096 × 2304 at most).");
		}
		if (_encodedWidth != width || _encodedHeight != height)
		{
			_padded = new int[_encodedWidth * _encodedHeight];
		}
		_planes = new YuvPlanes(_encodedWidth, _encodedHeight, studioRange: true, subsampled: true);
		if (File.Exists(path))
		{
			File.Delete(path); // the sink writer appends to nothing, but a stale file must not survive a failure
		}
		Quality = Math.Clamp(quality, 1, 100);
		MediaFoundation.Check(MediaFoundation.MFStartup(MediaFoundation.Version, 0));
		_started = true;
		try
		{
			IntPtr writerAttributes = MediaFoundation.CreateAttributes();
			try
			{
				MediaFoundation.SetUInt32(writerAttributes, MediaFoundation.EnableHardwareTransforms, 1);
				if (!Throttle)
				{
					MediaFoundation.SetUInt32(writerAttributes, MediaFoundation.DisableThrottling, 1);
				}
				if (UseDirect3D)
				{
					// a Direct3D device for the hardware encoder: with one, the writer moves the samples into textures and the
					// transform runs asynchronously instead of being driven one sample at a time through system memory
					try
					{
						_d3d = MediaFoundation.CreateDeviceManager();
						if (_d3d is { } d3d)
						{
							MediaFoundation.SetUnknown(writerAttributes, MediaFoundation.SinkWriterD3DManager, d3d.Manager);
						}
					}
					catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
					{
						Logger.Debug("No Direct3D device for the encoder: {Message}", ex.Message);
						_d3d = null;
					}
				}
				MediaFoundation.Check(MediaFoundation.MFCreateSinkWriterFromURL(path, IntPtr.Zero, writerAttributes, ref _writerPtr));
			}
			finally
			{
				Marshal.Release(writerAttributes);
			}
			_writer = (MediaFoundation.IMFSinkWriter)Marshal.GetObjectForIUnknown(_writerPtr);
			// the author's bitrate rule: 0.01 × 50^(quality/100) bits per pixel (0.15 at 70), 300 kbit/s to 200 Mbit/s
			double bitsPerPixel = 0.01 * Math.Pow(50.0, Quality / 100.0);
			long bitrate = (long)Math.Round((double)_encodedWidth * _encodedHeight * _fps * bitsPerPixel);
			Bitrate = (int)Math.Clamp(bitrate, 300_000L, 200_000_000L);
			IntPtr outputType = MakeType(MediaFoundation.H264Format, Bitrate, topDown: false);
			int streamIndex = 0;
			try
			{
				MediaFoundation.Check(_writer.AddStream(outputType, ref streamIndex));
			}
			finally
			{
				Marshal.Release(outputType);
			}
			// quality mode, no B-frames, a GOP per second; an encoder that rejects them gets the bitrate alone
			IntPtr parameters = MediaFoundation.CreateAttributes();
			try
			{
				MediaFoundation.SetUInt32(parameters, MediaFoundation.CodecRateControlMode, MediaFoundation.RateControlQuality);
				MediaFoundation.SetUInt32(parameters, MediaFoundation.CodecQuality, Quality);
				MediaFoundation.SetUInt32(parameters, MediaFoundation.CodecGopSize, _fps);
				MediaFoundation.SetUInt32(parameters, MediaFoundation.CodecBPictureCount, 0);
				if (Fastest)
				{
					// the encoder's fastest path: speed over quality, low latency, real time
					MediaFoundation.SetUInt32(parameters, MediaFoundation.CodecQualityVsSpeed, 0);
					MediaFoundation.SetUInt32(parameters, MediaFoundation.CodecLowLatency, 1);
					MediaFoundation.SetUInt32(parameters, MediaFoundation.CodecRealTime, 1);
				}
				// NV12 first, converted here on every core: with RGB32 the writer puts its own colour converter before the
				// encoder, one thread for a whole frame, which held the hardware encoder to 60 frames a second (2026-10-15)
				IntPtr wanted = QualityMode ? parameters : IntPtr.Zero;
				UsedBitrateOnly = !QualityMode;
				_nv12 = PreferNv12 && TrySetInput(streamIndex, MediaFoundation.Nv12Format, wanted);
				if (!_nv12 && !TrySetInput(streamIndex, MediaFoundation.Rgb32Format, wanted))
				{
					_nv12 = TrySetInput(streamIndex, MediaFoundation.Nv12Format, IntPtr.Zero);
					if (!_nv12)
					{
						IntPtr rgb = MakeType(MediaFoundation.Rgb32Format, 0, topDown: true);
						try
						{
							MediaFoundation.Check(_writer.SetInputMediaType(streamIndex, rgb, IntPtr.Zero));
						}
						finally
						{
							Marshal.Release(rgb);
						}
					}
					UsedBitrateOnly = true;
				}
			}
			finally
			{
				Marshal.Release(parameters);
			}
			MediaFoundation.Check(_writer.BeginWriting());
			Transforms = DescribeTransforms(streamIndex);
			Logger.Debug("MP4: {Input} in, {Transforms}, {D3D}; quality {Quality}, average bitrate {Bitrate} bit/s{Note}", _nv12 ? "NV12" : "RGB32", Transforms, _d3d != null ? "Direct3D device shared" : "system memory", Quality, Bitrate, UsedBitrateOnly ? ", the encoder took no quality parameters" : "");
		}
		catch
		{
			Cleanup();
			throw;
		}
	}

	/// <summary>The transforms the sink writer put behind the stream (a colour converter, the encoder), by their friendly names.</summary>
	public string Transforms { get; private set; } = "";

	/// <summary>Whether the sink writer paces <c>WriteSample</c> (its default); off, samples queue up to the encoder's speed. For the bench.</summary>
	public static bool Throttle { get; set; } = true;

	/// <summary>Whether the encoder is asked for its fastest path (quality-versus-speed 0, low latency, real time); for the bench's comparison.</summary>
	public static bool Fastest { get; set; }

	/// <summary>Whether a Direct3D 11 device is handed to the writer for the hardware encoder (the default); off for the bench's comparison.</summary>
	public static bool UseDirect3D { get; set; } = true;

	/// <summary>Whether the writer got a Direct3D device.</summary>
	public bool HasDirect3D => _d3d != null;

	/// <summary>Whether NV12 input is tried first (the default); off, the frames go in as RGB32 and the writer converts. For the bench.</summary>
	public static bool PreferNv12 { get; set; } = true;

	/// <summary>Whether the quality rate control is asked for (the default); off, the encoder gets the average bitrate alone. For the bench.</summary>
	public static bool QualityMode { get; set; } = true;

	/// <summary>Whether the frames go in as NV12 (converted here) rather than RGB32 (converted by the writer).</summary>
	public bool Nv12Input => _nv12;

	/// <summary>Sets the input type of the stream; false when the writer refused it (with or without the encoding parameters).</summary>
	private bool TrySetInput(int streamIndex, Guid format, IntPtr parameters)
	{
		IntPtr inputType = MakeType(format, 0, topDown: format == MediaFoundation.Rgb32Format);
		try
		{
			if (format == MediaFoundation.Nv12Format)
			{
				MediaFoundation.SetUInt32(inputType, MediaFoundation.NominalRange, 2); // studio range, what the planes produce
			}
			return _writer!.SetInputMediaType(streamIndex, inputType, parameters) >= 0;
		}
		finally
		{
			Marshal.Release(inputType);
		}
	}

	/// <summary>The registered H.264 encoders, hardware ones only or all, for the log (empty when Media Foundation is absent).</summary>
	public static IReadOnlyList<string> AvailableEncoders(bool hardwareOnly)
	{
		try
		{
			MediaFoundation.Check(MediaFoundation.MFStartup(MediaFoundation.Version, 0));
			try
			{
				return MediaFoundation.H264Encoders(hardwareOnly);
			}
			finally
			{
				MediaFoundation.MFShutdown();
			}
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
		{
			return [];
		}
	}

	private string DescribeTransforms(int streamIndex)
	{
		if (_writer is not MediaFoundation.IMFSinkWriterEx extended)
		{
			return "(not reported)";
		}
		var names = new List<string>();
		for (int i = 0; i < 8; i++)
		{
			if (extended.GetTransformForStream(streamIndex, i, out _, out IntPtr transform) < 0 || transform == IntPtr.Zero)
			{
				break;
			}
			names.Add(MediaFoundation.FriendlyNameOf(transform) ?? "(unnamed transform)");
			Marshal.Release(transform);
		}
		return names.Count == 0 ? "(none reported)" : string.Join(" → ", names);
	}

	/// <inheritdoc/>
	public int Width { get; }

	/// <inheritdoc/>
	public int Height { get; }

	/// <inheritdoc/>
	public int FrameCount { get; private set; }

	/// <inheritdoc/>
	public string Output { get; }

	/// <summary>The quality 1..100 the encoder was given.</summary>
	public int Quality { get; }

	/// <summary>The average bitrate in bits per second from the author's rule (the fallback, and what the output type declares).</summary>
	public int Bitrate { get; }

	/// <summary>Whether the encoder refused the quality parameters and runs on the bitrate alone.</summary>
	public bool UsedBitrateOnly { get; private set; }

	/// <summary>Whether Media Foundation is there at all: the libraries load and start (false on Windows N without the Media Feature Pack).</summary>
	public static bool IsAvailable()
	{
		try
		{
			if (MediaFoundation.MFStartup(MediaFoundation.Version, 0) < 0)
			{
				return false;
			}
			MediaFoundation.MFShutdown();
			return true;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			return false;
		}
	}

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<int> pixels)
	{
		if (_closed || _writer == null)
		{
			throw new InvalidOperationException("The video file is already closed.");
		}
		if (pixels.Length < Width * Height)
		{
			throw new ArgumentException("The frame is smaller than the video.", nameof(pixels));
		}
		ReadOnlySpan<int> source = pixels[..(Width * Height)];
		if (_padded != null)
		{
			for (int y = 0; y < _encodedHeight; y++)
			{
				ReadOnlySpan<int> row = pixels.Slice(Math.Min(y, Height - 1) * Width, Width);
				Span<int> target = _padded.AsSpan(y * _encodedWidth, _encodedWidth);
				row.CopyTo(target);
				if (_encodedWidth > Width)
				{
					target[Width] = row[Width - 1];
				}
			}
			source = _padded;
		}
		int byteLength = _nv12 ? _encodedWidth * _encodedHeight * 3 / 2 : _encodedWidth * _encodedHeight * 4;
		if (_nv12)
		{
			_planes.Convert(source.ToArray());
		}
		IntPtr bufferPtr = IntPtr.Zero;
		IntPtr samplePtr = IntPtr.Zero;
		MediaFoundation.IMFMediaBuffer? buffer = null;
		MediaFoundation.IMFSample? sample = null;
		try
		{
			MediaFoundation.Check(MediaFoundation.MFCreateMemoryBuffer(byteLength, ref bufferPtr));
			buffer = (MediaFoundation.IMFMediaBuffer)Marshal.GetObjectForIUnknown(bufferPtr);
			IntPtr data = IntPtr.Zero;
			int maxLength = 0;
			int currentLength = 0;
			MediaFoundation.Check(buffer.Lock(ref data, ref maxLength, ref currentLength));
			try
			{
				unsafe
				{
					if (_nv12)
					{
						CopyNv12(new Span<byte>((void*)data, byteLength));
					}
					else
					{
						source.CopyTo(new Span<int>((void*)data, source.Length));
					}
				}
			}
			finally
			{
				buffer.Unlock();
			}
			MediaFoundation.Check(buffer.SetCurrentLength(byteLength));
			MediaFoundation.Check(MediaFoundation.MFCreateSample(ref samplePtr));
			sample = (MediaFoundation.IMFSample)Marshal.GetObjectForIUnknown(samplePtr);
			MediaFoundation.Check(sample.AddBuffer(bufferPtr));
			MediaFoundation.Check(sample.SetSampleTime(FrameCount * 10_000_000L / _fps));
			MediaFoundation.Check(sample.SetSampleDuration(10_000_000L / _fps));
			MediaFoundation.Check(_writer.WriteSample(0, samplePtr));
			FrameCount++;
		}
		finally
		{
			if (sample != null)
			{
				Marshal.ReleaseComObject(sample);
			}
			if (samplePtr != IntPtr.Zero)
			{
				Marshal.Release(samplePtr);
			}
			if (buffer != null)
			{
				Marshal.ReleaseComObject(buffer);
			}
			if (bufferPtr != IntPtr.Zero)
			{
				Marshal.Release(bufferPtr);
			}
		}
	}

	/// <inheritdoc/>
	public void Close()
	{
		if (_closed)
		{
			return;
		}
		_closed = true;
		try
		{
			if (_writer != null && FrameCount > 0)
			{
				MediaFoundation.Check(_writer.FinalizeWriting());
			}
		}
		finally
		{
			Cleanup();
		}
	}

	/// <inheritdoc/>
	public void Dispose() => Close();

	/// <summary>The planes as NV12: the Y rows (the planes are padded to 16, the picture is not), then Cb and Cr interleaved at half resolution.</summary>
	private void CopyNv12(Span<byte> target)
	{
		int w = _encodedWidth;
		int h = _encodedHeight;
		for (int y = 0; y < h; y++)
		{
			_planes.Y.AsSpan(y * _planes.PaddedWidth, w).CopyTo(target.Slice(y * w, w));
		}
		Span<byte> uv = target[(w * h)..];
		int chromaWidth = _planes.ChromaWidth;
		for (int y = 0; y < h / 2; y++)
		{
			ReadOnlySpan<byte> cb = _planes.Cb.AsSpan(y * chromaWidth, w / 2);
			ReadOnlySpan<byte> cr = _planes.Cr.AsSpan(y * chromaWidth, w / 2);
			Span<byte> row = uv.Slice(y * w, w);
			for (int x = 0; x < w / 2; x++)
			{
				row[2 * x] = cb[x];
				row[2 * x + 1] = cr[x];
			}
		}
	}

	/// <summary>A progressive video media type of the encoded size, the frame rate and square pixels; the caller releases it.</summary>
	private IntPtr MakeType(Guid subtype, int bitrate, bool topDown)
	{
		IntPtr mediaType = IntPtr.Zero;
		MediaFoundation.Check(MediaFoundation.MFCreateMediaType(ref mediaType));
		try
		{
			MediaFoundation.SetGuid(mediaType, MediaFoundation.MajorType, MediaFoundation.VideoType);
			MediaFoundation.SetGuid(mediaType, MediaFoundation.SubType, subtype);
			if (bitrate > 0)
			{
				MediaFoundation.SetUInt32(mediaType, MediaFoundation.AvgBitrate, bitrate);
			}
			MediaFoundation.SetUInt32(mediaType, MediaFoundation.InterlaceMode, 2); // progressive
			MediaFoundation.SetUInt64(mediaType, MediaFoundation.FrameSize, ((ulong)(uint)_encodedWidth << 32) | (uint)_encodedHeight);
			MediaFoundation.SetUInt64(mediaType, MediaFoundation.FrameRate, ((ulong)(uint)_fps << 32) | 1UL);
			MediaFoundation.SetUInt64(mediaType, MediaFoundation.PixelAspectRatio, (1UL << 32) | 1UL);
			if (topDown)
			{
				MediaFoundation.SetUInt32(mediaType, MediaFoundation.DefaultStride, _encodedWidth * 4);
			}
		}
		catch
		{
			Marshal.Release(mediaType);
			throw;
		}
		return mediaType;
	}

	private void Cleanup()
	{
		if (_writer != null)
		{
			Marshal.ReleaseComObject(_writer);
			_writer = null;
		}
		if (_writerPtr != IntPtr.Zero)
		{
			Marshal.Release(_writerPtr);
			_writerPtr = IntPtr.Zero;
		}
		if (_d3d is { } d3d)
		{
			_d3d = null;
			MediaFoundation.ReleaseDeviceManager(d3d);
		}
		if (_started)
		{
			_started = false;
			MediaFoundation.MFShutdown();
		}
	}
}
