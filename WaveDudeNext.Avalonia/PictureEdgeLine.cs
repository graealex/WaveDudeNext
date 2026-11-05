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

// Contributors: Vinicius Miguel

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WaveDudeNext.CrossPlatform;

/// <summary>Draws the simulation boundary around the uniform-fit canvas, in device pixels. UI thread only.</summary>
internal sealed class PictureEdgeLine : Control
{
	/// <summary>Width of the canvas in cells.</summary>
	public static readonly StyledProperty<int> CanvasWidthProperty = AvaloniaProperty.Register<PictureEdgeLine, int>(nameof(CanvasWidth), 1);
	/// <summary>Height of the canvas in cells.</summary>
	public static readonly StyledProperty<int> CanvasHeightProperty = AvaloniaProperty.Register<PictureEdgeLine, int>(nameof(CanvasHeight), 1);
	/// <summary>Colour of the boundary.</summary>
	public static readonly StyledProperty<IBrush?> EdgeBrushProperty = AvaloniaProperty.Register<PictureEdgeLine, IBrush?>(nameof(EdgeBrush), Brushes.Transparent);

	static PictureEdgeLine() => AffectsRender<PictureEdgeLine>(CanvasWidthProperty, CanvasHeightProperty, EdgeBrushProperty);

	/// <summary>Width of the canvas in cells.</summary>
	public int CanvasWidth { get => GetValue(CanvasWidthProperty); set => SetValue(CanvasWidthProperty, value); }
	/// <summary>Height of the canvas in cells.</summary>
	public int CanvasHeight { get => GetValue(CanvasHeightProperty); set => SetValue(CanvasHeightProperty, value); }
	/// <summary>Colour of the boundary.</summary>
	public IBrush? EdgeBrush { get => GetValue(EdgeBrushProperty); set => SetValue(EdgeBrushProperty, value); }

	/// <inheritdoc/>
	public override void Render(DrawingContext context)
	{
		base.Render(context);
		if (CanvasWidth <= 0 || CanvasHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0 || EdgeBrush == null) return;
		double scale = Math.Min(Bounds.Width / CanvasWidth, Bounds.Height / CanvasHeight);
		double width = CanvasWidth * scale, height = CanvasHeight * scale;
		double thickness = 1.0 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
		if (width < thickness || height < thickness) return;
		var rect = new Rect((Bounds.Width - width + thickness) / 2, (Bounds.Height - height + thickness) / 2, width - thickness, height - thickness);
		context.DrawRectangle(null, new Pen(EdgeBrush, thickness), rect);
	}
}
