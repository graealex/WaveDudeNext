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
/// A menu entry's icon: a glyph of Windows' icon font (2026-10-22, the user's request for the standard icons), or one of
/// the app's own drawings (<see cref="DrawingProperty"/>). The glyph is attached as
/// <c>controls:MenuIcon.Glyph="&amp;#xE74E;"</c>, it gives the <see cref="MenuItem"/> a <see cref="TextBlock"/> of that
/// character in the theme's <c>MenuIconText</c> style (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10,
/// in the entry's text colour). UI thread.
/// </summary>
public static class MenuIcon
{
	/// <summary>Identifies the attached Glyph property: the icon font's character for the entry, or null for none.</summary>
	public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
		"Glyph", typeof(string), typeof(MenuIcon), new PropertyMetadata(null, OnGlyphChanged));

	/// <summary>Gets the glyph of <paramref name="item"/>.</summary>
	public static string? GetGlyph(MenuItem item) => (string?)item.GetValue(GlyphProperty);

	/// <summary>Sets the glyph of <paramref name="item"/>, which becomes its icon.</summary>
	public static void SetGlyph(MenuItem item, string? glyph) => item.SetValue(GlyphProperty, glyph);

	/// <summary>
	/// Identifies the attached Drawing property: the name of one of the app's own 16 px icon drawings (2026-10-23), for
	/// entries the icon font has no glyph for. The theme holds it as a stroked geometry <c>Icon{name}</c> and, where part of
	/// it is solid, a filled one <c>Icon{name}Fill</c>; both are drawn in the entry's text colour, as vectors.
	/// </summary>
	public static readonly DependencyProperty DrawingProperty = DependencyProperty.RegisterAttached(
		"Drawing", typeof(string), typeof(MenuIcon), new PropertyMetadata(null, OnDrawingChanged));

	/// <summary>Gets the drawing of <paramref name="item"/>.</summary>
	public static string? GetDrawing(MenuItem item) => (string?)item.GetValue(DrawingProperty);

	/// <summary>Sets the drawing of <paramref name="item"/>, which becomes its icon.</summary>
	public static void SetDrawing(MenuItem item, string? name) => item.SetValue(DrawingProperty, name);

	private static void OnDrawingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not MenuItem item)
		{
			return;
		}
		if (e.NewValue is not string { Length: > 0 } name)
		{
			item.Icon = null;
			return;
		}
		// the entry's text colour, which greys out when it is disabled, like a font glyph's: the inherited text foreground
		// each path holds itself (an ancestor lookup ran before the icon was in its menu and never found one)
		var colour = new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self };
		var stroke = new Path { StrokeThickness = 1.0 };
		stroke.SetResourceReference(Path.DataProperty, "Icon" + name);
		stroke.SetBinding(Shape.StrokeProperty, colour);
		var fill = new Path();
		fill.SetResourceReference(Path.DataProperty, "Icon" + name + "Fill");
		fill.SetBinding(Shape.FillProperty, colour);
		item.Icon = new Grid { Width = 16, Height = 16, Children = { stroke, fill } };
	}

	private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not MenuItem item)
		{
			return;
		}
		if (e.NewValue is not string { Length: > 0 } glyph)
		{
			item.Icon = null;
			return;
		}
		var text = new TextBlock { Text = glyph };
		// resolved once the entry is in its menu, so a context menu built in code finds the theme's style too
		text.SetResourceReference(FrameworkElement.StyleProperty, "MenuIconText");
		item.Icon = text;
	}
}
