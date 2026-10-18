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

namespace WaveDudeNext.Export;

/// <summary>The export formats, in the order of the Save dialog's filter.</summary>
public enum ExportFormat
{
	/// <summary>A PNG per frame in a folder of its own; lossless.</summary>
	Png,
	/// <summary>A JPEG per frame in a folder of its own; the quality setting applies.</summary>
	Jpeg,
	/// <summary>An OpenEXR file per frame in a folder of its own: the stored 16-bit floats themselves, one channel, uncompressed; data, not a rendered picture (2026-10-18).</summary>
	Exr,
	/// <summary>An MPEG-1 program stream of I-pictures (<c>.mpg</c>); the quality setting and the nearest standard frame rate apply.</summary>
	Mpeg1,
	/// <summary>An uncompressed AVI with OpenDML indexes; lossless, large.</summary>
	Avi,
	/// <summary>An H.264 MP4 through the Media Foundation of Windows; the quality setting applies.</summary>
	Mp4,
	/// <summary>A QuickTime movie with the Animation codec: lossless RGB, run-length coded, what the editing suites open natively.</summary>
	Mov,
}

/// <summary>What is known about each <see cref="ExportFormat"/>: extensions, whether it is a sequence, how to open its sink.</summary>
public static class ExportFormats
{
	private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

	/// <summary>
	/// The preferred JPEG encoder: given the frame size and the quality 1..100, a writer of one packed BGRA32
	/// frame to a stream. On Windows the WIC codec (<see cref="WicJpegEncoder.Encoder"/>, 4:4:4 natively),
	/// elsewhere null, which means the in-house <see cref="JpegEncoder"/>; when the preferred one throws, the
	/// sink logs it once and goes on with the in-house encoder. The harness swaps it to prove that fallback and
	/// to time the in-house encoder in a job (2026-10-18: until then the application installed WIC through a
	/// public hook, which the library no longer needs now that it holds the WIC encoder itself).
	/// </summary>
	internal static Func<int, int, int, Action<Stream, ReadOnlyMemory<int>>>? PreferredJpeg { get; set; } = DefaultPreferredJpeg();

	private static Func<int, int, int, Action<Stream, ReadOnlyMemory<int>>>? DefaultPreferredJpeg()
	{
		if (OperatingSystem.IsWindows())
		{
			return WicJpegEncoder.Encoder;
		}
		return null;
	}

	/// <summary>
	/// Frames of a sequence encoded at once: half the cores for encoders that are single-threaded per picture
	/// (PNG's zlib, the WIC codec), one for the in-house JPEG encoder, which already uses every core itself.
	/// </summary>
	public static int SequenceWorkers(bool encoderIsParallel) => encoderIsParallel ? 1 : Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

	/// <summary>
	/// The JPEG writer for a size and quality: the preferred encoder with the in-house fallback (the in-house
	/// encoder is only built when needed), or the in-house one. One writer per thread: neither is shareable.
	/// </summary>
	private static Action<Stream, ReadOnlyMemory<int>> JpegWriter(int width, int height, int quality)
	{
		JpegEncoder? inHouse = null;
		Action<Stream, ReadOnlyMemory<int>> InHouse() => (inHouse ??= new JpegEncoder(width, height, quality)).Write;
		if (PreferredJpeg is not { } make)
		{
			return InHouse();
		}
		Action<Stream, ReadOnlyMemory<int>> preferred;
		try
		{
			preferred = make(width, height, quality);
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "The preferred JPEG encoder is not available ({Message}); using the in-house encoder", ex.Message);
			return InHouse();
		}
		bool fallen = false;
		return (stream, frame) =>
		{
			if (!fallen)
			{
				long start = stream.CanSeek ? stream.Position : 0;
				try
				{
					preferred(stream, frame);
					return;
				}
				catch (Exception ex)
				{
					fallen = true;
					Logger.Warn(ex, "The preferred JPEG encoder failed ({Message}); the in-house encoder takes over", ex.Message);
					if (stream.CanSeek)
					{
						stream.SetLength(start);
						stream.Position = start;
					}
				}
			}
			InHouse()(stream, frame);
		};
	}

	/// <summary>The formats in filter order.</summary>
	public static readonly ExportFormat[] All = [ExportFormat.Png, ExportFormat.Jpeg, ExportFormat.Exr, ExportFormat.Mpeg1, ExportFormat.Avi, ExportFormat.Mp4, ExportFormat.Mov];

	/// <summary>The file extension with its dot.</summary>
	public static string Extension(ExportFormat format) => format switch
	{
		ExportFormat.Png => ".png",
		ExportFormat.Jpeg => ".jpg",
		ExportFormat.Exr => ".exr",
		ExportFormat.Mpeg1 => ".mpg",
		ExportFormat.Avi => ".avi",
		ExportFormat.Mp4 => ".mp4",
		ExportFormat.Mov => ".mov",
		_ => throw new ArgumentOutOfRangeException(nameof(format)),
	};

	/// <summary>The format of a path by its extension (<c>.jpeg</c> and <c>.mpeg</c> count too), or null.</summary>
	public static ExportFormat? FromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
	{
		".png" => ExportFormat.Png,
		".jpg" or ".jpeg" => ExportFormat.Jpeg,
		".exr" => ExportFormat.Exr,
		".mpg" or ".mpeg" or ".m1v" => ExportFormat.Mpeg1,
		".avi" => ExportFormat.Avi,
		".mp4" or ".m4v" => ExportFormat.Mp4,
		".mov" or ".qt" => ExportFormat.Mov,
		_ => null,
	};

	/// <summary>Whether the format writes one file per frame (these are also the formats a still can take).</summary>
	public static bool IsSequence(ExportFormat format) => format is ExportFormat.Png or ExportFormat.Jpeg or ExportFormat.Exr;

	/// <summary>Whether the format holds the stored values rather than a rendered picture (OpenEXR): colours, brightness, outline and boxes do not apply.</summary>
	public static bool IsData(ExportFormat format) => format == ExportFormat.Exr;

	/// <summary>Whether the quality setting means anything to the format.</summary>
	public static bool UsesQuality(ExportFormat format) => format is ExportFormat.Jpeg or ExportFormat.Mpeg1 or ExportFormat.Mp4;

	/// <summary>Whether the format can be written here (MP4 needs Media Foundation).</summary>
	public static bool IsAvailable(ExportFormat format) => format != ExportFormat.Mp4 || (OperatingSystem.IsWindows() && Mp4Writer.IsAvailable());

	/// <summary>
	/// Where a sequence chosen as <c>C:\Temp\name.png</c> goes: the folder <c>C:\Temp\name</c>, holding
	/// <c>name_0000.png</c> and on (the user's rule, 2026-10-14).
	/// </summary>
	public static string SequenceFolder(string path) =>
		Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path));

	/// <summary>Opens a one-file sink for a still (PNG, JPEG or OpenEXR) at <paramref name="path"/>.</summary>
	/// <param name="format">PNG, JPEG or OpenEXR.</param>
	/// <param name="path">The file.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="quality">1..100 for JPEG.</param>
	/// <exception cref="NotSupportedException">A video format was asked for.</exception>
	public static IFrameSink OpenStill(ExportFormat format, string path, int width, int height, int quality) => format switch
	{
		ExportFormat.Png => new ImageFileSink(path, width, height, (stream, frame) => PngEncoder.Write(stream, frame.Span, width, height)),
		ExportFormat.Jpeg => new ImageFileSink(path, width, height, JpegWriter(width, height, quality)),
		ExportFormat.Exr => ExrSink.Still(path, width, height),
		_ => throw new NotSupportedException("A still is a PNG, a JPEG or an OpenEXR file."),
	};

	/// <summary>
	/// Opens the sink for a format at <paramref name="path"/> (for a sequence, the folder of <see cref="SequenceFolder"/>).
	/// </summary>
	/// <param name="format">The format.</param>
	/// <param name="path">The file the user chose; a sequence takes its folder and base name from it.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="fps">Frame rate of a video.</param>
	/// <param name="quality">1..100 for the lossy formats.</param>
	/// <param name="plannedFrames">How many frames will come at most (the AVI sizes its indexes by it, a sequence the width of its frame numbers).</param>
	/// <exception cref="NotSupportedException">The format cannot carry the size, or MP4 is not available here.</exception>
	public static IFrameSink Open(ExportFormat format, string path, int width, int height, int fps, int quality, int plannedFrames)
	{
		switch (format)
		{
			case ExportFormat.Png:
				return new ImageSequenceSink(SequenceFolder(path), Path.GetFileNameWithoutExtension(path), ".png", width, height,
					() => (stream, frame) => PngEncoder.Write(stream, frame.Span, width, height), SequenceWorkers(encoderIsParallel: false), plannedFrames);
			case ExportFormat.Jpeg:
				return new ImageSequenceSink(SequenceFolder(path), Path.GetFileNameWithoutExtension(path), ".jpg", width, height,
					() => JpegWriter(width, height, quality), SequenceWorkers(encoderIsParallel: PreferredJpeg == null), plannedFrames);
			case ExportFormat.Exr:
				return ExrSink.Sequence(SequenceFolder(path), Path.GetFileNameWithoutExtension(path), width, height, plannedFrames);
			case ExportFormat.Mpeg1:
				return new Mpeg1Writer(path, width, height, fps, quality);
			case ExportFormat.Avi:
				return new AviWriter(path, width, height, fps, plannedFrames);
			case ExportFormat.Mp4:
				if (!OperatingSystem.IsWindows())
				{
					throw new NotSupportedException("MP4 export needs the Media Foundation of Windows.");
				}
				return new Mp4Writer(path, width, height, fps, quality);
			case ExportFormat.Mov:
				return new MovAnimationWriter(path, width, height, fps, plannedFrames);
			default:
				throw new ArgumentOutOfRangeException(nameof(format));
		}
	}
}
