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

using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using WaveDudeNext.App.Commands;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;
using WaveDudeNext.Runs;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// The graph boxes of the probes on one picture (the probes of this <see cref="Layer"/>), drawn over the frame as
/// upstream did: a translucent dark box with axes, the curve and a title strip with the live readout, placed where the
/// probe's <see cref="Probe.BoxX"/>/<see cref="Probe.BoxY"/> say (canvas pixels, mapped with the picture's uniform
/// scale). The graph is as live as the picture: while the picture shows a scratch frame (<see cref="Scratch"/>: no run,
/// a stale one, or one that has not got there) the data is that frame's recording; otherwise it is the run's recording
/// as it stood at <see cref="Frame"/> (the series cut off at that step, the profile of that frame), redrawn on every
/// probe batch. A stale run's data is shown while the pane shows its stored frame, so the graph follows the picture
/// instead of dropping to an empty axis until the scratch frame arrives. Each box is a draggable thumb (moves the box;
/// the arrow keys nudge it) with a second thumb at its bottom-right corner that resizes it, snapping to the fixed sizes
/// (<see cref="ProbeGraph.Snap"/>) or making it <see cref="ProbeBoxSize.Custom"/>. Grabbing or focusing a box selects
/// its probe. The geometry comes from <see cref="ProbeGraph"/>; this class only draws. A drag is measured from the
/// pointer's absolute position over this overlay (never from the thumb, which moves with the box and would feed the
/// miss back), so the thumbs' own drag events are not used. UI thread.
/// </summary>
public sealed class ProbeGraphOverlay : Panel
{
	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly StyledProperty<Project?> ProjectProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, Project?>(nameof(Project));

	/// <summary>Identifies the <see cref="Run"/> property.</summary>
	public static readonly StyledProperty<SimulationRun?> RunProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, SimulationRun?>(nameof(Run));

	/// <summary>Identifies the <see cref="Scratch"/> property.</summary>
	public static readonly StyledProperty<ScratchResult?> ScratchProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, ScratchResult?>(nameof(Scratch));

	/// <summary>Identifies the <see cref="Frame"/> property.</summary>
	public static readonly StyledProperty<int> FrameProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, int>(nameof(Frame));

	/// <summary>Identifies the <see cref="Layer"/> property.</summary>
	public static readonly StyledProperty<ProbeLayer> LayerProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, ProbeLayer>(nameof(Layer));

	/// <summary>Identifies the <see cref="CanvasWidth"/> property.</summary>
	public static readonly StyledProperty<int> CanvasWidthProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, int>(nameof(CanvasWidth), 1);

	/// <summary>Identifies the <see cref="CanvasHeight"/> property.</summary>
	public static readonly StyledProperty<int> CanvasHeightProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, int>(nameof(CanvasHeight), 1);

	/// <summary>Identifies the <see cref="SelectedItem"/> property.</summary>
	public static readonly StyledProperty<object?> SelectedItemProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

	/// <summary>Identifies the <see cref="BoxTheme"/> property.</summary>
	public static readonly StyledProperty<ControlTheme?> BoxThemeProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, ControlTheme?>(nameof(BoxTheme));

	/// <summary>Identifies the <see cref="CornerTheme"/> property.</summary>
	public static readonly StyledProperty<ControlTheme?> CornerThemeProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, ControlTheme?>(nameof(CornerTheme));

	/// <summary>Identifies the <see cref="AccentBrush"/> property.</summary>
	public static readonly StyledProperty<IBrush?> AccentBrushProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, IBrush?>(nameof(AccentBrush), new ImmutableSolidColorBrush(Colors.DodgerBlue));

	/// <summary>Identifies the <see cref="Commands"/> property.</summary>
	public static readonly StyledProperty<AppCommands?> CommandsProperty =
		AvaloniaProperty.Register<ProbeGraphOverlay, AppCommands?>(nameof(Commands));

	/// <summary>Pixels a focused box moves or grows per arrow key, and with Shift.</summary>
	private const int KeyStep = 1;
	private const int KeyStepFast = 10;

	/// <summary>Screen distance within which a dragged anchor (the box's, or the probe's point A) snaps the box onto the probe and attaches it.</summary>
	public const double SnapRadius = 14.0;

	/// <summary>Screen distance an attached box must be dragged from the probe before it comes off: wider than <see cref="SnapRadius"/>, so a drag near the edge does not attach and detach in turn (hysteresis).</summary>
	public const double SnapRelease = 28.0;

	/// <summary>The text metrics handed to the layout, built once (they do not depend on the scale, see <see cref="ProbeGraphDrawing.MakeStyle"/>).</summary>
	private ProbeGraphStyle? _style;

	private readonly HashSet<INotifyPropertyChanged> _subscribed = [];
	private readonly GraphCanvas _canvas;
	private BoxThumb? _dragging;

	static ProbeGraphOverlay()
	{
		AffectsMeasure<ProbeGraphOverlay>(CanvasWidthProperty, CanvasHeightProperty);
	}

	/// <summary>Creates the overlay: no background, so clicks between the boxes fall through to the picture (and the pane's focus handling).</summary>
	public ProbeGraphOverlay()
	{
		Focusable = false;
		_canvas = new GraphCanvas(this) { IsHitTestVisible = false };
		Children.Add(_canvas);
	}

	/// <summary>The layer the boxes are drawn on: a Panel's own Render is sealed in Avalonia, so a child control under the thumbs draws them.</summary>
	private sealed class GraphCanvas(ProbeGraphOverlay owner) : Control
	{
		public override void Render(DrawingContext context) => owner.DrawBoxes(context, Bounds.Size);
	}

	/// <summary>Redraws the boxes (their data, selection or frame changed; the thumbs stay).</summary>
	private void InvalidateGraphs() => _canvas.InvalidateVisual();

	/// <summary>The project whose probes are shown; null shows nothing.</summary>
	public Project? Project
	{
		get => GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>The run whose recordings are drawn; null (or a stale run) draws the boxes without data.</summary>
	public SimulationRun? Run
	{
		get => GetValue(RunProperty);
		set => SetValue(RunProperty, value);
	}

	/// <summary>The scratch frame the picture shows, or null; its recordings (the project as it is now) take precedence over the run's.</summary>
	public ScratchResult? Scratch
	{
		get => GetValue(ScratchProperty);
		set => SetValue(ScratchProperty, value);
	}

	/// <summary>The frame the picture shows; the graphs show the run's recording as it stood there.</summary>
	public int Frame
	{
		get => GetValue(FrameProperty);
		set => SetValue(FrameProperty, value);
	}

	/// <summary>Which picture this overlay sits on; only the probes of that layer are drawn.</summary>
	public ProbeLayer Layer
	{
		get => GetValue(LayerProperty);
		set => SetValue(LayerProperty, value);
	}

	/// <summary>Canvas width in pixels, the x range of the model coordinates.</summary>
	public int CanvasWidth
	{
		get => GetValue(CanvasWidthProperty);
		set => SetValue(CanvasWidthProperty, value);
	}

	/// <summary>Canvas height in pixels.</summary>
	public int CanvasHeight
	{
		get => GetValue(CanvasHeightProperty);
		set => SetValue(CanvasHeightProperty, value);
	}

	/// <summary>The item selected in the project (its box gets the accent border); set when a box is grabbed or focused.</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>Theme of the thumb covering a box (transparent, shows hover and focus).</summary>
	public ControlTheme? BoxTheme
	{
		get => GetValue(BoxThemeProperty);
		set => SetValue(BoxThemeProperty, value);
	}

	/// <summary>Theme of the resize grip at a box's bottom-right corner.</summary>
	public ControlTheme? CornerTheme
	{
		get => GetValue(CornerThemeProperty);
		set => SetValue(CornerThemeProperty, value);
	}

	/// <summary>Border of the selected probe's box.</summary>
	public IBrush? AccentBrush
	{
		get => GetValue(AccentBrushProperty);
		set => SetValue(AccentBrushProperty, value);
	}

	/// <summary>The application's commands: Delete on a box runs Remove, the box's context menu runs ExportProbeCsv.</summary>
	public AppCommands? Commands
	{
		get => GetValue(CommandsProperty);
		set => SetValue(CommandsProperty, value);
	}

	/// <summary>One draggable thumb of a box: the box itself (moves it) or its bottom-right corner (resizes it). Focusable, so the arrow keys work on it too.</summary>
	private sealed class BoxThumb(Probe probe, bool isCorner) : Thumb
	{
		public Probe Probe { get; } = probe;
		public bool IsCorner { get; } = isCorner;
		/// <summary>The box's position (or size, for the corner) when the current drag started, in canvas pixels.</summary>
		public int DragStartX { get; set; }
		public int DragStartY { get; set; }
		/// <summary>Where the pointer was when the drag started, in continuous canvas coordinates.</summary>
		public double PressCanvasX { get; set; }
		public double PressCanvasY { get; set; }
	}

	// ---------------------------------------------------------------- model and run wiring

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == ProjectProperty)
		{
			if (change.GetOldValue<Project?>() is { } old)
			{
				old.Probes.CollectionChanged -= OnCollectionChanged;
			}
			if (change.GetNewValue<Project?>() is { } project)
			{
				project.Probes.CollectionChanged += OnCollectionChanged;
			}
			Rebuild();
		}
		else if (change.Property == RunProperty)
		{
			if (change.GetOldValue<SimulationRun?>() is { } old)
			{
				old.ProbesUpdated -= OnProbesUpdated;
			}
			if (change.GetNewValue<SimulationRun?>() is { } run)
			{
				run.ProbesUpdated += OnProbesUpdated;
			}
			InvalidateGraphs();
		}
		else if (change.Property == LayerProperty || change.Property == BoxThemeProperty || change.Property == CornerThemeProperty || change.Property == CommandsProperty)
		{
			Rebuild();
		}
		else if (change.Property == ScratchProperty || change.Property == FrameProperty || change.Property == AccentBrushProperty
			|| change.Property == SelectedItemProperty || change.Property == CanvasWidthProperty || change.Property == CanvasHeightProperty)
		{
			if (change.Property == SelectedItemProperty)
			{
				// the selected box comes to the front: the thumbs' clips follow the new order
				InvalidateArrange();
			}
			InvalidateGraphs();
		}
	}

	private void OnProbesUpdated(object? sender, ProbeUpdate e) => InvalidateGraphs();

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

	/// <summary>Recreates the thumbs for the active probes of this layer and re-subscribes to every probe.</summary>
	private void Rebuild()
	{
		foreach (INotifyPropertyChanged item in _subscribed)
		{
			item.PropertyChanged -= OnProbePropertyChanged;
		}
		_subscribed.Clear();
		Children.Clear();
		Children.Add(_canvas);
		_dragging = null;
		if (Project is { } project)
		{
			foreach (Probe probe in project.Probes)
			{
				if (_subscribed.Add(probe))
				{
					probe.PropertyChanged += OnProbePropertyChanged;
				}
				if (!IsShown(probe))
				{
					continue;
				}
				AddThumb(probe, isCorner: false, BoxTheme, "TipProbeBoxFormat", "ProbeBox:" + probe.Name);
				AddThumb(probe, isCorner: true, CornerTheme, "TipProbeBoxCornerFormat", "ProbeBoxCorner:" + probe.Name);
			}
		}
		InvalidateArrange();
		InvalidateGraphs();
	}

	private bool IsShown(Probe probe) => probe.Active && probe.Layer == Layer;

	private void AddThumb(Probe probe, bool isCorner, ControlTheme? theme, string tipKey, string automationId)
	{
		var thumb = new BoxThumb(probe, isCorner) { Focusable = true };
		if (theme != null)
		{
			thumb.Theme = theme;
		}
		ToolTip.SetTip(thumb, this.TryFindResource(tipKey, out object? format) && format is string text ? string.Format(CultureInfo.CurrentUICulture, text, probe.Name) : probe.Name);
		AutomationProperties.SetName(thumb, probe.Name);
		AutomationProperties.SetAutomationId(thumb, automationId);
		// handled events too: the thumb marks its press handled before the overlay could see it
		thumb.AddHandler(PointerPressedEvent, OnThumbPressed, RoutingStrategies.Bubble, handledEventsToo: true);
		thumb.AddHandler(PointerMovedEvent, OnThumbMoved, RoutingStrategies.Bubble, handledEventsToo: true);
		thumb.AddHandler(PointerReleasedEvent, OnThumbReleased, RoutingStrategies.Bubble, handledEventsToo: true);
		thumb.AddHandler(PointerCaptureLostEvent, OnThumbCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
		thumb.GotFocus += (_, _) => SelectedItem = probe;
		if (!isCorner)
		{
			// the box's context menu: the probe's recording as CSV; the header follows the language like the XAML menus
			// (the thumbs are rebuilt when Commands changes, so the command set here is the current one)
			var export = new MenuItem { CommandParameter = probe, Command = Commands?.ExportProbeCsv };
			export.Bind(MenuItem.HeaderProperty, this.GetResourceObservable("MenuExportProbeCsv"));
			MenuIcon.SetDrawing(export, "Export");
			AutomationProperties.SetAutomationId(export, "ContextExportProbeCsv");
			thumb.ContextMenu = new ContextMenu { Items = { export } };
		}
		Children.Add(thumb);
	}

	private void OnProbePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(Probe.Active) or nameof(Probe.Layer) or nameof(Probe.Name))
		{
			Rebuild();
			return;
		}
		InvalidateArrange();
		InvalidateGraphs();
	}

	// ---------------------------------------------------------------- geometry

	/// <summary>Pixels per canvas pixel for an element of <paramref name="size"/>: the picture's uniform scale (the size is passed in because Bounds lags during Arrange).</summary>
	private double ScaleFor(Size size) => Math.Min(size.Width / Math.Max(1, CanvasWidth), size.Height / Math.Max(1, CanvasHeight));

	/// <summary>The box of <paramref name="probe"/> in this element's coordinates, for an element of <paramref name="size"/>; the picture is centred in it.</summary>
	private Rect BoxOf(Probe probe, Size size)
	{
		double scale = ScaleFor(size);
		double originX = (size.Width - CanvasWidth * scale) / 2;
		double originY = (size.Height - CanvasHeight * scale) / 2;
		BoxRect box = ProbeGraph.BoxInCanvas(probe, CanvasWidth, CanvasHeight);
		// whole view pixels, so the 1-pixel lines of the box stay crisp
		return new Rect(Math.Round(originX + box.X * scale), Math.Round(originY + box.Y * scale), Math.Max(1, Math.Round(box.Width * scale)), Math.Max(1, Math.Round(box.Height * scale)));
	}

	/// <summary>The box of <paramref name="probe"/> in this overlay's coordinates at its current size, for the tests.</summary>
	internal Rect BoxOf(Probe probe) => BoxOf(probe, Bounds.Size);

	/// <summary>
	/// The picture's size, the canvas fitted uniformly into the room (as <see cref="CanvasImage"/> fits it), so the cell
	/// holding the picture and its overlays is sized to the picture and its edge line hugs it; nothing without a bound.
	/// </summary>
	protected override Size MeasureOverride(Size availableSize)
	{
		foreach (Control child in Children)
		{
			child.Measure(Size.Infinity);
		}
		if (double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height))
		{
			return default;
		}
		double scale = Math.Min(availableSize.Width / Math.Max(1, CanvasWidth), availableSize.Height / Math.Max(1, CanvasHeight));
		return new Size(Math.Max(1, CanvasWidth) * scale, Math.Max(1, CanvasHeight) * scale);
	}

	/// <summary>
	/// The boxes that are drawn, in the order they are drawn, the last in front: the shown probes in the project's order,
	/// the selected one last, so its box lies over any box it overlaps; a box too small to draw
	/// (<see cref="ProbeGraph.MinVisibleWidth"/> × <see cref="ProbeGraph.MinVisibleHeight"/>) is left out.
	/// <see cref="DrawBoxes"/> draws by it and <see cref="ArrangeOverride"/> clips the thumbs by it.
	/// </summary>
	/// <param name="project">The project whose probes are shown.</param>
	/// <param name="size">This element's size (see <see cref="ScaleFor"/>).</param>
	private List<(Probe Probe, Rect Box)> DrawnBoxes(Project project, Size size)
	{
		object? selected = SelectedItem;
		var boxes = new List<(Probe, Rect)>();
		foreach (Probe probe in project.Probes.OrderBy(probe => ReferenceEquals(probe, selected)))
		{
			if (IsShown(probe) && BoxOf(probe, size) is { Width: >= ProbeGraph.MinVisibleWidth, Height: >= ProbeGraph.MinVisibleHeight } box)
			{
				boxes.Add((probe, box));
			}
		}
		return boxes;
	}

	/// <summary>
	/// The clip of a thumb of <paramref name="probe"/> placed at <paramref name="place"/>: what the boxes drawn in front
	/// of its probe's box leave of it, in the thumb's own coordinates; null when none of them covers any of it, an empty
	/// geometry when they cover all of it.
	/// </summary>
	private static Geometry? Uncovered(Rect place, List<(Probe Probe, Rect Box)> drawn, Probe probe)
	{
		Geometry? clip = null;
		// a box too small to be drawn is not in the list (-1): every drawn box is in front of its thumbs
		for (int i = drawn.FindIndex(entry => ReferenceEquals(entry.Probe, probe)) + 1; i < drawn.Count; i++)
		{
			Rect covered = place.Intersect(drawn[i].Box);
			if (covered.Width <= 0 || covered.Height <= 0)
			{
				continue;
			}
			clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip ?? new RectangleGeometry(new Rect(place.Size)), new RectangleGeometry(covered.Translate(new Vector(-place.X, -place.Y))));
		}
		return clip;
	}

	/// <summary>
	/// Places each box's two thumbs and clips them by the boxes drawn in front of theirs (2026-11-10, the user's report
	/// on the WPF head, whose overlay this one follows: a box's resize grip was drawn on a box lying in front of it).
	/// The boxes are one drawing under all the thumbs, in <see cref="DrawnBoxes"/>' order, while the thumbs are children
	/// in the project's order, which the Tab cycle goes by: so a rear box's grip showed through the front box's
	/// transparent thumb, and with the front box the earlier one in the project a press on its own grip, or anywhere on
	/// the overlap, took the box behind. A clip counts for drawing and for hit testing alike, so what is seen in front
	/// is what is grabbed; a thumb covered altogether is still reached with Tab, which selects its probe and so brings
	/// its box to the front.
	/// </summary>
	protected override Size ArrangeOverride(Size finalSize)
	{
		_canvas.Arrange(new Rect(finalSize));
		List<(Probe Probe, Rect Box)> drawn = Project is { } project ? DrawnBoxes(project, finalSize) : [];
		foreach (BoxThumb thumb in Children.OfType<BoxThumb>())
		{
			Rect place = BoxOf(thumb.Probe, finalSize);
			if (thumb.IsCorner)
			{
				Size grip = thumb.DesiredSize;
				place = new Rect(place.Right - grip.Width, place.Bottom - grip.Height, grip.Width, grip.Height);
			}
			thumb.Arrange(place);
			thumb.Clip = Uncovered(place, drawn, thumb.Probe);
		}
		return finalSize;
	}

	// ---------------------------------------------------------------- drawing

	/// <summary>
	/// The recording of <paramref name="probe"/> as the picture shows it (the scratch frame's, else the run's as
	/// it stood at <see cref="Frame"/>), and the run length for the step axis; empty data without either.
	/// </summary>
	private ProbeGraphData DataFor(Probe probe, out int totalSteps)
	{
		if (Scratch is { } scratch)
		{
			totalSteps = Math.Max(1, scratch.Spec.Steps);
			// a scratch trace holds one profile, the frame's own, at index 0
			return TraceOf(scratch.Spec.Probes, scratch.Traces, probe) is { } scratchTrace
				? ProbeGraphData.At(scratchTrace, scratch.Frame * scratch.Spec.VisualizeEvery, 0)
				: default;
		}
		SimulationRun? run = Run;
		totalSteps = Math.Max(1, run?.Spec.Steps ?? Project?.Simulation.Steps ?? 1);
		if (run == null)
		{
			return default;
		}
		return TraceOf(run.Spec.Probes, run.Traces, probe) is { } trace
			? ProbeGraphData.At(trace, Frame * run.Spec.VisualizeEvery, Frame)
			: default;
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

	/// <summary>Draws every shown probe's box into the canvas child, the selected one last so it lies over any box it overlaps.</summary>
	private void DrawBoxes(DrawingContext context, Size size)
	{
		if (Project is not { } project || size.Width <= 0 || size.Height <= 0)
		{
			return;
		}
		double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
		ProbeGraphStyle style = _style ??= ProbeGraphDrawing.MakeStyle(scale);
		object? selected = SelectedItem;
		foreach ((Probe probe, Rect box) in DrawnBoxes(project, size))
		{
			ProbeGraphData data = DataFor(probe, out int totalSteps);
			ProbeGraphLayout layout = ProbeGraph.Layout(probe, data, totalSteps, (float)box.Width, (float)box.Height, style, CultureInfo.CurrentCulture);
			if (!layout.IsVisible)
			{
				continue;
			}
			IPen? border = ReferenceEquals(probe, selected) && AccentBrush is { } accent ? new Pen(accent, ProbeGraphDrawing.Hairline(scale)) : null;
			using (context.PushTransform(Matrix.CreateTranslation(box.X, box.Y)))
			{
				ProbeGraphDrawing.DrawBox(context, layout, border, scale);
			}
		}
	}

	// ---------------------------------------------------------------- dragging and keys

	private void OnThumbPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not BoxThumb thumb || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		Probe probe = thumb.Probe;
		if (thumb.IsCorner)
		{
			BoxSize size = ProbeGraph.BoxSizeOf(probe);
			(thumb.DragStartX, thumb.DragStartY) = (size.Width, size.Height);
		}
		else
		{
			(thumb.DragStartX, thumb.DragStartY) = (probe.BoxX, probe.BoxY);
		}
		// the drag is measured from the pointer's absolute position over the overlay (in canvas units; only the difference
		// matters): the thumb's own deltas come from where the box is, and a box held at the canvas edge or snapped to a
		// size would add the miss again on every move
		double pressScale = ScaleFor(Bounds.Size);
		Point press = e.GetPosition(this);
		(thumb.PressCanvasX, thumb.PressCanvasY) = pressScale > 0 ? (press.X / pressScale, press.Y / pressScale) : (0.0, 0.0);
		_dragging = thumb;
		SelectedItem = probe;
		thumb.Focus();
	}

	private void OnThumbMoved(object? sender, PointerEventArgs e)
	{
		if (sender is not BoxThumb thumb || !ReferenceEquals(thumb, _dragging) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		double scale = ScaleFor(Bounds.Size);
		if (scale <= 0)
		{
			return;
		}
		Point pointer = e.GetPosition(this);
		int x = thumb.DragStartX + (int)Math.Round(pointer.X / scale - thumb.PressCanvasX);
		int y = thumb.DragStartY + (int)Math.Round(pointer.Y / scale - thumb.PressCanvasY);
		Probe probe = thumb.Probe;
		if (thumb.IsCorner)
		{
			// the fixed sizes are snap points of the drag
			(ProbeBoxSize size, BoxSize box) = ProbeGraph.Snap(x, y);
			SetBoxSize(probe, size, box);
		}
		else
		{
			SetBoxPosition(probe, x, y, snap: true);
		}
	}

	private void OnThumbReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (ReferenceEquals(sender, _dragging))
		{
			_dragging = null;
		}
	}

	private void OnThumbCaptureLost(object? sender, PointerCaptureLostEventArgs e)
	{
		if (ReferenceEquals(sender, _dragging))
		{
			_dragging = null;
		}
	}

	/// <summary>
	/// Moves the box, kept inside the canvas. With <paramref name="snap"/> (a drag, not a keyboard nudge) a box
	/// whose anchor comes within <see cref="SnapRadius"/> screen pixels of the probe's point A lands on it and is
	/// attached from then on (<see cref="Probe.IsBoxAttached"/>); an attached box stays on the probe until it is
	/// dragged more than <see cref="SnapRelease"/> away. One radius, not a snap per axis, with hysteresis.
	/// </summary>
	private void SetBoxPosition(Probe probe, int x, int y, bool snap)
	{
		if (snap)
		{
			double scale = ScaleFor(Bounds.Size);
			double dx = (x - probe.AX) * scale;
			double dy = (y - probe.AY) * scale;
			double limit = probe.IsBoxAttached ? SnapRelease : SnapRadius;
			if (dx * dx + dy * dy <= limit * limit)
			{
				x = probe.AX;
				y = probe.AY;
			}
		}
		BoxRect box = ProbeGraph.BoxInCanvas(probe, CanvasWidth, CanvasHeight);
		probe.BoxX = Math.Clamp(x, 0, Math.Max(0, CanvasWidth - box.Width));
		probe.BoxY = Math.Clamp(y, 0, Math.Max(0, CanvasHeight - box.Height));
	}

	/// <summary>Applies a box size: a fixed size as it is, a custom one with its width and height.</summary>
	private static void SetBoxSize(Probe probe, ProbeBoxSize size, BoxSize box)
	{
		if (size == ProbeBoxSize.Custom)
		{
			probe.BoxWidth = box.Width;
			probe.BoxHeight = box.Height;
		}
		probe.BoxSize = size;
	}

	/// <summary>The arrow keys move a focused box by one canvas pixel (ten with Shift); on the corner grip they resize it, to a custom size; Delete removes the probe.</summary>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.Handled || e.Source is not BoxThumb thumb)
		{
			return;
		}
		if (e.Key == Key.Delete)
		{
			// Del on a handle removes its item, or the selection it belongs to, as Del on a tree row does
			Commands?.Remove.Execute(thumb.Probe);
			e.Handled = true;
			return;
		}
		int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? KeyStepFast : KeyStep;
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
		Probe probe = thumb.Probe;
		if (thumb.IsCorner)
		{
			BoxSize size = ProbeGraph.BoxSizeOf(probe);
			SetBoxSize(probe, ProbeBoxSize.Custom, new BoxSize(Math.Clamp(size.Width + dx, Probe.MinBoxWidth, Probe.MaxBoxSide), Math.Clamp(size.Height + dy, Probe.MinBoxHeight, Probe.MaxBoxSide)));
		}
		else
		{
			SetBoxPosition(probe, probe.BoxX + dx, probe.BoxY + dy, snap: false);
		}
		e.Handled = true;
	}
}
