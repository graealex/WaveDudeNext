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
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WaveDudeNext.App.Panes;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.UI.Rendering;

namespace WaveDudeNext.App.Views;

/// <summary>
/// Editor of one project colour scheme: name, kind, description, the gradient with a marker under each stop
/// (<see cref="Controls.GradientBar"/>, 2026-11-11: a marker dragged moves its stop, a click on the gradient adds one),
/// and one row per stop with its position (0..1), a swatch and the colour as <c>#RRGGBB</c>; stops can be added (in
/// the widest gap) and removed. A "Based on" combo copies any existing scheme's stops into this one. Edits reach the
/// preview through the project watcher.
/// </summary>
public partial class ColorSchemeView : UserControl
{
	private bool _applyingBase;

	/// <summary>Creates the view; the Based-on list is filled when the scheme or the tree position changes.</summary>
	public ColorSchemeView()
	{
		InitializeComponent();
		DataContextChanged += (_, _) => FillBasedOn();
		AttachedToVisualTree += (_, _) => FillBasedOn();
	}

	private ColorScheme? Scheme => DataContext as ColorScheme;

	/// <summary>The "based on" choices: the project's other schemes, then the shared schemes (user folder, built-in).</summary>
	private void FillBasedOn()
	{
		_applyingBase = true;
		try
		{
			var choices = new List<ColorScheme>();
			Project? project = this.FindAncestorOfType<PropertiesPane>()?.Project;
			if (project != null)
			{
				choices.AddRange(project.ColorSchemes.Where(s => !ReferenceEquals(s, Scheme)));
			}
			choices.AddRange(ColorSchemeCatalog.Instance.Schemes);
			basedOn.ItemsSource = choices;
			basedOn.SelectedItem = null;
		}
		finally
		{
			_applyingBase = false;
		}
	}

	/// <summary>Copies the chosen scheme into this one (stops, kind, description); the choice stays shown until the next pick.</summary>
	private void OnBasedOnChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_applyingBase || Scheme is not { } scheme || basedOn.SelectedItem is not ColorScheme chosen)
		{
			return;
		}
		scheme.SetStops(chosen.Stops);
		scheme.Kind = chosen.Kind;
		scheme.Description = "copy of scheme '" + chosen.Name + "'";
	}

	/// <summary>Add stop: a stop in the widest gap with the gradient's colour there.</summary>
	private void OnAddStop(object? sender, RoutedEventArgs e) => Scheme?.AddStop();

	/// <summary>Removes the row's stop (the button's data context); the model keeps the last one.</summary>
	private void OnRemoveStop(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: ColorStop stop })
		{
			Scheme?.RemoveStop(stop);
		}
	}
}
