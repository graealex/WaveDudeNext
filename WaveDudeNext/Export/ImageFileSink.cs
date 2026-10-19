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

namespace WaveDudeNext.Export;

/// <summary>
/// A still: exactly one frame written to one file (PNG or JPEG through the encoder delegate, like
/// <see cref="ImageSequenceSink"/>). A second frame is refused. One thread at a time.
/// </summary>
public sealed class ImageFileSink : IFrameSink
{
	private readonly Action<Stream, ReadOnlyMemory<int>> _encode;
	private bool _closed;

	/// <param name="path">The file; replaced when it exists.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="encode">Writes the frame (packed BGRA32, width × height) to a stream.</param>
	public ImageFileSink(string path, int width, int height, Action<Stream, ReadOnlyMemory<int>> encode)
	{
		Output = path;
		Width = width;
		Height = height;
		_encode = encode;
	}

	/// <inheritdoc/>
	public int Width { get; }

	/// <inheritdoc/>
	public int Height { get; }

	/// <inheritdoc/>
	public int FrameCount { get; private set; }

	/// <inheritdoc/>
	public string Output { get; }

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<int> pixels)
	{
		if (_closed || FrameCount > 0)
		{
			throw new InvalidOperationException("A still takes one frame.");
		}
		if (pixels.Length < Width * Height)
		{
			throw new ArgumentException("The frame is smaller than the picture.", nameof(pixels));
		}
		int[] frame = pixels[..(Width * Height)].ToArray();
		using (var stream = new FileStream(Output, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16))
		{
			_encode(stream, frame);
		}
		FrameCount = 1;
	}

	/// <inheritdoc/>
	public void Close() => _closed = true;

	/// <inheritdoc/>
	public void Dispose() => Close();
}
