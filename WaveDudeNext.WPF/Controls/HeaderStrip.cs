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
/// A picture's header strip (2026-10-20): the title at the start, the tools (the brightness box and its Auto button)
/// at the far end, and a caption just before the tools that is left out when the three do not fit, so the title
/// stays whole on a small picture (a stacked picture turned the strip on its side and cut "Intensity" to "Inter").
/// Children in that order: title, caption, tools; each is centred across the strip. When even the title and the
/// tools do not fit, the title gets what the tools leave. UI thread, like every element.
/// </summary>
public sealed class HeaderStrip : Panel
{
	/// <summary>Identifies the <see cref="Spacing"/> property.</summary>
	public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
		nameof(Spacing), typeof(double), typeof(HeaderStrip),
		new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

	private bool _captionShown = true;

	/// <summary>Least space between the title and what follows it, in pixels.</summary>
	public double Spacing
	{
		get => (double)GetValue(SpacingProperty);
		set => SetValue(SpacingProperty, value);
	}

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
	{
		var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
		double width = 0.0;
		double height = 0.0;
		foreach (UIElement child in InternalChildren)
		{
			child.Measure(unbounded);
			width += child.DesiredSize.Width;
			height = Math.Max(height, child.DesiredSize.Height);
		}
		if (InternalChildren.Count < 3)
		{
			return new Size(Math.Min(width, availableSize.Width), height);
		}
		UIElement title = InternalChildren[0];
		UIElement caption = InternalChildren[1];
		UIElement tools = InternalChildren[2];
		double spacing = Spacing;
		double full = title.DesiredSize.Width + spacing + caption.DesiredSize.Width + tools.DesiredSize.Width;
		_captionShown = full <= availableSize.Width;
		double needed = _captionShown ? full : title.DesiredSize.Width + spacing + tools.DesiredSize.Width;
		if (needed > availableSize.Width)
		{
			// the title takes what the tools leave, trimmed by its own TextTrimming
			title.Measure(new Size(Math.Max(0.0, availableSize.Width - spacing - tools.DesiredSize.Width), availableSize.Height));
			needed = availableSize.Width;
		}
		return new Size(needed, height);
	}

	/// <inheritdoc/>
	protected override Size ArrangeOverride(Size finalSize)
	{
		if (InternalChildren.Count < 3)
		{
			double x = 0.0;
			foreach (UIElement child in InternalChildren)
			{
				child.Arrange(Centred(x, child, finalSize.Height));
				x += child.DesiredSize.Width;
			}
			return finalSize;
		}
		UIElement title = InternalChildren[0];
		UIElement caption = InternalChildren[1];
		UIElement tools = InternalChildren[2];
		double toolsX = Math.Max(0.0, finalSize.Width - tools.DesiredSize.Width);
		tools.Arrange(Centred(toolsX, tools, finalSize.Height));
		double captionX = toolsX - caption.DesiredSize.Width;
		caption.Arrange(_captionShown ? Centred(captionX, caption, finalSize.Height) : new Rect(toolsX, 0.0, 0.0, 0.0));
		double titleWidth = Math.Min(title.DesiredSize.Width, Math.Max(0.0, (_captionShown ? captionX : toolsX) - Spacing));
		title.Arrange(new Rect(0.0, (finalSize.Height - title.DesiredSize.Height) / 2.0, titleWidth, title.DesiredSize.Height));
		return finalSize;
	}

	/// <summary>The child's rectangle at <paramref name="x"/>, at its desired size, centred across the strip.</summary>
	private static Rect Centred(double x, UIElement child, double height) =>
		new(x, (height - child.DesiredSize.Height) / 2.0, child.DesiredSize.Width, child.DesiredSize.Height);
}
