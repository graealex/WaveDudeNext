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
using System.Windows.Documents;
using System.Windows.Input;

namespace WaveDudeNext.WPF.Platform;

/// <summary>
/// Tells when an edit has finished, from the input of the window it watches (2026-11-06, the user's rule: an edit
/// becomes an undo step when it is clear that editing the value is over, and time has no say in it). No control takes
/// part: the window's routed events are watched at its root, the handled ones too, and the thread's input once all
/// handlers have had it, so a control added later is covered as it is. The editing is over when
/// <list type="bullet">
/// <item>a mouse button is released: a drag of a handle, a scrub of a number, a click on a check box. Heard when the
/// release has been through every handler, so the control under the pointer has finished its part (a thumb its drag)
/// before; from the input manager, since WPF raises no bubbling event for a release whose preview event a control
/// handled (the number box ends its scrub there).</item>
/// <item>a mouse button is pressed: something new begins, so what the keys or the wheel were editing is finished, and
/// that is said before the press changes anything.</item>
/// <item>the focus leaves the element: the arrow keys on a handle, the keys and the wheel on a number box, typing in a
/// text box all edit the focused element's value for as long as the focus stays. Heard after a text box has written
/// its text on leaving.</item>
/// <item>Enter is pressed in a single-line text box, which writes its value there and then; heard like the release,
/// since the property editors handle the preview event.</item>
/// <item>a check box or another toggle button is clicked: it has written its value and nothing more can follow
/// (2026-11-10, the user's rule: ticked with a key, a box had waited for the focus to leave, while the mouse's release
/// finished it at once). Heard as the button's Click, which it raises after it has toggled, whatever clicked it, the
/// mouse, Space or its access key; a box changed from the model raises none. A check box also takes + and - as
/// "ticked" and "cleared" in its key handler without a click, heard like Enter.</item>
/// <item>a command is carried out, before it (it is something new) and after it (it is complete in itself): one of
/// the menus, the buttons or the shortcuts. Not the keys a text box turns into its own editing commands, which are
/// typing.</item>
/// <item>something is dropped.</item>
/// </list>
/// A call too many costs nothing, since the history does nothing without a step in progress; a moment missed (a release
/// swallowed by a system drag) is made up for by the next press or the next loss of focus, before anything new is
/// edited. The release and Enter are heard for every window of the UI thread, which is as harmless. UI thread.
/// </summary>
public sealed class EditBoundaries
{
	private readonly Action _finished;

	/// <summary>Watches the input of <paramref name="root"/>, a window, and calls <paramref name="finished"/> at each of the moments above.</summary>
	/// <param name="root">The element whose input is watched, with everything inside it.</param>
	/// <param name="finished">Called on the UI thread when the editing is over; usually the undo history's end of a step.</param>
	public EditBoundaries(UIElement root, Action finished)
	{
		_finished = finished;
		root.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => finished()), handledEventsToo: true);
		root.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => finished()), handledEventsToo: true);
		root.AddHandler(UIElement.LostFocusEvent, new RoutedEventHandler((_, _) => finished()), handledEventsToo: true);
		root.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler(OnCommand), handledEventsToo: true);
		root.AddHandler(CommandManager.ExecutedEvent, new ExecutedRoutedEventHandler(OnCommand), handledEventsToo: true);
		root.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), handledEventsToo: true);
		root.AddHandler(DragDrop.DropEvent, new DragEventHandler((_, _) => finished()), handledEventsToo: true);
		InputManager.Current.PostProcessInput += OnPostProcessInput;
	}

	/// <summary>
	/// An input event has been through all its handlers. A preview event that was handled is the last of its pair (WPF
	/// raises the bubbling one only for an unhandled preview), else the bubbling one is.
	/// </summary>
	private void OnPostProcessInput(object sender, ProcessInputEventArgs e)
	{
		InputEventArgs input = e.StagingItem.Input;
		RoutedEvent routed = input.RoutedEvent;
		bool released = routed == Mouse.MouseUpEvent || (routed == Mouse.PreviewMouseUpEvent && input.Handled);
		bool entered = (routed == Keyboard.KeyDownEvent || (routed == Keyboard.PreviewKeyDownEvent && input.Handled))
			&& input is KeyEventArgs { Key: Key.Enter } && input.OriginalSource is TextBoxBase { AcceptsReturn: false };
		// a two-state check box sets itself on + and clears itself on - in its own key handler, raising no Click
		bool ticked = routed == Keyboard.KeyDownEvent && input is KeyEventArgs { Key: Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus }
			&& input.OriginalSource is CheckBox { IsThreeState: false };
		if (released || entered || ticked)
		{
			_finished();
		}
	}

	/// <summary>A toggle button's click, whatever raised it: the button has toggled and written its value before it says so.</summary>
	private void OnClick(object sender, RoutedEventArgs e)
	{
		if (e.OriginalSource is ToggleButton)
		{
			_finished();
		}
	}

	/// <summary>A command, before and after it; not a text box's own editing commands, which are typing.</summary>
	private void OnCommand(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Command is RoutedCommand command
			&& (command.OwnerType == typeof(EditingCommands) || (e.OriginalSource is TextBoxBase && command.OwnerType == typeof(ApplicationCommands))))
		{
			return;
		}
		_finished();
	}
}
