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
/// One export output: a video file or an image sequence that takes rendered frames one after the other.
/// Frames are <see cref="Width"/> × <see cref="Height"/> packed BGRA32 pixels (<c>0xAARRGGBB</c> as an
/// <c>int</c>, blue in the lowest byte in memory), row-major, top row first; the alpha byte is ignored.
/// One thread at a time (the export worker); <see cref="Close"/> finishes the file and <see cref="IDisposable.Dispose"/>
/// does the same, so a failed export still leaves a readable file of the frames written so far where the
/// format allows it.
/// </summary>
public interface IFrameSink : IDisposable
{
	/// <summary>Width of every frame in pixels.</summary>
	int Width { get; }

	/// <summary>Height of every frame in pixels.</summary>
	int Height { get; }

	/// <summary>Frames taken so far.</summary>
	int FrameCount { get; }

	/// <summary>A description of the output for the log: the file, or the folder of a sequence.</summary>
	string Output { get; }

	/// <summary>Appends one frame of <see cref="Width"/> × <see cref="Height"/> pixels.</summary>
	/// <param name="pixels">The frame, packed BGRA32, row-major, at least width × height values.</param>
	/// <exception cref="InvalidOperationException">The sink is closed.</exception>
	/// <exception cref="IOException">The file could not be written.</exception>
	void Add(ReadOnlySpan<int> pixels);

	/// <summary>
	/// Whether the sink takes the store's halves through <see cref="Add(ReadOnlySpan{Half})"/> instead of rendered
	/// pixels: a data format such as OpenEXR (2026-10-18). The job then neither colours nor decorates its frames.
	/// </summary>
	bool WantsHalves => false;

	/// <summary>Appends one frame as the plane's halves, row-major, at least width × height values; only for a sink with <see cref="WantsHalves"/>.</summary>
	/// <param name="values">The plane as stored.</param>
	/// <exception cref="NotSupportedException">The sink takes rendered pixels.</exception>
	void Add(ReadOnlySpan<Half> values) => throw new NotSupportedException("This sink takes rendered pixels.");

	/// <summary>Finishes the output (indexes, trailers, open handles). Idempotent.</summary>
	void Close();
}
