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
using System.Windows.Data;
using System.Windows.Input;

namespace WaveDudeNext.WPF.Controls;

/// <summary>
/// Attached behaviour of the property editors' text boxes: Enter pushes the text into the bound property
/// at once (the default binding waits for the box to lose focus, so a source did not move until Tab was
/// pressed; the author's point 13, 2026-10-10) and selects the text for the next entry; Escape restores
/// the property's value when the text differs. Both keys are swallowed only when they did something,
/// so Escape still reaches Stop from an unchanged box. Boxes that accept Return keep it as a newline.
/// </summary>
public static class EditBehavior
{
	/// <summary>Identifies the <c>CommitOnEnter</c> attached property.</summary>
	public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
		"CommitOnEnter", typeof(bool), typeof(EditBehavior), new PropertyMetadata(false, OnCommitOnEnterChanged));

	public static bool GetCommitOnEnter(DependencyObject element) => (bool)element.GetValue(CommitOnEnterProperty);

	public static void SetCommitOnEnter(DependencyObject element, bool value) => element.SetValue(CommitOnEnterProperty, value);

	private static void OnCommitOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not TextBox box)
		{
			return;
		}
		box.PreviewKeyDown -= OnPreviewKeyDown;
		if ((bool)e.NewValue)
		{
			box.PreviewKeyDown += OnPreviewKeyDown;
		}
	}

	private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		var box = (TextBox)sender;
		if (box.AcceptsReturn || box.IsReadOnly || box.GetBindingExpression(TextBox.TextProperty) is not { } binding)
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
				string before = box.Text;
				binding.UpdateTarget();
				if (box.Text != before)
				{
					box.SelectAll();
					e.Handled = true;
				}
				break;
		}
	}
}
