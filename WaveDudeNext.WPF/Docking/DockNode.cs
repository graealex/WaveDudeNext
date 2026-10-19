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
using System.Collections.Specialized;
using System.Windows.Controls;

namespace WaveDudeNext.WPF.Docking;

/// <summary>
/// Node of the layout tree. The tree is made of <see cref="DockSplitNode"/>s whose children
/// are further splits, tabbed <see cref="DockGroup"/>s, or the single
/// <see cref="DockDocumentNode"/> that marks the centre area.
/// </summary>
public abstract class DockNode
{
	/// <summary>The split containing this node, or null for the root.</summary>
	public DockSplitNode? Parent { get; internal set; }

	/// <summary>
	/// Share of the parent split's extent along its orientation (star weight). After the host
	/// captured sizes this holds rendered pixels; only the ratio between siblings matters.
	/// </summary>
	public double Length { get; set; } = 1.0;
}

/// <summary>
/// A node that lays its children out side by side (<see cref="Orientation.Horizontal"/>) or
/// stacked (<see cref="Orientation.Vertical"/>) with splitters between them.
/// </summary>
public sealed class DockSplitNode : DockNode
{
	public DockSplitNode(Orientation orientation)
	{
		Orientation = orientation;
		Children.CollectionChanged += OnChildrenChanged;
	}

	/// <summary>Horizontal: children are columns. Vertical: children are rows.</summary>
	public Orientation Orientation { get; }

	/// <summary>Children in visual order (left to right, or top to bottom).</summary>
	public ObservableCollection<DockNode> Children { get; } = new();

	/// <summary>Sum of the children's <see cref="DockNode.Length"/> values.</summary>
	public double TotalLength => Children.Sum(c => c.Length);

	private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.OldItems != null)
		{
			foreach (DockNode node in e.OldItems)
			{
				if (ReferenceEquals(node.Parent, this))
				{
					node.Parent = null;
				}
			}
		}
		if (e.NewItems != null)
		{
			foreach (DockNode node in e.NewItems)
			{
				node.Parent = this;
			}
		}
	}
}

/// <summary>
/// The centre area of the host. Exactly one exists per host and it is never removed; its
/// content is <see cref="DockHost.DocumentContent"/>.
/// </summary>
public sealed class DockDocumentNode : DockNode
{
}
