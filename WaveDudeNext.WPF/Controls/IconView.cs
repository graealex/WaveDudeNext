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
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Shapes;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// A 16 px icon (2026-10-23): a <see cref="Glyph"/> of Windows' icon font, or one of the app's own <see cref="Drawing"/>s,
/// in its inherited text colour, so it greys out with the text beside it. Used in the project tree's rows and, through
/// <see cref="Create"/>, by <see cref="MenuIcon"/>. Not focusable. UI thread.
/// </summary>
public sealed class IconView : ContentControl
{
	/// <summary>Identifies the <see cref="Glyph"/> property.</summary>
	public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
		nameof(Glyph), typeof(string), typeof(IconView), new PropertyMetadata(null, OnIconChanged));

	/// <summary>Identifies the <see cref="Drawing"/> property.</summary>
	public static readonly DependencyProperty DrawingProperty = DependencyProperty.Register(
		nameof(Drawing), typeof(string), typeof(IconView), new PropertyMetadata(null, OnIconChanged));

	/// <summary>Identifies the <see cref="PlusBadge"/> property.</summary>
	public static readonly DependencyProperty PlusBadgeProperty = DependencyProperty.Register(
		nameof(PlusBadge), typeof(bool), typeof(IconView), new PropertyMetadata(false, OnIconChanged));

	public IconView()
	{
		Focusable = false;
		IsTabStop = false;
		Width = 16;
		Height = 16;
	}

	/// <summary>The icon font's character (Segoe Fluent Icons, Segoe MDL2 Assets on Windows 10), or null.</summary>
	public string? Glyph
	{
		get => (string?)GetValue(GlyphProperty);
		set => SetValue(GlyphProperty, value);
	}

	/// <summary>
	/// The name of one of the app's drawings: the theme's stroked geometry <c>Icon{name}</c> and, where part of it is solid,
	/// the filled <c>Icon{name}Fill</c>, both on a 16 px grid; or null.
	/// </summary>
	public string? Drawing
	{
		get => (string?)GetValue(DrawingProperty);
		set => SetValue(DrawingProperty, value);
	}

	/// <summary>
	/// Whether the <see cref="Drawing"/> carries the plus of an add command (2026-10-23, the tree's toolbar): the theme's
	/// <c>IconPlusBadge</c> in its bottom-right corner, which the drawings leave free, styled by <c>IconPlusBadgeStyle</c>
	/// (the accent colour, the text colour on a pressed or disabled button).
	/// </summary>
	public bool PlusBadge
	{
		get => (bool)GetValue(PlusBadgeProperty);
		set => SetValue(PlusBadgeProperty, value);
	}

	private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var view = (IconView)d;
		UIElement? icon = Create(view.Glyph, view.Drawing);
		if (view.PlusBadge && icon is Grid drawn)
		{
			var badge = new Path();
			badge.SetResourceReference(FrameworkElement.StyleProperty, "IconPlusBadgeStyle");
			drawn.Children.Add(badge);
		}
		view.Content = icon;
	}

	/// <summary>The element showing <paramref name="glyph"/>, else <paramref name="drawing"/>; null for neither.</summary>
	public static UIElement? Create(string? glyph, string? drawing)
	{
		if (glyph is { Length: > 0 })
		{
			var text = new TextBlock { Text = glyph };
			// resolved once the element is in place, so a menu built in code finds the theme's style too
			text.SetResourceReference(FrameworkElement.StyleProperty, "MenuIconText");
			return text;
		}
		if (drawing is not { Length: > 0 })
		{
			return null;
		}
		// the inherited text colour each path holds itself (an ancestor lookup ran before the icon was in place and never
		// found one, 2026-10-23)
		var colour = new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self };
		var stroke = new Path { StrokeThickness = 1.0 };
		stroke.SetResourceReference(Path.DataProperty, "Icon" + drawing);
		stroke.SetBinding(Shape.StrokeProperty, colour);
		var fill = new Path();
		fill.SetResourceReference(Path.DataProperty, "Icon" + drawing + "Fill");
		fill.SetBinding(Shape.FillProperty, colour);
		return new Grid { Width = 16, Height = 16, Children = { stroke, fill } };
	}
}
