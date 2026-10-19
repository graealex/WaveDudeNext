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
using System.Windows.Documents;
using System.Windows.Input;

namespace WaveDudeNext.WPF.Docking;

/// <summary>
/// Mouse-driven pane moving for a <see cref="DockHost"/>. A title bar or tab arms a drag on
/// mouse down; once the pointer travels past the system drag threshold the host captures the
/// mouse, a <see cref="DockDropAdorner"/> shows the drop targets, and releasing over one of
/// them performs the move.
/// </summary>
internal sealed class DockDragManager
{
	private readonly DockHost _host;
	private DockGroupControl? _source;
	private DockItem? _sourceItem;
	private Point _armPoint;
	private bool _armed;
	private bool _dragging;
	private bool _finishing;
	private DockDropAdorner? _adorner;

	public DockDragManager(DockHost host)
	{
		_host = host;
		_host.PreviewMouseMove += OnMouseMove;
		_host.PreviewMouseLeftButtonUp += OnMouseUp;
		_host.LostMouseCapture += OnLostMouseCapture;
	}

	/// <summary>
	/// Records a potential drag. <paramref name="item"/> is the single tab being dragged, or
	/// null when the whole group (title bar) is dragged.
	/// </summary>
	public void Arm(DockGroupControl source, DockItem? item, Point hostPoint)
	{
		_source = source;
		_sourceItem = item;
		_armPoint = hostPoint;
		_armed = true;
	}

	private void OnMouseMove(object sender, MouseEventArgs e)
	{
		if (!_armed || _source == null)
		{
			return;
		}
		if (e.LeftButton != MouseButtonState.Pressed)
		{
			Finish();
			return;
		}
		Point point = e.GetPosition(_host);
		if (!_dragging)
		{
			if (Math.Abs(point.X - _armPoint.X) < SystemParameters.MinimumHorizontalDragDistance
				&& Math.Abs(point.Y - _armPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
			{
				return;
			}
			AdornerLayer? layer = AdornerLayer.GetAdornerLayer(_host);
			if (layer == null)
			{
				Finish();
				return;
			}
			// State must be complete before capturing: Mouse.Capture re-raises a mouse move
			// synchronously, which re-enters this handler.
			_dragging = true;
			string title = _sourceItem?.Title ?? _source.Group.ActiveItem?.Title ?? string.Empty;
			_adorner = new DockDropAdorner(_host, title);
			layer.Add(_adorner);
			if (!Mouse.Capture(_host))
			{
				Finish();
				return;
			}
		}
		var hovered = _host.HitNode(_host.InputHitTest(point) as DependencyObject);
		_adorner!.Update(point, hovered, _source.Group, _sourceItem);
		e.Handled = true;
	}

	private void OnMouseUp(object sender, MouseButtonEventArgs e)
	{
		if (!_armed)
		{
			return;
		}
		if (!_dragging)
		{
			Finish();
			return;
		}
		DropTarget target = _adorner!.CurrentTarget;
		DockGroupControl source = _source!;
		DockItem? item = _sourceItem;
		Finish();
		Perform(target, source.Group, item);
		e.Handled = true;
	}

	private void OnLostMouseCapture(object sender, MouseEventArgs e)
	{
		if (_dragging && !_finishing)
		{
			Finish();
		}
	}

	private void Finish()
	{
		if (_finishing)
		{
			return;
		}
		_finishing = true;
		_armed = false;
		_dragging = false;
		if (_adorner != null)
		{
			AdornerLayer.GetAdornerLayer(_host)?.Remove(_adorner);
			_adorner = null;
		}
		if (ReferenceEquals(Mouse.Captured, _host))
		{
			_host.ReleaseMouseCapture();
		}
		_source = null;
		_sourceItem = null;
		_finishing = false;
	}

	private void Perform(DropTarget target, DockGroup group, DockItem? item)
	{
		if (target.Kind == DropKind.None)
		{
			return;
		}
		bool wholeGroup = item == null || group.Items.Count == 1;
		switch (target.Kind)
		{
			case DropKind.HostEdge:
				if (wholeGroup)
				{
					_host.DockGroupToEdge(group, target.Side);
				}
				else
				{
					_host.DockToEdge(item!, target.Side);
				}
				break;
			case DropKind.NodeCenter:
				if (wholeGroup)
				{
					_host.MergeGroup(group, (DockGroup)target.Node!);
				}
				else
				{
					_host.MoveToGroup(item!, (DockGroup)target.Node!);
				}
				break;
			case DropKind.NodeSide:
				if (wholeGroup)
				{
					_host.DockGroupRelative(group, target.Node!, target.Side);
				}
				else
				{
					_host.DockRelative(item!, target.Node!, target.Side);
				}
				break;
		}
	}
}

/// <summary>What a drop would do.</summary>
internal enum DropKind
{
	None,
	/// <summary>New pane at the outer edge of the host on <see cref="DropTarget.Side"/>.</summary>
	HostEdge,
	/// <summary>Tab into the group <see cref="DropTarget.Node"/>.</summary>
	NodeCenter,
	/// <summary>New pane next to <see cref="DropTarget.Node"/>, on <see cref="DropTarget.Side"/> of it.</summary>
	NodeSide,
}

/// <summary>A resolved drop target.</summary>
internal readonly record struct DropTarget(DropKind Kind, DockSide Side, DockNode? Node);
