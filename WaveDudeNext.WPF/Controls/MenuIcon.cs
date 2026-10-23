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
/// A menu entry's icon, an <see cref="IconView"/> made from an attached property: <c>controls:MenuIcon.Glyph="&amp;#xE74E;"</c>
/// for a glyph of Windows' icon font (2026-10-22, the user's request for the standard icons; Segoe Fluent Icons on Windows 11,
/// Segoe MDL2 Assets on Windows 10, in the theme's <c>IconGlyphText</c> style), <c>controls:MenuIcon.Drawing="Lens"</c> for one
/// of the app's own drawings (2026-10-23, for the entries the font has no glyph for). UI thread.
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

	/// <summary>Identifies the attached Drawing property: the name of one of the app's 16 px drawings (<see cref="IconView.Drawing"/>), or null for none.</summary>
	public static readonly DependencyProperty DrawingProperty = DependencyProperty.RegisterAttached(
		"Drawing", typeof(string), typeof(MenuIcon), new PropertyMetadata(null, OnDrawingChanged));

	/// <summary>Gets the drawing of <paramref name="item"/>.</summary>
	public static string? GetDrawing(MenuItem item) => (string?)item.GetValue(DrawingProperty);

	/// <summary>Sets the drawing of <paramref name="item"/>, which becomes its icon.</summary>
	public static void SetDrawing(MenuItem item, string? name) => item.SetValue(DrawingProperty, name);

	private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is MenuItem item)
		{
			item.Icon = e.NewValue is string glyph ? new IconView { Glyph = glyph } : null;
		}
	}

	private static void OnDrawingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is MenuItem item)
		{
			item.Icon = e.NewValue is string name ? new IconView { Drawing = name } : null;
		}
	}
}
