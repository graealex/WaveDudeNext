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

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// A text box for one number with up/down buttons (the usual spinner, asked for 2026-10-12): Up/Down
/// change the value by <see cref="Increment"/>, PageUp/PageDown by ten increments, the mouse wheel
/// does the same while the box has keyboard focus, and the two repeat buttons on the right do it with
/// the mouse, and a horizontal drag on the text itself scrubs the value, Blender style (the user's request,
/// 2026-10-18): press, move sideways past the drag threshold and the value follows the mouse, one increment
/// per <see cref="PixelsPerStep"/> pixels, ten times finer with Shift, ten times coarser with Ctrl; Escape during
/// the drag restores the value the drag started from; a press without movement stays an ordinary click that
/// places the caret. Every step clamps to <see cref="Minimum"/>..<see cref="Maximum"/>, writes the text back
/// through the <see cref="TextBox.Text"/> binding at once and selects it, so a source moves on the
/// picture with each key press. Typing stays plain text: the binding converts and the model clamps as
/// before, and Enter/Escape come from <see cref="EditBehavior"/> through the base text box style.
/// Numbers are parsed and written in the current culture, like the bindings. UI thread.
/// </summary>
public sealed class NumberBox : TextBox
{
	/// <summary>Identifies the <see cref="Minimum"/> property.</summary>
	public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
		nameof(Minimum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.NegativeInfinity));

	/// <summary>Identifies the <see cref="Maximum"/> property.</summary>
	public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
		nameof(Maximum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.PositiveInfinity));

	/// <summary>Identifies the <see cref="Increment"/> property.</summary>
	public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(
		nameof(Increment), typeof(double), typeof(NumberBox), new PropertyMetadata(1.0));

	/// <summary>Steps per PageUp/PageDown and per wheel notch with Shift.</summary>
	private const int PageSteps = 10;

	/// <summary>Mouse pixels per increment while scrubbing; Shift makes it ten times as many, Ctrl a tenth.</summary>
	public const double PixelsPerStep = 2.0;

	private Point? _pressPoint;
	private double _pressValue;
	private bool _scrubbing;

	/// <summary>Lowest value a step can reach (typed text is not limited here; the model clamps it).</summary>
	public double Minimum
	{
		get => (double)GetValue(MinimumProperty);
		set => SetValue(MinimumProperty, value);
	}

	/// <summary>Highest value a step can reach.</summary>
	public double Maximum
	{
		get => (double)GetValue(MaximumProperty);
		set => SetValue(MaximumProperty, value);
	}

	/// <summary>Amount of one Up/Down step; 1 for integers, a fraction for real-valued settings.</summary>
	public double Increment
	{
		get => (double)GetValue(IncrementProperty);
		set => SetValue(IncrementProperty, value);
	}

	/// <summary>The value the text holds, or null while it does not parse.</summary>
	public double? Value => double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ? value : null;

	/// <summary>
	/// Adds <paramref name="steps"/> increments to the value (a non-parsing text counts as 0), clamps,
	/// writes the text and pushes it into the binding. Rounded to the increment's precision so that
	/// 0.1 steps do not accumulate binary noise.
	/// </summary>
	public void Step(int steps)
	{
		if (IsReadOnly || !IsEnabled)
		{
			return;
		}
		Write((Value ?? 0.0) + steps * Increment);
	}

	/// <summary>Clamps <paramref name="value"/>, rounds it to the increment's precision, writes the text, pushes it into the binding and selects it.</summary>
	private void Write(double value)
	{
		int decimals = Math.Clamp(DecimalsOf(Increment), 0, 10);
		value = Math.Round(Math.Clamp(value, Minimum, Maximum), decimals);
		Text = value.ToString(CultureInfo.CurrentCulture);
		GetBindingExpression(TextProperty)?.UpdateSource();
		SelectAll();
	}

	/// <summary>How many decimals the increment needs ("0.01" → 2); 0 for whole numbers.</summary>
	private static int DecimalsOf(double increment)
	{
		int decimals = 0;
		while (decimals < 10 && Math.Abs(increment * Math.Pow(10, decimals) - Math.Round(increment * Math.Pow(10, decimals))) > 1e-9)
		{
			decimals++;
		}
		return decimals;
	}

	/// <inheritdoc/>
	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		if (GetTemplateChild("PART_Up") is RepeatButton up)
		{
			up.Click += (_, _) => StepFromButton(1);
		}
		if (GetTemplateChild("PART_Down") is RepeatButton down)
		{
			down.Click += (_, _) => StepFromButton(-1);
		}
	}

	/// <summary>A button step also takes the focus, so the written value is visibly the box's and the wheel works afterwards.</summary>
	private void StepFromButton(int direction)
	{
		if (!IsKeyboardFocusWithin)
		{
			Focus();
		}
		Step(direction);
	}

	// ---------------------------------------------------------------- scrubbing: a horizontal drag on the text changes the value

	/// <inheritdoc/>
	protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnPreviewMouseLeftButtonDown(e);
		_pressPoint = null;
		// a press on the repeat buttons is theirs; one on the text may become a scrub, or stay a click
		if (e.Handled || IsReadOnly || !IsEnabled || IsOnRepeatButton(e.OriginalSource))
		{
			return;
		}
		_pressPoint = e.GetPosition(this);
		_pressValue = Value ?? 0.0;
	}

	/// <inheritdoc/>
	protected override void OnPreviewMouseMove(MouseEventArgs e)
	{
		base.OnPreviewMouseMove(e);
		if (_pressPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		double dx = e.GetPosition(this).X - start.X;
		if (!_scrubbing)
		{
			if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance)
			{
				return;
			}
			_scrubbing = true;
			CaptureMouse();
			Cursor = Cursors.SizeWE;
			if (!IsKeyboardFocusWithin)
			{
				Focus();
			}
		}
		double pixels = PixelsPerStep;
		if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
		{
			pixels *= 10.0;
		}
		else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
		{
			pixels /= 10.0;
		}
		Write(_pressValue + Math.Round(dx / pixels) * Increment);
		e.Handled = true;   // not a text selection
	}

	/// <inheritdoc/>
	protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
	{
		base.OnPreviewMouseLeftButtonUp(e);
		if (_scrubbing)
		{
			EndScrub();
			e.Handled = true;
		}
		_pressPoint = null;
	}

	/// <inheritdoc/>
	protected override void OnLostMouseCapture(MouseEventArgs e)
	{
		base.OnLostMouseCapture(e);
		if (_scrubbing)
		{
			EndScrub();
		}
	}

	private void EndScrub()
	{
		_scrubbing = false;
		_pressPoint = null;
		ClearValue(CursorProperty);
		if (IsMouseCaptured)
		{
			ReleaseMouseCapture();
		}
	}

	private static bool IsOnRepeatButton(object source)
	{
		for (DependencyObject? d = source as DependencyObject; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : null)
		{
			if (d is RepeatButton)
			{
				return true;
			}
			if (d is NumberBox)
			{
				return false;
			}
		}
		return false;
	}

	/// <inheritdoc/>
	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		base.OnPreviewKeyDown(e);
		if (e.Handled)
		{
			return;
		}
		if (_scrubbing && e.Key == Key.Escape)
		{
			// the drag is abandoned: back to the value it started from
			Write(_pressValue);
			EndScrub();
			e.Handled = true;
			return;
		}
		int steps = e.Key switch
		{
			Key.Up => 1,
			Key.Down => -1,
			Key.PageUp => PageSteps,
			Key.PageDown => -PageSteps,
			_ => 0,
		};
		if (steps != 0)
		{
			Step(steps);
			e.Handled = true;
		}
	}

	/// <inheritdoc/>
	protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
	{
		base.OnPreviewMouseWheel(e);
		if (e.Handled || !IsKeyboardFocusWithin || e.Delta == 0)
		{
			return;
		}
		int steps = Math.Sign(e.Delta) * ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? PageSteps : 1);
		Step(steps);
		e.Handled = true;
	}
}
