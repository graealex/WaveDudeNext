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

// Contributors: Vinicius Miguel


using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;
using WaveDudeNext.Export;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>Draws the project's source and probe annotations into exported BGRA frames. Created on the UI thread; painted on one export thread.</summary>
internal sealed class ExportDecorations
{
	private readonly (int X, int Y, string Name, (int X, int Y)[]? Emitters)[] _sources;
	private readonly (Probe Probe, ProbeTrace? Trace, int Number)[] _probes;
	private readonly int _width, _height, _every, _steps;
	private readonly bool _drawSources, _drawMarkers, _drawGraphs;

	/// <summary>Captures export settings and model positions; the run's completed traces are read on the export thread.</summary>
	public ExportDecorations(Project project, SimulationRun run)
	{
		_width = run.Spec.Width; _height = run.Spec.Height; _every = run.Spec.VisualizeEvery; _steps = Math.Max(1, run.Spec.Steps);
		_drawSources = project.Display.SourceMarkersInExport; _drawMarkers = project.Display.ProbeMarkersInExport; _drawGraphs = project.Display.ProbeGraphsInExport;
		_sources = project.Sources.Where(source => source.Enabled).Select(source =>
		{
			Source copy = source.Clone();
			copy.GenerateElements(project.Seed);
			return (copy.X, copy.Y, copy.Name, copy.Array == null ? null : copy.Elements.Select(e => (e.X, e.Y)).ToArray());
		}).ToArray();
		_probes = project.Probes.Where(probe => probe.Active).Select((probe, number) =>
		{
			int index = -1;
			for (int i = 0; i < run.Spec.Probes.Length; i++) if (ReferenceEquals(run.Spec.Probes[i].Origin, probe)) { index = i; break; }
			return (probe.Clone(), index >= 0 && index < run.Traces.Length ? run.Traces[index] : null, number + 1);
		}).ToArray();
	}

	/// <summary>Returns a frame decorator for a layer, or null when no annotations apply.</summary>
	public Action<int, Memory<int>>? For(ProbeLayer layer) => (_drawSources && _sources.Length > 0 || (_drawMarkers || _drawGraphs) && _probes.Any(item => item.Probe.Layer == layer)) ? (frame, pixels) => Paint(layer, frame, pixels) : null;

	private void Paint(ProbeLayer layer, int frame, Memory<int> pixels)
	{
		using var bitmap = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Opaque));
		int[] buffer = pixels.ToArray();
		Marshal.Copy(buffer, 0, bitmap.GetPixels(), buffer.Length);
		using var canvas = new SKCanvas(bitmap);
		using var regular = new SKTypefaceOwner("Segoe UI", false);
		using var bold = new SKTypefaceOwner("Segoe UI", true);
		if (_drawSources) DrawSources(canvas, regular.Typeface);
		canvas.Flush();
		if (_drawMarkers)
		{
			Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);
			foreach (var item in _probes.Where(item => item.Probe.Layer == layer)) ProbeMarkers.DrawStrokes(buffer, _width, _height, item.Probe);
			Marshal.Copy(buffer, 0, bitmap.GetPixels(), buffer.Length);
			foreach (var item in _probes.Where(item => item.Probe.Layer == layer))
			{
				bool line = item.Probe.Kind == ProbeKind.Line;
				DrawMarkerLabel(canvas, bold.Typeface, item.Probe.AX, item.Probe.AY, ProbeMarkers.Label(item.Number, line, false));
				if (line) DrawMarkerLabel(canvas, bold.Typeface, item.Probe.BX, item.Probe.BY, ProbeMarkers.Label(item.Number, true, true));
			}
		}
		if (_drawGraphs)
			foreach (var item in _probes.Where(item => item.Probe.Layer == layer)) DrawGraph(canvas, regular.Typeface, item.Probe, item.Trace, frame);
		canvas.Flush();
		Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);
		buffer.AsSpan().CopyTo(pixels.Span);
	}

	private void DrawSources(SKCanvas canvas, SKTypeface typeface)
	{
		float scale = ProbeMarkers.FontSize(_height) / 11f;
		using var font = new SKFont(typeface, 11 * scale);
		using var halo = new SKPaint { Color = new SKColor(0, 0, 0, 191), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.5f * scale };
		using var white = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale };
		using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
		foreach (var source in _sources)
		{
			if (source.Emitters != null)
				foreach (var (x, y) in source.Emitters) { canvas.DrawCircle(x + .5f, y + .5f, 2.5f * scale, halo); canvas.DrawCircle(x + .5f, y + .5f, 2.5f * scale, fill); }
			float cx = source.X + .5f, cy = source.Y + .5f;
			canvas.DrawCircle(cx, cy, 6 * scale, halo); canvas.DrawCircle(cx, cy, 6 * scale, white);
			foreach (var pen in new[] { halo, white })
			{
				canvas.DrawLine(cx - 10 * scale, cy, cx - 7.5f * scale, cy, pen); canvas.DrawLine(cx + 7.5f * scale, cy, cx + 10 * scale, cy, pen);
				canvas.DrawLine(cx, cy - 10 * scale, cx, cy - 7.5f * scale, pen); canvas.DrawLine(cx, cy + 7.5f * scale, cx, cy + 10 * scale, pen);
			}
			canvas.DrawCircle(cx, cy, 1.5f * scale, fill);
			float width = font.MeasureText(source.Name), xLabel = source.Emitters != null ? cx - width / 2 : cx + 13 * scale;
			float yLabel = source.Emitters != null ? cy + 13 * scale + font.Size : cy + font.Size / 3;
			xLabel = Math.Clamp(xLabel, 0, Math.Max(0, _width - width)); yLabel = Math.Clamp(yLabel, font.Size, Math.Max(font.Size, _height));
			canvas.DrawText(source.Name, xLabel, yLabel, font, halo); canvas.DrawText(source.Name, xLabel, yLabel, font, fill);
		}
	}

	private void DrawMarkerLabel(SKCanvas canvas, SKTypeface typeface, int x, int y, string label)
	{
		using var font = new SKFont(typeface, ProbeMarkers.FontSize(_height));
		using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 220), IsAntialias = true };
		using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
		var (left, top) = ProbeMarkers.LabelOrigin(x, y, _height);
		canvas.DrawText(label, left + 3, top + 3 + font.Size, font, shadow);
		canvas.DrawText(label, left + 2, top + 2 + font.Size, font, white);
	}

	private void DrawGraph(SKCanvas canvas, SKTypeface typeface, Probe probe, ProbeTrace? trace, int frame)
	{
		BoxRect box = ProbeGraph.BoxInCanvas(probe, _width, _height);
		using var font = new SKFont(typeface, 11);
		var style = new ProbeGraphStyle(11, font.Spacing, text => font.MeasureText(text));
		ProbeGraphData data = trace == null ? default : ProbeGraphData.At(trace, frame * _every, frame);
		ProbeGraphLayout layout = ProbeGraph.Layout(probe, data, _steps, box.Width, box.Height, style, CultureInfo.CurrentCulture);
		if (!layout.IsVisible) return;
		using var background = new SKPaint { Color = new SKColor(14, 16, 22, 235) };
		using var line = new SKPaint { Color = new SKColor(200, 200, 210, 170), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
		using var grid = new SKPaint { Color = new SKColor(255, 255, 255, 46), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
		using var text = new SKPaint { Color = new SKColor(225, 225, 230, 225), IsAntialias = true };
		using var curve = new SKPaint { Color = new SKColor(255, 208, 64), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = layout.LineWidth };
		canvas.Save(); canvas.Translate(box.X, box.Y);
		canvas.DrawRect(0, 0, layout.Width, layout.Height, background); canvas.DrawRect(0, 0, layout.Width, layout.Height, line);
		if (layout.HasPlot)
		{
			float right = layout.PlotX + layout.PlotWidth, bottom = layout.PlotY + layout.PlotHeight;
			foreach (GraphTick tick in layout.YTicks)
			{
				canvas.DrawLine(layout.PlotX, tick.Position, right, tick.Position, grid);
				if (tick.Label.Length > 0) canvas.DrawText(tick.Label, Math.Max(layout.LabelLeft, layout.PlotX - ProbeGraph.LabelGap - font.MeasureText(tick.Label)), tick.Position + font.Size / 3, font, text);
			}
			foreach (GraphTick tick in layout.XTicks)
			{
				canvas.DrawLine(tick.Position, layout.PlotY, tick.Position, bottom, grid);
				if (tick.Label.Length > 0) canvas.DrawText(tick.Label, Math.Clamp(tick.Position - font.MeasureText(tick.Label) / 2, ProbeGraph.EdgePad, Math.Max(ProbeGraph.EdgePad, layout.Width - font.MeasureText(tick.Label) - ProbeGraph.EdgePad)), bottom + 2 + font.Size, font, text);
			}
			canvas.DrawRect(layout.PlotX, layout.PlotY, layout.PlotWidth, layout.PlotHeight, line);
			canvas.Save(); canvas.ClipRect(new SKRect(layout.PlotX, layout.PlotY, right, bottom));
			if (layout.Curve.Length == 1) canvas.DrawCircle(layout.Curve[0].X, layout.Curve[0].Y, 2, curve);
			else if (layout.Curve.Length > 1)
			{
				using var path = new SKPath(); path.MoveTo(layout.Curve[0].X, layout.Curve[0].Y);
				for (int i = 1; i < layout.Curve.Length; i++) path.LineTo(layout.Curve[i].X, layout.Curve[i].Y);
				canvas.DrawPath(path, curve);
			}
			canvas.Restore();
		}
		float readoutWidth = font.MeasureText(layout.Readout);
		canvas.Save(); canvas.ClipRect(new SKRect(6, 0, Math.Max(6, layout.Width - readoutWidth - 10), layout.Height));
		canvas.DrawText(layout.Title, 6, layout.TitleTop + font.Size, font, text); canvas.Restore();
		if (layout.Readout.Length > 0) { curve.Style = SKPaintStyle.Fill; canvas.DrawText(layout.Readout, Math.Max(0, layout.Width - readoutWidth - 8), layout.TitleTop + font.Size, font, curve); }
		canvas.Restore();
	}

	private sealed class SKTypefaceOwner : IDisposable
	{
		public SKTypefaceOwner(string family, bool bold) => Typeface = SKTypeface.FromFamilyName(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal);
		public SKTypeface Typeface { get; }
		public void Dispose() => Typeface.Dispose();
	}
}
