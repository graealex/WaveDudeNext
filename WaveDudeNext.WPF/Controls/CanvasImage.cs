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
using System.Windows.Controls;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// A preview picture that takes its size from the canvas, never from its bitmap (2026-10-23): the canvas, in DIPs,
/// fitted uniformly into the room it is given (the canvas size itself without a bound), and the bitmap drawn into that
/// rectangle, whatever its own pixel size. So a new bitmap never changes the layout: a 16-bit picture rendered at the
/// rectangle's size in screen pixels lands on them 1:1, a picture of palette indices at the store's size is scaled, and
/// no loop runs from the layout to the bitmap's size and back (one had been possible while the layout read the bitmap's
/// size in DIPs, which a resolution kept in single precision put a hair off the canvas). UI thread, like every element.
/// </summary>
public sealed class CanvasImage : Image
{
	/// <summary>Identifies the <see cref="CanvasWidth"/> property.</summary>
	public static readonly DependencyProperty CanvasWidthProperty = DependencyProperty.Register(
		nameof(CanvasWidth), typeof(int), typeof(CanvasImage), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

	/// <summary>Identifies the <see cref="CanvasHeight"/> property.</summary>
	public static readonly DependencyProperty CanvasHeightProperty = DependencyProperty.Register(
		nameof(CanvasHeight), typeof(int), typeof(CanvasImage), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

	/// <summary>Width of the canvas the picture shows, in canvas pixels (one DIP each at its natural size); at least 1.</summary>
	public int CanvasWidth
	{
		get => (int)GetValue(CanvasWidthProperty);
		set => SetValue(CanvasWidthProperty, value);
	}

	/// <summary>Height of the canvas the picture shows, in canvas pixels; at least 1.</summary>
	public int CanvasHeight
	{
		get => (int)GetValue(CanvasHeightProperty);
		set => SetValue(CanvasHeightProperty, value);
	}

	/// <summary>Width over height of the canvas, the picture's aspect ratio.</summary>
	public double Aspect => Math.Max(1, CanvasWidth) / (double)Math.Max(1, CanvasHeight);

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size constraint) => Fit(constraint);

	/// <inheritdoc/>
	protected override Size ArrangeOverride(Size arrangeSize) => Fit(arrangeSize);

	/// <summary>The canvas fitted uniformly into <paramref name="room"/>; its own size along an unbounded side, or with no bound at all.</summary>
	private Size Fit(Size room)
	{
		double width = Math.Max(1, CanvasWidth);
		double height = Math.Max(1, CanvasHeight);
		bool freeWidth = double.IsInfinity(room.Width);
		bool freeHeight = double.IsInfinity(room.Height);
		if (freeWidth && freeHeight)
		{
			return new Size(width, height);
		}
		double scale = freeWidth ? room.Height / height : freeHeight ? room.Width / width : Math.Min(room.Width / width, room.Height / height);
		return new Size(width * scale, height * scale);
	}
}
