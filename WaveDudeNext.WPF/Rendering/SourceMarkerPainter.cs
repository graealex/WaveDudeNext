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

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveDudeNext.Export;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF.Rendering;

/// <summary>
/// Draws the enabled sources into exported frames (2026-10-21, <see cref="Display.SourceMarkersInExport"/>) as the
/// preview shows them: the source marker (a ring of radius 6 with four ticks of its crosshair and a dot in the middle,
/// white over a dark halo, on a faint dark disc), a composite source's emitter dots (radius 2.5 with a dark rim), and
/// the name in Segoe UI outlined in dark, beside a point source and below a composite one (whose rotation handle sits
/// beside it in the preview). Everything is scaled from the preview's sizes by the ratio of the probe labels'
/// size at the frame's height to the preview's 11 px labels (<see cref="ProbeMarkers.FontSize"/>), so sources and
/// probe numbers read alike. The sources do not move during a run, so the markers are rendered once, on the export's
/// own thread at the first frame, into a premultiplied layer of the frame's size, whose covered pixels are then
/// blended over each frame. Built on the UI thread from the project (positions and names are copied, so edits during
/// the export change nothing).
/// </summary>
public sealed class SourceMarkerPainter
{
	/// <summary>The preview's label size in pixels, the reference of the scale.</summary>
	private const double PreviewLabelSize = 11.0;

	private static readonly Typeface LabelTypeface = new("Segoe UI");
	private static readonly Geometry Glyph = Frozen(Geometry.Parse("M4,10 A6,6 0 1 1 16,10 A6,6 0 1 1 4,10 M10,0 L10,2.5 M10,17.5 L10,20 M0,10 L2.5,10 M17.5,10 L20,10"));
	private static readonly Brush Halo = Frozen(new SolidColorBrush(Color.FromArgb(191, 0, 0, 0)));
	private static readonly Brush Disc = Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)));

	private readonly (int X, int Y, string Name, (int X, int Y)[]? Elements)[] _sources;
	private readonly int _width;
	private readonly int _height;
	private (int Index, int Bgra)[]? _covered;

	/// <param name="project">The project whose enabled sources are drawn.</param>
	/// <param name="width">Frame width in pixels (the canvas's: exports are at full resolution).</param>
	/// <param name="height">Frame height in pixels.</param>
	public SourceMarkerPainter(Project project, int width, int height)
	{
		_sources = [.. project.Sources.Where(source => source.Enabled)
			.Select(source => (source.X, source.Y, source.Name, source.Array != null ? source.Elements.Select(e => (e.X, e.Y)).ToArray() : null))];
		_width = width;
		_height = height;
	}

	/// <summary>Whether there is anything to draw.</summary>
	public bool IsEmpty => _sources.Length == 0;

	/// <summary>Draws the markers over one frame (packed BGRA32, the frame's size). Export thread.</summary>
	public void Paint(Memory<int> pixels)
	{
		_covered ??= Render();
		Span<int> frame = pixels.Span;
		foreach ((int index, int bgra) in _covered)
		{
			int a = (bgra >> 24) & 0xFF;
			int inverse = 255 - a;
			int argb = frame[index];
			int b = (bgra & 0xFF) + ((argb & 0xFF) * inverse + 127) / 255;
			int g = ((bgra >> 8) & 0xFF) + (((argb >> 8) & 0xFF) * inverse + 127) / 255;
			int r = ((bgra >> 16) & 0xFF) + (((argb >> 16) & 0xFF) * inverse + 127) / 255;
			frame[index] = unchecked((int)0xFF000000) | (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
		}
	}

	/// <summary>Renders every marker once and keeps the pixels it covers with their premultiplied colour. Export thread (an STA).</summary>
	private (int Index, int Bgra)[] Render()
	{
		double scale = ProbeMarkers.FontSize(_height) / PreviewLabelSize;
		var visual = new DrawingVisual();
		using (DrawingContext dc = visual.RenderOpen())
		{
			var dotRim = new Pen(Halo, 1.5 * scale);
			foreach ((int x, int y, string _, (int X, int Y)[]? elements) in _sources)
			{
				foreach ((int ex, int ey) in elements ?? [])
				{
					dc.DrawEllipse(Brushes.White, dotRim, Centre(ex, ey), 2.5 * scale, 2.5 * scale);
				}
			}
			var haloPen = new Pen(Halo, (1.5 + 2.0) * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
			var whitePen = new Pen(Brushes.White, 1.5 * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
			foreach ((int x, int y, string name, (int X, int Y)[]? elements) in _sources)
			{
				Point centre = Centre(x, y);
				// the glyph's 20 × 20 box centred on the source, at the export's scale
				dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, centre.X - 10 * scale, centre.Y - 10 * scale));
				dc.DrawEllipse(Disc, null, new Point(10, 10), 6, 6);
				dc.Pop();
				Geometry glyph = Glyph.Clone();
				glyph.Transform = new MatrixTransform(scale, 0, 0, scale, centre.X - 10 * scale, centre.Y - 10 * scale);
				dc.DrawGeometry(null, haloPen, glyph);
				dc.DrawGeometry(null, whitePen, glyph);
				dc.DrawEllipse(Halo, null, centre, 2.5 * scale, 2.5 * scale);
				dc.DrawEllipse(Brushes.White, null, centre, 1.5 * scale, 1.5 * scale);
				if (name.Length == 0)
				{
					continue;
				}
				var label = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelTypeface, PreviewLabelSize * scale, Brushes.White, 1.0);
				double offset = 13 * scale;
				bool below = elements != null;
				double lx = below ? Math.Clamp(centre.X - label.Width / 2, 0, Math.Max(0, _width - label.Width)) : Math.Min(centre.X + offset, Math.Max(0, _width - label.Width));
				double ly = below ? Math.Min(centre.Y + offset, Math.Max(0, _height - label.Height)) : Math.Clamp(centre.Y - label.Height / 2, 0, Math.Max(0, _height - label.Height));
				var origin = new Point(lx, ly);
				dc.DrawGeometry(null, new Pen(Halo, 3.0 * scale) { LineJoin = PenLineJoin.Round }, label.BuildGeometry(origin));
				dc.DrawText(label, origin);
			}
		}
		var bitmap = new RenderTargetBitmap(_width, _height, 96, 96, PixelFormats.Pbgra32);
		bitmap.Render(visual);
		int[] layer = new int[_width * _height];
		bitmap.CopyPixels(layer, _width * 4, 0);
		var covered = new List<(int, int)>();
		for (int i = 0; i < layer.Length; i++)
		{
			if (((layer[i] >> 24) & 0xFF) != 0)
			{
				covered.Add((i, layer[i]));
			}
		}
		return [.. covered];
	}

	/// <summary>The centre of canvas pixel (<paramref name="x"/>, <paramref name="y"/>) in the frame's coordinates, where the preview puts a handle.</summary>
	private static Point Centre(int x, int y) => new(x + 0.5, y + 0.5);

	private static T Frozen<T>(T freezable) where T : Freezable
	{
		freezable.Freeze();
		return freezable;
	}
}
