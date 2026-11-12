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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// Attached behaviour of the property editors' text boxes, upstream's <c>EditBehavior</c>: Enter pushes the text into
/// the bound property at once and selects the text for the next entry; Escape restores the property's value when the
/// text differs. Both keys are swallowed only when they did something, so Escape still reaches Stop from an unchanged
/// box. Boxes that accept Return keep it as a newline. In Avalonia a text binding writes on every keystroke by default,
/// so the editors bind their text with <c>UpdateSourceTrigger=LostFocus</c> (WPF's default) and this behaviour supplies
/// the commit on Enter through the binding expression. Not a static class, because an attached property's owner is a
/// generic type argument.
/// </summary>
public sealed class EditBehavior
{
	/// <summary>Identifies the <c>CommitOnEnter</c> attached property.</summary>
	public static readonly AttachedProperty<bool> CommitOnEnterProperty =
		AvaloniaProperty.RegisterAttached<EditBehavior, TextBox, bool>("CommitOnEnter");

	static EditBehavior()
	{
		CommitOnEnterProperty.Changed.AddClassHandler<TextBox>(OnCommitOnEnterChanged);
	}

	private EditBehavior()
	{
	}

	/// <summary>Gets <see cref="CommitOnEnterProperty"/>.</summary>
	public static bool GetCommitOnEnter(TextBox element) => element.GetValue(CommitOnEnterProperty);

	/// <summary>Sets <see cref="CommitOnEnterProperty"/>.</summary>
	public static void SetCommitOnEnter(TextBox element, bool value) => element.SetValue(CommitOnEnterProperty, value);

	private static void OnCommitOnEnterChanged(TextBox box, AvaloniaPropertyChangedEventArgs e)
	{
		box.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
		box.GotFocus -= OnGotFocus;
		if (e.GetNewValue<bool>())
		{
			// tunnelling, so the key is seen before the text box's own handling (WPF's PreviewKeyDown)
			box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
			box.GotFocus += OnGotFocus;
		}
	}

	/// <summary>
	/// A Tab into the box leaves the caret at the start with nothing selected, as WPF's text box does: Avalonia's selects the
	/// whole text on a Tab in its own OnGotFocus, with no property to say otherwise (read at 12.1.5; this handler runs after
	/// it). An editable combo box's text box keeps the selection, which WPF's combo box makes too (2026-11-11, the focus look
	/// brought to the WPF head's).
	/// </summary>
	private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
	{
		if (sender is TextBox box && e.NavigationMethod == NavigationMethod.Tab && box.TemplatedParent is not ComboBox)
		{
			box.CaretIndex = 0;
			box.SelectionStart = 0;
			box.SelectionEnd = 0;
		}
	}

	private static void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Handled || sender is not TextBox box || box.AcceptsReturn || box.IsReadOnly)
		{
			return;
		}
		BindingExpressionBase? binding = BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty);
		if (binding == null)
		{
			return;
		}
		switch (e.Key)
		{
			case Key.Enter:
				binding.UpdateSource();
				box.SelectAll();
				e.Handled = true;
				break;
			case Key.Escape:
				string before = box.Text ?? "";
				binding.UpdateTarget();
				if ((box.Text ?? "") != before)
				{
					box.SelectAll();
					e.Handled = true;
				}
				break;
		}
	}
}
