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
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WaveDudeNext.WPF.Docking;

/// <summary>
/// A tabbed set of <see cref="DockItem"/>s sharing one pane; a leaf of the layout tree.
/// </summary>
public sealed class DockGroup : DockNode, INotifyPropertyChanged
{
	private DockItem? _activeItem;

	public DockGroup()
	{
		Items.CollectionChanged += OnItemsChanged;
	}

	/// <summary>Items in tab order.</summary>
	public ObservableCollection<DockItem> Items { get; } = new();

	/// <summary>The item whose content is shown. Null only while the group is empty.</summary>
	public DockItem? ActiveItem
	{
		get => _activeItem;
		set
		{
			if (!ReferenceEquals(_activeItem, value) && (value == null || Items.Contains(value)))
			{
				_activeItem = value;
				OnPropertyChanged();
			}
		}
	}

	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.OldItems != null)
		{
			foreach (DockItem item in e.OldItems)
			{
				if (ReferenceEquals(item.Group, this))
				{
					item.Group = null;
				}
			}
		}
		if (e.NewItems != null)
		{
			foreach (DockItem item in e.NewItems)
			{
				item.Group = this;
			}
		}
		if (_activeItem == null || !Items.Contains(_activeItem))
		{
			ActiveItem = Items.Count > 0 ? Items[^1] : null;
		}
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
