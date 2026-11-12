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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// Lays out a label and a picture so the label takes as little of the picture's room as possible: above the picture
/// when the available space is taller than the picture's aspect ratio (spare room is above and below anyway), or turned
/// by 90° on its left when the space is wider (spare room is at the sides). The frame takes all the room it is given:
/// the label stays at the top (or the left) of the cell, the picture centred in the rest. The first child is the label,
/// the second the picture (a <see cref="CanvasImage"/>, whose canvas gives the aspect ratio, or an <see cref="Image"/>
/// whose source gives it, 16:9 when there is none; or a panel holding one under an overlay, possibly inside the edge
/// line's border). The label is a <see cref="LayoutTransformControl"/> holding the strip, whose
/// <see cref="LayoutTransformControl.LayoutTransform"/> the frame sets to the 90° turn and takes away again: a layout
/// transform, as the WPF frame's, so the label's margin counts in the frame's space and its content stays on whole pixels
/// (2026-11-11; a render transform about the centre had put the strip's borders on half pixels). UI thread.
/// </summary>
public sealed class PictureFrame : Panel
{
	/// <summary>Identifies the <see cref="Gap"/> property.</summary>
	public static readonly StyledProperty<double> GapProperty =
		AvaloniaProperty.Register<PictureFrame, double>(nameof(Gap), 4.0);

	/// <summary>Identifies the <see cref="IsLabelOnSide"/> property.</summary>
	public static readonly DirectProperty<PictureFrame, bool> IsLabelOnSideProperty =
		AvaloniaProperty.RegisterDirect<PictureFrame, bool>(nameof(IsLabelOnSide), frame => frame.IsLabelOnSide);

	private static readonly RotateTransform SideTransform = new(90);

	private const double DefaultAspect = 16.0 / 9.0;

	/// <summary>
	/// The weight of the turned text (2026-11-11, a test for the user): hinted in grey under the turn, each stem is one row
	/// of full intensity with nothing beside, where WPF's turned ClearType gives it a light shoulder either side (80 241 80
	/// along the text); a halo of the text's own colour, blurred by a pixel, puts the shoulders there. Shared by every
	/// text of the strip, its colour the inherited foreground at the last measure.
	/// </summary>
	private readonly DropShadowEffect _halo = new() { BlurRadius = 0.0, OffsetX = 1.0, OffsetY = 0.0, Opacity = 0.24 };

	private bool _isLabelOnSide;

	static PictureFrame()
	{
		AffectsMeasure<PictureFrame>(GapProperty);
	}

	/// <summary>Space between the label and the picture, in pixels.</summary>
	public double Gap
	{
		get => GetValue(GapProperty);
		set => SetValue(GapProperty, value);
	}

	/// <summary>Whether the label currently sits turned on the left instead of above.</summary>
	public bool IsLabelOnSide
	{
		get => _isLabelOnSide;
		private set => SetAndRaise(IsLabelOnSideProperty, ref _isLabelOnSide, value);
	}

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
	{
		if (Children.Count < 2)
		{
			Size desired = default;
			foreach (Control child in Children)
			{
				child.Measure(availableSize);
				desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width), Math.Max(desired.Height, child.DesiredSize.Height));
			}
			return desired;
		}
		Control label = Children[0];
		Control picture = Children[1];
		bool side = !double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height) && availableSize.Height > 0
			&& availableSize.Width / availableSize.Height > AspectOf(picture);
		if (side != IsLabelOnSide)
		{
			IsLabelOnSide = side;
		}
		// the turn is a layout transform on the label's host (WPF's LayoutTransform; Avalonia's LayoutTransformControl does
		// the same: its desired size is the turned one, its child laid out in its own space and drawn turned about the
		// corner, so the margin counts in the frame's space and the content stays on whole pixels; 2026-11-11, the look
		// brought to the WPF head's: a render transform about the centre had put the strip's borders on half pixels and its
		// margin on the wrong sides)
		if (label is LayoutTransformControl host && !ReferenceEquals(host.LayoutTransform, side ? SideTransform : null))
		{
			host.LayoutTransform = side ? SideTransform : null;
			// turned text is antialiased in grey (2026-11-11, the user's picture of the stacked strip's "Brightness"): under
			// the turn Skia's DirectWrite backend renders LCD text without grid fitting, heavy and strongly coloured at every
			// hinting level, where in grey it snaps every stem to a row, as WPF's turned ClearType does. The mode counts only
			// because the window leaves its own unspecified (Theming/TextRendering): the Skia backend's draw context merges a
			// visual's options into the current ones by filling what is unspecified, so a mode set on the window had stood
			// over this one (measured so, four ways, before the window's was taken away). Above the picture the mode goes
			// back to unspecified, the window's subpixel default
			TextOptions.SetTextRenderingMode(host, side ? TextRenderingMode.Antialias : TextRenderingMode.Unspecified);
			// the semibold face was tried for the weight and refused (2026-11-11): hinted under the turn it lost the stem of
			// the g into its bowl, where the regular face keeps every stroke
		}
		label.Measure(availableSize);
		if (label is LayoutTransformControl measured)
		{
			ApplyHalo(measured, side);
		}
		double gap = Gap;
		Size rest = side
			? new Size(Math.Max(0, availableSize.Width - label.DesiredSize.Width - gap), availableSize.Height)
			: new Size(availableSize.Width, Math.Max(0, availableSize.Height - label.DesiredSize.Height - gap));
		picture.Measure(rest);
		Size content = side
			? new Size(label.DesiredSize.Width + gap + picture.DesiredSize.Width, Math.Max(label.DesiredSize.Height, picture.DesiredSize.Height))
			: new Size(Math.Max(label.DesiredSize.Width, picture.DesiredSize.Width), label.DesiredSize.Height + gap + picture.DesiredSize.Height);
		return new Size(double.IsInfinity(availableSize.Width) ? content.Width : Math.Max(content.Width, availableSize.Width),
			double.IsInfinity(availableSize.Height) ? content.Height : Math.Max(content.Height, availableSize.Height));
	}

	/// <inheritdoc/>
	protected override Size ArrangeOverride(Size finalSize)
	{
		if (Children.Count < 2)
		{
			foreach (Control child in Children)
			{
				child.Arrange(new Rect(finalSize));
			}
			return finalSize;
		}
		Control label = Children[0];
		Control picture = Children[1];
		double gap = Gap;
		if (IsLabelOnSide)
		{
			// the turned label is a column down the left (its desired width is its thickness, margins included), the picture takes the rest
			double w = Math.Min(label.DesiredSize.Width, finalSize.Width);
			label.Arrange(new Rect(0, 0, w, finalSize.Height));
			picture.Arrange(new Rect(w + gap, 0, Math.Max(0, finalSize.Width - w - gap), finalSize.Height));
		}
		else
		{
			double h = Math.Min(label.DesiredSize.Height, finalSize.Height);
			label.Arrange(new Rect(0, 0, finalSize.Width, h));
			picture.Arrange(new Rect(0, h + gap, finalSize.Width, Math.Max(0, finalSize.Height - h - gap)));
		}
		return finalSize;
	}

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		// the halo keeps the text's colour through a theme change, which changes the inherited foreground without a measure
		if (change.Property == TextElement.ForegroundProperty && change.GetNewValue<IBrush?>() is ISolidColorBrush brush)
		{
			_halo.Color = brush.Color;
		}
	}

	/// <summary>
	/// Puts the halo on every text of the turned strip (the title, the caption, the tools' label, the number box's text and
	/// the button's), or takes it off every one when the strip lies above the picture; after the strip's measure, which
	/// applies the templates the texts live in. The colour is the inherited foreground's.
	/// </summary>
	private void ApplyHalo(Control host, bool on)
	{
		if (on && TextElement.GetForeground(host) is ISolidColorBrush brush)
		{
			_halo.Color = brush.Color;
		}
		foreach (Visual visual in host.GetVisualDescendants())
		{
			if (visual is TextBlock or TextPresenter)
			{
				visual.Effect = on ? _halo : null;
			}
		}
	}

	/// <summary>
	/// Width over height of the picture: a <see cref="CanvasImage"/>'s canvas, else the source's, or 16:9 without one. The
	/// picture is an <see cref="Image"/>, or a panel whose first <see cref="Image"/> child is it (an overlay on top), either
	/// possibly inside a <see cref="Decorator"/> (the edge line's border).
	/// </summary>
	private static double AspectOf(Control picture)
	{
		Control inner = picture is Decorator { Child: { } child } ? child : picture;
		Image? image = inner as Image ?? (inner as Panel)?.Children.OfType<Image>().FirstOrDefault();
		return image switch
		{
			CanvasImage canvas => canvas.Aspect,
			{ Source: { Size: { Width: > 0, Height: > 0 } size } } => size.Width / size.Height,
			_ => DefaultAspect,
		};
	}
}
