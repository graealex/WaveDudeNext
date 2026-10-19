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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// The sources, optical parts and probes of a <see cref="Project"/> drawn over a picture of the canvas, as draggable
/// handles (<see cref="MarkerThumb"/>): a source's position, a part's centre, a point probe's point, a line probe's two
/// ends with the line between them, plus the emitters of a composite source as dots, its rotation handle on
/// the row axis (shown while the source is selected; drag turns the array; Shift snaps to 15°; the arrow keys
/// turn it by a degree, 2026-10-14), the same handle on a part's axis (2026-10-18), and, while a part is selected, its
/// outline in the accent colour with a size grip on a corner or vertex (drag sets the extents) and a handle on each
/// point of an outline's or fibre's list (2026-10-19, the user's request), plus the item names as labels. Dragging a handle, or moving it with the arrow keys while it has keyboard focus, writes the
/// new canvas coordinates to the model; grabbing or focusing a handle makes its item the
/// <see cref="SelectedItem"/>. The overlay fills the same rectangle as the picture (an
/// <see cref="Image"/> with <see cref="Stretch.Uniform"/>), so it maps canvas pixels to its own
/// coordinates with the same uniform scale and centring. UI thread; listens to the model's change
/// notifications.
/// </summary>
public sealed class SceneOverlay : Panel
{
	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
		nameof(Project), typeof(Project), typeof(SceneOverlay), new PropertyMetadata(null, OnProjectChanged));

	/// <summary>Identifies the <see cref="CanvasWidth"/> property.</summary>
	public static readonly DependencyProperty CanvasWidthProperty = DependencyProperty.Register(
		nameof(CanvasWidth), typeof(int), typeof(SceneOverlay), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Identifies the <see cref="CanvasHeight"/> property.</summary>
	public static readonly DependencyProperty CanvasHeightProperty = DependencyProperty.Register(
		nameof(CanvasHeight), typeof(int), typeof(SceneOverlay), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Identifies the <see cref="SelectedItem"/> property.</summary>
	public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
		nameof(SelectedItem), typeof(object), typeof(SceneOverlay),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SceneOverlay)d).UpdateSelection()));

	/// <summary>Identifies the <see cref="Layer"/> property.</summary>
	public static readonly DependencyProperty LayerProperty = DependencyProperty.Register(
		nameof(Layer), typeof(ProbeLayer?), typeof(SceneOverlay), new PropertyMetadata(null, (d, _) => ((SceneOverlay)d).Rebuild()));

	/// <summary>Identifies the <see cref="SourceStyle"/> property.</summary>
	public static readonly DependencyProperty SourceStyleProperty = DependencyProperty.Register(
		nameof(SourceStyle), typeof(Style), typeof(SceneOverlay), new PropertyMetadata(null, (d, _) => ((SceneOverlay)d).Rebuild()));

	/// <summary>Identifies the <see cref="PartStyle"/> property.</summary>
	public static readonly DependencyProperty PartStyleProperty = DependencyProperty.Register(
		nameof(PartStyle), typeof(Style), typeof(SceneOverlay), new PropertyMetadata(null, (d, _) => ((SceneOverlay)d).Rebuild()));

	/// <summary>Identifies the <see cref="GripStyle"/> property.</summary>
	public static readonly DependencyProperty GripStyleProperty = DependencyProperty.Register(
		nameof(GripStyle), typeof(Style), typeof(SceneOverlay), new PropertyMetadata(null, (d, _) => ((SceneOverlay)d).Rebuild()));

	/// <summary>Identifies the <see cref="PointStyle"/> property.</summary>
	public static readonly DependencyProperty PointStyleProperty = DependencyProperty.Register(
		nameof(PointStyle), typeof(Style), typeof(SceneOverlay), new PropertyMetadata(null, (d, _) => ((SceneOverlay)d).Rebuild()));

	/// <summary>Identifies the <see cref="AngleStyle"/> property.</summary>
	public static readonly DependencyProperty AngleStyleProperty = DependencyProperty.Register(
		nameof(AngleStyle), typeof(Style), typeof(SceneOverlay), new PropertyMetadata(null, (d, _) => ((SceneOverlay)d).Rebuild()));

	/// <summary>Identifies the <see cref="ProbeStyle"/> property.</summary>
	public static readonly DependencyProperty ProbeStyleProperty = DependencyProperty.Register(
		nameof(ProbeStyle), typeof(Style), typeof(SceneOverlay), new PropertyMetadata(null, (d, _) => ((SceneOverlay)d).Rebuild()));

	/// <summary>Identifies the <see cref="LineBrush"/> property.</summary>
	public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
		nameof(LineBrush), typeof(Brush), typeof(SceneOverlay), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Identifies the <see cref="LabelBrush"/> property.</summary>
	public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
		nameof(LabelBrush), typeof(Brush), typeof(SceneOverlay), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Identifies the <see cref="AccentBrush"/> property.</summary>
	public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
		nameof(AccentBrush), typeof(Brush), typeof(SceneOverlay), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Pixels a focused handle moves per arrow key, and with Shift.</summary>
	private const int KeyStep = 1;
	private const int KeyStepFast = 10;
	private const double LabelOffset = 13.0;

	/// <summary>Least distance of a rotation handle from its source's centre, in pixels.</summary>
	private const double AngleHandleReach = 28.0;

	/// <summary>Room between the end of the first row and the rotation handle, in pixels.</summary>
	private const double AngleHandleGap = 14.0;
	private const double ElementDotRadius = 2.5;
	private static readonly Brush HaloBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)));
	private static readonly Typeface LabelTypeface = new("Segoe UI");

	private readonly HashSet<INotifyPropertyChanged> _subscribed = [];
	private readonly HashSet<INotifyCollectionChanged> _pointLists = [];
	private bool _regenerating;

	public SceneOverlay()
	{
		// no background: a click beside the handles falls through to the probe graph boxes under this layer
		// (and on to the picture), 2026-10-14
		IsHitTestVisible = true;
		Focusable = false;
	}

	/// <summary>The project whose sources and probes are shown; null shows nothing.</summary>
	public Project? Project
	{
		get => (Project?)GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>Canvas width in pixels, the x range of the model coordinates.</summary>
	public int CanvasWidth
	{
		get => (int)GetValue(CanvasWidthProperty);
		set => SetValue(CanvasWidthProperty, value);
	}

	/// <summary>Canvas height in pixels.</summary>
	public int CanvasHeight
	{
		get => (int)GetValue(CanvasHeightProperty);
		set => SetValue(CanvasHeightProperty, value);
	}

	/// <summary>The source, part or probe selected in the project (highlighted); set when a handle is grabbed or focused.</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>
	/// The picture's layer: only the probes measuring it get handles and lines here (2026-10-18, the user's rule: a probe
	/// on both pictures confused, above all in a single-picture view). Sources drive both layers and show on both.
	/// Null shows every probe.
	/// </summary>
	public ProbeLayer? Layer
	{
		get => (ProbeLayer?)GetValue(LayerProperty);
		set => SetValue(LayerProperty, value);
	}

	/// <summary>Whether <paramref name="probe"/> belongs on this picture (see <see cref="Layer"/>).</summary>
	private bool Shows(Probe probe) => Layer is not { } layer || probe.Layer == layer;

	/// <summary>Style of the source handles (a <see cref="MarkerThumb"/> style).</summary>
	public Style? SourceStyle
	{
		get => (Style?)GetValue(SourceStyleProperty);
		set => SetValue(SourceStyleProperty, value);
	}

	/// <summary>Style of the optical part handles (a <see cref="MarkerThumb"/> style).</summary>
	public Style? PartStyle
	{
		get => (Style?)GetValue(PartStyleProperty);
		set => SetValue(PartStyleProperty, value);
	}

	/// <summary>Style of a part's size grip (a <see cref="MarkerThumb"/> style).</summary>
	public Style? GripStyle
	{
		get => (Style?)GetValue(GripStyleProperty);
		set => SetValue(GripStyleProperty, value);
	}

	/// <summary>Style of the handles on an outline's or fibre's points (a <see cref="MarkerThumb"/> style).</summary>
	public Style? PointStyle
	{
		get => (Style?)GetValue(PointStyleProperty);
		set => SetValue(PointStyleProperty, value);
	}

	/// <summary>Style of the rotation handles of composite sources and parts (a <see cref="MarkerThumb"/> style).</summary>
	public Style? AngleStyle
	{
		get => (Style?)GetValue(AngleStyleProperty);
		set => SetValue(AngleStyleProperty, value);
	}

	/// <summary>Style of the probe handles (a <see cref="MarkerThumb"/> style).</summary>
	public Style? ProbeStyle
	{
		get => (Style?)GetValue(ProbeStyleProperty);
		set => SetValue(ProbeStyleProperty, value);
	}

	/// <summary>Line of a line probe and the emitter dots of a composite source.</summary>
	public Brush LineBrush
	{
		get => (Brush)GetValue(LineBrushProperty);
		set => SetValue(LineBrushProperty, value);
	}

	/// <summary>The item names next to the handles.</summary>
	public Brush LabelBrush
	{
		get => (Brush)GetValue(LabelBrushProperty);
		set => SetValue(LabelBrushProperty, value);
	}

	/// <summary>Line and label of the selected item.</summary>
	public Brush AccentBrush
	{
		get => (Brush)GetValue(AccentBrushProperty);
		set => SetValue(AccentBrushProperty, value);
	}

	// ---------------------------------------------------------------- model wiring

	private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var overlay = (SceneOverlay)d;
		if (e.OldValue is Project old)
		{
			old.Sources.CollectionChanged -= overlay.OnCollectionChanged;
			old.Parts.CollectionChanged -= overlay.OnCollectionChanged;
			old.Probes.CollectionChanged -= overlay.OnCollectionChanged;
		}
		if (e.NewValue is Project project)
		{
			project.Sources.CollectionChanged += overlay.OnCollectionChanged;
			project.Parts.CollectionChanged += overlay.OnCollectionChanged;
			project.Probes.CollectionChanged += overlay.OnCollectionChanged;
		}
		overlay.Rebuild();
	}

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

	/// <summary>Recreates the handles from the project's lists and re-subscribes to every item.</summary>
	private void Rebuild()
	{
		foreach (INotifyPropertyChanged item in _subscribed)
		{
			item.PropertyChanged -= OnItemPropertyChanged;
		}
		_subscribed.Clear();
		foreach (INotifyCollectionChanged list in _pointLists)
		{
			list.CollectionChanged -= OnCollectionChanged;
		}
		_pointLists.Clear();
		Children.Clear();
		if (Project is not { } project)
		{
			InvalidateVisual();
			return;
		}
		foreach (Source source in project.Sources)
		{
			Subscribe(source);
			if (source.Array != null)
			{
				Subscribe(source.Array);
			}
			Regenerate(source);
			AddThumb(source, MarkerRole.Source, SourceStyle, source.Name, "Source:" + source.Name);
			if (source.Array != null)
			{
				AddThumb(source, MarkerRole.SourceAngle, AngleStyle, source.Name, "SourceAngle:" + source.Name, "TipAngleMarkerFormat");
			}
		}
		foreach (OpticalPart part in project.Parts)
		{
			Subscribe(part);
			AddThumb(part, MarkerRole.Part, PartStyle, part.Name, "Part:" + part.Name);
			AddThumb(part, MarkerRole.PartAngle, AngleStyle, part.Name, "PartAngle:" + part.Name, "TipAngleMarkerFormat");
			if (GripOf(part) != null)
			{
				AddThumb(part, MarkerRole.PartGrip, GripStyle, part.Name, "PartGrip:" + part.Name, "TipGripFormat");
			}
			if (PointsOf(part) is { } points)
			{
				// the list and its points: a point added, removed or moved rebuilds or re-arranges
				if (_pointLists.Add(points))
				{
					points.CollectionChanged += OnCollectionChanged;
				}
				for (int i = 0; i < points.Count; i++)
				{
					Subscribe(points[i]);
					AddThumb(part, MarkerRole.PartPoint, PointStyle, part.Name, $"PartPoint:{part.Name}:{i}", "TipPointFormat", i);
				}
			}
		}
		foreach (Probe probe in project.Probes)
		{
			Subscribe(probe);
			if (!Shows(probe))
			{
				continue;
			}
			AddThumb(probe, MarkerRole.ProbeA, ProbeStyle, probe.Name, "Probe:" + probe.Name + ":A");
			if (probe.Kind == ProbeKind.Line)
			{
				AddThumb(probe, MarkerRole.ProbeB, ProbeStyle, probe.Name + " B", "Probe:" + probe.Name + ":B");
			}
		}
		UpdateSelection();
		InvalidateVisual();
	}

	private void Subscribe(INotifyPropertyChanged item)
	{
		if (_subscribed.Add(item))
		{
			item.PropertyChanged += OnItemPropertyChanged;
		}
	}

	private void AddThumb(object item, MarkerRole role, Style? style, string name, string automationId, string tipKey = "TipMarkerFormat", int index = -1)
	{
		var thumb = new MarkerThumb(item, role, index) { Style = style };
		thumb.ToolTip = TryFindResource(tipKey) is string format ? string.Format(CultureInfo.CurrentUICulture, format, name, index) : name;
		AutomationProperties.SetName(thumb, name);
		AutomationProperties.SetAutomationId(thumb, automationId);
		thumb.DragStarted += OnDragStarted;
		thumb.DragDelta += OnDragDelta;
		thumb.GotKeyboardFocus += (_, _) => SelectedItem = item;
		UpdateOpacity(thumb);
		Children.Add(thumb);
	}

	private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (sender)
		{
			case Probe when e.PropertyName is nameof(Probe.Kind) or nameof(Probe.Layer):
			case Source when e.PropertyName == nameof(Source.Array):
			case OutlinePart or FiberPart when e.PropertyName == "Points":
				Rebuild();
				return;
			case Source source:
				Regenerate(source);
				break;
			case SourceArray array when Project?.Sources.FirstOrDefault(s => ReferenceEquals(s.Array, array)) is { } owner:
				// a grid edit (size, spacing, rotation) moves the emitter dots at once
				Regenerate(owner);
				break;
		}
		if (e.PropertyName is nameof(Source.Name) or nameof(Source.Enabled) or nameof(Probe.Active))
		{
			Rebuild();
			return;
		}
		InvalidateArrange();
		InvalidateVisual();
	}

	/// <summary>Keeps a composite source's emitter dots current while it is edited (they are only regenerated for a run otherwise).</summary>
	private void Regenerate(Source source)
	{
		if (_regenerating || Project is not { } project || source.Array == null)
		{
			return;
		}
		_regenerating = true;
		try
		{
			source.GenerateElements(project.Seed);
		}
		finally
		{
			_regenerating = false;
		}
	}

	private void UpdateSelection()
	{
		object? selected = SelectedItem;
		foreach (MarkerThumb thumb in Children)
		{
			thumb.IsSelectedMarker = ReferenceEquals(thumb.Item, selected);
			if (thumb.Role is MarkerRole.SourceAngle or MarkerRole.PartAngle or MarkerRole.PartGrip or MarkerRole.PartPoint)
			{
				// the rotation handle, grip and point handles only while their source or part is selected (the user's rule, 2026-10-14); collapsed, they are out of the Tab cycle too
				thumb.Visibility = thumb.IsSelectedMarker ? Visibility.Visible : Visibility.Collapsed;
			}
		}
		InvalidateVisual();
	}

	/// <summary>The items with the selected one last, so what is drawn for it lies over the others (the user's rule, 2026-10-18).</summary>
	private static IEnumerable<T> SelectedLast<T>(IEnumerable<T> items, object? selected) where T : class =>
		items.OrderBy(item => ReferenceEquals(item, selected));

	private static void UpdateOpacity(MarkerThumb thumb) =>
		thumb.Opacity = thumb.Item switch { Source { Enabled: false } => 0.45, OpticalPart { Enabled: false } => 0.45, Probe { Active: false } => 0.45, _ => 1.0 };

	// ---------------------------------------------------------------- geometry

	/// <summary>
	/// Pixels per canvas pixel: the picture's uniform scale inside this element. Every mapping takes the
	/// element size explicitly, because during <see cref="ArrangeOverride"/> <see cref="FrameworkElement.ActualWidth"/>
	/// still holds the previous layout's size (WPF sets <see cref="UIElement.RenderSize"/> after arrange);
	/// placing the handles from it left them where the picture used to be after every resize, while the
	/// labels drawn in <see cref="OnRender"/> moved with it (2026-10-10).
	/// </summary>
	private double ScaleFor(Size size) => Math.Min(size.Width / Math.Max(1, CanvasWidth), size.Height / Math.Max(1, CanvasHeight));

	/// <summary>The current scale, for rendering and dragging (after layout).</summary>
	private double Scale => ScaleFor(RenderSize);

	/// <summary>The centre of canvas pixel (x, y) in this element's coordinates, for an element of <paramref name="size"/>; the picture is centred in it.</summary>
	private Point ToView(int x, int y, Size size)
	{
		double scale = ScaleFor(size);
		double originX = (size.Width - CanvasWidth * scale) / 2;
		double originY = (size.Height - CanvasHeight * scale) / 2;
		return new Point(originX + (x + 0.5) * scale, originY + (y + 0.5) * scale);
	}

	/// <summary>The centre of canvas pixel (x, y) in this element's coordinates at its current size.</summary>
	private Point ToView(int x, int y) => ToView(x, y, RenderSize);

	/// <summary>A continuous canvas point (pixel (x, y) spanning [x, x + 1)) in this element's coordinates, for an element of <paramref name="size"/>.</summary>
	private Point ToViewExact(double x, double y, Size size)
	{
		double scale = ScaleFor(size);
		double originX = (size.Width - CanvasWidth * scale) / 2;
		double originY = (size.Height - CanvasHeight * scale) / 2;
		return new Point(originX + x * scale, originY + y * scale);
	}

	// ---------------------------------------------------------------- a part's grip and points

	/// <summary>The point list a part's handles edit: an outline's or a fibre's; null for the other kinds.</summary>
	private static ObservableCollection<PartPoint>? PointsOf(OpticalPart part) => part switch
	{
		OutlinePart outline => outline.Points,
		FiberPart fiber => fiber.Points,
		_ => null,
	};

	/// <summary>
	/// Where a part's size grip sits in the part frame: the corner of a rectangle, lens (thickness, diameter), GRIN lens
	/// or Bragg stack, the first vertex of a polygon, the second end of a triangle's base; null for a kind without one.
	/// </summary>
	private static (double X, double Y)? GripOf(OpticalPart part) => part switch
	{
		RectanglePart rectangle => (rectangle.Length / 2.0, rectangle.Width / 2.0),
		LensPart lens => (lens.Thickness / 2.0, lens.Diameter / 2.0),
		PolygonPart polygon => polygon.Vertices()[0],
		TrianglePart triangle => triangle.Vertices()[1],
		GrinLensPart grin => (grin.Length / 2.0, grin.Diameter / 2.0),
		BraggReflectorPart bragg => (bragg.Length / 2.0, bragg.Width / 2.0),
		_ => null,
	};

	/// <summary>Sets the extents a grip stands for from the grip's new canvas position: whole pixels, never below the kind's least size.</summary>
	private static void SetGrip(OpticalPart part, double canvasX, double canvasY)
	{
		(double x, double y) = part.ToFrame(canvasX, canvasY);
		int across = (int)Math.Round(2.0 * Math.Abs(y));
		int along = (int)Math.Round(2.0 * Math.Abs(x));
		switch (part)
		{
			case RectanglePart rectangle:
				rectangle.Length = Math.Max(1, along);
				rectangle.Width = Math.Max(1, across);
				break;
			case LensPart lens:
				lens.Thickness = Math.Max(1, along);
				lens.Diameter = Math.Max(2, across);
				break;
			case PolygonPart polygon:
				// the vertex stays on its ray; its distance is the circumradius
				polygon.Side = Math.Max(2, (int)Math.Round(2.0 * Math.Sqrt(x * x + y * y) * Math.Sin(Math.PI / Math.Clamp(polygon.Sides, PolygonPart.MinSides, PolygonPart.MaxSides))));
				break;
			case TrianglePart triangle:
				triangle.Base = Math.Max(2, across);
				break;
			case GrinLensPart grin:
				grin.Length = Math.Max(1, along);
				grin.Diameter = Math.Max(2, across);
				break;
			case BraggReflectorPart bragg:
				bragg.Width = Math.Max(2, across);
				break;
		}
	}

	/// <summary>The canvas position of a grip or point handle (continuous coordinates), or null when the handle has nothing to stand on.</summary>
	private static (double X, double Y)? CanvasOf(MarkerThumb thumb) => (thumb.Item, thumb.Role) switch
	{
		(OpticalPart part, MarkerRole.PartGrip) when GripOf(part) is { } grip => part.ToCanvas(grip.X, grip.Y),
		(OpticalPart part, MarkerRole.PartPoint) when PointsOf(part) is { } points && thumb.Index >= 0 && thumb.Index < points.Count => part.ToCanvas(points[thumb.Index].X, points[thumb.Index].Y),
		_ => null,
	};

	/// <summary>The picture's rectangle in this element's coordinates for an element of <paramref name="size"/>: the canvas at its uniform scale, centred (the element fills the cell, the picture is letterboxed in it).</summary>
	private Rect PictureRect(Size size)
	{
		double scale = ScaleFor(size);
		double width = CanvasWidth * scale, height = CanvasHeight * scale;
		return new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
	}

	/// <summary>
	/// Where the rotation handle of a composite source or a part sits, for an element of <paramref name="size"/>: on
	/// the array's row axis or the part's axis, turned by its angle (counter-clockwise on the screen), just beyond the
	/// first row's half-width or the part's reach along the axis, and at least <see cref="AngleHandleReach"/> from the centre.
	/// </summary>
	private Point AnglePoint(object item, Size size)
	{
		(int x, int y, double angle, double extent) = item switch
		{
			Source { Array: { } array } source => (source.X, source.Y, array.Angle, (array.SizeX - 1) / 2.0 * array.SpacingX),
			OpticalPart part => (part.X, part.Y, part.Angle, part.AxisReach),
			_ => (0, 0, 0.0, 0.0),
		};
		Point centre = ToView(x, y, size);
		double reach = Math.Max(AngleHandleReach, extent * ScaleFor(size) + AngleHandleGap);
		double radians = angle * Math.PI / 180.0;
		return new Point(centre.X + reach * Math.Cos(radians), centre.Y - reach * Math.Sin(radians));
	}

	/// <summary>The angle of a composite source's array or a part, in degrees.</summary>
	private static double AngleOf(object item) => item switch
	{
		Source source => source.Array?.Angle ?? 0.0,
		OpticalPart part => part.Angle,
		_ => 0.0,
	};

	/// <summary>Turns the array or part so its axis points from its centre to <paramref name="view"/>: to whole degrees, or to the nearest 15 with <paramref name="snap"/>.</summary>
	private void SetAngle(object item, Point view, bool snap)
	{
		(int x, int y) = item switch { Source source => (source.X, source.Y), OpticalPart part => (part.X, part.Y), _ => (0, 0) };
		Point centre = ToView(x, y);
		double degrees = Math.Atan2(-(view.Y - centre.Y), view.X - centre.X) * 180.0 / Math.PI;
		double unit = snap ? 15.0 : 1.0;
		SetAngle(item, Math.Round(degrees / unit) * unit);
	}

	/// <summary>Sets the array's or part's angle, kept in (-180, 180].</summary>
	private static void SetAngle(object item, double degrees)
	{
		switch (item)
		{
			case Source { Array: { } array }:
				array.Angle = degrees;
				array.Normalize();
				break;
			case OpticalPart part:
				part.Angle = degrees;
				part.Normalize();
				break;
		}
	}

	private static (int X, int Y) PointOf(MarkerThumb thumb) => (thumb.Item, thumb.Role) switch
	{
		(Source source, _) => (source.X, source.Y),
		(OpticalPart, MarkerRole.PartGrip or MarkerRole.PartPoint) when CanvasOf(thumb) is { } canvas => ((int)Math.Round(canvas.X), (int)Math.Round(canvas.Y)),
		(OpticalPart part, _) => (part.X, part.Y),
		(Probe probe, MarkerRole.ProbeB) => (probe.BX, probe.BY),
		(Probe probe, _) => (probe.AX, probe.AY),
		_ => (0, 0),
	};

	private void SetPoint(MarkerThumb thumb, int x, int y)
	{
		x = Math.Clamp(x, 0, Math.Max(0, CanvasWidth - 1));
		y = Math.Clamp(y, 0, Math.Max(0, CanvasHeight - 1));
		switch (thumb.Item, thumb.Role)
		{
			case (Source source, _):
				source.X = x;
				source.Y = y;
				break;
			case (OpticalPart part, MarkerRole.PartGrip):
				SetGrip(part, x, y);
				break;
			case (OpticalPart part, MarkerRole.PartPoint):
				if (PointsOf(part) is { } points && thumb.Index >= 0 && thumb.Index < points.Count)
				{
					(double fx, double fy) = part.ToFrame(x, y);
					points[thumb.Index].X = (int)Math.Round(fx);
					points[thumb.Index].Y = (int)Math.Round(fy);
				}
				break;
			case (OpticalPart part, _):
				part.X = x;
				part.Y = y;
				break;
			case (Probe probe, MarkerRole.ProbeB):
				probe.BX = x;
				probe.BY = y;
				break;
			case (Probe probe, _):
				probe.AX = x;
				probe.AY = y;
				// a probe dragged onto its (detached) graph box picks it up, as the box does when dragged onto the probe
				if (!probe.IsBoxAttached)
				{
					double scale = Scale;
					double dx = (probe.BoxX - x) * scale;
					double dy = (probe.BoxY - y) * scale;
					if (dx * dx + dy * dy <= ProbeGraphOverlay.SnapRadius * ProbeGraphOverlay.SnapRadius)
					{
						probe.BoxX = x;
						probe.BoxY = y;
					}
				}
				break;
		}
	}

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
	{
		foreach (UIElement child in Children)
		{
			child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
		}
		return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
	}

	/// <inheritdoc/>
	protected override Size ArrangeOverride(Size finalSize)
	{
		Rect picture = PictureRect(finalSize);
		foreach (MarkerThumb thumb in Children)
		{
			(int x, int y) = PointOf(thumb);
			Point centre = thumb.Role switch
			{
				MarkerRole.SourceAngle or MarkerRole.PartAngle => AnglePoint(thumb.Item, finalSize),
				MarkerRole.PartGrip or MarkerRole.PartPoint when CanvasOf(thumb) is { } canvas => ToViewExact(canvas.X, canvas.Y, finalSize),
				_ => ToView(x, y, finalSize),
			};
			Size size = thumb.DesiredSize;
			var rect = new Rect(centre.X - size.Width / 2, centre.Y - size.Height / 2, size.Width, size.Height);
			thumb.Arrange(rect);
			if (thumb.Role is MarkerRole.SourceAngle or MarkerRole.PartAngle or MarkerRole.PartGrip or MarkerRole.PartPoint)
			{
				// the rotation handle stays on the row axis, however far out; what falls outside the picture is clipped
				// (2026-10-16, the user's rule: the lever bending to keep the handle inside made no sense). The other
				// handles sit on clamped canvas positions and show whole at the edge
				Rect visible = Rect.Intersect(picture, rect);
				thumb.Clip = visible == rect ? null : new RectangleGeometry(new Rect(visible.X - rect.X, visible.Y - rect.Y, Math.Max(0, visible.Width), Math.Max(0, visible.Height)));
			}
		}
		return finalSize;
	}

	/// <inheritdoc/>
	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		if (Project is not { } project || ActualWidth <= 0 || ActualHeight <= 0)
		{
			return;
		}
		object? selected = SelectedItem;
		// lines and dots stay within the picture (2026-10-16, after a rotated array dragged to the border put its
		// emitter dots over the menu bar): an emitter or a line end outside the canvas is clipped here, the handles
		// are children and sit on clamped positions, and the labels below are kept inside by their placement. The
		// picture's rectangle, not this element's: the element fills the cell and the picture is letterboxed in it
		dc.PushClip(new RectangleGeometry(PictureRect(RenderSize)));
		foreach (Probe probe in SelectedLast(project.Probes, selected))
		{
			if (probe.Kind != ProbeKind.Line || !Shows(probe))
			{
				continue;
			}
			bool isSelected = ReferenceEquals(probe, selected);
			Point a = ToView(probe.AX, probe.AY);
			Point b = ToView(probe.BX, probe.BY);
			// a dark line under the dashed light one, so the line reads on any picture
			dc.DrawLine(new Pen(HaloBrush, isSelected ? 3.5 : 3.0), a, b);
			dc.DrawLine(new Pen(isSelected ? AccentBrush : LineBrush, isSelected ? 1.5 : 1.0) { DashStyle = DashStyles.Dash }, a, b);
		}
		foreach (Source source in SelectedLast(project.Sources, selected))
		{
			if (source.Array == null)
			{
				continue;
			}
			bool isSelected = ReferenceEquals(source, selected);
			Brush dots = isSelected ? AccentBrush : LineBrush;
			var halo = new Pen(HaloBrush, 1.5);
			if (isSelected)
			{
				// the row axis from the centre to the rotation handle, so the turn reads as a lever; with the handle, only while selected
				Point axisEnd = AnglePoint(source, RenderSize);
				dc.DrawLine(new Pen(HaloBrush, 3.0), ToView(source.X, source.Y), axisEnd);
				dc.DrawLine(new Pen(dots, 1.0) { DashStyle = DashStyles.Dot }, ToView(source.X, source.Y), axisEnd);
			}
			foreach (SourceElement element in source.Elements)
			{
				dc.DrawEllipse(dots, halo, ToView(element.X, element.Y), ElementDotRadius, ElementDotRadius);
			}
		}
		if (selected is OpticalPart selectedPart && project.Parts.Contains(selectedPart))
		{
			// the part's outline as painted, in the accent colour, so the shape reads apart from the scene's glow
			(double X, double Y)[] outline = selectedPart.Outline();
			if (outline.Length >= 2)
			{
				var geometry = new StreamGeometry();
				using (StreamGeometryContext context = geometry.Open())
				{
					(double x0, double y0) = selectedPart.ToCanvas(outline[0].X, outline[0].Y);
					context.BeginFigure(ToViewExact(x0, y0, RenderSize), false, selectedPart.OutlineClosed);
					for (int i = 1; i < outline.Length; i++)
					{
						(double x, double y) = selectedPart.ToCanvas(outline[i].X, outline[i].Y);
						context.LineTo(ToViewExact(x, y, RenderSize), true, true);
					}
				}
				geometry.Freeze();
				dc.DrawGeometry(null, new Pen(HaloBrush, 3.0) { LineJoin = PenLineJoin.Round }, geometry);
				dc.DrawGeometry(null, new Pen(AccentBrush, 1.0) { LineJoin = PenLineJoin.Round }, geometry);
			}
			// the part's axis from the centre to its rotation handle, the lever, while it is selected
			Point axisEnd = AnglePoint(selectedPart, RenderSize);
			dc.DrawLine(new Pen(HaloBrush, 3.0), ToView(selectedPart.X, selectedPart.Y), axisEnd);
			dc.DrawLine(new Pen(AccentBrush, 1.0) { DashStyle = DashStyles.Dot }, ToView(selectedPart.X, selectedPart.Y), axisEnd);
		}
		dc.Pop();
		foreach (MarkerThumb thumb in Children.OfType<MarkerThumb>().OrderBy(thumb => thumb.IsSelectedMarker))
		{
			if (thumb.Role is MarkerRole.ProbeB or MarkerRole.SourceAngle or MarkerRole.PartAngle or MarkerRole.PartGrip or MarkerRole.PartPoint)
			{
				continue;
			}
			(int x, int y) = PointOf(thumb);
			Point centre = ToView(x, y);
			string name = thumb.Item switch { Source source => source.Name, OpticalPart part => part.Name, _ => ((Probe)thumb.Item).Name };
			var label = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelTypeface, 11, thumb.IsSelectedMarker ? AccentBrush : LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
			// beside the point; below it for a composite source or a part, whose rotation handle sits beside it at 0 degrees
			bool below = thumb.Item is Source { Array: not null } or OpticalPart;
			double lx = below ? Math.Clamp(centre.X - label.Width / 2, 0, Math.Max(0, ActualWidth - label.Width)) : Math.Min(centre.X + LabelOffset, Math.Max(0, ActualWidth - label.Width));
			double ly = below ? Math.Min(centre.Y + LabelOffset, Math.Max(0, ActualHeight - label.Height)) : Math.Clamp(centre.Y - label.Height / 2, 0, Math.Max(0, ActualHeight - label.Height));
			var origin = new Point(lx, ly);
			// the text's outline in dark first (a halo), then the text
			dc.DrawGeometry(null, new Pen(HaloBrush, 3.0) { LineJoin = PenLineJoin.Round }, label.BuildGeometry(origin));
			dc.DrawText(label, origin);
		}
	}

	// ---------------------------------------------------------------- dragging and keys

	private void OnDragStarted(object sender, DragStartedEventArgs e)
	{
		var thumb = (MarkerThumb)sender;
		(thumb.DragStartX, thumb.DragStartY) = PointOf(thumb);
		thumb.DragDeltaX = 0;
		thumb.DragDeltaY = 0;
		SelectedItem = thumb.Item;
		thumb.Focus();
	}

	private void OnDragDelta(object sender, DragDeltaEventArgs e)
	{
		var thumb = (MarkerThumb)sender;
		double scale = Scale;
		if (scale <= 0)
		{
			return;
		}
		if (thumb.Role is MarkerRole.SourceAngle or MarkerRole.PartAngle)
		{
			// the handle follows the mouse around the centre; Shift snaps to 15 degrees
			SetAngle(thumb.Item, Mouse.GetPosition(this), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
			return;
		}
		thumb.DragDeltaX += e.HorizontalChange / scale;
		thumb.DragDeltaY += e.VerticalChange / scale;
		SetPoint(thumb, thumb.DragStartX + (int)Math.Round(thumb.DragDeltaX), thumb.DragStartY + (int)Math.Round(thumb.DragDeltaY));
	}

	private static Brush Freeze(Brush brush)
	{
		brush.Freeze();
		return brush;
	}

	/// <summary>The arrow keys move the focused handle by one canvas pixel, ten with Shift; on a rotation handle they turn the array or part by a degree (Right and Up counter-clockwise), ten with Shift.</summary>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.Handled || e.OriginalSource is not MarkerThumb thumb)
		{
			return;
		}
		int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? KeyStepFast : KeyStep;
		if (thumb.Role is MarkerRole.SourceAngle or MarkerRole.PartAngle)
		{
			int turn = e.Key switch { Key.Right or Key.Up => step, Key.Left or Key.Down => -step, _ => 0 };
			if (turn != 0)
			{
				SetAngle(thumb.Item, AngleOf(thumb.Item) + turn);
				e.Handled = true;
			}
			return;
		}
		(int dx, int dy) = e.Key switch
		{
			Key.Left => (-step, 0),
			Key.Right => (step, 0),
			Key.Up => (0, -step),
			Key.Down => (0, step),
			_ => (0, 0),
		};
		if (dx == 0 && dy == 0)
		{
			return;
		}
		(int x, int y) = PointOf(thumb);
		SetPoint(thumb, x + dx, y + dy);
		e.Handled = true;
	}
}
