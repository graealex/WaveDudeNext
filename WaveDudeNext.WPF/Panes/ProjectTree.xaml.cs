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
using NLog;
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

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

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

	/// <summary>
	/// Attached to a menu or menu item: the entries (the shared part entries, a fresh set per menu) are added to its Items,
	/// so they are its logical children as entries written inside it would be. As an ItemsSource they were not, and a
	/// submenu opened by keyboard the first time put the focus on its last entry instead of its first.
	/// </summary>
	public static readonly DependencyProperty MenuEntriesProperty = DependencyProperty.RegisterAttached(
		"MenuEntries", typeof(IEnumerable), typeof(ProjectTree), new PropertyMetadata(null, OnMenuEntriesChanged));

	/// <summary>Gets the entries attached to <paramref name="menu"/>.</summary>
	public static IEnumerable? GetMenuEntries(ItemsControl menu) => (IEnumerable?)menu.GetValue(MenuEntriesProperty);

	/// <summary>Attaches <paramref name="entries"/> to <paramref name="menu"/>, which adds them to its items.</summary>
	public static void SetMenuEntries(ItemsControl menu, IEnumerable? entries) => menu.SetValue(MenuEntriesProperty, entries);

	private static void OnMenuEntriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is ItemsControl menu && e.NewValue is IEnumerable entries)
		{
			foreach (object entry in entries)
			{
				menu.Items.Add(entry);
			}
		}
	}

	private object? _dragCandidate;
	/// <summary>Whether a drag started in this tree is under way: its drops reorder, and never paste a copy of themselves.</summary>
	private bool _dragging;
	/// <summary>The dragged text last parsed during a drag over this tree, and what it held.</summary>
	private (string Text, ProjectClipboard.Clip? Clip)? _dropCache;
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

	// ----- drag and drop: reordering within the tree, copying between windows -----
	// A drag carries the item itself, for reordering within its list here, and the item as Copy writes it (the app's
	// clipboard format and plain text, 2026-10-23), so another WaveDude Next window takes a copy as Paste would and a
	// text editor takes the XML. A drop from another window is placed by the paste logic: before or after a row of a list
	// (by the half of the row), at the end of a folder's list, or onto Scene, Simulation, Display or the project root.

	private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_dragCandidate = null;
		var origin = e.OriginalSource as DependencyObject;
		if (FindAncestor<Button>(origin) != null)
		{
			return; // row buttons are clicks, not drags
		}
		if (FindAncestor<TreeViewItem>(origin) is { } row && Project is { } project && ProjectClipboard.CanCopy(project, row.DataContext))
		{
			if (row.DataContext is Source or OpticalPart or Probe or ColorScheme && TryModifiedClick(row.DataContext))
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
		if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed || Project is not { } project)
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
		var data = new DataObject();
		if (ListOf(item) != null)
		{
			data.SetData(DragFormat, item);
		}
		string text = ProjectClipboard.Write(project, [item]);
		data.SetData(ProjectClipboard.Format, text);
		data.SetText(text);
		_dragging = true;
		try
		{
			DragDrop.DoDragDrop(tree, data, DragDropEffects.Move | DragDropEffects.Copy);
		}
		finally
		{
			_dragging = false;
			_dropCache = null;
			ClearAdorner();
		}
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = DragDropEffects.None;
		e.Handled = true;
		ClearAdorner();
		if (OwnItem(e.Data) is { } item)
		{
			if (FindDropTarget(item, e.GetPosition(tree)) is { } target)
			{
				e.Effects = DragDropEffects.Move;
				ShowAdorner(target.Row, target.Header, target.Placement);
			}
			return;
		}
		if (!_dragging && DraggedClip(e.Data) is { } clip && FindPasteTarget(clip, e.GetPosition(tree)) is { } paste)
		{
			e.Effects = DragDropEffects.Copy;
			ShowAdorner(paste.Row, paste.Header, paste.Placement);
		}
	}

	private void OnDragLeave(object sender, DragEventArgs e) => ClearAdorner();

	private void OnDrop(object sender, DragEventArgs e)
	{
		e.Handled = true;
		ClearAdorner();
		if (Project is not { } project)
		{
			return;
		}
		if (OwnItem(e.Data) is { } item)
		{
			if (FindDropTarget(item, e.GetPosition(tree)) is not { } target)
			{
				return;
			}
			bool moved = item switch
			{
				Source source => project.Move(source, target.Index),
				OpticalPart part => project.Move(part, target.Index),
				Probe probe => project.Move(probe, target.Index),
				_ => false,
			};
			if (moved)
			{
				Select(item);
			}
			return;
		}
		// read afresh: the paste takes the clip's objects into the project
		if (_dragging || ObjectsText(e.Data) is not { } text || ProjectClipboard.Read(text) is not { } clip
			|| FindPasteTarget(clip, e.GetPosition(tree)) is not { } paste)
		{
			return;
		}
		IReadOnlyList<object> pasted = ProjectClipboard.Paste(project, clip, paste.Current, paste.Before);
		_dropCache = null;
		if (pasted.Count == 0)
		{
			return;
		}
		Logger.Debug("Dropped {Count} object(s) from another window", pasted.Count);
		if (SelectedItems is { } selection)
		{
			selection.Clear();
			foreach (object added in pasted.Where(added => added is Source or OpticalPart or Probe or ColorScheme))
			{
				selection.Add(added);
			}
		}
		Select(pasted[^1]);
	}

	/// <summary>
	/// Whether a drag started in this window: it carries a live item of <paramref name="project"/>'s lists (a drag from
	/// another window carries none that can be read here). The pictures refuse such drops: they take objects from other
	/// windows only.
	/// </summary>
	internal static bool IsFromThisWindow(IDataObject data, Project project)
	{
		try
		{
			return data.GetDataPresent(DragFormat) && data.GetData(DragFormat) is { } item
				&& ((IList[])[project.Sources, project.Parts, project.Probes, project.ColorSchemes]).Any(list => list.Contains(item));
		}
		catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
		{
			return false;
		}
	}

	/// <summary>The dragged item when the drag started in this tree and the item is in this project's lists; null otherwise (a drag from another window carries no live object).</summary>
	private object? OwnItem(IDataObject data)
	{
		try
		{
			return data.GetDataPresent(DragFormat) && data.GetData(DragFormat) is { } item && ListOf(item)?.Contains(item) == true ? item : null;
		}
		catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>
	/// The objects a drag or the clipboard carries, as Copy writes them: the app's format, else plain text that may hold
	/// them (2026-10-23: copied back from a text editor, which keeps text only; <see cref="ProjectClipboard.Read"/> decides,
	/// and text not starting with an element is not even parsed). Null for neither. Shared by the tree, the pictures and
	/// Paste.
	/// </summary>
	internal static string? ObjectsText(IDataObject data)
	{
		try
		{
			if (data.GetDataPresent(ProjectClipboard.Format))
			{
				return data.GetData(ProjectClipboard.Format) as string;
			}
			return data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.TrimStart().StartsWith('<')
				? text
				: null;
		}
		catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>The dragged objects as last parsed, for the many drag-over events of one drag; null without any.</summary>
	private ProjectClipboard.Clip? DraggedClip(IDataObject data)
	{
		if (ObjectsText(data) is not { } text)
		{
			return null;
		}
		if (_dropCache is not { } cache || cache.Text != text)
		{
			_dropCache = cache = (text, ProjectClipboard.Read(text));
		}
		return cache.Clip;
	}

	/// <summary>Where a drop from another window would go, as the paste logic takes it: the current item it is placed by, and before it or not.</summary>
	private sealed record PasteTarget(TreeViewItem Row, FrameworkElement Header, InsertionPlacement Placement, object? Current, bool Before);

	/// <summary>
	/// Resolves a drop of <paramref name="clip"/> from another window: on a list item before or after it (by the half of the
	/// row), on a folder at the end of its list, on Scene, Simulation, Display or the project root as a paste onto them. Null
	/// where the clip has nothing to give (settings onto a list row without list items, for instance).
	/// </summary>
	private PasteTarget? FindPasteTarget(ProjectClipboard.Clip clip, Point position)
	{
		if (Project is not { } project || tree.InputHitTest(position) is not DependencyObject hit
			|| FindAncestor<TreeViewItem>(hit) is not { } row
			|| row.Template.FindName("PART_Header", row) is not FrameworkElement header)
		{
			return null;
		}
		switch (row.DataContext)
		{
			case Source or OpticalPart or Probe or ColorScheme:
				bool before = position.Y < header.TransformToAncestor(tree).Transform(new Point(0, header.ActualHeight / 2)).Y;
				return clip.CanPaste(project, row.DataContext)
					? new PasteTarget(row, header, before ? InsertionPlacement.Before : InsertionPlacement.After, row.DataContext, before)
					: null;
			case NamedCollection:
				// the folder's list takes its items at the end, as do the others
				return clip.HasItems ? new PasteTarget(row, header, InsertionPlacement.Into, null, false) : null;
			case { } other when clip.CanPaste(project, other):
				return new PasteTarget(row, header, InsertionPlacement.Into, other, false);
			default:
				return null;
		}
	}

	private void ShowAdorner(TreeViewItem row, FrameworkElement header, InsertionPlacement placement)
	{
		if (AdornerLayer.GetAdornerLayer(row) is { } layer && tree.TryFindResource("Accent") is Brush brush)
		{
			_adorner = new InsertionAdorner(header, brush, placement);
			layer.Add(_adorner);
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
