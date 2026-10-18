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

using WaveDudeNext.Rendering;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WaveDudeNext.Model;
using WaveDudeNext.WPF.Controls;
using WaveDudeNext.WPF.Converters;

namespace WaveDudeNext.WPF.Panes;

/// <summary>
/// The project tree: the <see cref="Project"/> as root with its sections, sources, optical parts and probes
/// beneath. Nodes are the model objects themselves; the templates in the XAML decide how each
/// type is shown and which properties form its children. Sources, parts and probes can be reordered
/// by dragging a row onto a sibling (line above or below) or onto their folder (append).
/// </summary>
public partial class ProjectTree : UserControl
{
	private const string DragFormat = "WaveDudeNext.ProjectTree.Item";

	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
		nameof(Project), typeof(Project), typeof(ProjectTree));

	private static readonly DependencyPropertyKey SelectedItemKey = DependencyProperty.RegisterReadOnly(
		nameof(SelectedItem), typeof(object), typeof(ProjectTree), new PropertyMetadata(null));

	/// <summary>Identifies the <see cref="SelectedItem"/> property.</summary>
	public static readonly DependencyProperty SelectedItemProperty = SelectedItemKey.DependencyProperty;

	private object? _dragCandidate;
	private Point _dragStart;
	private InsertionAdorner? _adorner;

	public ProjectTree()
	{
		InitializeComponent();
	}

	/// <summary>The project shown as the root node; null shows an empty tree.</summary>
	public Project? Project
	{
		get => (Project?)GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>The model object of the selected node, or null.</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		private set => SetValue(SelectedItemKey, value);
	}

	/// <summary>Raised when <see cref="SelectedItem"/> changes.</summary>
	public event RoutedPropertyChangedEventHandler<object>? SelectedItemChanged;

	/// <summary>Selects the node of a model object once the tree has generated it; with <paramref name="focus"/> the node also takes keyboard focus.</summary>
	/// <summary>Deselects the selected node, if any; the tree then raises its change with null (2026-10-15, a click on empty preview space).</summary>
	public void ClearSelection()
	{
		if (tree.SelectedItem is { } item)
		{
			tree.SetSelectedItem(item, expand: false, focus: false, select: false);
		}
	}

	public void Select(object item, bool focus = true) =>
		Dispatcher.InvokeAsync(() => tree.SetSelectedItem(item, expand: true, focus), DispatcherPriority.Loaded);

	/// <summary>Selects the root node; call after setting <see cref="Project"/>.</summary>
	public void SelectRoot()
	{
		if (Project != null)
		{
			Select(Project);
		}
	}

	/// <summary>Selects the folder node that shows <paramref name="list"/> (the project's sources or probes).</summary>
	public void SelectFolder(IEnumerable list) =>
		Dispatcher.InvokeAsync(() =>
		{
			if (FindNode(tree, node => node is NamedCollection folder && ReferenceEquals(folder.Items, list)) is { } folder)
			{
				tree.SetSelectedItem(folder);
			}
		}, DispatcherPriority.Loaded);

	private static object? FindNode(ItemsControl parent, Func<object, bool> match)
	{
		foreach (object node in parent.Items)
		{
			if (match(node))
			{
				return node;
			}
			if (parent.ItemContainerGenerator.ContainerFromItem(node) is ItemsControl container
				&& FindNode(container, match) is { } found)
			{
				return found;
			}
		}
		return null;
	}

	private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
	{
		SelectedItem = e.NewValue;
		SelectedItemChanged?.Invoke(this, e);
	}

	// ----- drag-and-drop reordering -----

	private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_dragCandidate = null;
		var origin = e.OriginalSource as DependencyObject;
		if (FindAncestor<Button>(origin) != null)
		{
			return; // row buttons are clicks, not drags
		}
		if (FindAncestor<TreeViewItem>(origin) is { DataContext: Source or OpticalPart or Probe or ColorScheme } row)
		{
			_dragCandidate = row.DataContext;
			_dragStart = e.GetPosition(tree);
		}
	}

	private void OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		Vector moved = e.GetPosition(tree) - _dragStart;
		if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
			&& Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		object item = _dragCandidate;
		_dragCandidate = null;
		DragDrop.DoDragDrop(tree, new DataObject(DragFormat, item), DragDropEffects.Move);
		ClearAdorner();
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = DragDropEffects.None;
		e.Handled = true;
		ClearAdorner();
		if (e.Data.GetData(DragFormat) is not { } item)
		{
			return;
		}
		DropTarget? target = FindDropTarget(item, e.GetPosition(tree));
		if (target == null)
		{
			return;
		}
		e.Effects = DragDropEffects.Move;
		if (AdornerLayer.GetAdornerLayer(target.Row) is { } layer && tree.TryFindResource("Accent") is Brush brush)
		{
			_adorner = new InsertionAdorner(target.Header, brush, target.Placement);
			layer.Add(_adorner);
		}
	}

	private void OnDragLeave(object sender, DragEventArgs e) => ClearAdorner();

	private void OnDrop(object sender, DragEventArgs e)
	{
		e.Handled = true;
		ClearAdorner();
		if (Project == null || e.Data.GetData(DragFormat) is not { } item)
		{
			return;
		}
		if (FindDropTarget(item, e.GetPosition(tree)) is not { } target)
		{
			return;
		}
		bool moved = item switch
		{
			Source source => Project.Move(source, target.Index),
			OpticalPart part => Project.Move(part, target.Index),
			Probe probe => Project.Move(probe, target.Index),
			_ => false,
		};
		if (moved)
		{
			Select(item);
		}
	}

	private void ClearAdorner()
	{
		if (_adorner != null)
		{
			AdornerLayer.GetAdornerLayer(_adorner.AdornedElement)?.Remove(_adorner);
			_adorner = null;
		}
	}

	/// <summary>Row under the mouse, where a drop would put the item, and how to show it.</summary>
	private sealed record DropTarget(TreeViewItem Row, FrameworkElement Header, InsertionPlacement Placement, int Index);

	/// <summary>
	/// Resolves a drop position: onto a sibling of the dragged item (before or after, by the
	/// upper or lower half of its row) or onto the item's own folder (append). Null when the
	/// position is not a valid target or would not move anything.
	/// </summary>
	private DropTarget? FindDropTarget(object item, Point position)
	{
		IList? list = ListOf(item);
		if (list == null || tree.InputHitTest(position) is not DependencyObject hit
			|| FindAncestor<TreeViewItem>(hit) is not { } row
			|| row.Template.FindName("PART_Header", row) is not FrameworkElement header)
		{
			return null;
		}
		int current = list.IndexOf(item);
		if (row.DataContext is NamedCollection folder && ReferenceEquals(folder.Items, list))
		{
			int last = list.Count - 1;
			return current == last ? null : new DropTarget(row, header, InsertionPlacement.Into, last);
		}
		if (row.DataContext == item || ListOf(row.DataContext) != list)
		{
			return null;
		}
		int targetIndex = list.IndexOf(row.DataContext);
		bool before = position.Y < header.TransformToAncestor(tree).Transform(new Point(0, header.ActualHeight / 2)).Y;
		int insert = targetIndex + (before ? 0 : 1);           // slot in the list as it is now
		int final = insert - (current < insert ? 1 : 0);       // index after the item left its slot
		return final == current ? null : new DropTarget(row, header, before ? InsertionPlacement.Before : InsertionPlacement.After, final);
	}

	private IList? ListOf(object? node) => node switch
	{
		Source => Project?.Sources,
		OpticalPart => Project?.Parts,
		Probe => Project?.Probes,
		ColorScheme => Project?.ColorSchemes,
		_ => null,
	};

	private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
	{
		for (DependencyObject? d = start; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
		{
			if (d is T match)
			{
				return match;
			}
		}
		return null;
	}
}
