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

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes OpenEXR files (2026-10-18, the user's choice for a lossless export of the stored data: the one image format
/// made for 16-bit floats, which the compositing viewers, DJV among them, open as pictures and NumPy, MATLAB's
/// <c>exrread</c> and the like read as arrays). A single-part scanline file, version 2, no compression, one HALF
/// channel named <see cref="Channel"/> ("Y", luminance: every reader shows a one-channel file as grey), data and
/// display window the frame, increasing line order, pixel aspect 1, and a <c>comments</c> attribute naming the
/// program. The values are the store's halves as they are: field amplitudes in simulation units, negatives
/// included, intensity as accumulated; no palette, brightness, outline or boxes. Little-endian throughout, as the
/// format demands. Static and thread-free.
/// </summary>
public static class ExrWriter
{
	/// <summary>The channel every file holds.</summary>
	public const string Channel = "Y";

	private const int Magic = 20000630;
	private const int Version = 2;
	private const int PixelTypeHalf = 1;
	private const byte NoCompression = 0;
	private const byte IncreasingY = 0;

	/// <summary>Writes <paramref name="values"/> (row-major, at least width × height) as one file to <paramref name="stream"/>.</summary>
	/// <param name="stream">Takes the whole file in one write.</param>
	/// <param name="values">The plane's halves, row-major.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="comment">Goes into the file's <c>comments</c> attribute; null for the program name alone.</param>
	public static void Write(Stream stream, ReadOnlySpan<Half> values, int width, int height, string? comment = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
		if (values.Length < width * height)
		{
			throw new ArgumentException("Fewer values than width × height.", nameof(values));
		}
		byte[] header = Header(width, height, comment ?? "WaveDude Next");
		int rowBytes = width * 2;
		int chunkBytes = 8 + rowBytes;
		long firstChunk = 8L + header.Length + 8L * height;
		var file = new byte[firstChunk + (long)chunkBytes * height];
		Span<byte> span = file;
		BinaryPrimitives.WriteInt32LittleEndian(span, Magic);
		BinaryPrimitives.WriteInt32LittleEndian(span[4..], Version);
		header.CopyTo(span[8..]);
		int at = 8 + header.Length;
		for (int y = 0; y < height; y++)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(span[at..], (ulong)(firstChunk + (long)chunkBytes * y));
			at += 8;
		}
		for (int y = 0; y < height; y++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(span[at..], y);
			BinaryPrimitives.WriteInt32LittleEndian(span[(at + 4)..], rowBytes);
			Span<byte> row = span.Slice(at + 8, rowBytes);
			ReadOnlySpan<Half> source = values.Slice(y * width, width);
			if (BitConverter.IsLittleEndian)
			{
				MemoryMarshal.AsBytes(source).CopyTo(row);
			}
			else
			{
				for (int x = 0; x < width; x++)
				{
					BinaryPrimitives.WriteHalfLittleEndian(row[(x * 2)..], source[x]);
				}
			}
			at += chunkBytes;
		}
		stream.Write(file, 0, file.Length);
	}

	/// <summary>The header: the required attributes in the order the reference implementation writes them, then the comment, then the terminating null.</summary>
	private static byte[] Header(int width, int height, string comment)
	{
		using var header = new MemoryStream();
		// chlist: name, pixel type, pLinear, three reserved bytes, x and y sampling; the list ends with a null
		using (var channels = new MemoryStream())
		{
			channels.Write(Encoding.ASCII.GetBytes(Channel));
			channels.WriteByte(0);
			WriteInt(channels, PixelTypeHalf);
			channels.WriteByte(0);
			channels.Write(new byte[3]);
			WriteInt(channels, 1);
			WriteInt(channels, 1);
			channels.WriteByte(0);
			Attribute(header, "channels", "chlist", channels.ToArray());
		}
		Attribute(header, "compression", "compression", [NoCompression]);
		byte[] window = new byte[16];
		BinaryPrimitives.WriteInt32LittleEndian(window.AsSpan(8), width - 1);
		BinaryPrimitives.WriteInt32LittleEndian(window.AsSpan(12), height - 1);
		Attribute(header, "dataWindow", "box2i", window);
		Attribute(header, "displayWindow", "box2i", window);
		Attribute(header, "lineOrder", "lineOrder", [IncreasingY]);
		Attribute(header, "pixelAspectRatio", "float", Float(1f));
		Attribute(header, "screenWindowCenter", "v2f", new byte[8]);
		Attribute(header, "screenWindowWidth", "float", Float(1f));
		Attribute(header, "comments", "string", Encoding.UTF8.GetBytes(comment));
		header.WriteByte(0);
		return header.ToArray();
	}

	private static void Attribute(Stream stream, string name, string type, byte[] value)
	{
		stream.Write(Encoding.ASCII.GetBytes(name));
		stream.WriteByte(0);
		stream.Write(Encoding.ASCII.GetBytes(type));
		stream.WriteByte(0);
		WriteInt(stream, value.Length);
		stream.Write(value);
	}

	private static void WriteInt(Stream stream, int value)
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
		stream.Write(bytes);
	}

	private static byte[] Float(float value)
	{
		byte[] bytes = new byte[4];
		BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
		return bytes;
	}
}

/// <summary>
/// A sink of OpenEXR files, one per frame (<see cref="Sequence"/>: <c>name_0000.exr</c> and on in a folder, the frame
/// number at least four digits wide and as many as the planned frames need) or exactly one (<see cref="Still"/>).
/// Takes the store's halves (<see cref="WantsHalves"/>), never rendered pixels. Each file is written and closed in
/// <see cref="Add(ReadOnlySpan{Half})"/>; a frame of 1920 × 1080 is 4 MB, so no worker pool is needed. One thread at a time.
/// </summary>
public sealed class ExrSink : IFrameSink
{
	private readonly string? _stillPath;
	private readonly string _folder;
	private readonly string _baseName;
	private readonly int _digits;
	private bool _closed;

	private ExrSink(string? stillPath, string folder, string baseName, int width, int height, int plannedFrames)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
		_stillPath = stillPath;
		_folder = folder;
		_baseName = baseName;
		_digits = Math.Max(4, Math.Max(0, plannedFrames - 1).ToString(CultureInfo.InvariantCulture).Length);
		Width = width;
		Height = height;
		Directory.CreateDirectory(folder);
	}

	/// <summary>A sequence in <paramref name="folder"/> (created when missing) named <paramref name="baseName"/><c>_0000.exr</c> and on.</summary>
	public static ExrSink Sequence(string folder, string baseName, int width, int height, int plannedFrames) =>
		new(null, folder, baseName, width, height, plannedFrames);

	/// <summary>One file at <paramref name="path"/>; a second frame is refused.</summary>
	public static ExrSink Still(string path, int width, int height) =>
		new(path, Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", Path.GetFileNameWithoutExtension(path), width, height, 1);

	/// <inheritdoc/>
	public int Width { get; }

	/// <inheritdoc/>
	public int Height { get; }

	/// <inheritdoc/>
	public int FrameCount { get; private set; }

	/// <inheritdoc/>
	public string Output => _stillPath ?? _folder;

	/// <inheritdoc/>
	public bool WantsHalves => true;

	/// <summary>The file of frame <paramref name="index"/> of a sequence.</summary>
	public string PathOf(int index) => Path.Combine(_folder, _baseName + "_" + index.ToString("D" + _digits, CultureInfo.InvariantCulture) + ".exr");

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<int> pixels) => throw new NotSupportedException("An OpenEXR file holds the store's halves, not rendered pixels.");

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<Half> values)
	{
		if (_closed)
		{
			throw new InvalidOperationException("The sink is closed.");
		}
		if (_stillPath != null && FrameCount > 0)
		{
			throw new InvalidOperationException("A still takes one frame.");
		}
		string path = _stillPath ?? PathOf(FrameCount);
		using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
		{
			ExrWriter.Write(stream, values, Width, Height, "WaveDude Next, frame " + FrameCount.ToString(CultureInfo.InvariantCulture));
		}
		FrameCount++;
	}

	/// <inheritdoc/>
	public void Close() => _closed = true;

	/// <inheritdoc/>
	public void Dispose() => Close();
}
