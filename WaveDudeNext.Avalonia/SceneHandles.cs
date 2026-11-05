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
	private readonly Dictionary<(object, MarkerRole, int), Marker> _markers = new();
	private readonly Dictionary<Source, IReadOnlyList<SourceElement>> _emitters = new();
	private enum MarkerRole { Centre, ProbeEnd, Angle, Grip, Surface1, Surface2, Point, FiberIn, FiberOut }
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
		if (e.PropertyName == nameof(WindowStateModel.Project)) _emitters.Clear();
		if (e.PropertyName is nameof(WindowStateModel.Project) or nameof(WindowStateModel.SelectedItem) or nameof(WindowStateModel.CanvasWidth) or nameof(WindowStateModel.CanvasHeight)) Refresh();
	}
	private void SceneChanged(object? sender, ProjectChange change)
	{
		if (change.Item is Source source) _emitters.Remove(source);
		else if (change.Item is SourceArray array)
		{
			Source? owner = _state.Project.Sources.FirstOrDefault(source => ReferenceEquals(source.Array, array));
			if (owner != null) _emitters.Remove(owner);
		}
		else if (ReferenceEquals(change.Item, _state.Project) && change.Property == nameof(Project.Seed)) _emitters.Clear();
		Refresh();
	}
	private void Refresh()
	{
		foreach (Source source in _emitters.Keys.Where(source => source.Array == null || !_state.Project.Sources.Contains(source)).ToArray()) _emitters.Remove(source);
		foreach (Source source in _state.Project.Sources.Where(source => source.Array != null && !_emitters.ContainsKey(source)))
		{
			// Generate on a copy: the preview must not normalise or modify the live project.
			Source copy = source.Clone();
			copy.GenerateElements(_state.Project.Seed);
			_emitters[source] = copy.Elements;
		}
		var wanted = new List<(object, MarkerRole, int)>();
		wanted.AddRange(_state.Project.Sources.Select(s => ((object)s, MarkerRole.Centre, 0)));
		wanted.AddRange(_state.Project.Parts.Select(p => ((object)p, MarkerRole.Centre, 0)));
		foreach (Probe probe in _state.Project.Probes.Where(p => p.Layer == _layer))
		{
			wanted.Add((probe, MarkerRole.Centre, 0));
			if (probe.Kind == ProbeKind.Line) wanted.Add((probe, MarkerRole.ProbeEnd, 0));
		}
		if (_state.SelectedItem is OpticalPart or Source { Array: not null }) wanted.Add((_state.SelectedItem, MarkerRole.Angle, 0));
		if (_state.SelectedItem is OpticalPart selectedPart && GripOf(selectedPart) != null) wanted.Add((selectedPart, MarkerRole.Grip, 0));
		if (_state.SelectedItem is LensPart lens)
		{
			wanted.Add((lens, MarkerRole.Surface1, 0));
			wanted.Add((lens, MarkerRole.Surface2, 0));
		}
		if (_state.SelectedItem is OutlinePart outline)
			for (int i = 0; i < outline.Points.Count; i++) wanted.Add((outline, MarkerRole.Point, i));
		if (_state.SelectedItem is FiberPart fiber)
		{
			for (int i = 0; i < fiber.Nodes.Count; i++)
			{
				if (i > 0) wanted.Add((fiber, MarkerRole.FiberIn, i));
				if (i < fiber.Nodes.Count - 1) wanted.Add((fiber, MarkerRole.FiberOut, i));
				wanted.Add((fiber, MarkerRole.Point, i));
			}
		}
		foreach (var key in _markers.Keys.Except(wanted).ToArray())
		{
			Children.Remove(_markers[key]);
			_markers.Remove(key);
		}
		foreach (var key in wanted)
		{
			if (!_markers.TryGetValue(key, out Marker? marker))
			{
				marker = new Marker(this, key.Item1, key.Item2, key.Item3);
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
			Point centre = marker.IsAngle ? AnglePoint(marker.Item, finalSize) : marker.HasDetailPosition ? ViewDetail(marker.DetailPosition, rect, scale) : new Point(rect.X + (x + .5) * scale, rect.Y + (y + .5) * scale);
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
	private static (double X, double Y)? GripOf(OpticalPart part) => part switch
	{
		RectanglePart p => (p.Length / 2.0, p.Width / 2.0),
		EllipsePart p => (p.Length / 2.0, p.Width / 2.0),
		LensPart p => (p.Thickness / 2.0, p.Diameter / 2.0),
		PolygonPart p => p.Vertices()[0],
		TrianglePart p => p.Vertices()[1],
		GrinLensPart p => (p.Length / 2.0, p.Diameter / 2.0),
		BraggReflectorPart p => (p.Length / 2.0, p.Width / 2.0),
		ZonePlatePart p => (p.Thickness / 2.0, p.Diameter / 2.0),
		_ => null,
	};
	private static (double X, double Y) GripCanvas(OpticalPart part)
	{
		var (x, y) = GripOf(part) ?? default;
		return part.ToCanvas(x, y);
	}
	private static Point ViewDetail((double X, double Y) position, Rect rect, double scale)
	{
		var (x, y) = position;
		return new Point(rect.X + x * scale, rect.Y + y * scale);
	}
	private static void SetGrip(OpticalPart part, int canvasX, int canvasY)
	{
		var (x, y) = part.ToFrame(canvasX, canvasY);
		int across = (int)Math.Round(2 * Math.Abs(y)), along = (int)Math.Round(2 * Math.Abs(x));
		switch (part)
		{
			case RectanglePart p: p.Length = Math.Max(1, p.Linked ? Math.Max(along, across) : along); p.Width = Math.Max(1, p.Linked ? Math.Max(along, across) : across); break;
			case EllipsePart p: p.Length = Math.Max(1, p.Linked ? Math.Max(along, across) : along); p.Width = Math.Max(1, p.Linked ? Math.Max(along, across) : across); break;
			case LensPart p: p.SetExtents(across, along); break;
			case PolygonPart p: p.Side = Math.Max(2, (int)Math.Round(2 * Math.Sqrt(x * x + y * y) * Math.Sin(Math.PI / Math.Clamp(p.Sides, PolygonPart.MinSides, PolygonPart.MaxSides)))); break;
			case TrianglePart p: p.Base = Math.Max(2, across); break;
			case GrinLensPart p: p.Length = Math.Max(1, along); p.Diameter = Math.Max(2, across); break;
			case BraggReflectorPart p: p.Width = Math.Max(2, across); break;
			case ZonePlatePart p: p.Thickness = Math.Max(1, along); p.Diameter = Math.Max(2, across); break;
		}
	}
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
		foreach (Source source in _state.Project.Sources.Where(s => s.Array != null && (display.HandlesInPreview || ReferenceEquals(s, selected))).OrderBy(s => ReferenceEquals(s, selected)))
		{
			if (!_emitters.TryGetValue(source, out IReadOnlyList<SourceElement>? elements)) continue;
			IBrush? brush = ReferenceEquals(source, selected) ? SelectionBrush : HandleBrush;
			var halo = new Pen(Halo, 1.5);
			foreach (SourceElement element in elements) context.DrawEllipse(brush, halo, ViewPoint(element.X, element.Y), 2.5, 2.5);
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
		if (selected is FiberPart fiber)
		{
			var (rect, scale) = Canvas(Bounds.Size);
			var halo = new Pen(Halo, 3);
			var line = new Pen(SelectionBrush, 1, dashStyle: DashStyle.Dot);
			for (int i = 0; i < fiber.Nodes.Count; i++)
			{
				FiberNode node = fiber.Nodes[i];
				Point centre = ViewDetail(fiber.ToCanvas(node.X, node.Y), rect, scale);
				void DrawHandle((double X, double Y) position)
				{
					Point end = ViewDetail(fiber.ToCanvas(position.X, position.Y), rect, scale);
					context.DrawLine(halo, centre, end);
					context.DrawLine(line, centre, end);
				}
				if (i > 0) DrawHandle(node.In);
				if (i < fiber.Nodes.Count - 1) DrawHandle(node.Out);
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
		private readonly int _index;
		private Point? _drag;
		private IPointer? _pointer;
		private (int X, int Y) _start;
		private (double X, double Y) _gripStart;
		private (double X, double Y) _rimAnchor;
		private (double X, double Y) _otherAnchor;
		private double? _mirrorLength;
		private double _startAngle;
		public Marker(SceneHandles owner, object item, MarkerRole role, int index) { _owner = owner; _item = item; _role = role; _index = index; Focusable = true; }
		public object Item => _item;
		public bool IsAngle => _role == MarkerRole.Angle;
		public bool IsGrip => _role == MarkerRole.Grip;
		public bool IsSurface => _role is MarkerRole.Surface1 or MarkerRole.Surface2;
		private int Surface => _role == MarkerRole.Surface1 ? 1 : 2;
		public bool IsPoint => _role == MarkerRole.Point;
		private bool IsFiberHandle => _role is MarkerRole.FiberIn or MarkerRole.FiberOut;
		public bool HasDetailPosition => IsGrip || IsSurface || IsPoint || IsFiberHandle;
		private FiberNode? Node => _item is FiberPart fiber && _index < fiber.Nodes.Count ? fiber.Nodes[_index] : null;
		public (double X, double Y) DetailPosition => _item switch
		{
			LensPart lens when IsSurface => lens.ToCanvas(lens.VertexX(Surface), 0),
			OutlinePart part when IsPoint && _index < part.Points.Count => part.ToCanvas(part.Points[_index].X, part.Points[_index].Y),
			FiberPart part when Node is { } node && IsPoint => part.ToCanvas(node.X, node.Y),
			FiberPart part when Node is { } node && IsFiberHandle => part.ToCanvas(_role == MarkerRole.FiberOut ? node.Out.X : node.In.X, _role == MarkerRole.FiberOut ? node.Out.Y : node.In.Y),
			OpticalPart part when IsGrip => GripCanvas(part),
			_ => default,
		};
		public (int X, int Y) Position => _item switch
		{
			Source s => (s.X, s.Y), OpticalPart when HasDetailPosition => ((int)Math.Round(DetailPosition.X), (int)Math.Round(DetailPosition.Y)), OpticalPart p => (p.X, p.Y), Probe p when _role == MarkerRole.ProbeEnd => (p.BX, p.BY), Probe p => (p.AX, p.AY), _ => default
		};
		public void UpdateName()
		{
			string name = ((WaveDudeNext.Model.INamed)_item).Name;
			string id = _item switch { Source when IsAngle => $"SourceAngle:{name}", OpticalPart when IsAngle => $"PartAngle:{name}", OpticalPart when IsGrip => $"PartGrip:{name}", LensPart when IsSurface => $"PartSurface:{name}:{Surface}", OpticalPart when IsPoint => $"PartPoint:{name}:{_index}", FiberPart when IsFiberHandle => $"{(_role == MarkerRole.FiberIn ? "FiberIn" : "FiberOut")}:{name}:{_index}", Source => $"Source:{name}", OpticalPart => $"Part:{name}", _ => $"Probe:{name}:{(_role == MarkerRole.ProbeEnd ? "B" : "A")}" };
			AutomationProperties.SetAutomationId(this, id);
			AutomationProperties.SetName(this, id);
			string tip = IsAngle ? "TipAngleMarkerFormat" : IsGrip ? "TipGripFormat" : IsSurface ? "TipSurfaceFormat" : IsPoint && _item is FiberPart ? "TipNodeFormat" : IsPoint ? "TipPointFormat" : _role == MarkerRole.FiberIn ? "TipInHandleFormat" : _role == MarkerRole.FiberOut ? "TipOutHandleFormat" : "TipMarkerFormat";
			string format = Application.Current?.Resources.TryGetValue(tip, out object? value) == true && value is string text ? text : "{0}";
			ToolTip.SetTip(this, string.Format(CultureInfo.CurrentCulture, format, name, IsPoint || IsFiberHandle ? _index : Surface));
		}
		private void SetPoint(int x, int y)
		{
			x = Math.Clamp(x, 0, Math.Max(0, _owner._state.CanvasWidth - 1));
			y = Math.Clamp(y, 0, Math.Max(0, _owner._state.CanvasHeight - 1));
			switch (_item)
			{
				case Source s: s.X = x; s.Y = y; break;
				case LensPart p when IsSurface:
					(double axisX, _) = p.ToFrame(x, y);
					if (_drag != null) p.SetVertex(Surface, axisX, p.ToFrame(_rimAnchor.X, _rimAnchor.Y).X, p.ToFrame(_otherAnchor.X, _otherAnchor.Y).X);
					else p.SetVertex(Surface, axisX);
					break;
				case OutlinePart p when IsPoint && _index < p.Points.Count:
					(double pointX, double pointY) = p.ToFrame(x, y);
					p.Points[_index].X = (int)Math.Round(pointX); p.Points[_index].Y = (int)Math.Round(pointY); break;
				case FiberPart p when Node is { } node && (IsPoint || IsFiberHandle):
					(double nodeX, double nodeY) = p.ToFrame(x, y);
					if (IsPoint) { node.X = (int)Math.Round(nodeX); node.Y = (int)Math.Round(nodeY); }
					else node.SetHandle(_role == MarkerRole.FiberOut, (int)Math.Round(nodeX) - node.X, (int)Math.Round(nodeY) - node.Y, _drag != null ? _mirrorLength : null);
					break;
				case OpticalPart p when IsGrip: SetGrip(p, x, y); break;
				case OpticalPart p: p.X = x; p.Y = y; break;
				case Probe p when _role == MarkerRole.ProbeEnd: p.BX = x; p.BY = y; break;
				case Probe p: p.AX = x; p.AY = y; break;
			}
		}
		public override void Render(DrawingContext context)
		{
			IBrush? brush = ReferenceEquals(_item, _owner._state.SelectedItem) ? _owner.SelectionBrush : _owner.HandleBrush;
			context.DrawEllipse(Halo, null, new Point(7, 7), 6, 6);
			if (IsGrip) context.DrawRectangle(null, new Pen(brush, IsFocused ? 2 : 1), new Rect(3, 3, 8, 8));
			else context.DrawEllipse(null, new Pen(brush, IsFocused ? 2 : 1), new Point(7, 7), 4, 4);
		}
		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
		{
			base.OnPropertyChanged(change);
			if (change.Property == IsFocusedProperty)
			{
				if (IsFocused) _owner._state.SelectItem(_item);
				InvalidateVisual();
			}
		}
		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
			_owner._state.SelectItem(_item); Focus(); _start = Position; _startAngle = AngleOf(_item); if (HasDetailPosition) _gripStart = DetailPosition;
			if (_item is LensPart lens && IsSurface) { _rimAnchor = lens.ToCanvas(lens.RimX(Surface), 0); _otherAnchor = lens.ToCanvas(lens.VertexX(Surface == 1 ? 2 : 1), 0); }
			if (IsFiberHandle && Node is { } node) { int x = _role == MarkerRole.FiberOut ? node.InX : node.OutX, y = _role == MarkerRole.FiberOut ? node.InY : node.OutY; _mirrorLength = Math.Sqrt((double)x * x + (double)y * y); }
			_drag = e.GetPosition(_owner); _pointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true;
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
			if (HasDetailPosition) SetPoint((int)Math.Round(_gripStart.X + delta.X / scale), (int)Math.Round(_gripStart.Y + delta.Y / scale));
			else SetPoint(_start.X + (int)Math.Round(delta.X / scale), _start.Y + (int)Math.Round(delta.Y / scale));
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
