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

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// Tells when an edit has finished, from the input of the window it watches: the Avalonia head's counterpart of the WPF
/// head's <c>EditBoundaries</c> (2026-11-06, the user's rule: an edit becomes an undo step when it is clear that editing
/// the value is over, and time has no say in it). No control takes part: the window's routed events are watched at its
/// root, the handled ones too, so a control added later is covered as it is. The editing is over when
/// <list type="bullet">
/// <item>a pointer button is released: a drag of a handle, a scrub of a number, a click on a check box. Heard after
/// every handler, so the control under the pointer has finished its part before.</item>
/// <item>a pointer button is pressed: something new begins, so what the keys or the wheel were editing is finished,
/// and that is said before the press changes anything (the tunnelling pass).</item>
/// <item>the focus leaves an element: the arrow keys on a handle, the keys and the wheel on a number box, typing in a
/// text box all edit the focused element's value for as long as the focus stays. Heard after a text box has written
/// its text on leaving.</item>
/// <item>Enter is pressed in a single-line text box, which writes its value there and then.</item>
/// <item>a check box or another toggle button is clicked: it has written its value and nothing more can follow
/// (2026-11-10, the user's rule: ticked with a key, a box had waited for the focus to leave, while the pointer's
/// release finished it at once). Heard as the button's Click, which it raises after it has toggled, whatever clicked
/// it, the pointer, Space or Enter; a box changed from the model raises none.</item>
/// <item>a command is carried out, before and after it: the application's commands say so themselves through
/// <see cref="Command"/>, since Avalonia has no routed commands to hear at the root.</item>
/// <item>something is dropped.</item>
/// </list>
/// A call too many costs nothing, since the history does nothing without a step in progress. UI thread.
/// </summary>
public sealed class EditBoundaries
{
	private readonly Action _finished;

	/// <summary>Watches the input of <paramref name="root"/>, a window, and calls <paramref name="finished"/> at each of the moments above.</summary>
	/// <param name="root">The element whose input is watched, with everything inside it.</param>
	/// <param name="finished">Called on the UI thread when the editing is over; usually the undo history's end of a step.</param>
	public EditBoundaries(InputElement root, Action finished)
	{
		_finished = finished;
		root.AddHandler(InputElement.PointerPressedEvent, (_, _) => _finished(), RoutingStrategies.Tunnel, handledEventsToo: true);
		root.AddHandler(InputElement.PointerReleasedEvent, (_, _) => _finished(), RoutingStrategies.Bubble, handledEventsToo: true);
		root.AddHandler(InputElement.LostFocusEvent, (_, _) => _finished(), RoutingStrategies.Bubble, handledEventsToo: true);
		root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
		root.AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble, handledEventsToo: true);
		root.AddHandler(DragDrop.DropEvent, (_, _) => _finished(), RoutingStrategies.Bubble, handledEventsToo: true);
	}

	/// <summary>A toggle button's click, whatever raised it: the button has toggled and written its value before it says so.</summary>
	private void OnClick(object? sender, RoutedEventArgs e)
	{
		if (e.Source is ToggleButton)
		{
			_finished();
		}
	}

	/// <summary>Wraps a command's action so that the editing counts as finished before it and after it.</summary>
	public Action<object?> Command(Action<object?> action) => parameter =>
	{
		_finished();
		action(parameter);
		_finished();
	};

	/// <summary>Enter in a single-line text box: the value is written there and then.</summary>
	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && e.Source is TextBox { AcceptsReturn: false })
		{
			_finished();
		}
	}
}
