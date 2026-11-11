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

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using WaveDudeNext.Rendering;
using WaveDudeNext.WPF.Converters;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// The gradient of a colour scheme with a marker under each stop (2026-11-11, the user's request: the gradient editor
/// of a contributed port, WaveDudeRust, replicated): a triangle in the stop's colour whose tip points at the stop's
/// place on the gradient. Mouse: a marker dragged moves its stop, which changes places with the stops it passes
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
/// <see cref="OnRender"/>, the colours from the brush properties; the gradient's outline is a whole number of device
/// pixels on the pixel grid at any scale, the markers are antialiased shapes. UI thread.
/// </summary>
public sealed class GradientBar : FrameworkElement
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
	public static readonly DependencyProperty SchemeProperty = DependencyProperty.Register(
		nameof(Scheme), typeof(ColorScheme), typeof(GradientBar),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSchemeChanged));

	/// <summary>Identifies the <see cref="RemoveCommand"/> property.</summary>
	public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(
		nameof(RemoveCommand), typeof(ICommand), typeof(GradientBar));

	/// <summary>Identifies the <see cref="OutlineBrush"/> property: the gradient's border.</summary>
	public static readonly DependencyProperty OutlineBrushProperty = DependencyProperty.Register(
		nameof(OutlineBrush), typeof(Brush), typeof(GradientBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Identifies the <see cref="MarkerBrush"/> property: a marker's outline.</summary>
	public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
		nameof(MarkerBrush), typeof(Brush), typeof(GradientBar), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Identifies the <see cref="AccentBrush"/> property: the selected marker and the keyboard focus.</summary>
	public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
		nameof(AccentBrush), typeof(Brush), typeof(GradientBar), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

	private readonly Dictionary<(Brush Brush, double Thickness), Pen> _pens = new();
	private ColorStop? _selected;
	private Brush? _fill;
	private bool _dragging;
	private bool _moved;
	private bool _pulledAway;
	private double _pressX;
	private double _grab;

	/// <summary>A focusable bar that shows the keyboard focus itself, as the gradient's outline in the accent colour.</summary>
	public GradientBar()
	{
		Focusable = true;
		FocusVisualStyle = null;
		SnapsToDevicePixels = true;
	}

	/// <summary>The scheme whose gradient is shown and whose stops the markers move; the bar follows its every change.</summary>
	public ColorScheme? Scheme
	{
		get => (ColorScheme?)GetValue(SchemeProperty);
		set => SetValue(SchemeProperty, value);
	}

	/// <summary>Executed with the selected stop (a <see cref="ColorStop"/>) when Del is pressed; removes it from the scheme.</summary>
	public ICommand? RemoveCommand
	{
		get => (ICommand?)GetValue(RemoveCommandProperty);
		set => SetValue(RemoveCommandProperty, value);
	}

	/// <summary>The gradient's border.</summary>
	public Brush OutlineBrush
	{
		get => (Brush)GetValue(OutlineBrushProperty);
		set => SetValue(OutlineBrushProperty, value);
	}

	/// <summary>A marker's outline: a colour that stands out from the form's background, whatever the stop's own.</summary>
	public Brush MarkerBrush
	{
		get => (Brush)GetValue(MarkerBrushProperty);
		set => SetValue(MarkerBrushProperty, value);
	}

	/// <summary>The selected marker, fill and outline, and the gradient's border while the bar has the keyboard focus.</summary>
	public Brush AccentBrush
	{
		get => (Brush)GetValue(AccentBrushProperty);
		set => SetValue(AccentBrushProperty, value);
	}

	/// <summary>
	/// The stop the keys act on, whose marker is drawn in the accent's colour; null for none, also once the stop has
	/// left the scheme (removed in its row, or an undo, which refills the list with new stops).
	/// </summary>
	public ColorStop? SelectedStop => _selected != null && Scheme?.Stops.Contains(_selected) == true ? _selected : null;

	private static void OnSchemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var bar = (GradientBar)d;
		// weak, so a scheme that outlives its form does not keep the form's bar
		if (e.OldValue is ColorScheme old)
		{
			PropertyChangedEventManager.RemoveHandler(old, bar.OnStopsChanged, nameof(ColorScheme.Stops));
		}
		if (e.NewValue is ColorScheme scheme)
		{
			PropertyChangedEventManager.AddHandler(scheme, bar.OnStopsChanged, nameof(ColorScheme.Stops));
		}
		bar.EndDrag();
		bar._selected = null;
		bar._fill = null;
	}

	/// <summary>The scheme raises <c>Stops</c> at every change of a stop or of the list.</summary>
	private void OnStopsChanged(object? sender, PropertyChangedEventArgs e)
	{
		_fill = null;
		InvalidateVisual();
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
	private double TrackWidth => Math.Max(1.0, ActualWidth - 2 * Inset - 1.0);

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
	protected override void OnRender(DrawingContext dc)
	{
		double w = ActualWidth;
		if (Scheme is not { } scheme || scheme.Stops.Count == 0 || w <= 2 * Inset + 2)
		{
			return;
		}
		// the whole control takes the mouse, the rows between the markers too (WPF hit-tests what is drawn)
		dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));

		// a line is a whole number of device pixels thick and its edges lie on guidelines, which WPF moves onto the device
		// grid under any scale (2026-11-02, the user's rule: a line is there or not, never cut); at 100 % the gradient and
		// its outline are the rectangle's that stood here before the markers
		double perDip = DevicePixels.PerDip(this);
		double t = DevicePixels.Hairline(perDip);
		double half = t / 2;
		dc.PushGuidelineSet(new GuidelineSet([Inset, Inset + t, w - Inset - t, w - Inset], [0, t, GradientHeight - t, GradientHeight]));
		dc.DrawRectangle(_fill ??= ColorSchemeBrushConverter.BrushOf(scheme.Stops), PenFor(IsKeyboardFocused ? AccentBrush : OutlineBrush, t),
			new Rect(Inset + half, half, w - 2 * Inset - t, GradientHeight - t));
		dc.Pop();

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
	/// the gradient above and in its row). The column's and the base line's edges are guidelines, so the tip's column and
	/// the base are whole pixels; the slanted sides are antialiased.
	/// </summary>
	private void DrawMarker(DrawingContext dc, ColorStop stop, bool selected, double t)
	{
		double half = t / 2;
		double column = Math.Floor(XOf(stop.Position));
		double x = column + half;
		double tip = GradientHeight + 1.0 + half;
		double baseTop = GradientHeight + 1.0 + MarkerHeight;
		Brush fill = AccentBrush;
		if (!selected)
		{
			fill = new SolidColorBrush(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B));
			fill.Freeze();
		}
		// a marker pulled away from the bar is on its way out: faded until it is released or comes back
		bool leaving = selected && _pulledAway;
		if (leaving)
		{
			dc.PushOpacity(PulledAwayOpacity);
		}
		dc.PushGuidelineSet(new GuidelineSet([column, column + t], [baseTop, baseTop + t]));
		dc.DrawGeometry(fill, PenFor(selected ? AccentBrush : MarkerBrush, t), Triangle(x, tip, baseTop + half, MarkerHalfWidth));
		dc.Pop();
		if (leaving)
		{
			dc.Pop();
		}
	}

	/// <summary>A triangle with its tip at (<paramref name="x"/>, <paramref name="tip"/>) and its base <paramref name="reach"/> to either side at <paramref name="bottom"/>.</summary>
	private static StreamGeometry Triangle(double x, double tip, double bottom, double reach)
	{
		var triangle = new StreamGeometry();
		using (StreamGeometryContext g = triangle.Open())
		{
			g.BeginFigure(new Point(x, tip), true, true);
			g.LineTo(new Point(x + reach, bottom), true, false);
			g.LineTo(new Point(x - reach, bottom), true, false);
		}
		triangle.Freeze();
		return triangle;
	}

	/// <summary>A frozen pen of <paramref name="brush"/> and <paramref name="thickness"/>, made once per pair (the theme brushes are shared instances).</summary>
	private Pen PenFor(Brush brush, double thickness)
	{
		if (!_pens.TryGetValue((brush, thickness), out Pen? pen))
		{
			pen = new Pen(brush, thickness);
			pen.Freeze();
			_pens[(brush, thickness)] = pen;
		}
		return pen;
	}

	// ---------------------------------------------------------------- mouse

	/// <inheritdoc/>
	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonDown(e);
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
		if (stop == null && at.Y < GradientHeight && at.X >= Inset && at.X <= ActualWidth - Inset)
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
		_dragging = CaptureMouse();
	}

	/// <inheritdoc/>
	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		Point at = e.GetPosition(this);
		if (!_dragging)
		{
			Cursor = MarkerAt(at) != null ? Cursors.SizeWE : null;
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
		bool away = scheme.Stops.Count > 1 && (at.Y < -PullAway || at.Y > ActualHeight + PullAway);
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
	protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonUp(e);
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
	protected override void OnLostMouseCapture(MouseEventArgs e)
	{
		base.OnLostMouseCapture(e);
		_dragging = false;
		if (_pulledAway)
		{
			_pulledAway = false;
			InvalidateVisual();
		}
	}

	private void EndDrag()
	{
		if (_dragging)
		{
			_dragging = false;
			ReleaseMouseCapture();
		}
		if (_pulledAway)
		{
			_pulledAway = false;
			InvalidateVisual();
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
			Execute(RemoveCommand, stop);
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
		ModifierKeys modifiers = Keyboard.Modifiers;
		if (Scheme is not { } scheme || scheme.Stops.Count == 0 || (modifiers & ~ModifierKeys.Shift) != 0)
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
				double step = modifiers == ModifierKeys.Shift ? LargeKeyStep : KeyStep;
				Select(stop);
				scheme.MoveStop(stop, Math.Round(stop.Position + (e.Key == Key.Left ? -step : step), Decimals));
				break;
			case Key.PageUp when modifiers == ModifierKeys.None:
				Select(sorted[Math.Max(index - 1, 0)]);
				break;
			case Key.PageDown when modifiers == ModifierKeys.None:
				Select(sorted[Math.Min(index + 1, sorted.Count - 1)]);
				break;
			case Key.Delete when modifiers == ModifierKeys.None:
				// handled whether or not a stop goes: Del must never reach the window's Delete from here, which removes the scheme
				Select(stop);
				Remove(stop, byCommand: true);
				break;
			default:
				return;
		}
		e.Handled = true;
	}

	private void Execute(ICommand? command, ColorStop stop)
	{
		if (command is RoutedCommand routed)
		{
			if (routed.CanExecute(stop, this))
			{
				routed.Execute(stop, this);
			}
		}
		else if (command?.CanExecute(stop) == true)
		{
			command.Execute(stop);
		}
	}

	/// <inheritdoc/>
	protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
	{
		base.OnGotKeyboardFocus(e);
		// the keys act on the selected stop: with none yet, the first along the gradient, so its marker says so
		if (SelectedStop == null && Scheme is { Stops.Count: > 0 } scheme)
		{
			_selected = Sorted(scheme)[0];
		}
		InvalidateVisual();
	}

	/// <inheritdoc/>
	protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
	{
		base.OnLostKeyboardFocus(e);
		InvalidateVisual();
	}
}

/// <summary>
/// Exposes a <see cref="GradientBar"/> to UI Automation, so it can take the focus by its id and a harness can read it:
/// the item status is "stops p p p; selected p", the positions along the gradient in invariant spelling, the selected
/// stop's after them or "none".
/// </summary>
public sealed class GradientBarAutomationPeer : FrameworkElementAutomationPeer
{
	/// <summary>A peer of <paramref name="owner"/>.</summary>
	public GradientBarAutomationPeer(GradientBar owner) : base(owner)
	{
	}

	private GradientBar Bar => (GradientBar)Owner;

	/// <inheritdoc/>
	protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

	/// <inheritdoc/>
	protected override string GetLocalizedControlTypeCore() => "gradient";

	/// <inheritdoc/>
	protected override string GetClassNameCore() => nameof(GradientBar);

	/// <inheritdoc/>
	protected override string GetItemStatusCore()
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
