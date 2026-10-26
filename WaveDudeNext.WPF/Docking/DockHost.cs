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

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WaveDudeNext.WPF.Commands;

namespace WaveDudeNext.WPF.Docking;

/// <summary>
/// Docking surface. The layout is a tree (<see cref="DockNode"/>): nested
/// <see cref="DockSplitNode"/>s with splitters between their children, tabbed
/// <see cref="DockGroup"/>s as leaves, and one <see cref="DockDocumentNode"/> for the centre
/// area. Panes dock relative to any pane, to the document area, or to the outer edges, by drag
/// and drop (see <see cref="DockDragManager"/>) or through the title bar menu. The arrangement
/// round-trips through <see cref="DockLayout"/>.
/// </summary>
/// <remarks>
/// The visual tree is rebuilt from the model after every structural change. Sizes the user
/// dragged are captured back into the model first, so a rebuild never loses them. All members
/// must be used on the UI thread.
/// </remarks>
public sealed class DockHost : Grid
{
	private const double SplitterThickness = 4;
	private const double PaneMinSize = 60;
	private const double DocumentMinSize = 100;
	/// <summary>Share a pane gets when docked next to another pane.</summary>
	private const double RelativeShare = 1.0 / 3.0;
	/// <summary>Share a pane gets when docked at an outer edge.</summary>
	private const double EdgeShare = 0.25;

	private readonly Dictionary<string, DockItem> _items = new();
	private readonly Dictionary<DockNode, FrameworkElement> _elements = new();
	private readonly List<DockGroupControl> _groupControls = new();
	private readonly ContentPresenter _center = new();
	private readonly DockDocumentNode _document = new();
	private DockNode _root;
	private DockGroupControl? _activeGroup;

	public DockHost()
	{
		_root = _document;
		Items = new DockItemCollection(item =>
		{
			if (string.IsNullOrEmpty(item.Id))
			{
				throw new ArgumentException("A dock item needs an id.", nameof(item));
			}
			if (!_items.TryAdd(item.Id, item))
			{
				throw new ArgumentException($"A dock item with id '{item.Id}' is already registered.", nameof(item));
			}
		});
		DragManager = new DockDragManager(this);
		// the hosted content is re-parented on every rebuild and leaves the tree for a moment, so it gets the
		// host's data context handed over explicitly rather than inherited (2026-10-15): inherited, its
		// bindings passed through null and back, which put the preview at frame 0 and made the project tree
		// drop its selection (an ItemsSource going null makes a TreeView select nothing)
		DataContextChanged += (_, _) => HandOverDataContext();
		Items.CollectionChanged += (_, _) => HandOverDataContext();
		ToggleCommand = new RelayCommand<DockItem>(item =>
		{
			if (item.Group != null)
			{
				Hide(item);
			}
			else
			{
				Show(item);
			}
		});
		AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), true);
		AddHandler(PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnPreviewGotKeyboardFocus), true);
		Loaded += OnLoaded;
		Rebuild();
	}

	/// <summary>
	/// The arrangement of a first start, set in the window's XAML; the window loads it when no saved layout applies, and
	/// the XAML designer shows it (2026-10-26, the user's request: the designer never runs the window's constructor, so
	/// its preview had shown the document area alone, and the items docked one by one at their default sides looked
	/// nothing like the app). Null leaves the host empty until the window shows the items.
	/// </summary>
	public DockLayout? DefaultLayout { get; set; }

	/// <summary>
	/// In the XAML designer, loads <see cref="DefaultLayout"/>, then shows every item it leaves hidden at its default side
	/// (the user's choice, 2026-10-26: the designer shows every pane, Messages at the bottom, where the app's first start
	/// hides it). At run time nothing happens here.
	/// </summary>
	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (!DesignerProperties.GetIsInDesignMode(this))
		{
			return;
		}
		if (DefaultLayout != null)
		{
			LoadLayout(DefaultLayout);
		}
		// from the model alone, never through Show: that captures the sizes of a visual tree the designer has arranged at
		// some provisional size, and the left column came out squashed (2026-10-26)
		foreach (DockItem item in Items.Where(item => item.Group == null).ToList())
		{
			InsertRelative(NewGroup(item), _root, item.DefaultSide, EdgeShare);
		}
		Rebuild();
	}

	/// <summary>Content of the centre area (documents, preview).</summary>
	public object? DocumentContent
	{
		get => _center.Content;
		set => _center.Content = value;
	}

	/// <summary>
	/// All registered items in registration order, shown or hidden. Adding registers an item;
	/// XAML can fill it through <c>&lt;docking:DockHost.Items&gt;</c>.
	/// </summary>
	public DockItemCollection Items { get; }

	/// <summary>The node that marks the centre area.</summary>
	public DockDocumentNode Document => _document;

	/// <summary>Root of the layout tree.</summary>
	public DockNode Root => _root;

	/// <summary>Shows a hidden item or hides a shown one. Parameter: the <see cref="DockItem"/>.</summary>
	public ICommand ToggleCommand { get; }

	/// <summary>Raised after the arrangement changed and the visual tree was rebuilt.</summary>
	public event EventHandler? LayoutChanged;

	internal DockDragManager DragManager { get; }

	/// <summary>Makes an item known to the host without showing it; same as adding it to <see cref="Items"/>.</summary>
	/// <exception cref="ArgumentException">The item has no id, or one that is already registered.</exception>
	public void Register(DockItem item) => Items.Add(item);

	/// <summary>True when the item currently occupies a group.</summary>
	public bool IsShown(DockItem item) => item.Group != null;

	/// <summary>Shows the item: activates it where it is, or docks it at its default edge.</summary>
	public void Show(DockItem item)
	{
		EnsureRegistered(item);
		if (item.Group != null)
		{
			item.Group.ActiveItem = item;
			ActivateGroupOf(item);
			return;
		}
		DockToEdge(item, item.DefaultSide);
	}

	/// <summary>Removes the item from its group. Its content is kept and can be shown again.</summary>
	public void Hide(DockItem item)
	{
		if (item.Group == null)
		{
			return;
		}
		CaptureSizes();
		Detach(item);
		Rebuild();
	}

	/// <summary>Docks the item in a new pane at the outer edge of the host.</summary>
	public void DockToEdge(DockItem item, DockSide side)
	{
		EnsureRegistered(item);
		CaptureSizes();
		Detach(item);
		InsertRelative(NewGroup(item), _root, side, EdgeShare);
		Rebuild();
		ActivateGroupOf(item);
	}

	/// <summary>Docks the item in a new pane next to <paramref name="target"/>, on the given side of it.</summary>
	public void DockRelative(DockItem item, DockNode target, DockSide side)
	{
		EnsureRegistered(item);
		CaptureSizes();
		Detach(item);
		InsertRelative(NewGroup(item), target, side, RelativeShare);
		Rebuild();
		ActivateGroupOf(item);
	}

	/// <summary>Moves the item into <paramref name="target"/> as its active tab.</summary>
	public void MoveToGroup(DockItem item, DockGroup target)
	{
		EnsureRegistered(item);
		if (ReferenceEquals(item.Group, target))
		{
			target.ActiveItem = item;
			ActivateGroupOf(item);
			return;
		}
		CaptureSizes();
		Detach(item);
		target.Items.Add(item);
		target.ActiveItem = item;
		Rebuild();
		ActivateGroupOf(item);
	}

	/// <summary>Docks a whole group at the outer edge of the host.</summary>
	public void DockGroupToEdge(DockGroup group, DockSide side)
	{
		CaptureSizes();
		DetachGroup(group);
		InsertRelative(group, _root, side, EdgeShare);
		Rebuild();
		ActivateGroup(group);
	}

	/// <summary>Docks a whole group next to <paramref name="target"/>, on the given side of it.</summary>
	public void DockGroupRelative(DockGroup group, DockNode target, DockSide side)
	{
		if (ReferenceEquals(group, target))
		{
			return;
		}
		CaptureSizes();
		DetachGroup(group);
		InsertRelative(group, target, side, RelativeShare);
		Rebuild();
		ActivateGroup(group);
	}

	/// <summary>Moves every item of <paramref name="source"/> into <paramref name="target"/>.</summary>
	public void MergeGroup(DockGroup source, DockGroup target)
	{
		if (ReferenceEquals(source, target))
		{
			return;
		}
		CaptureSizes();
		DockItem? active = source.ActiveItem;
		DetachGroup(source);
		while (source.Items.Count > 0)
		{
			DockItem item = source.Items[0];
			source.Items.RemoveAt(0);
			target.Items.Add(item);
		}
		if (active != null)
		{
			target.ActiveItem = active;
		}
		Rebuild();
		ActivateGroup(target);
	}

	/// <summary>Captures the current arrangement, including user-dragged sizes.</summary>
	public DockLayout SaveLayout()
	{
		CaptureSizes();
		return new DockLayout { Root = Export(_root) };
	}

	/// <summary>
	/// Replaces the arrangement with <paramref name="layout"/>. Ids that are not registered are
	/// skipped and registered items the layout does not mention end up hidden. Returns false,
	/// leaving the current arrangement, when the layout has no document node.
	/// </summary>
	public bool LoadLayout(DockLayout layout)
	{
		if (layout.Root == null)
		{
			return false;
		}
		var assigned = new HashSet<DockItem>();
		DockNode? root = Import(layout.Root, assigned);
		if (root == null || !ContainsDocument(root))
		{
			return false;
		}
		foreach (DockGroup group in Groups(_root).ToList())
		{
			while (group.Items.Count > 0)
			{
				DockItem item = group.Items[^1];
				if (!assigned.Contains(item))
				{
					group.Items.RemoveAt(group.Items.Count - 1);
				}
				else
				{
					break;
				}
			}
		}
		_root = root;
		_root.Parent = null;
		NormalizeTree(_root);
		Rebuild();
		return true;
	}

	internal void SetActiveGroup(DockGroupControl? control)
	{
		if (ReferenceEquals(_activeGroup, control))
		{
			return;
		}
		if (_activeGroup != null)
		{
			_activeGroup.IsActive = false;
		}
		_activeGroup = control;
		if (control != null)
		{
			control.IsActive = true;
		}
	}

	/// <summary>Finds the group control that visually contains <paramref name="element"/>.</summary>
	internal static DockGroupControl? FindGroupControl(DependencyObject? element)
	{
		while (element != null)
		{
			if (element is DockGroupControl control)
			{
				return control;
			}
			element = ParentOf(element);
		}
		return null;
	}

	/// <summary>
	/// Resolves the pane or document area under <paramref name="element"/> and the element
	/// that renders it, for drop-target placement.
	/// </summary>
	internal (DockNode Node, FrameworkElement Element)? HitNode(DependencyObject? element)
	{
		while (element != null)
		{
			if (element is DockGroupControl control)
			{
				return (control.Group, control);
			}
			if (ReferenceEquals(element, _center))
			{
				return (_document, _center);
			}
			element = ParentOf(element);
		}
		return null;
	}

	private static DependencyObject? ParentOf(DependencyObject element) =>
		element is Visual or System.Windows.Media.Media3D.Visual3D
			? VisualTreeHelper.GetParent(element)
			: LogicalTreeHelper.GetParent(element);

	private void EnsureRegistered(DockItem item)
	{
		if (!_items.TryGetValue(item.Id, out DockItem? known) || !ReferenceEquals(known, item))
		{
			throw new InvalidOperationException($"Dock item '{item.Id}' is not registered with this host.");
		}
	}

	// ---- tree mutation ----

	private void Detach(DockItem item)
	{
		DockGroup? group = item.Group;
		if (group == null)
		{
			return;
		}
		group.Items.Remove(item);
		if (group.Items.Count == 0)
		{
			DetachGroup(group);
		}
	}

	/// <summary>Removes the group from the tree and collapses splits left with one child.</summary>
	private void DetachGroup(DockGroup group)
	{
		DockSplitNode? parent = group.Parent;
		if (parent == null)
		{
			return;
		}
		parent.Children.Remove(group);
		Normalize(parent);
	}

	/// <summary>
	/// Inserts <paramref name="node"/> on the given side of <paramref name="target"/>, giving it
	/// <paramref name="share"/> of the space the two then occupy together.
	/// </summary>
	private void InsertRelative(DockNode node, DockNode target, DockSide side, double share)
	{
		Orientation orientation = side is DockSide.Left or DockSide.Right ? Orientation.Horizontal : Orientation.Vertical;
		bool before = side is DockSide.Left or DockSide.Top;
		DockSplitNode? parent = target.Parent;

		if (target is DockSplitNode targetSplit && targetSplit.Orientation == orientation)
		{
			// docking at the edge of a split that already runs this way: become its first or last child
			double total = targetSplit.TotalLength;
			node.Length = total * share / (1 - share);
			targetSplit.Children.Insert(before ? 0 : targetSplit.Children.Count, node);
			return;
		}

		if (parent != null && parent.Orientation == orientation)
		{
			// join the existing split; the newcomer takes its share from the target only
			double taken = target.Length * share;
			target.Length -= taken;
			node.Length = taken;
			int index = parent.Children.IndexOf(target);
			parent.Children.Insert(before ? index : index + 1, node);
			return;
		}

		var split = new DockSplitNode(orientation) { Length = target.Length };
		Replace(target, split);
		target.Length = 1 - share;
		node.Length = share;
		split.Children.Add(before ? node : target);
		split.Children.Add(before ? target : node);
	}

	private void Replace(DockNode old, DockNode replacement)
	{
		DockSplitNode? parent = old.Parent;
		if (parent == null)
		{
			_root = replacement;
			replacement.Parent = null;
			return;
		}
		int index = parent.Children.IndexOf(old);
		parent.Children[index] = replacement;
	}

	/// <summary>Collapses a split with one child and merges same-orientation nesting.</summary>
	private void Normalize(DockSplitNode split)
	{
		if (split.Children.Count == 0)
		{
			DockSplitNode? parent = split.Parent;
			if (parent != null)
			{
				parent.Children.Remove(split);
				Normalize(parent);
			}
			return;
		}
		if (split.Children.Count == 1)
		{
			DockNode only = split.Children[0];
			split.Children.RemoveAt(0);
			only.Length = split.Length;
			Replace(split, only);
			if (only is DockSplitNode inner && inner.Parent is { } outer && inner.Orientation == outer.Orientation)
			{
				Splice(inner, outer);
			}
			return;
		}
		foreach (DockNode child in split.Children.ToList())
		{
			if (child is DockSplitNode inner && inner.Orientation == split.Orientation)
			{
				Splice(inner, split);
			}
		}
	}

	/// <summary>Replaces <paramref name="inner"/> in <paramref name="outer"/> by its children, keeping proportions.</summary>
	private static void Splice(DockSplitNode inner, DockSplitNode outer)
	{
		double total = inner.TotalLength;
		double scale = total > 0 ? inner.Length / total : 1;
		int index = outer.Children.IndexOf(inner);
		outer.Children.RemoveAt(index);
		while (inner.Children.Count > 0)
		{
			DockNode child = inner.Children[^1];
			inner.Children.RemoveAt(inner.Children.Count - 1);
			child.Length *= scale;
			outer.Children.Insert(index, child);
		}
	}

	private void NormalizeTree(DockNode node)
	{
		if (node is not DockSplitNode split)
		{
			return;
		}
		foreach (DockNode child in split.Children.ToList())
		{
			NormalizeTree(child);
		}
		if (split.Parent != null || ReferenceEquals(split, _root))
		{
			Normalize(split);
		}
	}

	private static DockGroup NewGroup(DockItem item)
	{
		var group = new DockGroup();
		group.Items.Add(item);
		return group;
	}

	private static IEnumerable<DockGroup> Groups(DockNode node)
	{
		switch (node)
		{
			case DockGroup group:
				yield return group;
				break;
			case DockSplitNode split:
				foreach (DockNode child in split.Children)
				{
					foreach (DockGroup group in Groups(child))
					{
						yield return group;
					}
				}
				break;
		}
	}

	private static bool ContainsDocument(DockNode node) => node switch
	{
		DockDocumentNode => true,
		DockSplitNode split => split.Children.Any(ContainsDocument),
		_ => false,
	};

	// ---- persistence ----

	private NodeLayout Export(DockNode node) => node switch
	{
		DockSplitNode split => new SplitLayout
		{
			Length = split.Length,
			Orientation = split.Orientation,
			Children = split.Children.Select(Export).ToList(),
		},
		DockGroup group => new GroupLayout
		{
			Length = group.Length,
			Active = group.ActiveItem?.Id,
			Items = group.Items.Select(i => i.Id).ToList(),
		},
		_ => new DocumentLayout { Length = node.Length },
	};

	private DockNode? Import(NodeLayout layout, HashSet<DockItem> assigned)
	{
		double length = layout.Length > 0 ? layout.Length : 1;
		switch (layout)
		{
			case DocumentLayout:
				if (!assigned.Add(DocumentMarker))
				{
					return null; // a second document node is not allowed
				}
				_document.Length = length;
				return _document;

			case GroupLayout groupLayout:
			{
				var group = new DockGroup { Length = length };
				foreach (string id in groupLayout.Items)
				{
					if (_items.TryGetValue(id, out DockItem? item) && assigned.Add(item))
					{
						group.Items.Add(item);
					}
				}
				if (group.Items.Count == 0)
				{
					return null;
				}
				if (groupLayout.Active != null && _items.TryGetValue(groupLayout.Active, out DockItem? active))
				{
					group.ActiveItem = active;
				}
				return group;
			}

			case SplitLayout splitLayout:
			{
				var split = new DockSplitNode(splitLayout.Orientation) { Length = length };
				foreach (NodeLayout childLayout in splitLayout.Children)
				{
					DockNode? child = Import(childLayout, assigned);
					if (child != null)
					{
						split.Children.Add(child);
					}
				}
				return split.Children.Count switch
				{
					0 => null,
					1 => Unwrap(split),
					_ => split,
				};
			}

			default:
				return null;
		}
	}

	/// <summary>Placeholder entry in the "assigned" set so a layout can carry only one document node.</summary>
	private static readonly DockItem DocumentMarker = new("\0document", string.Empty, string.Empty);

	private static DockNode Unwrap(DockSplitNode split)
	{
		DockNode only = split.Children[0];
		split.Children.RemoveAt(0);
		only.Length = split.Length;
		return only;
	}

	// ---- visual tree ----

	private void ActivateGroupOf(DockItem item)
	{
		if (item.Group != null)
		{
			ActivateGroup(item.Group);
		}
	}

	private void ActivateGroup(DockGroup group) =>
		SetActiveGroup(_groupControls.FirstOrDefault(c => ReferenceEquals(c.Group, group)));

	private void CaptureSizes()
	{
		foreach ((DockNode node, FrameworkElement element) in _elements)
		{
			if (node.Parent is not { } parent)
			{
				continue;
			}
			double length = parent.Orientation == Orientation.Horizontal ? element.ActualWidth : element.ActualHeight;
			if (length > 0)
			{
				node.Length = length;
			}
		}
	}

	private void Rebuild()
	{
		HandOverDataContext();
		foreach (DockGroupControl control in _groupControls)
		{
			control.Detach();
		}
		if (VisualTreeHelper.GetParent(_center) is Panel centerParent)
		{
			centerParent.Children.Remove(_center);
		}
		Children.Clear();
		_elements.Clear();
		_groupControls.Clear();

		Children.Add(Build(_root));

		if (_activeGroup != null && !_groupControls.Contains(_activeGroup))
		{
			DockGroup group = _activeGroup.Group;
			_activeGroup = null;
			ActivateGroup(group);
		}
		LayoutChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The hosted contents whose data context the host set, so a change of the host's reaches them and one of their own is left alone.</summary>
	private readonly HashSet<FrameworkElement> _handedOver = [];

	/// <summary>
	/// Sets the host's data context on every hosted content (the items' and the document) that has none of its
	/// own, as a local value that survives the re-parenting of a rebuild; a content with its own stays untouched.
	/// </summary>
	private void HandOverDataContext()
	{
		object? context = DataContext;
		IEnumerable<FrameworkElement> contents = Items.Select(item => item.Content as FrameworkElement).Append(_center).OfType<FrameworkElement>();
		foreach (FrameworkElement element in contents)
		{
			if (_handedOver.Contains(element) || element.ReadLocalValue(DataContextProperty) == DependencyProperty.UnsetValue)
			{
				if (context != null)
				{
					element.DataContext = context;
					_handedOver.Add(element);
				}
			}
		}
	}

	private FrameworkElement Build(DockNode node)
	{
		FrameworkElement element;
		switch (node)
		{
			case DockGroup group:
			{
				var control = new DockGroupControl(this, group);
				_groupControls.Add(control);
				element = control;
				break;
			}
			case DockSplitNode split:
				element = BuildSplit(split);
				break;
			default:
				element = _center;
				break;
		}
		_elements[node] = element;
		return element;
	}

	private Grid BuildSplit(DockSplitNode split)
	{
		var grid = new Grid();
		bool columns = split.Orientation == Orientation.Horizontal;
		int slot = 0;
		for (int i = 0; i < split.Children.Count; i++)
		{
			DockNode child = split.Children[i];
			if (i > 0)
			{
				AddDefinition(grid, GridLength.Auto, 0, columns);
				AddSplitter(grid, columns ? 0 : slot, columns ? slot : 0, rows: !columns);
				slot++;
			}
			double min = ContainsDocument(child) ? DocumentMinSize : PaneMinSize;
			AddDefinition(grid, new GridLength(Math.Max(child.Length, 0.01), GridUnitType.Star), min, columns);
			FrameworkElement element = Build(child);
			SetRow(element, columns ? 0 : slot);
			SetColumn(element, columns ? slot : 0);
			grid.Children.Add(element);
			slot++;
		}
		return grid;
	}

	private static void AddDefinition(Grid grid, GridLength length, double min, bool columns)
	{
		if (columns)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length, MinWidth = min });
		}
		else
		{
			grid.RowDefinitions.Add(new RowDefinition { Height = length, MinHeight = min });
		}
	}

	private static void AddSplitter(Grid grid, int row, int column, bool rows)
	{
		var splitter = new GridSplitter
		{
			ResizeBehavior = GridResizeBehavior.PreviousAndNext,
			ResizeDirection = rows ? GridResizeDirection.Rows : GridResizeDirection.Columns,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			Focusable = false,
		};
		if (rows)
		{
			splitter.Height = SplitterThickness;
		}
		else
		{
			splitter.Width = SplitterThickness;
		}
		SetRow(splitter, row);
		SetColumn(splitter, column);
		grid.Children.Add(splitter);
	}

	private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		DockGroupControl? control = FindGroupControl(e.OriginalSource as DependencyObject);
		if (control != null)
		{
			SetActiveGroup(control);
		}
	}

	private void OnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		DockGroupControl? control = FindGroupControl(e.NewFocus as DependencyObject);
		if (control != null)
		{
			SetActiveGroup(control);
		}
	}
}
