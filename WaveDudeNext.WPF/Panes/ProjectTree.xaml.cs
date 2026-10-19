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

using WaveDudeNext.Rendering;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
/// by dragging a row onto a sibling (line above or below) or onto their folder (append). Several sources, parts and
/// probes can be selected at once (2026-10-19, the user's request): Ctrl+click or Insert toggles a row, Shift+click or
/// Shift with the arrows selects the range from the anchor (the last plain selection), Ctrl+A everything; the extra
/// rows are marked through <see cref="IsInSelectionProperty"/>, the TreeView's own selection stays the current item.
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

	/// <summary>Identifies the <see cref="SelectedItems"/> property.</summary>
	public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
		nameof(SelectedItems), typeof(ObservableCollection<object>), typeof(ProjectTree), new PropertyMetadata(null, OnSelectedItemsChanged));

	/// <summary>Attached to a row whose item is in <see cref="SelectedItems"/>; the row style highlights it.</summary>
	public static readonly DependencyProperty IsInSelectionProperty = DependencyProperty.RegisterAttached(
		"IsInSelection", typeof(bool), typeof(ProjectTree), new PropertyMetadata(false));

	private object? _dragCandidate;
	private Point _dragStart;
	private InsertionAdorner? _adorner;
	private object? _anchor;
	private bool _mirroring;

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

	/// <summary>The shared selection (the main window's); the tree adds and removes items on Ctrl, Shift, Insert and marks their rows.</summary>
	public ObservableCollection<object>? SelectedItems
	{
		get => (ObservableCollection<object>?)GetValue(SelectedItemsProperty);
		set => SetValue(SelectedItemsProperty, value);
	}

	/// <summary>Whether a row's item is in the selection beside the current one.</summary>
	public static bool GetIsInSelection(DependencyObject element) => (bool)element.GetValue(IsInSelectionProperty);

	/// <summary>Sets <see cref="IsInSelectionProperty"/>.</summary>
	public static void SetIsInSelection(DependencyObject element, bool value) => element.SetValue(IsInSelectionProperty, value);

	private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var owner = (ProjectTree)d;
		if (e.OldValue is ObservableCollection<object> old)
		{
			old.CollectionChanged -= owner.OnSelectionChanged;
		}
		if (e.NewValue is ObservableCollection<object> set)
		{
			set.CollectionChanged += owner.OnSelectionChanged;
		}
		owner.UpdateRowStates();
	}

	private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateRowStates();

	/// <summary>Marks the rows of the selected items; a row not generated yet is marked when the selection next changes.</summary>
	private void UpdateRowStates()
	{
		ObservableCollection<object>? set = SelectedItems;
		MarkRows(tree, row => set != null && set.Contains(row.DataContext));
	}

	private static void MarkRows(ItemsControl parent, Func<TreeViewItem, bool> inSelection)
	{
		foreach (object node in parent.Items)
		{
			if (parent.ItemContainerGenerator.ContainerFromItem(node) is TreeViewItem row)
			{
				SetIsInSelection(row, inSelection(row));
				MarkRows(row, inSelection);
			}
		}
	}

	/// <summary>Deselects the selected node, if any; the tree then raises its change with null (2026-10-15, a click on empty preview space).</summary>
	public void ClearSelection()
	{
		if (tree.SelectedItem is { } item)
		{
			Mirror(() => tree.SetSelectedItem(item, expand: false, focus: false, select: false));
		}
	}

	/// <summary>Selects the node of a model object once the tree has generated it; with <paramref name="focus"/> the node also takes keyboard focus. A selection mirrored from elsewhere leaves the shared selection as it is.</summary>
	public void Select(object item, bool focus = true) =>
		Dispatcher.InvokeAsync(() => Mirror(() => tree.SetSelectedItem(item, expand: true, focus)), DispatcherPriority.Loaded);

	private void Mirror(Action action)
	{
		_mirroring = true;
		try
		{
			action();
		}
		finally
		{
			_mirroring = false;
		}
	}

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

	/// <summary>
	/// The TreeView's selection changed: by the user (a click, the arrows) the shared selection becomes that item alone,
	/// or with Shift held the range from the anchor to it; mirrored from elsewhere it is left alone. Then the window hears.
	/// </summary>
	private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
	{
		if (!_mirroring && SelectedItems is { } set && e.NewValue is Source or OpticalPart or Probe)
		{
			if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _anchor != null && !ReferenceEquals(_anchor, e.NewValue))
			{
				SelectRange(_anchor, e.NewValue);
			}
			else
			{
				set.Clear();
				set.Add(e.NewValue);
				_anchor = e.NewValue;
			}
		}
		SelectedItem = e.NewValue;
		SelectedItemChanged?.Invoke(this, e);
	}

	/// <summary>Every source, part and probe in tree order.</summary>
	private IEnumerable<object> Selectable() =>
		Project == null ? [] : Project.Sources.Cast<object>().Concat(Project.Parts).Concat(Project.Probes);

	/// <summary>Makes the selection the items from <paramref name="from"/> to <paramref name="to"/> in tree order, either way round.</summary>
	private void SelectRange(object from, object to)
	{
		if (SelectedItems is not { } set)
		{
			return;
		}
		object[] all = [.. Selectable()];
		int a = Array.IndexOf(all, from), b = Array.IndexOf(all, to);
		if (a < 0 || b < 0)
		{
			return;
		}
		set.Clear();
		for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
		{
			set.Add(all[i]);
		}
	}

	/// <summary>Ctrl+click toggles the row; Shift+click selects the range from the anchor. Both make the row current without the TreeView's own plain selection.</summary>
	private bool TryModifiedClick(object item)
	{
		if (SelectedItems is not { } set || item is not (Source or OpticalPart or Probe))
		{
			return false;
		}
		if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
		{
			if (set.Contains(item))
			{
				set.Remove(item);
				object? current = set.LastOrDefault();
				if (current != null)
				{
					Mirror(() => tree.SetSelectedItem(current, expand: false, focus: true));
				}
				else
				{
					ClearSelection();
				}
			}
			else
			{
				set.Add(item);
				_anchor = item;
				Mirror(() => tree.SetSelectedItem(item, expand: false, focus: true));
			}
			return true;
		}
		if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
		{
			SelectRange(_anchor ?? tree.SelectedItem ?? item, item);
			Mirror(() => tree.SetSelectedItem(item, expand: false, focus: true));
			return true;
		}
		return false;
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
			if (TryModifiedClick(row.DataContext))
			{
				e.Handled = true;   // the TreeView would make the row its plain selection
				return;
			}
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
