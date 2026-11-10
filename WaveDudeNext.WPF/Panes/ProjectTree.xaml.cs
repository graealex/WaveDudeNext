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
using WaveDudeNext.UI;
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
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
		nameof(Project), typeof(Project), typeof(ProjectTree));

	/// <summary>Identifies the <see cref="SelectedItem"/> property.</summary>
	public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
		nameof(SelectedItem), typeof(object), typeof(ProjectTree),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemPropertyChanged));

	/// <summary>Identifies the <see cref="RevealRequest"/> property.</summary>
	public static readonly DependencyProperty RevealRequestProperty = DependencyProperty.Register(
		nameof(RevealRequest), typeof(RevealRequest), typeof(ProjectTree), new PropertyMetadata(null, OnRevealRequestChanged));

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
	/// <summary>The list item a drag from this tree carries, which a drop on this tree reorders; null while a settings row is dragged.</summary>
	private object? _dragItem;
	/// <summary>The objects a drag from elsewhere carries, parsed once per drag.</summary>
	private readonly ObjectsData _dragged = new();
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

	/// <summary>
	/// The model object of the selected node, or null; binds two-way by default (the window's current item). The tree
	/// writes it when the user selects a row, and shows a value set from outside (see <see cref="OnSelectedItemPropertyChanged"/>).
	/// </summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>
	/// A selection made elsewhere (a marker or graph box on the preview, a command) is mirrored in the tree, which then
	/// raises its own change with the same item; the tree's own change is already shown. The row does not take the
	/// focus: a handle on the preview keeps it, so Tab can move on to the next handle (the tree stole it and broke the Tab
	/// cycle, 2026-10-14). Null, a click on empty preview space, deselects the row.
	/// </summary>
	private static void OnSelectedItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var owner = (ProjectTree)d;
		if (e.NewValue == null)
		{
			owner.ClearSelection();
		}
		else if (!ReferenceEquals(owner.tree.SelectedItem, e.NewValue))
		{
			owner.Select(e.NewValue, focus: false);
		}
	}

	/// <summary>
	/// The newest request to bring an item or a folder to the user (the selection's; <see cref="UI.RevealRequest"/>): on
	/// each change the tree selects that row, expanded, and gives it the keyboard focus, once it has generated the row.
	/// </summary>
	public RevealRequest? RevealRequest
	{
		get => (RevealRequest?)GetValue(RevealRequestProperty);
		set => SetValue(RevealRequestProperty, value);
	}

	private static void OnRevealRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var owner = (ProjectTree)d;
		if (e.NewValue is not RevealRequest request)
		{
			return;
		}
		if (request.IsFolder)
		{
			owner.SelectFolder((IEnumerable)request.Target);
		}
		else
		{
			owner.Select(request.Target);
		}
	}

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
	private void ClearSelection()
	{
		if (tree.SelectedItem is { } item)
		{
			Mirror(() => tree.SetSelectedItem(item, expand: false, focus: false, select: false));
		}
	}

	/// <summary>Selects the node of a model object once the tree has generated it; with <paramref name="focus"/> the node also takes keyboard focus. A selection mirrored from elsewhere leaves the shared selection as it is.</summary>
	private void Select(object item, bool focus = true) =>
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

	/// <summary>Selects the folder node that shows <paramref name="list"/> (the project's sources or probes).</summary>
	private void SelectFolder(IEnumerable list) =>
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
	/// or with Shift held the range from the anchor to it; mirrored from elsewhere it is left alone. Then
	/// <see cref="SelectedItem"/> takes it, and with it whatever is bound there.
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
		SetCurrentValue(SelectedItemProperty, e.NewValue);
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

	// ----- drag and drop: reordering within the tree, copying anywhere else -----
	// A drag carries the item as Copy writes it (the app's clipboard format and plain text, 2026-10-23) and nothing else;
	// the item itself stays in a field here, so a drop on this tree reorders it within its list, while any other target
	// takes a copy: another WaveDude Next window's tree as Paste would, a picture of this window or another at the drop
	// pixel, a text editor as XML. A drop of objects on this tree is placed by the paste logic: before or after a row of a
	// list (by the half of the row), at the end of a folder's list, or onto Scene, Simulation, Display or the project root.

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
			if (row.DataContext is INamed && TryModifiedClick(row.DataContext))
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
		_dragItem = ListOf(item) != null ? item : null;
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
			_dragItem = null;
			_dragged.Clear();
			ClearAdorner();
		}
	}

	/// <summary>
	/// The tree claims a drag only when it takes it (an own row to reorder, project objects to paste); anything else, a
	/// file above all, is left unhandled for the window, which opens a dropped file and refuses other data (2026-10-26,
	/// the user's report: handling every drag here had refused a project file over the tree since 2026-10-23).
	/// Handles DragEnter too: WPF takes every element under the pointer for a drop target of its own, since AllowDrop is
	/// inherited, and at each crossing from one to the next raises DragLeave for the one and DragEnter for the other and
	/// no DragOver, so a tree that hears DragOver alone shows nothing new until the pointer moves again (2026-11-10).
	/// </summary>
	private void OnDragOver(object sender, DragEventArgs e)
	{
		if (_dragItem is { } item)
		{
			e.Handled = true;
			if (FindDropTarget(item, e.GetPosition(tree)) is { } target)
			{
				e.Effects = DragDropEffects.Move;
				ShowAdorner(target.Row, target.Header, target.Placement);
			}
			else
			{
				e.Effects = DragDropEffects.None;
				ClearAdorner();
			}
			return;
		}
		if (!_dragging && _dragged.Clip(e.Data) is { } clip && FindPasteTarget(clip, e.GetPosition(tree)) is { } paste)
		{
			e.Handled = true;
			e.Effects = DragDropEffects.Copy;
			ShowAdorner(paste.Row, paste.Header, paste.Placement);
			return;
		}
		ClearAdorner();
	}

	/// <summary>
	/// The pointer has left an element of the tree, for another of the tree or for good: the mark is hidden, and where
	/// the pointer went on inside the tree the DragEnter WPF raises next, before anything is drawn, shows it again (the
	/// user's report of 2026-11-10, a flickering line: the adorner had been taken away here and made again at the
	/// pointer's next move).
	/// </summary>
	private void OnDragLeave(object sender, DragEventArgs e) => ClearAdorner();

	private void OnDrop(object sender, DragEventArgs e)
	{
		ClearAdorner();
		if (_dragItem is { } item)
		{
			e.Handled = true;
			if (Project is not { } own || FindDropTarget(item, e.GetPosition(tree)) is not { } target)
			{
				return;
			}
			bool moved = item switch
			{
				Source source => own.Move(source, target.Index),
				OpticalPart part => own.Move(part, target.Index),
				Probe probe => own.Move(probe, target.Index),
				_ => false,
			};
			if (moved)
			{
				Select(item);
			}
			return;
		}
		// taken, not kept: the paste makes the clip's objects the project's. A drag the tree does not take (a file) stays
		// unhandled for the window
		if (_dragging || Project is not { } project || _dragged.Take(e.Data) is not { } clip || FindPasteTarget(clip, e.GetPosition(tree)) is not { } paste)
		{
			return;
		}
		e.Handled = true;
		IReadOnlyList<object> pasted = ProjectClipboard.Paste(project, clip, paste.Current, paste.Before);
		if (pasted.Count == 0)
		{
			return;
		}
		Logger.Debug("Dropped {Count} object(s) into the tree", pasted.Count);
		if (SelectedItems is { } selection)
		{
			selection.Clear();
			foreach (object added in pasted.Where(added => added is INamed))
			{
				selection.Add(added);
			}
		}
		Select(pasted[^1]);
	}

	/// <summary>Where a drop of objects would go, as the paste logic takes it: the current item it is placed by, and before it or not.</summary>
	private sealed record PasteTarget(TreeViewItem Row, FrameworkElement Header, InsertionPlacement Placement, object? Current, bool Before);

	/// <summary>
	/// Resolves a drop of <paramref name="clip"/> from elsewhere: on a list item before or after it (by the half of the
	/// row), on a folder at the end of its list, on Scene, Simulation, Display or the project root as a paste onto them. Null
	/// where the clip has nothing to give (settings onto a list row without list items, for instance).
	/// </summary>
	private PasteTarget? FindPasteTarget(ProjectClipboard.Clip clip, Point position)
	{
		if (Project is not { } project || RowAt(position) is not { } row
			|| row.Template.FindName("PART_Header", row) is not FrameworkElement header)
		{
			return null;
		}
		switch (row.DataContext)
		{
			case Source or OpticalPart or Probe or ColorScheme:
				bool before = IsUpperHalf(row, header, position);
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

	/// <summary>
	/// Shows where a drop would go: a folder or a node that takes it gets an outline around its header; a slot between
	/// two rows a line on the edge of the row's strip (<see cref="StripOf"/>), which is the same line whether the slot
	/// was reached as "after this row" or as "before the next" (2026-11-10, the user's report: the line had been drawn
	/// inside the row under the pointer and so jumped as the pointer crossed from one row to the other).
	/// </summary>
	private void ShowAdorner(TreeViewItem row, FrameworkElement header, InsertionPlacement placement)
	{
		if (Mark() is not { } mark)
		{
			return;
		}
		FrameworkElement element = placement == InsertionPlacement.Into ? header : StripOf(row, header);
		Rect bounds = element.TransformToAncestor((Visual)mark.AdornedElement).TransformBounds(new Rect(element.RenderSize));
		switch (placement)
		{
			case InsertionPlacement.Before:
				mark.ShowLine(bounds.Left, bounds.Right, bounds.Top);
				break;
			case InsertionPlacement.After:
				mark.ShowLine(bounds.Left, bounds.Right, bounds.Bottom);
				break;
			default:
				mark.ShowOutline(bounds);
				break;
		}
	}

	/// <summary>
	/// The tree's one drop mark, made at its first use and kept: an adorner over the rows' panel inside the tree's
	/// scroll viewer, whose layer scrolls the mark with the rows and clips it to the view. Kept so that a drag never
	/// takes the mark out of the picture and puts another in, which flickered (2026-11-10); made again only when the
	/// tree's template has made another panel. Null while the tree has no template or no accent colour.
	/// </summary>
	private InsertionAdorner? Mark()
	{
		if (tree.TryFindResource("Accent") is not Brush brush)
		{
			return null;
		}
		if (_adorner is { } kept && kept.AdornedElement.IsDescendantOf(tree) && AdornerLayer.GetAdornerLayer(kept.AdornedElement) is not null)
		{
			kept.Brush = brush;
			return kept;
		}
		_adorner = null;
		if (FindDescendant<ScrollContentPresenter>(tree) is { } view && VisualTreeHelper.GetChildrenCount(view) > 0
			&& VisualTreeHelper.GetChild(view, 0) is UIElement rows && AdornerLayer.GetAdornerLayer(rows) is { } layer)
		{
			_adorner = new InsertionAdorner(rows, brush);
			layer.Add(_adorner);
		}
		return _adorner;
	}

	/// <summary>The row's strip: the template's border that is the row itself, from its indent to the rows' right edge and as high as the row; the header where a template has none.</summary>
	private static FrameworkElement StripOf(TreeViewItem row, FrameworkElement header) => row.Template.FindName("Bd", row) as FrameworkElement ?? header;

	/// <summary>Whether <paramref name="position"/> (in the tree's coordinates) lies in the upper half of the row.</summary>
	private bool IsUpperHalf(TreeViewItem row, FrameworkElement header, Point position)
	{
		FrameworkElement strip = StripOf(row, header);
		return position.Y < strip.TransformToAncestor(tree).Transform(new Point(0, strip.ActualHeight / 2)).Y;
	}

	/// <summary>
	/// The row at the height of <paramref name="position"/> (in the tree's coordinates), wherever across the tree the
	/// pointer is: the first row met from the pointer to the right. Under the pointer itself there is none in the gutter
	/// left of a row, the room of its indent and of the chevron a leaf does not show, where a drag had lost its target
	/// and its line (2026-11-10). A hit test, so only what the tree shows counts: no row scrolled out of view, none
	/// under a scroll bar.
	/// </summary>
	private TreeViewItem? RowAt(Point position)
	{
		// half the chevron's column: no row is narrower
		const double Step = 8;
		for (double x = Math.Max(0, position.X); x < tree.ActualWidth; x += Step)
		{
			if (tree.InputHitTest(new Point(x, position.Y)) is DependencyObject hit && FindAncestor<TreeViewItem>(hit) is { } row)
			{
				return row;
			}
		}
		return null;
	}

	/// <summary>Hides the drop mark; the adorner stays (see <see cref="Mark"/>).</summary>
	private void ClearAdorner() => _adorner?.Hide();

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
		if (list == null || RowAt(position) is not { } row
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
		bool before = IsUpperHalf(row, header, position);
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

	/// <summary>The first element of type <typeparamref name="T"/> below <paramref name="root"/> in the visual tree, depth first.</summary>
	private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is T match || (match = FindDescendant<T>(child)!) != null)
			{
				return match;
			}
		}
		return null;
	}

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
