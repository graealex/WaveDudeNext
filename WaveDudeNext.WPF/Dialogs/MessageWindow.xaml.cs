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


using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using NLog;
using WaveDudeNext.WPF.Theming;

namespace WaveDudeNext.WPF.Dialogs;

/// <summary>What a <see cref="MessageWindow"/> reports: an error (red disc, the error sound) or a question (accent disc).</summary>
public enum MessageKind
{
	/// <summary>Something failed; the text names it.</summary>
	Error,
	/// <summary>A decision the user has to take.</summary>
	Question,
}

/// <summary>The button sets a <see cref="MessageWindow"/> offers.</summary>
public enum MessageButtons
{
	/// <summary>OK alone; Enter, Esc and the close box all give <see cref="MessageAnswer.Ok"/>.</summary>
	Ok,
	/// <summary>Yes (default) and No (Esc and the close box).</summary>
	YesNo,
	/// <summary>Save (default), Don't save, and Cancel (Esc and the close box).</summary>
	SaveDiscardCancel,
}

/// <summary>The button a <see cref="MessageWindow"/> was closed with.</summary>
public enum MessageAnswer
{
	/// <summary>Cancel, Esc or the close box of a <see cref="MessageButtons.SaveDiscardCancel"/> window.</summary>
	Cancel,
	/// <summary>OK, or any way of closing a <see cref="MessageButtons.Ok"/> window.</summary>
	Ok,
	/// <summary>Yes.</summary>
	Yes,
	/// <summary>No, Esc or the close box of a <see cref="MessageButtons.YesNo"/> window.</summary>
	No,
	/// <summary>Save.</summary>
	Save,
	/// <summary>Don't save.</summary>
	Discard,
}

/// <summary>
/// The application's message box, replacing the native <see cref="MessageBox"/>, which no theme reaches: a modal window
/// owned by its caller with the theme's brushes and title bar, the text wrapped beside an error or question disc, and
/// the buttons of a <see cref="MessageButtons"/> set with labels from the string tables (<c>Dialog*</c> keys), so they
/// speak the app's language, not the operating system's. Enter takes the default button, Esc the cancel button, the
/// access keys work without Alt; the buttons carry <c>Message&lt;Answer&gt;</c> automation ids. UI thread only.
/// </summary>
public partial class MessageWindow : Window
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
	private MessageAnswer _answer;

	private MessageWindow(string text, MessageKind kind, MessageButtons buttons)
	{
		InitializeComponent();
		SourceInitialized += (_, _) => ThemeController.Instance.ApplyTitleBar(this);
		messageText.Text = text;
		bool error = kind == MessageKind.Error;
		iconDisc.SetResourceReference(Shape.FillProperty, error ? "StatusBarErrorBackground" : "Accent");
		iconGlyph.SetResourceReference(ForegroundProperty, error ? "StatusBarErrorForeground" : "StatusBarForeground");
		iconGlyph.Text = error ? "!" : "?";
		// the answer for the close box: what the cancel button of the set would give
		switch (buttons)
		{
			case MessageButtons.Ok:
				_answer = MessageAnswer.Ok;
				AddButton("DialogOk", MessageAnswer.Ok, isDefault: true, isCancel: true);
				break;
			case MessageButtons.YesNo:
				_answer = MessageAnswer.No;
				AddButton("DialogYes", MessageAnswer.Yes, isDefault: true, isCancel: false);
				AddButton("DialogNo", MessageAnswer.No, isDefault: false, isCancel: true);
				break;
			default:
				_answer = MessageAnswer.Cancel;
				AddButton("DialogSave", MessageAnswer.Save, isDefault: true, isCancel: false);
				AddButton("DialogDiscard", MessageAnswer.Discard, isDefault: false, isCancel: false);
				AddButton("DialogCancel", MessageAnswer.Cancel, isDefault: false, isCancel: true);
				break;
		}
	}

	/// <summary>
	/// Shows <paramref name="text"/> modally over <paramref name="owner"/> with the sound of its
	/// <paramref name="kind"/> and returns the button it was closed with. UI thread only.
	/// </summary>
	public static MessageAnswer Show(Window owner, string text, MessageKind kind, MessageButtons buttons)
	{
		Logger.Debug("Message ({Kind}, {Buttons}): {Text}", kind, buttons, text.ReplaceLineEndings(" "));
		(kind == MessageKind.Error ? SystemSounds.Hand : SystemSounds.Question).Play();
		var window = new MessageWindow(text, kind, buttons) { Owner = owner };
		window.ShowDialog();
		Logger.Debug("Message answered: {Answer}", window._answer);
		return window._answer;
	}

	private void AddButton(string labelKey, MessageAnswer answer, bool isDefault, bool isCancel)
	{
		var button = new Button { MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
		button.SetResourceReference(ContentProperty, labelKey);
		AutomationProperties.SetAutomationId(button, "Message" + answer);
		button.Click += (_, _) =>
		{
			_answer = answer;
			DialogResult = true;
		};
		buttonRow.Children.Add(button);
		if (isDefault)
		{
			FocusManager.SetFocusedElement(this, button);
		}
	}
}
