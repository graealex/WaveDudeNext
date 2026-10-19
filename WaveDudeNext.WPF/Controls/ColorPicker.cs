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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// The colour picker inside a <see cref="ColorPickerButton"/>'s popup (2026-10-15, the user's choice of an in-house
/// picker over the Windows dialog): a saturation/value square under the pure hue, a hue strip, red, green and blue
/// number boxes and the hex text, all editing the one <see cref="Color"/>, which the owner binds two-way. Keyboard:
/// the square and the strip are focusable; on the square the arrow keys move saturation (left, right) and value
/// (up, down) by 1/100, on the strip Up and Down move the hue by a degree, ten with Shift; the mouse drags both
/// markers. The HSV side keeps its hue for a grey (a grey has no hue) and whenever the colour set from outside is
/// what the current hue, saturation and value already give, so a drag never jumps. **The owner is told at most
/// ten times a second** (2026-10-15, after a drag felt sluggish): a change of hue, saturation or value moves the
/// markers and recolours the square at once, but <see cref="Color"/>, which the owner binds and which sets off the
/// scheme's change, the gradient bar, the dirty flag and a re-render of both preview pictures, is committed after
/// <see cref="CommitInterval"/> at most, and once more when a drag ends; a colour set from outside cancels a
/// pending commit (Escape puts the old colour back and nothing may overwrite it). The template in
/// <c>Editors.xaml</c> names PART_Square and PART_HueStrip (both <see cref="Thumb"/>s: a thumb captures the mouse
/// for the drag by itself and has an automation peer, so a harness sees which one has the focus; a Border has
/// neither) and PART_SquareMarker and PART_HueMarker, which sit on a Canvas and are placed from here. UI thread only.
/// </summary>
[TemplatePart(Name = SquarePart, Type = typeof(Thumb))]
[TemplatePart(Name = SquareMarkerPart, Type = typeof(FrameworkElement))]
[TemplatePart(Name = HueStripPart, Type = typeof(Thumb))]
[TemplatePart(Name = HueMarkerPart, Type = typeof(FrameworkElement))]
public class ColorPicker : Control
{
	private const string SquarePart = "PART_Square";
	private const string SquareMarkerPart = "PART_SquareMarker";
	private const string HueStripPart = "PART_HueStrip";
	private const string HueMarkerPart = "PART_HueMarker";

	/// <summary>Identifies the <see cref="Color"/> property.</summary>
	public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
		nameof(Color), typeof(Rgb), typeof(ColorPicker),
		new FrameworkPropertyMetadata(default(Rgb), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

	/// <summary>Identifies the <see cref="Hue"/> property.</summary>
	public static readonly DependencyProperty HueProperty = DependencyProperty.Register(
		nameof(Hue), typeof(double), typeof(ColorPicker), new PropertyMetadata(0.0, OnHsvChanged));

	/// <summary>Identifies the <see cref="Saturation"/> property.</summary>
	public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(
		nameof(Saturation), typeof(double), typeof(ColorPicker), new PropertyMetadata(0.0, OnHsvChanged));

	/// <summary>Identifies the <see cref="Value"/> property.</summary>
	public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
		nameof(Value), typeof(double), typeof(ColorPicker), new PropertyMetadata(0.0, OnHsvChanged));

	/// <summary>Identifies the <see cref="Red"/> property.</summary>
	public static readonly DependencyProperty RedProperty = DependencyProperty.Register(
		nameof(Red), typeof(int), typeof(ColorPicker), new PropertyMetadata(0, OnChannelChanged));

	/// <summary>Identifies the <see cref="Green"/> property.</summary>
	public static readonly DependencyProperty GreenProperty = DependencyProperty.Register(
		nameof(Green), typeof(int), typeof(ColorPicker), new PropertyMetadata(0, OnChannelChanged));

	/// <summary>Identifies the <see cref="Blue"/> property.</summary>
	public static readonly DependencyProperty BlueProperty = DependencyProperty.Register(
		nameof(Blue), typeof(int), typeof(ColorPicker), new PropertyMetadata(0, OnChannelChanged));

	/// <summary>Identifies the <see cref="HueBrush"/> property.</summary>
	public static readonly DependencyProperty HueBrushProperty = DependencyProperty.Register(
		nameof(HueBrush), typeof(Brush), typeof(ColorPicker), new PropertyMetadata(Brushes.Red));

	/// <summary>The least time between two commits of <see cref="Color"/> to the owner while the picker is being worked.</summary>
	public const int CommitInterval = 100;

	private readonly DispatcherTimer _commitTimer;
	private Thumb? _square, _strip;
	private FrameworkElement? _squareMarker, _hueMarker;
	private bool _updating, _pending;
	private Rgb _pendingColor;
	private long _lastCommit;

	/// <summary>Builds the picker with its commit timer.</summary>
	public ColorPicker()
	{
		_commitTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(CommitInterval) };
		_commitTimer.Tick += (_, _) => Commit();
	}

	/// <summary>The colour being edited; every other property is a view of it.</summary>
	public Rgb Color
	{
		get => (Rgb)GetValue(ColorProperty);
		set => SetValue(ColorProperty, value);
	}

	/// <summary>Hue in degrees, 0 to 360 (red at 0, green at 120, blue at 240).</summary>
	public double Hue
	{
		get => (double)GetValue(HueProperty);
		set => SetValue(HueProperty, value);
	}

	/// <summary>Saturation 0 (grey) to 1 (the pure hue).</summary>
	public double Saturation
	{
		get => (double)GetValue(SaturationProperty);
		set => SetValue(SaturationProperty, value);
	}

	/// <summary>Value (brightness) 0 (black) to 1.</summary>
	public double Value
	{
		get => (double)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	/// <summary>The red channel, 0 to 255.</summary>
	public int Red
	{
		get => (int)GetValue(RedProperty);
		set => SetValue(RedProperty, value);
	}

	/// <summary>The green channel, 0 to 255.</summary>
	public int Green
	{
		get => (int)GetValue(GreenProperty);
		set => SetValue(GreenProperty, value);
	}

	/// <summary>The blue channel, 0 to 255.</summary>
	public int Blue
	{
		get => (int)GetValue(BlueProperty);
		set => SetValue(BlueProperty, value);
	}

	/// <summary>The pure hue at full saturation and value, the square's background.</summary>
	public Brush HueBrush
	{
		get => (Brush)GetValue(HueBrushProperty);
		set => SetValue(HueBrushProperty, value);
	}

	/// <summary>Puts the keyboard focus on the saturation/value square (the popup does this when it opens).</summary>
	public void FocusSquare() => _square?.Focus();

	/// <inheritdoc />
	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		_square = GetTemplateChild(SquarePart) as Thumb;
		_strip = GetTemplateChild(HueStripPart) as Thumb;
		// the markers live in the thumbs' own templates, which have to be applied before their names resolve
		_squareMarker = MarkerOf(_square, SquareMarkerPart);
		_hueMarker = MarkerOf(_strip, HueMarkerPart);
		if (_square != null)
		{
			_square.DragStarted += OnSquareDrag;
			_square.DragDelta += OnSquareDrag;
			_square.DragCompleted += (_, _) => Commit();
			_square.SizeChanged += (_, _) => UpdateMarkers();
		}
		if (_strip != null)
		{
			_strip.DragStarted += OnStripDrag;
			_strip.DragDelta += OnStripDrag;
			_strip.DragCompleted += (_, _) => Commit();
			_strip.SizeChanged += (_, _) => UpdateMarkers();
		}
		ApplyColor(Color);
	}

	private static FrameworkElement? MarkerOf(Thumb? thumb, string name)
	{
		if (thumb == null)
		{
			return null;
		}
		thumb.ApplyTemplate();
		return thumb.Template?.FindName(name, thumb) as FrameworkElement;
	}

	private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorPicker)d).ApplyColor((Rgb)e.NewValue);

	/// <summary>The colour changed from outside (the owner, the hex box or the channels): the other views follow.</summary>
	private void ApplyColor(Rgb color)
	{
		if (_updating)
		{
			return;
		}
		_commitTimer.Stop();
		_pending = false;
		_updating = true;
		try
		{
			Red = color.R;
			Green = color.G;
			Blue = color.B;
			if (Rgb.FromHsv(Hue, Saturation, Value) != color)
			{
				(double hue, double saturation, double value) = color.ToHsv();
				if (saturation > 0 && value > 0)
				{
					Hue = hue;
				}
				Saturation = saturation;
				Value = value;
				UpdateHueBrush();
			}
		}
		finally
		{
			_updating = false;
		}
		UpdateMarkers();
	}

	/// <summary>
	/// Hue, saturation or value changed (keys, mouse or a binding): the channels, the square and the markers follow at
	/// once, the owner's colour through the throttled commit.
	/// </summary>
	private static void OnHsvChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var picker = (ColorPicker)d;
		if (picker._updating)
		{
			return;
		}
		picker._updating = true;
		Rgb color;
		try
		{
			color = Rgb.FromHsv(picker.Hue, picker.Saturation, picker.Value);
			picker.Red = color.R;
			picker.Green = color.G;
			picker.Blue = color.B;
			picker.UpdateHueBrush();
		}
		finally
		{
			picker._updating = false;
		}
		picker.UpdateMarkers();
		picker.RequestCommit(color);
	}

	/// <summary>Commits now when the last commit is <see cref="CommitInterval"/> or more ago, else when the timer fires.</summary>
	private void RequestCommit(Rgb color)
	{
		_pendingColor = color;
		_pending = true;
		if (Environment.TickCount64 - _lastCommit >= CommitInterval)
		{
			Commit();
		}
		else if (!_commitTimer.IsEnabled)
		{
			_commitTimer.Start();
		}
	}

	/// <summary>Hands the pending colour to the owner; the colour's own change handler is skipped, the views are current.</summary>
	private void Commit()
	{
		_commitTimer.Stop();
		if (!_pending)
		{
			return;
		}
		_pending = false;
		_lastCommit = Environment.TickCount64;
		_updating = true;
		try
		{
			Color = _pendingColor;
		}
		finally
		{
			_updating = false;
		}
	}

	/// <summary>A channel box changed: the colour follows, and through it the HSV side.</summary>
	private static void OnChannelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var picker = (ColorPicker)d;
		if (!picker._updating)
		{
			picker.Color = Rgb.Clamped(picker.Red, picker.Green, picker.Blue);
		}
	}

	private void UpdateHueBrush()
	{
		Rgb pure = Rgb.FromHsv(Hue, 1, 1);
		HueBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(pure.R, pure.G, pure.B));
	}

	/// <summary>Places the two markers: saturation along the square, value up it, hue down the strip.</summary>
	private void UpdateMarkers()
	{
		if (_square != null && _squareMarker != null)
		{
			Canvas.SetLeft(_squareMarker, Saturation * _square.ActualWidth - _squareMarker.Width / 2);
			Canvas.SetTop(_squareMarker, (1 - Value) * _square.ActualHeight - _squareMarker.Height / 2);
		}
		if (_strip != null && _hueMarker != null)
		{
			Canvas.SetTop(_hueMarker, Hue / 360 * _strip.ActualHeight - _hueMarker.Height / 2);
		}
	}

	// ---------------------------------------------------------------- mouse (the thumbs capture it; the position is read, not the delta)

	private void OnSquareDrag(object sender, RoutedEventArgs e)
	{
		_square!.Focus();
		Point p = Mouse.GetPosition(_square);
		Saturation = Math.Clamp(p.X / _square.ActualWidth, 0, 1);
		Value = Math.Clamp(1 - p.Y / _square.ActualHeight, 0, 1);
	}

	private void OnStripDrag(object sender, RoutedEventArgs e)
	{
		_strip!.Focus();
		Hue = Math.Clamp(Mouse.GetPosition(_strip).Y / _strip.ActualHeight, 0, 1) * 360 % 360;
	}

	// ---------------------------------------------------------------- keyboard

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e)
	{
		bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		if (_square?.IsKeyboardFocused == true)
		{
			double step = shift ? 0.1 : 0.01;
			switch (e.Key)
			{
				case Key.Left: Saturation = Math.Clamp(Saturation - step, 0, 1); break;
				case Key.Right: Saturation = Math.Clamp(Saturation + step, 0, 1); break;
				case Key.Up: Value = Math.Clamp(Value + step, 0, 1); break;
				case Key.Down: Value = Math.Clamp(Value - step, 0, 1); break;
				default: base.OnKeyDown(e); return;
			}
			e.Handled = true;
			return;
		}
		if (_strip?.IsKeyboardFocused == true)
		{
			double degrees = shift ? 10 : 1;
			switch (e.Key)
			{
				case Key.Up: Hue = (Hue - degrees + 360) % 360; break;
				case Key.Down: Hue = (Hue + degrees) % 360; break;
				default: base.OnKeyDown(e); return;
			}
			e.Handled = true;
			return;
		}
		base.OnKeyDown(e);
	}
}
