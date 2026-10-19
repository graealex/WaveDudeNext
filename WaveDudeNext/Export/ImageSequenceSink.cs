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

using System.Collections.Concurrent;

namespace WaveDudeNext.Export;

/// <summary>
/// An image sequence: one file per frame, <c>&lt;name&gt;_0000.&lt;ext&gt;</c> and on, in a folder of its own. The
/// frame number has a fixed width for the whole sequence, at least four digits and as many as the planned frame
/// count needs, so the names sort in frame order past 9999 frames and a <c>%05d</c> pattern takes them all
/// (2026-10-15).
/// The encoder is given as a factory, so the same sink serves PNG (<see cref="PngEncoder"/>) and JPEG
/// (<see cref="JpegEncoder"/> or the application's alternative). Frames are independent files, so with
/// <see cref="Workers"/> above one they are encoded on that many threads at once, each with its own encoder
/// from the factory (2026-10-15: WIC and zlib are single-threaded per picture); <see cref="Add"/> then only
/// queues the frame and <see cref="Close"/> waits for the last file. A worker's error surfaces from the next
/// <see cref="Add"/> or from <see cref="Close"/>. The folder is created; files of the same names are
/// replaced. One caller thread at a time.
/// </summary>
public sealed class ImageSequenceSink : IFrameSink
{
	/// <summary>Fewest digits of the frame number in the file names.</summary>
	public const int MinDigits = 4;

	private readonly Func<Action<Stream, ReadOnlyMemory<int>>> _makeEncoder;
	private readonly Action<Stream, ReadOnlyMemory<int>>? _encoder;
	private readonly BlockingCollection<(int Index, int[] Frame)>? _queue;
	private readonly ConcurrentBag<int[]> _pool = [];
	private readonly Thread[] _threads = [];
	private Exception? _error;
	private bool _closed;

	/// <param name="folder">Where the files go; created when missing.</param>
	/// <param name="baseName">The name before the underscore and number.</param>
	/// <param name="extension">The file extension with its dot.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="makeEncoder">Makes an encoder that writes one frame (packed BGRA32, width × height) to a stream; called once per worker.</param>
	/// <param name="workers">Frames encoded at once; 1 encodes on the caller's thread.</param>
	/// <param name="plannedFrames">How many frames will come at most; with <paramref name="firstFrame"/> it decides the width of the frame numbers (<see cref="Digits"/>).</param>
	/// <param name="firstFrame">The number of the first file: the timeline frame number of the first frame added, so a range export is numbered as the timeline is (2026-10-19, the user's rule: the file number is the frame number, never a step or a count from zero).</param>
	public ImageSequenceSink(string folder, string baseName, string extension, int width, int height, Func<Action<Stream, ReadOnlyMemory<int>>> makeEncoder, int workers = 1, int plannedFrames = 0, int firstFrame = 0)
	{
		Folder = folder;
		BaseName = baseName;
		Extension = extension;
		Width = width;
		Height = height;
		Workers = Math.Max(1, workers);
		FirstFrame = Math.Max(0, firstFrame);
		Digits = Math.Max(MinDigits, Math.Max(0, FirstFrame + plannedFrames - 1).ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
		_makeEncoder = makeEncoder;
		Directory.CreateDirectory(folder);
		if (Workers == 1)
		{
			_encoder = makeEncoder();
			return;
		}
		_queue = new BlockingCollection<(int, int[])>(Workers * 2);
		_threads = new Thread[Workers];
		for (int i = 0; i < Workers; i++)
		{
			_threads[i] = new Thread(Work) { Name = "Image sequence " + i, IsBackground = true };
			_threads[i].Start();
		}
	}

	/// <summary>The sequence's folder.</summary>
	public string Folder { get; }

	/// <summary>The name before the frame number.</summary>
	public string BaseName { get; }

	/// <summary>The extension with its dot.</summary>
	public string Extension { get; }

	/// <inheritdoc/>
	public int Width { get; }

	/// <inheritdoc/>
	public int Height { get; }

	/// <summary>Frames encoded at once.</summary>
	public int Workers { get; }

	/// <summary>Digits of the frame number in every file name of this sequence: <see cref="MinDigits"/>, or more when the last planned frame number needs them.</summary>
	public int Digits { get; }

	/// <summary>The frame number of the first file; the files count on from it.</summary>
	public int FirstFrame { get; }

	/// <inheritdoc/>
	public int FrameCount { get; private set; }

	/// <inheritdoc/>
	public string Output => Folder;

	/// <summary>The path of frame <paramref name="index"/>.</summary>
	public string PathOf(int index) => Path.Combine(Folder, BaseName + "_" + (FirstFrame + index).ToString("D" + Digits, System.Globalization.CultureInfo.InvariantCulture) + Extension);

	/// <inheritdoc/>
	public void Add(ReadOnlySpan<int> pixels)
	{
		if (_closed)
		{
			throw new InvalidOperationException("The sequence is already closed.");
		}
		if (pixels.Length < Width * Height)
		{
			throw new ArgumentException("The frame is smaller than the sequence.", nameof(pixels));
		}
		Rethrow();
		// the encoders read the frame from other threads, so it is copied into a buffer of the sink's own (pooled:
		// a fresh eight-megabyte array per frame kept the garbage collector busier than the encoders, 2026-10-15)
		if (!_pool.TryTake(out int[]? frame))
		{
			frame = new int[Width * Height];
		}
		pixels[..(Width * Height)].CopyTo(frame);
		int index = FrameCount++;
		if (_queue == null)
		{
			Encode(_encoder!, index, frame);
			_pool.Add(frame);
		}
		else
		{
			_queue.Add((index, frame));
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
		if (_queue != null)
		{
			_queue.CompleteAdding();
			foreach (Thread thread in _threads)
			{
				thread.Join();
			}
		}
		Rethrow();
	}

	/// <inheritdoc/>
	public void Dispose() => Close();

	private void Work()
	{
		try
		{
			Action<Stream, ReadOnlyMemory<int>> encoder = _makeEncoder();
			foreach ((int index, int[] frame) in _queue!.GetConsumingEnumerable())
			{
				Encode(encoder, index, frame);
				_pool.Add(frame);
			}
		}
		catch (Exception ex)
		{
			Interlocked.CompareExchange(ref _error, ex, null);
			// let the others finish; nothing more is taken by this worker
		}
	}

	private void Encode(Action<Stream, ReadOnlyMemory<int>> encoder, int index, int[] frame)
	{
		using var stream = new FileStream(PathOf(index), FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		encoder(stream, frame);
	}

	private void Rethrow()
	{
		if (Volatile.Read(ref _error) is { } error)
		{
			throw new IOException("Writing the image sequence failed: " + error.Message, error);
		}
	}
}
