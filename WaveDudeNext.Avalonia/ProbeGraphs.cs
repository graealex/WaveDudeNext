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


using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;

namespace WaveDudeNext.CrossPlatform;

/// <summary>Live probe graphs and their movable boxes over one picture. UI thread only.</summary>
internal sealed class ProbeGraphs : Panel
{
	private readonly WindowStateModel _state;
	private readonly ProbeLayer _layer;
	private readonly Dictionary<Probe, (GraphBox Box, Corner Corner)> _boxes = new();
	public ProbeGraphs(WindowStateModel state, ProbeLayer layer) { _state = state; _layer = layer; }
	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		_state.PropertyChanged += StateChanged;
		_state.SceneVisualChanged += SceneChanged;
		_state.ProbeGraphsChanged += GraphsChanged;
		Refresh();
	}
	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		_state.PropertyChanged -= StateChanged;
		_state.SceneVisualChanged -= SceneChanged;
		_state.ProbeGraphsChanged -= GraphsChanged;
		base.OnDetachedFromVisualTree(e);
	}
	private void StateChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(WindowStateModel.Project) or nameof(WindowStateModel.SelectedItem) or nameof(WindowStateModel.CanvasWidth) or nameof(WindowStateModel.CanvasHeight)) Refresh();
	}
	private void SceneChanged(object? sender, ProjectChange e) => Refresh();
	private void GraphsChanged(object? sender, EventArgs e) { foreach (var pair in _boxes.Values) pair.Box.InvalidateVisual(); }
	private void Refresh()
	{
		Probe[] wanted = _state.Project.Probes.Where(p => p.Active && p.Layer == _layer).ToArray();
		foreach (Probe probe in _boxes.Keys.Except(wanted).ToArray())
		{
			var pair = _boxes[probe]; Children.Remove(pair.Box); Children.Remove(pair.Corner); _boxes.Remove(probe);
		}
		foreach (Probe probe in wanted)
		{
			if (!_boxes.TryGetValue(probe, out var pair))
			{
				pair = (new GraphBox(this, probe), new Corner(this, probe));
				_boxes.Add(probe, pair); Children.Add(pair.Box); Children.Add(pair.Corner);
			}
			AutomationProperties.SetAutomationId(pair.Box, "ProbeBox:" + probe.Name);
			AutomationProperties.SetAutomationId(pair.Corner, "ProbeBoxCorner:" + probe.Name);
			AutomationProperties.SetName(pair.Box, probe.Name);
			AutomationProperties.SetName(pair.Corner, probe.Name);
			SetTip(pair.Box, "TipProbeBoxFormat", probe.Name);
			SetTip(pair.Corner, "TipProbeBoxCornerFormat", probe.Name);
			pair.Box.InvalidateVisual(); pair.Corner.InvalidateVisual();
		}
		InvalidateMeasure();
	}
	private static void SetTip(Control control, string key, string name)
	{
		string format = Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is string text ? text : "{0}";
		ToolTip.SetTip(control, string.Format(CultureInfo.CurrentCulture, format, name));
	}
	private (Rect Rect, double Scale) Canvas(Size size)
	{
		double width = Math.Max(1, _state.CanvasWidth), height = Math.Max(1, _state.CanvasHeight);
		double scale = Math.Min(size.Width / width, size.Height / height);
		return (new Rect((size.Width - width * scale) / 2, (size.Height - height * scale) / 2, width * scale, height * scale), scale);
	}
	protected override Size MeasureOverride(Size availableSize) { foreach (Control child in Children) child.Measure(availableSize); return default; }
	protected override Size ArrangeOverride(Size finalSize)
	{
		var (canvas, scale) = Canvas(finalSize); Clip = new RectangleGeometry(canvas);
		foreach (var (probe, pair) in _boxes)
		{
			BoxRect box = ProbeGraph.BoxInCanvas(probe, _state.CanvasWidth, _state.CanvasHeight);
			var rect = new Rect(Math.Round(canvas.X + box.X * scale), Math.Round(canvas.Y + box.Y * scale), Math.Max(1, Math.Round(box.Width * scale)), Math.Max(1, Math.Round(box.Height * scale)));
			pair.Box.Arrange(rect);
			pair.Corner.Arrange(new Rect(rect.Right - 12, rect.Bottom - 12, 12, 12));
		}
		return finalSize;
	}
	private void Move(Probe probe, int x, int y, bool snap)
	{
		if (snap)
		{
			double scale = Canvas(Bounds.Size).Scale, dx = (x - probe.AX) * scale, dy = (y - probe.AY) * scale;
			double limit = probe.IsBoxAttached ? 28 : 14;
			if (dx * dx + dy * dy <= limit * limit) { x = probe.AX; y = probe.AY; }
		}
		BoxRect box = ProbeGraph.BoxInCanvas(probe, _state.CanvasWidth, _state.CanvasHeight);
		probe.BoxX = Math.Clamp(x, 0, Math.Max(0, _state.CanvasWidth - box.Width));
		probe.BoxY = Math.Clamp(y, 0, Math.Max(0, _state.CanvasHeight - box.Height));
	}
	private static void Resize(Probe probe, int width, int height, bool snap)
	{
		var (size, box) = snap ? ProbeGraph.Snap(width, height) : (ProbeBoxSize.Custom, new BoxSize(Math.Clamp(width, Probe.MinBoxWidth, Probe.MaxBoxSide), Math.Clamp(height, Probe.MinBoxHeight, Probe.MaxBoxSide)));
		if (size == ProbeBoxSize.Custom) { probe.BoxWidth = box.Width; probe.BoxHeight = box.Height; }
		probe.BoxSize = size;
	}
	private abstract class GraphControl(ProbeGraphs owner, Probe probe) : Control
	{
		protected readonly ProbeGraphs Owner = owner;
		protected readonly Probe Probe = probe;
		private Point? _start;
		private IPointer? _pointer;
		private int _x, _y;
		private ProbeBoxSize _originalSize;
		private int _originalWidth, _originalHeight;
		protected abstract bool IsCorner { get; }
		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == IsFocusedProperty) { if (IsFocused) Owner._state.SelectItem(Probe); InvalidateVisual(); }
		}
		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
			Owner._state.SelectItem(Probe); Focus();
			if (IsCorner) { BoxSize size = ProbeGraph.BoxSizeOf(Probe); (_x, _y) = (size.Width, size.Height); _originalSize = Probe.BoxSize; _originalWidth = Probe.BoxWidth; _originalHeight = Probe.BoxHeight; }
			else (_x, _y) = (Probe.BoxX, Probe.BoxY);
			_start = e.GetPosition(Owner); _pointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true;
		}
		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e); if (_start is not Point start) return;
			double scale = Owner.Canvas(Owner.Bounds.Size).Scale; if (scale <= 0) return;
			Vector delta = e.GetPosition(Owner) - start;
			int x = _x + (int)Math.Round(delta.X / scale), y = _y + (int)Math.Round(delta.Y / scale);
			if (IsCorner) Resize(Probe, x, y, true); else Owner.Move(Probe, x, y, true);
			e.Handled = true;
		}
		protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (_start != null) { _start = null; _pointer = null; e.Pointer.Capture(null); e.Handled = true; } }
		protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _start = null; _pointer = null; }
		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (e.Key == Key.Escape && _start != null) { if (IsCorner) { Probe.BoxWidth = _originalWidth; Probe.BoxHeight = _originalHeight; Probe.BoxSize = _originalSize; } else Owner.Move(Probe, _x, _y, false); _start = null; _pointer?.Capture(null); _pointer = null; e.Handled = true; return; }
			if (e.Key == Key.Delete) { Owner._state.SelectItem(Probe); if (Owner._state.RemoveCommand.CanExecute(null)) Owner._state.RemoveCommand.Execute(null); e.Handled = true; return; }
			if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
			int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
			(int dx, int dy) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0), Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
			if (dx == 0 && dy == 0) return;
			if (IsCorner) { BoxSize size = ProbeGraph.BoxSizeOf(Probe); Resize(Probe, size.Width + dx, size.Height + dy, false); }
			else Owner.Move(Probe, Probe.BoxX + dx, Probe.BoxY + dy, false);
			e.Handled = true;
		}
	}
	private sealed class Corner : GraphControl
	{
		public Corner(ProbeGraphs owner, Probe probe) : base(owner, probe) { Focusable = true; }
		protected override bool IsCorner => true;
		public override void Render(DrawingContext context)
		{
			context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
			context.DrawLine(new Pen(IsFocused ? Brushes.Yellow : Brushes.LightGray, 1), new Point(3, 10), new Point(10, 3));
		}
	}
	private sealed class GraphBox : GraphControl
	{
		private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(235, 14, 16, 22));
		private static readonly IBrush Grid = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
		private static readonly IBrush Curve = new SolidColorBrush(Color.FromRgb(255, 208, 64));
		private static readonly Typeface Font = new("Segoe UI");
		public GraphBox(ProbeGraphs owner, Probe probe) : base(owner, probe) { Focusable = true; ContextMenu = owner._state.ProbeCsvMenu(probe); }
		protected override bool IsCorner => false;
		private static FormattedText Text(string text, IBrush brush) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 11, brush);
		public override void Render(DrawingContext context)
		{
			ProbeGraphData data = Owner._state.ProbeData(Probe, out int steps);
			var style = new ProbeGraphStyle(11, (float)Text("0", Brushes.White).Height, s => (float)Text(s, Brushes.White).Width);
			ProbeGraphLayout graph = ProbeGraph.Layout(Probe, data, steps, (float)Bounds.Width, (float)Bounds.Height, style, CultureInfo.CurrentCulture);
			if (!graph.IsVisible) return;
			context.DrawRectangle(Background, new Pen(ReferenceEquals(Probe, Owner._state.SelectedItem) ? Brushes.Yellow : Brushes.LightGray, 1), new Rect(Bounds.Size));
			if (graph.HasPlot)
			{
				double right = graph.PlotX + graph.PlotWidth, bottom = graph.PlotY + graph.PlotHeight;
				foreach (GraphTick tick in graph.YTicks)
				{
					context.DrawLine(new Pen(tick.IsZero ? Brushes.Gray : Grid, 1), new Point(graph.PlotX, tick.Position), new Point(right, tick.Position));
					if (tick.Label.Length > 0) { var label = Text(tick.Label, Brushes.White); context.DrawText(label, new Point(Math.Max(graph.LabelLeft, graph.PlotX - ProbeGraph.LabelGap - label.Width), tick.Position - label.Height / 2)); }
				}
				foreach (GraphTick tick in graph.XTicks)
				{
					context.DrawLine(new Pen(Grid, 1), new Point(tick.Position, graph.PlotY), new Point(tick.Position, bottom));
					if (tick.Label.Length > 0) { var label = Text(tick.Label, Brushes.White); context.DrawText(label, new Point(Math.Clamp(tick.Position - label.Width / 2, ProbeGraph.EdgePad, Math.Max(ProbeGraph.EdgePad, graph.Width - label.Width - ProbeGraph.EdgePad)), bottom + 2)); }
				}
				context.DrawRectangle(null, new Pen(Brushes.Gray, 1), new Rect(graph.PlotX, graph.PlotY, graph.PlotWidth, graph.PlotHeight));
				using (context.PushClip(new Rect(graph.PlotX, graph.PlotY, graph.PlotWidth, graph.PlotHeight)))
				{
					if (graph.Curve.Length == 1) context.DrawEllipse(Curve, null, new Point(graph.Curve[0].X, graph.Curve[0].Y), 2, 2);
					if (graph.Curve.Length > 1)
					{
						var geometry = new StreamGeometry();
						using (StreamGeometryContext path = geometry.Open())
						{
							path.BeginFigure(new Point(graph.Curve[0].X, graph.Curve[0].Y), false);
							for (int i = 1; i < graph.Curve.Length; i++) path.LineTo(new Point(graph.Curve[i].X, graph.Curve[i].Y));
							path.EndFigure(false);
						}
						context.DrawGeometry(null, new Pen(Curve, graph.LineWidth), geometry);
					}
				}
			}
			FormattedText? readout = graph.Readout.Length > 0 ? Text(graph.Readout, Curve) : null;
			double titleWidth = Math.Max(0, graph.Width - (readout?.Width ?? 0) - 16);
			if (titleWidth > 0)
			{
				FormattedText title = Text(graph.Title, Brushes.White);
				title.MaxTextWidth = titleWidth;
				title.MaxLineCount = 1;
				title.Trimming = TextTrimming.CharacterEllipsis;
				context.DrawText(title, new Point(6, graph.TitleTop));
			}
			if (readout != null) context.DrawText(readout, new Point(Math.Max(0, graph.Width - readout.Width - 8), graph.TitleTop));
		}
	}
}
