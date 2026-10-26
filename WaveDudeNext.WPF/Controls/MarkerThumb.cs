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
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WaveDudeNext.WPF.Controls;

/// <summary>What a <see cref="MarkerThumb"/> stands for.</summary>
public enum MarkerRole
{
	/// <summary>The position of a source (its centre for a composite source).</summary>
	Source,
	/// <summary>The rotation handle of a composite source: sits on the array's row axis and turns it (2026-10-14).</summary>
	SourceAngle,
	/// <summary>The centre of an optical part.</summary>
	Part,
	/// <summary>The rotation handle of an optical part: sits on the part's axis beyond its reach and turns it.</summary>
	PartAngle,
	/// <summary>The size grip of an optical part: a corner or vertex of its outline that sets its extents (2026-10-19).</summary>
	PartGrip,
	/// <summary>One point of an outline's list, or one node of a fibre's path (<see cref="MarkerThumb.Index"/> says which).</summary>
	PartPoint,
	/// <summary>The vertex of a lens surface (<see cref="MarkerThumb.Index"/> 1 or 2): moves along the axis and sets the surface's radius, the rim held (2026-10-26, Jeroen's proposal).</summary>
	PartSurface,
	/// <summary>The in-handle of a node of a fibre's path: the Bézier handle of the segment arriving there (<see cref="MarkerThumb.Index"/> says which node; 2026-10-22).</summary>
	FiberIn,
	/// <summary>The out-handle of a node of a fibre's path: the Bézier handle of the segment leaving there.</summary>
	FiberOut,
	/// <summary>Point A of a probe (the point of a point probe, the start of a line probe).</summary>
	ProbeA,
	/// <summary>Point B of a line probe.</summary>
	ProbeB,
	/// <summary>The graph box of a probe (moves it), see <see cref="ProbeGraphOverlay"/>.</summary>
	ProbeBox,
	/// <summary>The bottom-right corner of a probe's graph box (resizes it).</summary>
	ProbeBoxCorner,
}

/// <summary>
/// One draggable handle of the <see cref="SceneOverlay"/> or <see cref="ProbeGraphOverlay"/>: a
/// <see cref="Thumb"/> that knows which model object and which of its points (or its graph box) it moves.
/// Focusable, so the arrow keys can move it too; the overlay does the geometry and the model updates. UI thread.
/// </summary>
public sealed class MarkerThumb : Thumb
{
	/// <summary>Identifies the <see cref="IsSelectedMarker"/> property.</summary>
	public static readonly DependencyProperty IsSelectedMarkerProperty = DependencyProperty.Register(
		nameof(IsSelectedMarker), typeof(bool), typeof(MarkerThumb), new PropertyMetadata(false));

	/// <summary>Identifies the <see cref="StrokeThickness"/> property.</summary>
	public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
		nameof(StrokeThickness), typeof(double), typeof(MarkerThumb),
		new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => d.SetValue(HaloThicknessKey, (double)e.NewValue + HaloWidth)));

	private static readonly DependencyPropertyKey HaloThicknessKey = DependencyProperty.RegisterReadOnly(
		nameof(HaloThickness), typeof(double), typeof(MarkerThumb), new FrameworkPropertyMetadata(1.5 + HaloWidth, FrameworkPropertyMetadataOptions.AffectsRender));

	/// <summary>Identifies the <see cref="HaloThickness"/> property.</summary>
	public static readonly DependencyProperty HaloThicknessProperty = HaloThicknessKey.DependencyProperty;

	/// <summary>How far the dark halo sticks out on each side of a stroke, in pixels, so the glyph reads on any picture.</summary>
	public const double HaloWidth = 2.0;

	/// <param name="item">The source, part or probe.</param>
	/// <param name="role">Which point of it.</param>
	/// <param name="index">For a point, node or Bézier handle, the index in the part's point or node list.</param>
	public MarkerThumb(object item, MarkerRole role, int index = -1)
	{
		Item = item;
		Role = role;
		Index = index;
		Focusable = true;
	}

	/// <summary>For a point, node or Bézier handle, the index in the part's point or node list; -1 otherwise.</summary>
	public int Index { get; }

	/// <summary>The source, part or probe this handle moves.</summary>
	public object Item { get; }

	/// <summary>Which point of the item.</summary>
	public MarkerRole Role { get; }

	/// <summary>Whether the item is the one selected in the project; the style highlights it.</summary>
	public bool IsSelectedMarker
	{
		get => (bool)GetValue(IsSelectedMarkerProperty);
		set => SetValue(IsSelectedMarkerProperty, value);
	}

	/// <summary>Outline width of the glyph in pixels; the style thickens it on hover and focus.</summary>
	public double StrokeThickness
	{
		get => (double)GetValue(StrokeThicknessProperty);
		set => SetValue(StrokeThicknessProperty, value);
	}

	/// <summary>Width of the dark outline drawn under the glyph: <see cref="StrokeThickness"/> plus <see cref="HaloWidth"/> on each side.</summary>
	public double HaloThickness => (double)GetValue(HaloThicknessProperty);

	/// <summary>Model coordinate of the point (or the box's width, for a corner) when the current drag started (canvas pixels).</summary>
	internal int DragStartX { get; set; }

	/// <summary>Model coordinate of the point (or the box's height, for a corner) when the current drag started (canvas pixels).</summary>
	internal int DragStartY { get; set; }

	/// <summary>Where the mouse was when the drag started, in continuous canvas coordinates; a drag is measured from here against the mouse's absolute position, never from the handle, which may be held back (a clamp, a group's limit) and would feed the miss back (2026-10-19).</summary>
	internal double PressCanvasX { get; set; }

	/// <summary>The vertical part of <see cref="PressCanvasX"/>.</summary>
	internal double PressCanvasY { get; set; }

	/// <summary>For a grip or point handle: where the mouse took hold, relative to the handle's canvas position (canvas pixels), so the handle keeps that grip while it follows the mouse.</summary>
	internal double GrabOffsetX { get; set; }

	/// <summary>For a grip or point handle: the vertical part of the grab offset (canvas pixels).</summary>
	internal double GrabOffsetY { get; set; }

	/// <summary>For the Bézier handle of a smooth node: the other handle's length when the drag or key press began, which it keeps while it turns (canvas pixels); null otherwise.</summary>
	internal double? MirrorLength { get; set; }

	/// <summary>Canvas points captured as a drag or a run of keyboard steps starts, which it holds still: a lens surface's rim and the other surface's vertex (2026-10-26); null for a handle without any. Forgotten when the focus leaves.</summary>
	internal (double X, double Y)[]? DragAnchors { get; set; }

	/// <inheritdoc/>
	protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
	{
		base.OnLostKeyboardFocus(e);
		DragAnchors = null;
	}
}
