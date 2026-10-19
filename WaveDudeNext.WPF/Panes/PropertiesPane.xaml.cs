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
using System.Windows.Media;
using System.Windows.Threading;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF.Panes;

/// <summary>
/// The Properties pane: shows the editor view that matches the type of
/// <see cref="SelectedObject"/>, mapped by the data templates in the XAML, or a hint when
/// nothing is selected.
/// </summary>
public partial class PropertiesPane : UserControl
{
	/// <summary>Identifies the <see cref="SelectedObject"/> property.</summary>
	public static readonly DependencyProperty SelectedObjectProperty = DependencyProperty.Register(
		nameof(SelectedObject), typeof(object), typeof(PropertiesPane));

	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
		nameof(Project), typeof(Project), typeof(PropertiesPane));

	public PropertiesPane()
	{
		InitializeComponent();
	}

	/// <summary>The model object to edit, or null for the empty hint.</summary>
	public object? SelectedObject
	{
		get => GetValue(SelectedObjectProperty);
		set => SetValue(SelectedObjectProperty, value);
	}

	/// <summary>The project the selected object belongs to, for views that need its other parts (a source's wavelength needs the solver settings).</summary>
	public Project? Project
	{
		get => (Project?)GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>Puts the caret into the first text box of the current view once it is built, selecting its text.</summary>
	public void FocusFirstEditor() =>
		Dispatcher.InvokeAsync(() =>
		{
			if (FirstDescendant<TextBox>(this) is { } box)
			{
				box.Focus();
				box.SelectAll();
			}
		}, DispatcherPriority.Background);

	private static T? FirstDescendant<T>(DependencyObject root) where T : DependencyObject
	{
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
			{
				return match;
			}
			if (FirstDescendant<T>(child) is { } nested)
			{
				return nested;
			}
		}
		return null;
	}
}
