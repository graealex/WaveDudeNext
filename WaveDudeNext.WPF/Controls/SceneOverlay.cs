// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

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
/// The sources and probes of a <see cref="Project"/> drawn over a picture of the canvas, as draggable
/// handles (<see cref="MarkerThumb"/>): a source's position, a point probe's point, a line probe's two
/// ends with the line between them, plus the emitters of a composite source as dots, its rotation handle on
/// the row axis (shown while the source is selected; drag turns the array; Shift snaps to 15°; the arrow keys
/// turn it by a degree, 2026-10-14) and the item names as labels. Dragging a handle, or moving it with the arrow keys while it has keyboard focus, writes the
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

	/// <summary>The source or probe selected in the project (highlighted); set when a handle is grabbed or focused.</summary>
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

	/// <summary>Style of the rotation handles of composite sources (a <see cref="MarkerThumb"/> style).</summary>
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
			old.Probes.CollectionChanged -= overlay.OnCollectionChanged;
		}
		if (e.NewValue is Project project)
		{
			project.Sources.CollectionChanged += overlay.OnCollectionChanged;
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

	private void AddThumb(object item, MarkerRole role, Style? style, string name, string automationId, string tipKey = "TipMarkerFormat")
	{
		var thumb = new MarkerThumb(item, role) { Style = style };
		thumb.ToolTip = TryFindResource(tipKey) is string format ? string.Format(CultureInfo.CurrentUICulture, format, name) : name;
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
			if (thumb.Role == MarkerRole.SourceAngle)
			{
				// the rotation handle only while its source is selected (the user's rule, 2026-10-14); collapsed, it is out of the Tab cycle too
				thumb.Visibility = thumb.IsSelectedMarker ? Visibility.Visible : Visibility.Collapsed;
			}
		}
		InvalidateVisual();
	}

	/// <summary>The items with the selected one last, so what is drawn for it lies over the others (the user's rule, 2026-10-18).</summary>
	private static IEnumerable<T> SelectedLast<T>(IEnumerable<T> items, object? selected) where T : class =>
		items.OrderBy(item => ReferenceEquals(item, selected));

	private static void UpdateOpacity(MarkerThumb thumb) =>
		thumb.Opacity = thumb.Item switch { Source { Enabled: false } => 0.45, Probe { Active: false } => 0.45, _ => 1.0 };

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

	/// <summary>The picture's rectangle in this element's coordinates for an element of <paramref name="size"/>: the canvas at its uniform scale, centred (the element fills the cell, the picture is letterboxed in it).</summary>
	private Rect PictureRect(Size size)
	{
		double scale = ScaleFor(size);
		double width = CanvasWidth * scale, height = CanvasHeight * scale;
		return new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
	}

	/// <summary>
	/// Where the rotation handle of a composite source sits, for an element of <paramref name="size"/>: on the
	/// array's row axis, turned by its angle (counter-clockwise on the screen), just beyond the first row's
	/// half-width and at least <see cref="AngleHandleReach"/> from the centre.
	/// </summary>
	private Point AnglePoint(Source source, Size size)
	{
		Point centre = ToView(source.X, source.Y, size);
		SourceArray array = source.Array!;
		double reach = Math.Max(AngleHandleReach, (array.SizeX - 1) / 2.0 * array.SpacingX * ScaleFor(size) + AngleHandleGap);
		double radians = array.Angle * Math.PI / 180.0;
		return new Point(centre.X + reach * Math.Cos(radians), centre.Y - reach * Math.Sin(radians));
	}

	/// <summary>Turns the array so its row axis points from the source to <paramref name="view"/>: to whole degrees, or to the nearest 15 with <paramref name="snap"/>.</summary>
	private void SetAngle(Source source, Point view, bool snap)
	{
		Point centre = ToView(source.X, source.Y);
		double degrees = Math.Atan2(-(view.Y - centre.Y), view.X - centre.X) * 180.0 / Math.PI;
		double unit = snap ? 15.0 : 1.0;
		SetAngle(source, Math.Round(degrees / unit) * unit);
	}

	/// <summary>Sets the array's angle, kept in (-180, 180].</summary>
	private static void SetAngle(Source source, double degrees)
	{
		if (source.Array is { } array)
		{
			array.Angle = degrees;
			array.Normalize();
		}
	}

	private static (int X, int Y) PointOf(MarkerThumb thumb) => (thumb.Item, thumb.Role) switch
	{
		(Source source, _) => (source.X, source.Y),
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
			Point centre = thumb.Role == MarkerRole.SourceAngle ? AnglePoint((Source)thumb.Item, finalSize) : ToView(x, y, finalSize);
			Size size = thumb.DesiredSize;
			var rect = new Rect(centre.X - size.Width / 2, centre.Y - size.Height / 2, size.Width, size.Height);
			thumb.Arrange(rect);
			if (thumb.Role == MarkerRole.SourceAngle)
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
		dc.Pop();
		foreach (MarkerThumb thumb in Children.OfType<MarkerThumb>().OrderBy(thumb => thumb.IsSelectedMarker))
		{
			if (thumb.Role is MarkerRole.ProbeB or MarkerRole.SourceAngle)
			{
				continue;
			}
			(int x, int y) = PointOf(thumb);
			Point centre = ToView(x, y);
			string name = thumb.Item is Source source ? source.Name : ((Probe)thumb.Item).Name;
			var label = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelTypeface, 11, thumb.IsSelectedMarker ? AccentBrush : LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
			// beside the point; below it for a composite source, whose rotation handle sits beside it at 0 degrees
			bool below = thumb.Item is Source { Array: not null };
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
		if (thumb.Role == MarkerRole.SourceAngle)
		{
			// the handle follows the mouse around the centre; Shift snaps to 15 degrees
			SetAngle((Source)thumb.Item, Mouse.GetPosition(this), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
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

	/// <summary>The arrow keys move the focused handle by one canvas pixel, ten with Shift; on a rotation handle they turn the array by a degree (Right and Up counter-clockwise), ten with Shift.</summary>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.Handled || e.OriginalSource is not MarkerThumb thumb)
		{
			return;
		}
		int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? KeyStepFast : KeyStep;
		if (thumb.Role == MarkerRole.SourceAngle)
		{
			int turn = e.Key switch { Key.Right or Key.Up => step, Key.Left or Key.Down => -step, _ => 0 };
			if (turn != 0 && thumb.Item is Source turned)
			{
				SetAngle(turned, (turned.Array?.Angle ?? 0.0) + turn);
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
