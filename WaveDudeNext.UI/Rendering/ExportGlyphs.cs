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

namespace WaveDudeNext.UI.Rendering;

/// <summary>
/// The drawings an export paints over its frames, as path markup both heads' painters read: WPF's
/// <c>Geometry.Parse</c> and SkiaSharp's <c>SKPath.ParseSvgPathData</c> take the same text, so the painters
/// (each head's <c>SourceMarkerPainter</c>) draw one shape (2026-11-11; each had carried the text itself). What the
/// windows draw is not here: those geometries are XAML resources, WaveDudeNext.WPF/Themes/Icons.xaml, which the
/// Avalonia head converts at build.
/// </summary>
public static class ExportGlyphs
{
	/// <summary>
	/// A source's marker: the preview's handle (<c>SourceGlyph</c> in Icons.xaml) moved by half a pixel onto whole
	/// coordinates, a ring of radius 6 and the four ticks of its crosshair in a 20 by 20 box about (10, 10), in
	/// pixels at the preview's size; a painter scales it about that centre and strokes it.
	/// </summary>
	public const string SourceMarker = "M4,10 A6,6 0 1 1 16,10 A6,6 0 1 1 4,10 M10,0 L10,2.5 M10,17.5 L10,20 M0,10 L2.5,10 M17.5,10 L20,10";
}
