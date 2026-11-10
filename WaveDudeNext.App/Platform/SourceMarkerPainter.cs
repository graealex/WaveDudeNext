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

using System.Runtime.InteropServices;
using SkiaSharp;
using WaveDudeNext.Export;
using WaveDudeNext.Model;
using WaveDudeNext.UI.Rendering;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// Draws the enabled sources into exported frames (<see cref="Display.SourceMarkersInExport"/>) as the preview shows
/// them, the port of the WPF application's painter of the same name: the source marker (a ring of radius 6 with four
/// ticks of its crosshair and a dot in the middle, white over a dark halo, on a faint dark disc), a composite source's
/// emitter dots (radius 2.5 with a dark rim), and the name in the default typeface outlined in dark, beside a point
/// source and below a composite one (whose rotation handle sits beside it in the preview). Everything is scaled from
/// the preview's sizes by the ratio of the probe labels' size at the frame's height to the preview's 11 px labels
/// (<see cref="ProbeMarkers.FontSize"/>), so sources and probe numbers read alike. The emitter positions are the
/// source's <see cref="Source.Elements"/> as generated for the run (<see cref="Engine.Emitter.FromProject"/>, which
/// <see cref="Runs.RunSpec.Create"/> calls, regenerates them from the project seed). The sources do not move during a
/// run, so the markers are rendered once, on the export's render thread at the first frame, into a premultiplied
/// SkiaSharp layer of the frame's size, whose covered pixels are kept as (index, BGRA) pairs and blended over each
/// frame. Built before the export from the project (positions and names are copied, so edits during the export change
/// nothing). The lazy render is thread-safe; the painter belongs to one export job.
/// </summary>
public sealed class SourceMarkerPainter
{
	/// <summary>The preview's label size in pixels, the reference of the scale.</summary>
	private const float PreviewLabelSize = 11f;

	private static readonly SKColor Halo = new(0, 0, 0, 191);
	private static readonly SKColor Disc = new(0, 0, 0, 0x60);

	private readonly (int X, int Y, string Name, (int X, int Y)[]? Elements)[] _sources;
	private readonly int _width;
	private readonly int _height;
	private readonly Lazy<(int Index, int Bgra)[]> _covered;

	/// <param name="project">The project whose enabled sources are drawn.</param>
	/// <param name="width">Frame width in pixels (the canvas's: exports are at full resolution).</param>
	/// <param name="height">Frame height in pixels.</param>
	public SourceMarkerPainter(Project project, int width, int height)
	{
		_sources = [.. project.Sources.Where(source => source.Enabled)
			.Select(source => (source.X, source.Y, source.Name, source.Array != null ? source.Elements.Select(e => (e.X, e.Y)).ToArray() : null))];
		_width = width;
		_height = height;
		_covered = new Lazy<(int Index, int Bgra)[]>(Render, LazyThreadSafetyMode.ExecutionAndPublication);
	}

	/// <summary>Whether there is anything to draw.</summary>
	public bool IsEmpty => _sources.Length == 0;

	/// <summary>Draws the markers over one frame (packed ARGB32, the frame's size). Export thread.</summary>
	/// <param name="pixels">The frame, width × height, modified in place.</param>
	public void Paint(Memory<int> pixels)
	{
		Span<int> frame = pixels.Span;
		foreach ((int index, int bgra) in _covered.Value)
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

	/// <summary>Renders every marker once into a premultiplied layer and keeps the pixels it covers with their colour (packed as the frame packs them: A, R, G, B from the top byte down).</summary>
	private (int Index, int Bgra)[] Render()
	{
		float scale = ProbeMarkers.FontSize(_height) / PreviewLabelSize;
		using var bitmap = new SKBitmap(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
		using (var canvas = new SKCanvas(bitmap))
		using (var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
		using (var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round })
		using (var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, Color = Halo, StrokeWidth = 3f * scale })
		using (SKPath glyph = SKPath.ParseSvgPathData(ExportGlyphs.SourceMarker) ?? throw new InvalidOperationException("The source marker glyph did not parse."))
		using (var labelFont = new SKFont(SKTypeface.Default, PreviewLabelSize * scale))
		{
			canvas.Clear(SKColors.Transparent);
			// the emitters of composite sources first, under their source's marker: white dots with a dark rim
			foreach ((_, _, _, (int X, int Y)[]? elements) in _sources)
			{
				foreach ((int ex, int ey) in elements ?? [])
				{
					fill.Color = SKColors.White;
					canvas.DrawCircle(ex + 0.5f, ey + 0.5f, 2.5f * scale, fill);
					stroke.Color = Halo;
					stroke.StrokeWidth = 1.5f * scale;
					canvas.DrawCircle(ex + 0.5f, ey + 0.5f, 2.5f * scale, stroke);
				}
			}
			float labelHeight = labelFont.Spacing;
			float labelAscent = labelFont.Metrics.Ascent;
			foreach ((int x, int y, string name, (int X, int Y)[]? elements) in _sources)
			{
				// the centre of canvas pixel (x, y), where the preview puts a handle
				float cx = x + 0.5f;
				float cy = y + 0.5f;
				fill.Color = Disc;
				canvas.DrawCircle(cx, cy, 6f * scale, fill);
				// the glyph's 20 × 20 box centred on the source, at the export's scale
				using var path = new SKPath(glyph);
				path.Transform(SKMatrix.CreateScaleTranslation(scale, scale, cx - 10f * scale, cy - 10f * scale));
				stroke.Color = Halo;
				stroke.StrokeWidth = (1.5f + 2.0f) * scale;
				canvas.DrawPath(path, stroke);
				stroke.Color = SKColors.White;
				stroke.StrokeWidth = 1.5f * scale;
				canvas.DrawPath(path, stroke);
				fill.Color = Halo;
				canvas.DrawCircle(cx, cy, 2.5f * scale, fill);
				fill.Color = SKColors.White;
				canvas.DrawCircle(cx, cy, 1.5f * scale, fill);
				if (name.Length == 0)
				{
					continue;
				}
				// the name 13 px (scaled) right of a point source, centred below a composite one, kept inside the frame
				float labelWidth = labelFont.MeasureText(name);
				float offset = 13f * scale;
				bool below = elements != null;
				float lx = below ? Math.Clamp(cx - labelWidth / 2, 0f, MathF.Max(0f, _width - labelWidth)) : MathF.Min(cx + offset, MathF.Max(0f, _width - labelWidth));
				float ly = below ? MathF.Min(cy + offset, MathF.Max(0f, _height - labelHeight)) : Math.Clamp(cy - labelHeight / 2, 0f, MathF.Max(0f, _height - labelHeight));
				// the dark outline is the text's own outlines stroked (WPF stroked the text geometry), then the white text over it
				float baseline = ly - labelAscent;
				canvas.DrawText(name, lx, baseline, SKTextAlign.Left, labelFont, outline);
				fill.Color = SKColors.White;
				canvas.DrawText(name, lx, baseline, SKTextAlign.Left, labelFont, fill);
			}
		}
		// premultiplied B, G, R, A bytes read as little-endian ints are the frame's packing
		ReadOnlySpan<byte> bytes = bitmap.GetPixelSpan();
		int rowBytes = bitmap.RowBytes;
		var covered = new List<(int Index, int Bgra)>();
		for (int y = 0; y < _height; y++)
		{
			ReadOnlySpan<int> row = MemoryMarshal.Cast<byte, int>(bytes.Slice(y * rowBytes, _width * 4));
			for (int x = 0; x < _width; x++)
			{
				int bgra = row[x];
				if (((bgra >> 24) & 0xFF) != 0)
				{
					covered.Add((y * _width + x, bgra));
				}
			}
		}
		return [.. covered];
	}
}
