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
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// A colour swatch that opens a <see cref="ColorPicker"/> in a popup below it (2026-10-15): the stop rows of the
/// colour scheme editor use it next to the hex box. A toggle button whose checked state is the popup; Space, Enter
/// or Down open it, the focus goes to the picker's square, the colour follows every move at once (the gradient and
/// the preview recolour live, as the hex box does), Enter keeps and closes, Escape puts the colour back as it was
/// and closes, and a click outside closes (the click that lands on the swatch itself is swallowed, else it would
/// reopen the popup at once). The template in <c>Editors.xaml</c> names PART_Popup and PART_Picker. UI thread only.
/// </summary>
[TemplatePart(Name = PopupPart, Type = typeof(Popup))]
[TemplatePart(Name = PickerPart, Type = typeof(ColorPicker))]
public class ColorPickerButton : ToggleButton
{
	private const string PopupPart = "PART_Popup";
	private const string PickerPart = "PART_Picker";

	/// <summary>Identifies the <see cref="Color"/> property.</summary>
	public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
		nameof(Color), typeof(Rgb), typeof(ColorPickerButton),
		new FrameworkPropertyMetadata(default(Rgb), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

	private Popup? _popup;
	private ColorPicker? _picker;
	private Rgb _original;
	private long _closedAt;

	/// <summary>The colour shown and edited; bind it two-way to the model's colour.</summary>
	public Rgb Color
	{
		get => (Rgb)GetValue(ColorProperty);
		set => SetValue(ColorProperty, value);
	}

	/// <inheritdoc />
	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		if (_popup != null)
		{
			_popup.Opened -= OnOpened;
			_popup.Closed -= OnClosed;
			_popup.PreviewKeyDown -= OnPopupKeyDown;
		}
		_popup = GetTemplateChild(PopupPart) as Popup;
		_picker = GetTemplateChild(PickerPart) as ColorPicker;
		if (_popup != null)
		{
			_popup.Opened += OnOpened;
			_popup.Closed += OnClosed;
			_popup.PreviewKeyDown += OnPopupKeyDown;
		}
	}

	private void OnOpened(object? sender, EventArgs e)
	{
		_original = Color;
		Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _picker?.FocusSquare());
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		_closedAt = Environment.TickCount64;
		IsChecked = false;
		Focus();
	}

	/// <summary>Enter commits a text box being edited and keeps the colour; Escape puts the original back; both close.</summary>
	private void OnPopupKeyDown(object sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
			case Key.Enter:
				if (Keyboard.FocusedElement is TextBox box)
				{
					box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
				}
				IsChecked = false;
				e.Handled = true;
				break;
			case Key.Escape:
				Color = _original;
				IsChecked = false;
				e.Handled = true;
				break;
		}
	}

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Down && IsChecked != true)
		{
			IsChecked = true;
			e.Handled = true;
			return;
		}
		base.OnKeyDown(e);
	}

	/// <summary>The click that closed the popup (a click outside it, on this swatch) must not reopen it.</summary>
	protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		if (Environment.TickCount64 - _closedAt < 250)
		{
			e.Handled = true;
			return;
		}
		base.OnPreviewMouseLeftButtonDown(e);
	}
}
