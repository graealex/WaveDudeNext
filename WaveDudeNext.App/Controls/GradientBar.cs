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

using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using WaveDudeNext.App.Converters;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// The gradient of a colour scheme with a marker under each stop (2026-11-11, the user's request: the gradient editor
/// of a contributed port, WaveDudeRust, replicated): a triangle in the stop's colour whose tip points at the stop's
/// place on the gradient. The WPF head's <c>GradientBar</c> (W/Controls/GradientBar.cs), same geometry, draw order and
/// rules. Pointer: a marker dragged moves its stop, which changes places with the stops it passes
/// (<see cref="ColorScheme.MoveStop"/>); a press on the gradient adds a stop there in the gradient's colour
/// (<see cref="ColorScheme.AddStop(double)"/>) and drags it on; either selects the marker, which is then the
/// accent's colour as a whole. A marker is taken within <see cref="MarkerReach"/> pixels of its tip, from the
/// gradient's lowest rows down; a press that does not move the pointer changes no position. A marker dragged more
/// than <see cref="PullAway"/> pixels above or below the bar fades, and released there its stop is removed; a double
/// click on a marker removes its stop as well (2026-11-11, the user's choice of the mouse's ways to remove one; the
/// last stop stays). Both go to the scheme itself, not through <see cref="RemoveCommand"/>: the press and the release
/// bound the edit, so a stop dragged and then pulled away is one undo step, which brings it back where it was.
/// Keyboard, the bar being one tab stop: Left and Right move the selected stop by a hundredth, by a tenth with Shift; Page Up and Page Down
/// select the stop before and after it along the gradient; Del removes it through <see cref="RemoveCommand"/>, a
/// command, so the removal is an undo step of its own, while a drag ends its step at the button's release and the
/// arrow keys' when the focus leaves, as everywhere. Positions set here are thousandths: finer than a pixel of the
/// bar, and a number the rows below and the file show in full. The gradient spans the control less
/// <see cref="Inset"/> at either end, the room of the half marker under a stop at 0 or 1. Drawn in
/// <see cref="Render"/>, the colours from the brush properties; the gradient's outline is a whole number of device
/// pixels on the pixel grid, the markers are antialiased shapes. UI thread.
/// </summary>
public sealed class GradientBar : Control
{
	/// <summary>Height of the gradient, in pixels; the markers lie in the rows under it.</summary>
	public const double GradientHeight = 18.0;

	/// <summary>Room left and right of the gradient, in pixels: half a marker with its outline.</summary>
	public const double Inset = 8.0;

	/// <summary>Half the width of a marker's base and the marker's height, tip to base, in pixels.</summary>
	private const double MarkerHalfWidth = 6.0;
	private const double MarkerHeight = 8.0;

	/// <summary>How far left and right of its tip, and how far up into the gradient, a press takes a marker, in pixels.</summary>
	private const double MarkerReach = 7.0;
	private const double MarkerReachUp = 4.0;

	/// <summary>How far above or below the bar a dragged marker is pulled away, in pixels: released there, its stop is removed.</summary>
	private const double PullAway = 24.0;

	/// <summary>How much of a pulled-away marker shows.</summary>
	private const double PulledAwayOpacity = 0.35;

	/// <summary>What an arrow key moves a stop by, and with Shift.</summary>
	private const double KeyStep = 0.01;
	private const double LargeKeyStep = 0.1;

	/// <summary>Decimals of a position set by the pointer or the keys.</summary>
	private const int Decimals = 3;

	/// <summary>Identifies the <see cref="Scheme"/> property.</summary>
	public static readonly StyledProperty<ColorScheme?> SchemeProperty =
		AvaloniaProperty.Register<GradientBar, ColorScheme?>(nameof(Scheme));

	/// <summary>Identifies the <see cref="RemoveCommand"/> property.</summary>
	public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
		AvaloniaProperty.Register<GradientBar, ICommand?>(nameof(RemoveCommand));

	/// <summary>Identifies the <see cref="OutlineBrush"/> property: the gradient's border.</summary>
	public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
		AvaloniaProperty.Register<GradientBar, IBrush?>(nameof(OutlineBrush), Brushes.Gray);

	/// <summary>Identifies the <see cref="MarkerBrush"/> property: a marker's outline.</summary>
	public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
		AvaloniaProperty.Register<GradientBar, IBrush?>(nameof(MarkerBrush), Brushes.Black);

	/// <summary>Identifies the <see cref="AccentBrush"/> property: the selected marker and the keyboard focus.</summary>
	public static readonly StyledProperty<IBrush?> AccentBrushProperty =
		AvaloniaProperty.Register<GradientBar, IBrush?>(nameof(AccentBrush), Brushes.DodgerBlue);

	private readonly Dictionary<(IBrush? Brush, double Thickness), IPen> _pens = new();
	private ColorStop? _selected;
	private IBrush? _fill;
	private Cursor? _markerCursor;
	private IPointer? _pointer;
	private bool _attached;
	private bool _dragging;
	private bool _moved;
	private bool _pulledAway;
	private double _pressX;
	private double _grab;
	private double _scale = 1.0;

	static GradientBar()
	{
		AffectsRender<GradientBar>(SchemeProperty, OutlineBrushProperty, MarkerBrushProperty, AccentBrushProperty);
	}

	/// <summary>A focusable bar that shows the keyboard focus itself, as the gradient's outline in the accent colour (so no theme adorner).</summary>
	public GradientBar()
	{
		Focusable = true;
		FocusAdorner = null;
	}

	/// <summary>The scheme whose gradient is shown and whose stops the markers move; the bar follows its every change.</summary>
	public ColorScheme? Scheme
	{
		get => GetValue(SchemeProperty);
		set => SetValue(SchemeProperty, value);
	}

	/// <summary>Executed with the selected stop (a <see cref="ColorStop"/>) when Del is pressed; removes it from the scheme.</summary>
	public ICommand? RemoveCommand
	{
		get => GetValue(RemoveCommandProperty);
		set => SetValue(RemoveCommandProperty, value);
	}

	/// <summary>The gradient's border.</summary>
	public IBrush? OutlineBrush
	{
		get => GetValue(OutlineBrushProperty);
		set => SetValue(OutlineBrushProperty, value);
	}

	/// <summary>A marker's outline: a colour that stands out from the form's background, whatever the stop's own.</summary>
	public IBrush? MarkerBrush
	{
		get => GetValue(MarkerBrushProperty);
		set => SetValue(MarkerBrushProperty, value);
	}

	/// <summary>The selected marker, fill and outline, and the gradient's border while the bar has the keyboard focus.</summary>
	public IBrush? AccentBrush
	{
		get => GetValue(AccentBrushProperty);
		set => SetValue(AccentBrushProperty, value);
	}

	/// <summary>
	/// The stop the keys act on, whose marker is drawn in the accent's colour; null for none, also once the stop has
	/// left the scheme (removed in its row, or an undo, which refills the list with new stops).
	/// </summary>
	public ColorStop? SelectedStop => _selected != null && Scheme?.Stops.Contains(_selected) == true ? _selected : null;

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == SchemeProperty)
		{
			// the scheme is listened to while the bar is in a window, so a scheme that outlives its form does not keep the form's bar
			if (change.GetOldValue<ColorScheme?>() is { } old)
			{
				old.PropertyChanged -= OnSchemePropertyChanged;
			}
			if (_attached && change.GetNewValue<ColorScheme?>() is { } scheme)
			{
				scheme.PropertyChanged += OnSchemePropertyChanged;
			}
			EndDrag();
			_selected = null;
			_fill = null;
		}
	}

	/// <inheritdoc/>
	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		_attached = true;
		_fill = null;
		if (Scheme is { } scheme)
		{
			scheme.PropertyChanged += OnSchemePropertyChanged;
		}
	}

	/// <inheritdoc/>
	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		_attached = false;
		EndDrag();
		if (Scheme is { } scheme)
		{
			scheme.PropertyChanged -= OnSchemePropertyChanged;
		}
	}

	/// <summary>The scheme raises <c>Stops</c> at every change of a stop or of the list.</summary>
	private void OnSchemePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ColorScheme.Stops))
		{
			_fill = null;
			InvalidateVisual();
		}
	}

	private void Select(ColorStop? stop)
	{
		if (!ReferenceEquals(_selected, stop))
		{
			_selected = stop;
			InvalidateVisual();
		}
	}

	/// <summary>The stops along the gradient; stops at one position in the list's order.</summary>
	private static List<ColorStop> Sorted(ColorScheme scheme) => [.. scheme.Stops.OrderBy(s => s.Position)];

	/// <inheritdoc/>
	protected override AutomationPeer OnCreateAutomationPeer() => new GradientBarAutomationPeer(this);

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize) =>
		new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, GradientHeight + MarkerHeight + 3.0);

	// ---------------------------------------------------------------- geometry

	/// <summary>
	/// Position 0 lies on the centre of the gradient's first pixel column and 1 on that of its last, the outline's, where
	/// the gradient's brush has its ends, so the end markers point at the gradient's ends.
	/// </summary>
	private double TrackLeft => Inset + 0.5;
	private double TrackWidth => Math.Max(1.0, Bounds.Width - 2 * Inset - 1.0);

	/// <summary>Pixel x of a position along the gradient.</summary>
	private double XOf(double position) => TrackLeft + Math.Clamp(position, 0.0, 1.0) * TrackWidth;

	/// <summary>The position at a pixel x, clamped to the gradient, in thousandths.</summary>
	private double PositionAt(double x) => Math.Round(Math.Clamp((x - TrackLeft) / TrackWidth, 0.0, 1.0), Decimals);

	/// <summary>
	/// The stop whose marker a press at <paramref name="at"/> takes: the one whose tip is nearest, within
	/// <see cref="MarkerReach"/>, from <see cref="MarkerReachUp"/> pixels inside the gradient down; of several as near
	/// the one drawn on top, the selected one, else the later in the list.
	/// </summary>
	private ColorStop? MarkerAt(Point at)
	{
		if (Scheme is not { } scheme || at.Y < GradientHeight - MarkerReachUp)
		{
			return null;
		}
		ColorStop? nearest = null;
		double distance = MarkerReach;
		foreach (ColorStop stop in scheme.Stops)
		{
			double d = Math.Abs(at.X - XOf(stop.Position));
			if (d <= distance)
			{
				nearest = stop;
				distance = d;
			}
		}
		return SelectedStop is { } selected && Math.Abs(at.X - XOf(selected.Position)) <= distance ? selected : nearest;
	}

	// ---------------------------------------------------------------- rendering

	/// <inheritdoc/>
	public override void Render(DrawingContext dc)
	{
		base.Render(dc);
		double w = Bounds.Width;
		if (Scheme is not { } scheme || scheme.Stops.Count == 0 || w <= 2 * Inset + 2)
		{
			return;
		}
		// the whole control takes the pointer, the rows between the markers too (a press goes to what is drawn)
		dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

		// a line is a whole number of device pixels thick and its edges are moved onto the device grid by Snap (the WPF
		// head's rule of 2026-11-02, there through guidelines, which Avalonia has not); at 100 % the gradient and its
		// outline are the rectangle's that stood here before the markers
		_scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
		double t = Hairline;
		double half = t / 2;
		double left = Snap(Inset);
		double right = Snap(w - Inset);
		double bottom = Snap(GradientHeight);
		dc.DrawRectangle(_fill ??= ColorSchemeBrushConverter.BrushOf(scheme.Stops), PenFor(IsFocused ? AccentBrush : OutlineBrush, t),
			new Rect(left + half, half, right - left - t, bottom - t));

		// the markers in the list's order, a later one over an earlier, the selected one over all
		ColorStop? selected = SelectedStop;
		foreach (ColorStop stop in scheme.Stops)
		{
			if (!ReferenceEquals(stop, selected))
			{
				DrawMarker(dc, stop, selected: false, t);
			}
		}
		if (selected != null)
		{
			DrawMarker(dc, selected, selected: true, t);
		}
	}

	/// <summary>
	/// A stop's marker: a triangle filled with the stop's colour, its tip a pixel under the gradient on the centre of
	/// the stop's pixel column, its base <see cref="MarkerHeight"/> below, outlined by a line of thickness
	/// <paramref name="t"/> along its edge. The selected marker is the accent's colour as a whole, fill and outline
	/// (2026-11-11, the user's word: no line around it, the whole arrow changes its colour; the stop's own colour is on
	/// the gradient above and in its row). The tip's column and the base line are snapped, so they are whole pixels;
	/// the slanted sides are antialiased.
	/// </summary>
	private void DrawMarker(DrawingContext dc, ColorStop stop, bool selected, double t)
	{
		double half = t / 2;
		double x = Snap(Math.Floor(XOf(stop.Position))) + half;
		double tip = Snap(GradientHeight + 1.0) + half;
		double baseTop = Snap(GradientHeight + 1.0 + MarkerHeight);
		IBrush? fill = selected ? AccentBrush : new ImmutableSolidColorBrush(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B));
		// a marker pulled away from the bar is on its way out: faded until it is released or comes back
		using (dc.PushOpacity(selected && _pulledAway ? PulledAwayOpacity : 1.0))
		{
			dc.DrawGeometry(fill, PenFor(selected ? AccentBrush : MarkerBrush, t), Triangle(x, tip, baseTop + half, MarkerHalfWidth));
		}
	}

	/// <summary>A triangle with its tip at (<paramref name="x"/>, <paramref name="tip"/>) and its base <paramref name="reach"/> to either side at <paramref name="bottom"/>.</summary>
	private static StreamGeometry Triangle(double x, double tip, double bottom, double reach)
	{
		var triangle = new StreamGeometry();
		using (StreamGeometryContext g = triangle.Open())
		{
			g.BeginFigure(new Point(x, tip), true);
			g.LineTo(new Point(x + reach, bottom), true);
			g.LineTo(new Point(x - reach, bottom), true);
			g.EndFigure(true);
		}
		return triangle;
	}

	/// <summary>Thickness of a line that is a whole number of device pixels (one at 100 %, two at 150 and 200 %), in layout pixels.</summary>
	private double Hairline => Math.Max(1.0, Math.Round(_scale)) / _scale;

	/// <summary>The layout coordinate moved onto the nearest device pixel boundary (the job of a WPF guideline).</summary>
	private double Snap(double v) => Math.Round(v * _scale) / _scale;

	/// <summary>A pen of <paramref name="brush"/> and <paramref name="thickness"/>, made once per pair (the theme brushes are shared instances).</summary>
	private IPen PenFor(IBrush? brush, double thickness)
	{
		if (!_pens.TryGetValue((brush, thickness), out IPen? pen))
		{
			pen = new Pen(brush, thickness);
			_pens[(brush, thickness)] = pen;
		}
		return pen;
	}

	// ---------------------------------------------------------------- pointer

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		e.Handled = true;
		Focus();
		if (Scheme is not { } scheme)
		{
			return;
		}
		Point at = e.GetPosition(this);
		ColorStop? stop = MarkerAt(at);
		if (e.ClickCount > 1)
		{
			// a double click on a marker removes its stop; on the gradient its first click has added a stop there, and
			// the second adds no other
			if (e.ClickCount == 2 && stop != null)
			{
				Remove(stop, byCommand: false);
			}
			return;
		}
		_grab = stop != null ? at.X - XOf(stop.Position) : 0.0;
		if (stop == null && at.Y < GradientHeight && at.X >= Inset && at.X <= Bounds.Width - Inset)
		{
			// a press on the gradient: a new stop there, which leaves the gradient as it looks, dragged on from here
			stop = scheme.AddStop(PositionAt(at.X));
		}
		if (stop == null)
		{
			return;
		}
		Select(stop);
		_pressX = at.X;
		_moved = false;
		_dragging = true;
		_pointer = e.Pointer;
		e.Pointer.Capture(this);
	}

	/// <inheritdoc/>
	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		Point at = e.GetPosition(this);
		if (!_dragging)
		{
			// the cursor is made on first use: a cursor needs the platform, which is not up when the type loads
			Cursor = MarkerAt(at) != null ? _markerCursor ??= new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
			return;
		}
		// the stop follows from the pointer's first step away from the press: a click on a marker selects it and changes
		// nothing, where the marker's place taken back from its pixel would round the position
		_moved |= at.X != _pressX;
		if (SelectedStop is not { } stop || Scheme is not { } scheme)
		{
			return;
		}
		// pulled away above or below the bar: the marker fades and its stop goes at the release; back in reach it stays
		bool away = scheme.Stops.Count > 1 && (at.Y < -PullAway || at.Y > Bounds.Height + PullAway);
		if (away != _pulledAway)
		{
			_pulledAway = away;
			InvalidateVisual();
		}
		if (_moved)
		{
			scheme.MoveStop(stop, PositionAt(at.X - _grab));
		}
	}

	/// <inheritdoc/>
	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		if (_dragging)
		{
			ColorStop? pulledAway = _pulledAway ? SelectedStop : null;
			EndDrag();
			if (pulledAway != null)
			{
				Remove(pulledAway, byCommand: false);
			}
			e.Handled = true;
		}
	}

	/// <inheritdoc/>
	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		base.OnPointerCaptureLost(e);
		// another control took the pointer mid-drag (a popup, a window switch): the drag ends there
		_dragging = false;
		_pointer = null;
		if (_pulledAway)
		{
			_pulledAway = false;
			InvalidateVisual();
		}
	}

	private void EndDrag()
	{
		if (_pulledAway)
		{
			_pulledAway = false;
			InvalidateVisual();
		}
		if (_dragging)
		{
			_dragging = false;
			IPointer? pointer = _pointer;
			_pointer = null;
			pointer?.Capture(null);
		}
	}

	/// <summary>
	/// Removes a stop, by the command (the keyboard: a step of its own) or from the scheme itself (the mouse, whose press
	/// and release bound the step), and selects the stop that took its place along the gradient, the last one after the
	/// last. Nothing happens to the scheme's last stop.
	/// </summary>
	private void Remove(ColorStop stop, bool byCommand)
	{
		if (Scheme is not { } scheme)
		{
			return;
		}
		List<ColorStop> sorted = Sorted(scheme);
		int index = sorted.IndexOf(stop);
		if (byCommand)
		{
			if (RemoveCommand is { } command && command.CanExecute(stop))
			{
				command.Execute(stop);
			}
		}
		else
		{
			scheme.RemoveStop(stop);
		}
		if (index >= 0 && !scheme.Stops.Contains(stop))
		{
			sorted.RemoveAt(index);
			Select(sorted.Count > 0 ? sorted[Math.Min(index, sorted.Count - 1)] : null);
		}
	}

	// ---------------------------------------------------------------- keyboard

	/// <inheritdoc/>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		KeyModifiers modifiers = e.KeyModifiers;
		if (Scheme is not { } scheme || scheme.Stops.Count == 0 || (modifiers & ~KeyModifiers.Shift) != 0)
		{
			return;
		}
		List<ColorStop> sorted = Sorted(scheme);
		ColorStop stop = SelectedStop ?? sorted[0];
		int index = sorted.IndexOf(stop);
		switch (e.Key)
		{
			case Key.Left:
			case Key.Right:
				double step = modifiers == KeyModifiers.Shift ? LargeKeyStep : KeyStep;
				Select(stop);
				scheme.MoveStop(stop, Math.Round(stop.Position + (e.Key == Key.Left ? -step : step), Decimals));
				break;
			case Key.PageUp when modifiers == KeyModifiers.None:
				Select(sorted[Math.Max(index - 1, 0)]);
				break;
			case Key.PageDown when modifiers == KeyModifiers.None:
				Select(sorted[Math.Min(index + 1, sorted.Count - 1)]);
				break;
			case Key.Delete when modifiers == KeyModifiers.None:
				// handled whether or not a stop goes: Del must never reach the window's Delete from here, which removes the scheme
				Select(stop);
				Remove(stop, byCommand: true);
				break;
			default:
				return;
		}
		e.Handled = true;
	}

	/// <inheritdoc/>
	protected override void OnGotFocus(FocusChangedEventArgs e)
	{
		base.OnGotFocus(e);
		// the keys act on the selected stop: with none yet, the first along the gradient, so its marker says so
		if (SelectedStop == null && Scheme is { Stops.Count: > 0 } scheme)
		{
			_selected = Sorted(scheme)[0];
		}
		InvalidateVisual();
	}

	/// <inheritdoc/>
	protected override void OnLostFocus(FocusChangedEventArgs e)
	{
		base.OnLostFocus(e);
		InvalidateVisual();
	}
}

/// <summary>
/// Exposes a <see cref="GradientBar"/> to UI Automation, so it can take the focus by its id and a harness can read it:
/// the item status is "stops p p p; selected p", the positions along the gradient in invariant spelling, the selected
/// stop's after them or "none".
/// </summary>
public sealed class GradientBarAutomationPeer : ControlAutomationPeer
{
	/// <summary>A peer of <paramref name="owner"/>.</summary>
	public GradientBarAutomationPeer(GradientBar owner) : base(owner)
	{
	}

	private GradientBar Bar => (GradientBar)Owner;

	/// <inheritdoc/>
	protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

	/// <inheritdoc/>
	protected override string GetClassNameCore() => nameof(GradientBar);

	/// <inheritdoc/>
	protected override string? GetItemStatusCore()
	{
		static string Spell(double position) => position.ToString("0.###", CultureInfo.InvariantCulture);
		IEnumerable<ColorStop> stops = Bar.Scheme?.Stops ?? Enumerable.Empty<ColorStop>();
		return "stops " + string.Join(" ", stops.Select(s => s.Position).Order().Select(Spell)) + "; selected " + (Bar.SelectedStop is { } stop ? Spell(stop.Position) : "none");
	}

	/// <inheritdoc/>
	protected override bool IsContentElementCore() => true;

	/// <inheritdoc/>
	protected override bool IsControlElementCore() => true;
}
