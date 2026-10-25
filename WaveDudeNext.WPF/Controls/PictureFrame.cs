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
using System.Windows.Media;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// Lays out a label and a picture so the label takes as little of the picture's room as possible:
/// above the picture when the available space is taller than the picture's aspect ratio (spare room
/// is above and below anyway), or rotated by 90° on its left when the space is wider (spare room is
/// at the sides). The frame takes all the room it is given (2026-10-25: the picture's cell sizes itself to the
/// picture since its edge line, and the label stays at the top of the cell, the picture centred in the rest). The
/// first child is the label, the second the picture (a <see cref="CanvasImage"/>, whose canvas
/// gives the aspect ratio, or an <see cref="Image"/> with <see cref="Stretch.Uniform"/>, whose <see cref="Image.Source"/>
/// gives it, 16:9 when there is none; or a panel holding one under an overlay). UI thread, like every element.
/// </summary>
public sealed class PictureFrame : Panel
{
	/// <summary>Identifies the <see cref="Gap"/> property.</summary>
	public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
		nameof(Gap), typeof(double), typeof(PictureFrame),
		new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

	private static readonly DependencyPropertyKey IsLabelOnSideKey = DependencyProperty.RegisterReadOnly(
		nameof(IsLabelOnSide), typeof(bool), typeof(PictureFrame), new PropertyMetadata(false));

	/// <summary>Identifies the <see cref="IsLabelOnSide"/> property.</summary>
	public static readonly DependencyProperty IsLabelOnSideProperty = IsLabelOnSideKey.DependencyProperty;

	private static readonly Transform SideTransform = Freeze(new RotateTransform(90));

	private const double DefaultAspect = 16.0 / 9.0;

	/// <summary>Space between the label and the picture, in pixels.</summary>
	public double Gap
	{
		get => (double)GetValue(GapProperty);
		set => SetValue(GapProperty, value);
	}

	/// <summary>Whether the label currently sits rotated on the left instead of above.</summary>
	public bool IsLabelOnSide
	{
		get => (bool)GetValue(IsLabelOnSideProperty);
		private set => SetValue(IsLabelOnSideKey, value);
	}

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
	{
		if (Children.Count < 2)
		{
			Size desired = default;
			foreach (UIElement child in Children)
			{
				child.Measure(availableSize);
				desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width), Math.Max(desired.Height, child.DesiredSize.Height));
			}
			return desired;
		}
		UIElement label = Children[0];
		UIElement picture = Children[1];
		bool side = !double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height) && availableSize.Height > 0
			&& availableSize.Width / availableSize.Height > AspectOf(picture);
		if (side != IsLabelOnSide)
		{
			IsLabelOnSide = side;
		}
		if (label is FrameworkElement element && !ReferenceEquals(element.LayoutTransform, side ? SideTransform : Transform.Identity))
		{
			element.LayoutTransform = side ? SideTransform : Transform.Identity;
		}
		label.Measure(availableSize);
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
			foreach (UIElement child in Children)
			{
				child.Arrange(new Rect(finalSize));
			}
			return finalSize;
		}
		UIElement label = Children[0];
		UIElement picture = Children[1];
		double gap = Gap;
		if (IsLabelOnSide)
		{
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

	/// <summary>
	/// Width over height of the picture: a <see cref="CanvasImage"/>'s canvas, else the source's, or 16:9 without one. The
	/// picture is an <see cref="Image"/>, or a panel whose first <see cref="Image"/> child is it (an overlay on top), either
	/// possibly inside a <see cref="Decorator"/> (the edge line's border).
	/// </summary>
	private static double AspectOf(UIElement picture)
	{
		UIElement inner = picture is Decorator { Child: { } child } ? child : picture;
		Image? image = inner as Image ?? (inner as Panel)?.Children.OfType<Image>().FirstOrDefault();
		return image switch
		{
			CanvasImage canvas => canvas.Aspect,
			{ Source: { Width: > 0, Height: > 0 } source } => source.Width / source.Height,
			_ => DefaultAspect,
		};
	}

	private static Transform Freeze(Transform transform)
	{
		transform.Freeze();
		return transform;
	}
}
