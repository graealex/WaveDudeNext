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

using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveDudeNext.Export;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;
using WaveDudeNext.Runs;
using WaveDudeNext.WPF.Controls;

namespace WaveDudeNext.WPF.Rendering;

/// <summary>
/// Draws the probes into exported frames at canvas scale: the markers in the author's style (the strokes by
/// <see cref="ProbeMarkers"/> on the pixels, the numbers as bold Segoe UI with a drop shadow rendered here)
/// and the graph boxes as the preview shows them (for every active probe of a layer, the box where the probe
/// says, with the run's recording as it stood at the frame, <see cref="ProbeGraphData.At"/>). Built on the UI
/// thread from the project (the probes are cloned, so edits during the export do not move anything) and the
/// run; <see cref="For"/> gives the export job a decoration that runs on the export's own thread (a
/// single-threaded apartment), where each box and label is rendered into a <see cref="RenderTargetBitmap"/>
/// of its size and blended over the frame's pixels. Probes are numbered in project order, active ones only,
/// as the author numbered his run list.
/// </summary>
public sealed class ProbeGraphPainter
{
	private readonly (Probe Probe, ProbeTrace? Trace, int Number)[] _items;
	private readonly bool _graphs;
	private readonly bool _markers;
	private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
	private static readonly Brush ShadowBrush = Frozen(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)));
	private readonly int _every;
	private readonly int _totalSteps;
	private readonly int _width;
	private readonly int _height;
	private ProbeGraphStyle? _style;

	/// <param name="project">The project whose active probes are drawn.</param>
	/// <param name="run">The run whose recordings the graphs show; its size is the frame size.</param>
	/// <param name="graphs">Draw the graph boxes.</param>
	/// <param name="markers">Draw the markers.</param>
	public ProbeGraphPainter(Project project, SimulationRun run, bool graphs, bool markers)
	{
		_graphs = graphs;
		_markers = markers;
		_items = [.. project.Probes.Where(probe => probe.Active).Select((probe, i) => (probe.Clone(), TraceOf(run.Spec.Probes, run.Traces, probe), i + 1))];
		_every = run.Spec.VisualizeEvery;
		_totalSteps = Math.Max(1, run.Spec.Steps);
		_width = run.Spec.Width;
		_height = run.Spec.Height;
	}

	/// <summary>The decoration for a layer: null when nothing is to be drawn or no active probe sits on it.</summary>
	public Action<int, Memory<int>>? For(ProbeLayer layer) =>
		(_graphs || _markers) && _items.Any(item => item.Probe.Layer == layer) ? (frame, pixels) => Paint(layer, frame, pixels) : null;

	/// <summary>Draws the layer's markers, then its boxes, over one frame. Export thread.</summary>
	private void Paint(ProbeLayer layer, int frame, Memory<int> pixels)
	{
		if (_markers)
		{
			foreach ((Probe probe, _, int number) in _items)
			{
				if (probe.Layer == layer)
				{
					ProbeMarkers.DrawStrokes(pixels.Span, _width, _height, probe);
				}
			}
			foreach ((Probe probe, _, int number) in _items)
			{
				if (probe.Layer != layer)
				{
					continue;
				}
				bool line = probe.Kind == ProbeKind.Line;
				DrawLabel(pixels.Span, ProbeMarkers.Label(number, line, endB: false), probe.AX, probe.AY);
				if (line)
				{
					DrawLabel(pixels.Span, ProbeMarkers.Label(number, line, endB: true), probe.BX, probe.BY);
				}
			}
		}
		if (!_graphs)
		{
			return;
		}
		_style ??= ProbeGraphDrawing.MakeStyle(1.0);
		foreach ((Probe probe, ProbeTrace? trace, _) in _items)
		{
			if (probe.Layer != layer)
			{
				continue;
			}
			BoxRect box = ProbeGraph.BoxInCanvas(probe, _width, _height);
			if (box.Width < 1 || box.Height < 1)
			{
				continue;
			}
			ProbeGraphData data = trace != null ? ProbeGraphData.At(trace, frame * _every, frame) : default;
			ProbeGraphLayout layout = ProbeGraph.Layout(probe, data, _totalSteps, box.Width, box.Height, _style, CultureInfo.CurrentCulture);
			if (!layout.IsVisible)
			{
				continue;
			}
			var visual = new DrawingVisual();
			using (DrawingContext dc = visual.RenderOpen())
			{
				ProbeGraphDrawing.DrawBox(dc, layout, null, 1.0);
			}
			var bitmap = new RenderTargetBitmap(box.Width, box.Height, 96, 96, PixelFormats.Pbgra32);
			bitmap.Render(visual);
			var buffer = new byte[box.Width * box.Height * 4];
			bitmap.CopyPixels(buffer, box.Width * 4, 0);
			Blend(buffer, box, pixels.Span);
		}
	}

	/// <summary>A marker's label above and to the right of the cross at (<paramref name="x"/>, <paramref name="y"/>): bold, white over a shadow two pixels in, like the author's.</summary>
	private void DrawLabel(Span<int> frame, string text, int x, int y)
	{
		double fontPx = ProbeMarkers.FontSize(_height);
		var shadow = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, fontPx, ShadowBrush, 1.0);
		var white = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, fontPx, Brushes.White, 1.0);
		int boxWidth = (int)Math.Ceiling(white.Width) + 4;
		int boxHeight = (int)Math.Ceiling(white.Height) + 4;
		var visual = new DrawingVisual();
		using (DrawingContext dc = visual.RenderOpen())
		{
			dc.DrawText(shadow, new Point(3, 3));
			dc.DrawText(white, new Point(2, 2));
		}
		var bitmap = new RenderTargetBitmap(boxWidth, boxHeight, 96, 96, PixelFormats.Pbgra32);
		bitmap.Render(visual);
		var buffer = new byte[boxWidth * boxHeight * 4];
		bitmap.CopyPixels(buffer, boxWidth * 4, 0);
		(int left, int top) = ProbeMarkers.LabelOrigin(x, y, _height);
		Blend(buffer, new BoxRect(left, top, boxWidth, boxHeight), frame);
	}

	private static Brush Frozen(Brush brush)
	{
		brush.Freeze();
		return brush;
	}

	/// <summary>Composites a premultiplied BGRA box over the frame at the box's position, clipped to the frame.</summary>
	private void Blend(byte[] box, BoxRect rect, Span<int> frame)
	{
		for (int y = 0; y < rect.Height; y++)
		{
			int fy = rect.Y + y;
			if (fy < 0 || fy >= _height)
			{
				continue;
			}
			for (int x = 0; x < rect.Width; x++)
			{
				int fx = rect.X + x;
				if (fx < 0 || fx >= _width)
				{
					continue;
				}
				int i = (y * rect.Width + x) * 4;
				int a = box[i + 3];
				if (a == 0)
				{
					continue;
				}
				int argb = frame[fy * _width + fx];
				int inverse = 255 - a;
				int b = box[i] + ((argb & 0xFF) * inverse + 127) / 255;
				int g = box[i + 1] + (((argb >> 8) & 0xFF) * inverse + 127) / 255;
				int r = box[i + 2] + (((argb >> 16) & 0xFF) * inverse + 127) / 255;
				frame[fy * _width + fx] = unchecked((int)0xFF000000) | (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
			}
		}
	}

	/// <summary>The trace of the model probe among a spec's probes (matched by <see cref="ProbeSpec.Origin"/>), or null when it was not recorded.</summary>
	private static ProbeTrace? TraceOf(ImmutableArray<ProbeSpec> specs, ProbeTrace[] traces, Probe probe)
	{
		for (int i = 0; i < specs.Length && i < traces.Length; i++)
		{
			if (ReferenceEquals(specs[i].Origin, probe))
			{
				return traces[i];
			}
		}
		return null;
	}
}
