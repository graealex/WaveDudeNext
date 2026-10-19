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

namespace WaveDudeNext.Runs;

/// <summary>
/// A decoded scene image as 32-bit BGRA pixels, the UI-independent input of <see cref="RunSpec"/>: the
/// application decodes the file (WPF with <c>BitmapDecoder</c>) and hands the bytes over; the run turns them
/// into <see cref="Engine.SceneData"/> on its worker. Immutable; the array must not be modified after
/// construction.
/// </summary>
/// <param name="Width">Image width in pixels (= canvas width).</param>
/// <param name="Height">Image height in pixels (= canvas height).</param>
/// <param name="StrideBytes">Bytes per row, at least 4 × <paramref name="Width"/>.</param>
/// <param name="Bgra">Pixel bytes, B, G, R, A per pixel, <paramref name="Height"/> rows of <paramref name="StrideBytes"/>.</param>
public sealed record ScenePixels(int Width, int Height, int StrideBytes, byte[] Bgra);
