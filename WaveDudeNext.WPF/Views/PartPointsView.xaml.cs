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

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF.Views;

/// <summary>
/// Editor of the point list of an <see cref="OutlinePart"/> or a <see cref="FiberPart"/> (its path): one row
/// per point, add and remove. The data context is the part; the list is its <c>Points</c>. Every control is in the Tab
/// cycle (the keyboard-first rule).
/// </summary>
public partial class PartPointsView : UserControl
{
	/// <summary>Identifies the <see cref="HintText"/> property.</summary>
	public static readonly DependencyProperty HintTextProperty = DependencyProperty.Register(
		nameof(HintText), typeof(string), typeof(PartPointsView), new PropertyMetadata(""));

	/// <summary>How far a new point is put from the last one, in canvas pixels.</summary>
	private const int NewPointStep = 20;

	public PartPointsView()
	{
		InitializeComponent();
	}

	/// <summary>The line above the list saying what the points are (an outline, a path); set by the kind's form.</summary>
	public string HintText
	{
		get => (string)GetValue(HintTextProperty);
		set => SetValue(HintTextProperty, value);
	}

	/// <summary>The edited list: the outline's or fibre's points, or null for another data context.</summary>
	private ObservableCollection<PartPoint>? Points => DataContext switch
	{
		OutlinePart outline => outline.Points,
		FiberPart fiber => fiber.Points,
		_ => null,
	};

	private void CanAddPoint(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Points != null;

	/// <summary>Appends a point a step to the right of the last one (at the centre for an empty list).</summary>
	private void OnAddPoint(object sender, ExecutedRoutedEventArgs e)
	{
		if (Points is not { } points)
		{
			return;
		}
		PartPoint? last = points.LastOrDefault();
		points.Add(new PartPoint { X = (last?.X ?? -NewPointStep) + NewPointStep, Y = last?.Y ?? 0 });
	}

	private void CanRemovePoint(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.Parameter is PartPoint point && Points?.Contains(point) == true;

	private void OnRemovePoint(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is PartPoint point)
		{
			Points?.Remove(point);
		}
	}
}
