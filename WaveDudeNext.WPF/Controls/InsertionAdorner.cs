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

using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// Drop feedback for reordering, one mark over the rows of a list for as long as the list lives: a horizontal line
/// between two rows, or an outline around a row's header when dropping appends to a folder. It adorns the element that
/// holds the rows and is told where its mark is, in that element's coordinates (<see cref="ShowLine"/>,
/// <see cref="ShowOutline"/>, <see cref="Hide"/>); it draws again only when the mark changes.
/// <para>
/// Two reports of the user's, 2026-11-10. The line had been drawn inside the row under the pointer, so the slot between
/// two rows stood 2 px higher over the lower half of the upper row than over the upper half of the lower: the line is
/// centred on the height it is given, the rows' common edge, so "after this row" and "before the next" are one mark.
/// And it flickered: an adorner was made for every drag event and taken away at every DragLeave, which WPF raises for
/// each element the pointer leaves inside the list, so the line was gone until the pointer's next move; one adorner
/// that stays and changes its mark is never out of the picture.
/// </para>
/// UI thread.
/// </summary>
public sealed class InsertionAdorner : Adorner
{
	private const double Thickness = 2;

	private Pen _pen;
	private Brush _brush;
	private (double Left, double Right, double Y)? _line;
	private Rect? _outline;

	/// <param name="adorned">The element holding the rows; the mark's coordinates are its.</param>
	/// <param name="brush">Colour of the line or outline.</param>
	public InsertionAdorner(UIElement adorned, Brush brush) : base(adorned)
	{
		_brush = brush;
		_pen = MakePen(brush);
		IsHitTestVisible = false;
	}

	/// <summary>Colour of the line or outline; a theme change gives another.</summary>
	public Brush Brush
	{
		get => _brush;
		set
		{
			if (!ReferenceEquals(_brush, value))
			{
				_brush = value;
				_pen = MakePen(value);
				InvalidateVisual();
			}
		}
	}

	/// <summary>Shows a line from <paramref name="left"/> to <paramref name="right"/>, centred on the height <paramref name="y"/>, in the adorned element's coordinates.</summary>
	public void ShowLine(double left, double right, double y) => Set((left, right, y), null);

	/// <summary>Shows an outline just inside <paramref name="bounds"/>, in the adorned element's coordinates.</summary>
	public void ShowOutline(Rect bounds) => Set(null, bounds);

	/// <summary>Shows nothing; the adorner stays where it is.</summary>
	public void Hide() => Set(null, null);

	private void Set((double Left, double Right, double Y)? line, Rect? outline)
	{
		if (line != _line || outline != _outline)
		{
			_line = line;
			_outline = outline;
			InvalidateVisual();
		}
	}

	private static Pen MakePen(Brush brush)
	{
		var pen = new Pen(brush, Thickness);
		pen.Freeze();
		return pen;
	}

	/// <inheritdoc/>
	protected override void OnRender(DrawingContext drawingContext)
	{
		if (_line is { } line)
		{
			// half the thickness to either side of the height, the two edges on whole device pixels at any scale
			drawingContext.PushGuidelineSet(new GuidelineSet(null, [line.Y - Thickness / 2, line.Y + Thickness / 2]));
			drawingContext.DrawLine(_pen, new Point(line.Left, line.Y), new Point(line.Right, line.Y));
			drawingContext.Pop();
		}
		else if (_outline is { } bounds)
		{
			drawingContext.DrawRectangle(null, _pen, new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2)));
		}
	}
}

/// <summary>Position of the drop feedback relative to a row.</summary>
public enum InsertionPlacement
{
	/// <summary>Line on the row's top edge: insert before this item.</summary>
	Before,
	/// <summary>Line on the row's bottom edge: insert after this item.</summary>
	After,
	/// <summary>Outline around the row's header: append to this folder.</summary>
	Into,
}
