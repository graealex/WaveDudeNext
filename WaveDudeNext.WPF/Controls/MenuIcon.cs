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
/// A menu entry's icon from Windows' icon font (2026-10-22, the user's request for the standard icons): attached as
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
