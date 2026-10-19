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

namespace WaveDudeNext.Model;

/// <summary>
/// An item of the project that sits on the canvas and moves as a whole: a source, an optical part (its centre, the
/// shape follows) or a probe (both ends; its graph box follows while attached to point A). The preview moves a
/// selection of several through this, by a drag of one of their handles or the arrow keys (2026-10-19, the user's
/// request). UI thread, like the rest of the model.
/// </summary>
public interface IMovable
{
	/// <summary>The canvas points the item is anchored at, in canvas pixels; a group move keeps every one of them on the canvas (<see cref="Movable.Limit"/>).</summary>
	IEnumerable<(int X, int Y)> Anchors { get; }

	/// <summary>Moves the item by <paramref name="dx"/>, <paramref name="dy"/> canvas pixels, without clamping.</summary>
	void MoveBy(int dx, int dy);
}

/// <summary>Moving several <see cref="IMovable"/>s as a group. Pure functions over the model; UI thread.</summary>
public static class Movable
{
	/// <summary>
	/// The part of a wanted move (<paramref name="dx"/>, <paramref name="dy"/>) that keeps every anchor of
	/// <paramref name="items"/> on a canvas of <paramref name="width"/> × <paramref name="height"/> pixels, so a group
	/// pushed against the edge stops as a whole and keeps its formation instead of piling up there (the user's choice,
	/// 2026-10-19). An anchor already outside the canvas can only move back in. Items without anchors do not limit the move.
	/// </summary>
	public static (int Dx, int Dy) Limit(IEnumerable<IMovable> items, int dx, int dy, int width, int height)
	{
		int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
		foreach (IMovable item in items)
		{
			foreach ((int x, int y) in item.Anchors)
			{
				minX = Math.Min(minX, x);
				minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}
		}
		if (minX == int.MaxValue)
		{
			return (dx, dy);
		}
		int right = Math.Max(0, width - 1);
		int bottom = Math.Max(0, height - 1);
		dx = Math.Clamp(dx, Math.Min(0, -minX), Math.Max(0, right - maxX));
		dy = Math.Clamp(dy, Math.Min(0, -minY), Math.Max(0, bottom - maxY));
		return (dx, dy);
	}

	/// <summary>Moves every item by the same delta; nothing happens for a zero delta.</summary>
	public static void MoveAll(IEnumerable<IMovable> items, int dx, int dy)
	{
		if (dx == 0 && dy == 0)
		{
			return;
		}
		foreach (IMovable item in items)
		{
			item.MoveBy(dx, dy);
		}
	}
}
