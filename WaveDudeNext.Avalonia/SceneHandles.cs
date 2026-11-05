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

namespace WaveDudeNext.CrossPlatform;

/// <summary>Interactive canvas-pixel positions over one picture. UI thread only.</summary>
internal sealed class SceneHandles : Panel
{
	public static readonly StyledProperty<IBrush?> HandleBrushProperty = AvaloniaProperty.Register<SceneHandles, IBrush?>(nameof(HandleBrush), Brushes.LightGray);
	public static readonly StyledProperty<IBrush?> SelectionBrushProperty = AvaloniaProperty.Register<SceneHandles, IBrush?>(nameof(SelectionBrush), Brushes.Yellow);
	private readonly WindowStateModel _state;
	private readonly ProbeLayer _layer;
	private readonly Dictionary<(object, MarkerRole), Marker> _markers = new();
	private enum MarkerRole { Centre, ProbeEnd, Angle }
	private static readonly IBrush Halo = new SolidColorBrush(Color.FromArgb(192, 0, 0, 0));

	public SceneHandles(WindowStateModel state, ProbeLayer layer)
	{
		_state = state;
		_layer = layer;
		Background = Brushes.Transparent;
		Children.Add(new Drawing(this) { IsHitTestVisible = false });
	}
	public IBrush? HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }
	public IBrush? SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		_state.PropertyChanged += StateChanged;
		_state.SceneVisualChanged += SceneChanged;
		Refresh();
	}
	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		_state.PropertyChanged -= StateChanged;
		_state.SceneVisualChanged -= SceneChanged;
		base.OnDetachedFromVisualTree(e);
	}
	private void StateChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(WindowStateModel.Project) or nameof(WindowStateModel.SelectedItem) or nameof(WindowStateModel.CanvasWidth) or nameof(WindowStateModel.CanvasHeight)) Refresh();
	}
	private void SceneChanged(object? sender, EventArgs e) => Refresh();
	private void Refresh()
	{
		var wanted = new List<(object, MarkerRole)>();
		wanted.AddRange(_state.Project.Sources.Select(s => ((object)s, MarkerRole.Centre)));
		wanted.AddRange(_state.Project.Parts.Select(p => ((object)p, MarkerRole.Centre)));
		foreach (Probe probe in _state.Project.Probes.Where(p => p.Layer == _layer))
		{
			wanted.Add((probe, MarkerRole.Centre));
			if (probe.Kind == ProbeKind.Line) wanted.Add((probe, MarkerRole.ProbeEnd));
		}
		if (_state.SelectedItem is OpticalPart or Source { Array: not null }) wanted.Add((_state.SelectedItem, MarkerRole.Angle));
		foreach (var key in _markers.Keys.Except(wanted).ToArray())
		{
			Children.Remove(_markers[key]);
			_markers.Remove(key);
		}
		foreach (var key in wanted)
		{
			if (!_markers.TryGetValue(key, out Marker? marker))
			{
				marker = new Marker(this, key.Item1, key.Item2);
				_markers.Add(key, marker);
				Children.Add(marker);
			}
			marker.UpdateName();
			marker.IsVisible = _state.Project.Display.HandlesInPreview || ReferenceEquals(key.Item1, _state.SelectedItem);
			marker.Opacity = key.Item1 switch { Source { Enabled: false } => .45, OpticalPart { Enabled: false } => .45, Probe { Active: false } => .45, _ => 1 };
			marker.InvalidateVisual();
		}
		InvalidateMeasure();
		Children[0].InvalidateVisual();
	}
	private (Rect Rect, double Scale) Canvas(Size size)
	{
		double width = Math.Max(1, _state.CanvasWidth), height = Math.Max(1, _state.CanvasHeight);
		double scale = Math.Min(size.Width / width, size.Height / height);
		return (new Rect((size.Width - width * scale) / 2, (size.Height - height * scale) / 2, width * scale, height * scale), scale);
	}
	private Point ViewPoint(double x, double y)
	{
		var (rect, scale) = Canvas(Bounds.Size);
		return new Point(rect.X + (x + .5) * scale, rect.Y + (y + .5) * scale);
	}
	protected override Size MeasureOverride(Size availableSize)
	{
		foreach (Control child in Children) child.Measure(new Size(14, 14));
		return default;
	}
	protected override Size ArrangeOverride(Size finalSize)
	{
		var (rect, scale) = Canvas(finalSize);
		Clip = new RectangleGeometry(rect);
		Children[0].Arrange(new Rect(finalSize));
		foreach (Marker marker in Children.OfType<Marker>())
		{
			var (x, y) = marker.Position;
			Point centre = marker.IsAngle ? AnglePoint(marker.Item, finalSize) : new Point(rect.X + (x + .5) * scale, rect.Y + (y + .5) * scale);
			marker.Arrange(new Rect(centre.X - 7, centre.Y - 7, 14, 14));
		}
		return finalSize;
	}
	private Point AnglePoint(object item, Size size)
	{
		(int x, int y, double angle, double extent) = item switch
		{
			Source { Array: { } array } source => (source.X, source.Y, array.Angle, (array.SizeX - 1) / 2.0 * array.SpacingX),
			OpticalPart part => (part.X, part.Y, part.Angle, part.AxisReach),
			_ => (0, 0, 0, 0),
		};
		var (rect, scale) = Canvas(size);
		double reach = Math.Max(28, extent * scale + 14);
		double radians = angle * Math.PI / 180;
		return new Point(rect.X + (x + .5) * scale + reach * Math.Cos(radians), rect.Y + (y + .5) * scale - reach * Math.Sin(radians));
	}
	private static double AngleOf(object item) => item switch { Source { Array: { } array } => array.Angle, OpticalPart part => part.Angle, _ => 0 };
	private static void SetAngle(object item, double degrees)
	{
		switch (item)
		{
			case Source { Array: { } array }: array.Angle = degrees; array.Normalize(); break;
			case OpticalPart part: part.Angle = degrees; part.Normalize(); break;
		}
	}
	private void Draw(DrawingContext context)
	{
		object? selected = _state.SelectedItem;
		var display = _state.Project.Display;
		foreach (Probe probe in _state.Project.Probes.Where(p => p.Layer == _layer && p.Kind == ProbeKind.Line && (display.HandlesInPreview || ReferenceEquals(p, selected))).OrderBy(p => ReferenceEquals(p, selected)))
		{
			Point a = ViewPoint(probe.AX, probe.AY), b = ViewPoint(probe.BX, probe.BY);
			context.DrawLine(new Pen(Halo, 3), a, b);
			context.DrawLine(new Pen(ReferenceEquals(probe, selected) ? SelectionBrush : HandleBrush, 1, dashStyle: DashStyle.Dash), a, b);
		}
		if (selected is OpticalPart or Source { Array: not null })
		{
			(int x, int y) = selected is OpticalPart p ? (p.X, p.Y) : (((Source)selected).X, ((Source)selected).Y);
			Point centre = ViewPoint(x, y), end = AnglePoint(selected, Bounds.Size);
			context.DrawLine(new Pen(Halo, 3), centre, end);
			context.DrawLine(new Pen(SelectionBrush, 1, dashStyle: DashStyle.Dot), centre, end);
		}
		if (selected is OpticalPart part)
		{
			(double X, double Y)[] outline = part.Outline();
			if (outline.Length >= 2)
			{
				var geometry = new StreamGeometry();
				using (StreamGeometryContext path = geometry.Open())
				{
					var (x, y) = part.ToCanvas(outline[0].X, outline[0].Y);
					path.BeginFigure(ViewPoint(x, y), false);
					foreach (var point in outline.Skip(1))
					{
						(x, y) = part.ToCanvas(point.X, point.Y);
						path.LineTo(ViewPoint(x, y));
					}
					path.EndFigure(part.OutlineClosed);
				}
				context.DrawGeometry(null, new Pen(Halo, 3), geometry);
				context.DrawGeometry(null, new Pen(SelectionBrush, 1), geometry);
			}
		}
		foreach (var entry in _markers.Where(entry => entry.Key.Item2 == MarkerRole.Centre && (display.LabelsInPreview || ReferenceEquals(entry.Key.Item1, selected))).OrderBy(entry => ReferenceEquals(entry.Key.Item1, selected)))
		{
			var marker = entry.Value;
			var (x, y) = marker.Position;
			Point centre = ViewPoint(x, y);
			var text = new FormattedText(((WaveDudeNext.Model.INamed)entry.Key.Item1).Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, ReferenceEquals(entry.Key.Item1, selected) ? SelectionBrush : HandleBrush);
			Rect canvas = Canvas(Bounds.Size).Rect;
			bool below = entry.Key.Item1 is OpticalPart or Source { Array: not null };
			double left = below ? centre.X - text.Width / 2 : centre.X + 8;
			double top = below ? centre.Y + 8 : centre.Y - text.Height / 2;
			Point point = new(Math.Clamp(left, canvas.Left, Math.Max(canvas.Left, canvas.Right - text.Width)), Math.Clamp(top, canvas.Top, Math.Max(canvas.Top, canvas.Bottom - text.Height)));
			context.DrawText(text, point);
		}
	}
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		if (e.Source == this && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) _state.SelectItem(null);
	}
	private sealed class Drawing(SceneHandles owner) : Control
	{
		public override void Render(DrawingContext context) => owner.Draw(context);
	}
	private sealed class Marker : Control
	{
		private readonly SceneHandles _owner;
		private readonly object _item;
		private readonly MarkerRole _role;
		private Point? _drag;
		private IPointer? _pointer;
		private (int X, int Y) _start;
		private double _startAngle;
		public Marker(SceneHandles owner, object item, MarkerRole role) { _owner = owner; _item = item; _role = role; Focusable = true; }
		public object Item => _item;
		public bool IsAngle => _role == MarkerRole.Angle;
		public (int X, int Y) Position => _item switch
		{
			Source s => (s.X, s.Y), OpticalPart p => (p.X, p.Y), Probe p when _role == MarkerRole.ProbeEnd => (p.BX, p.BY), Probe p => (p.AX, p.AY), _ => default
		};
		public void UpdateName()
		{
			string name = ((WaveDudeNext.Model.INamed)_item).Name;
			string id = _item switch { Source when IsAngle => $"SourceAngle:{name}", OpticalPart when IsAngle => $"PartAngle:{name}", Source => $"Source:{name}", OpticalPart => $"Part:{name}", _ => $"Probe:{name}:{(_role == MarkerRole.ProbeEnd ? "B" : "A")}" };
			AutomationProperties.SetAutomationId(this, id);
			AutomationProperties.SetName(this, id);
			string format = Application.Current?.Resources.TryGetValue(IsAngle ? "TipAngleMarkerFormat" : "TipMarkerFormat", out object? value) == true && value is string text ? text : "{0}";
			ToolTip.SetTip(this, string.Format(CultureInfo.CurrentCulture, format, name));
		}
		private void SetPoint(int x, int y)
		{
			x = Math.Clamp(x, 0, Math.Max(0, _owner._state.CanvasWidth - 1));
			y = Math.Clamp(y, 0, Math.Max(0, _owner._state.CanvasHeight - 1));
			switch (_item)
			{
				case Source s: s.X = x; s.Y = y; break;
				case OpticalPart p: p.X = x; p.Y = y; break;
				case Probe p when _role == MarkerRole.ProbeEnd: p.BX = x; p.BY = y; break;
				case Probe p: p.AX = x; p.AY = y; break;
			}
		}
		public override void Render(DrawingContext context)
		{
			IBrush? brush = ReferenceEquals(_item, _owner._state.SelectedItem) ? _owner.SelectionBrush : _owner.HandleBrush;
			context.DrawEllipse(Halo, null, new Point(7, 7), 6, 6);
			context.DrawEllipse(null, new Pen(brush, IsFocused ? 2 : 1), new Point(7, 7), 4, 4);
		}
		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == IsFocusedProperty) InvalidateVisual();
		}
		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
			_owner._state.SelectItem(_item); Focus(); _start = Position; _startAngle = AngleOf(_item); _drag = e.GetPosition(_owner); _pointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true;
		}
		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (_drag is not Point start) return;
			if (IsAngle)
			{
				Point centre = _owner.ViewPoint(_start.X, _start.Y), current = e.GetPosition(_owner);
				double degrees = Math.Atan2(centre.Y - current.Y, current.X - centre.X) * 180 / Math.PI;
				double unit = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 15 : 1;
				SetAngle(_item, Math.Round(degrees / unit) * unit);
				e.Handled = true;
				return;
			}
			double scale = _owner.Canvas(_owner.Bounds.Size).Scale;
			if (scale <= 0) return;
			Vector delta = e.GetPosition(_owner) - start;
			SetPoint(_start.X + (int)Math.Round(delta.X / scale), _start.Y + (int)Math.Round(delta.Y / scale));
			e.Handled = true;
		}
		protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (_drag != null) { _drag = null; _pointer = null; e.Pointer.Capture(null); e.Handled = true; } }
		protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _drag = null; _pointer = null; }
		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (e.Key == Key.Escape && _drag != null) { if (IsAngle) SetAngle(_item, _startAngle); else SetPoint(_start.X, _start.Y); _drag = null; _pointer?.Capture(null); _pointer = null; e.Handled = true; return; }
			if (e.Key == Key.Delete) { _owner._state.SelectItem(_item); if (_owner._state.RemoveCommand.CanExecute(null)) _owner._state.RemoveCommand.Execute(null); e.Handled = true; return; }
			if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
			int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
			if (IsAngle)
			{
				int turn = e.Key switch { Key.Right or Key.Up => step, Key.Left or Key.Down => -step, _ => 0 };
				if (turn != 0) { SetAngle(_item, AngleOf(_item) + turn); e.Handled = true; }
				return;
			}
			var (x, y) = Position;
			switch (e.Key) { case Key.Left: x -= step; break; case Key.Right: x += step; break; case Key.Up: y -= step; break; case Key.Down: y += step; break; default: return; }
			_owner._state.SelectItem(_item); SetPoint(x, y); e.Handled = true;
		}
	}
}
