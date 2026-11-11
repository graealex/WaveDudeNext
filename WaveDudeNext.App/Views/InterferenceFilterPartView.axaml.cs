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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NLog;
using WaveDudeNext.Model;

namespace WaveDudeNext.App.Views;

/// <summary>
/// Editor for an <see cref="InterferenceFilterPart"/>: the plate's material (its coating on the back alone) and size, and
/// the layer list, the outermost layer first (2026-11-07, the user's request): a row per layer with its thickness and
/// index, buttons to move it up or down and to remove it (in the Tab cycle, the keyboard's way), a grip to drag it to
/// another place (the mouse's way: the row moves as the pointer passes over the others), and below an add button and a
/// button that removes every layer. Upstream routed the buttons through PartCommands; here they are the view's own
/// handlers on the model, as the point list's are. A row's number and whether it is the last are attached properties
/// the view sets on each container (<see cref="IndexProperty"/>, <see cref="IsLastProperty"/>), since Avalonia has no
/// AlternationIndex; the rows bind them for the ordinal and the two buttons' enabled state. The data context is the
/// part. UI thread.
/// </summary>
public partial class InterferenceFilterPartView : UserControl
{
	/// <summary>The place of a row's layer in the list, set on the row's container; -1 before the view said.</summary>
	public static readonly AttachedProperty<int> IndexProperty =
		AvaloniaProperty.RegisterAttached<InterferenceFilterPartView, Control, int>("Index", -1);

	/// <summary>Whether a row's layer is the last of the list, set on the row's container: its Down button is disabled.</summary>
	public static readonly AttachedProperty<bool> IsLastProperty =
		AvaloniaProperty.RegisterAttached<InterferenceFilterPartView, Control, bool>("IsLast");

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>The drag format of a layer row: a marker, in this process only; the layer itself stays in <see cref="_dragged"/>, and another target does not take it.</summary>
	private static readonly DataFormat<string> LayerFormat = DataFormat.CreateStringApplicationFormat("WaveDudeNext.FilterLayer");

	/// <summary>How far the pointer must move with the button held before a grip press becomes a drag, in pixels.</summary>
	private const double DragDistance = 4;

	/// <summary>The layer whose grip was pressed, the press (the drag starts from it) and where, until the pointer moves far enough to start a drag.</summary>
	private FilterLayer? _pressed;
	private PointerPressedEventArgs? _press;
	private Point _pressedAt;

	/// <summary>The layer being dragged by its grip, for the whole drag.</summary>
	private FilterLayer? _dragged;

	/// <summary>The list whose changes renumber the rows, so the handler can be taken off when the data context changes.</summary>
	private ObservableCollection<FilterLayer>? _watched;

	/// <summary>Creates the view.</summary>
	public InterferenceFilterPartView()
	{
		InitializeComponent();
		list.ContainerPrepared += (_, _) => RefreshRows();
		list.ContainerIndexChanged += (_, _) => RefreshRows();
		DragDrop.SetAllowDrop(list, true);
		list.AddHandler(DragDrop.DragOverEvent, OnLayerDragOver);
		list.AddHandler(DragDrop.DropEvent, OnLayerDrop);
		DataContextChanged += (_, _) => WatchLayers();
	}

	/// <summary>The edited list: the filter's layers, or null for another data context.</summary>
	private ObservableCollection<FilterLayer>? Layers => (DataContext as InterferenceFilterPart)?.Layers;

	/// <summary>Follows the current part's list: a layer added or removed renumbers the rows and moves the last one's mark.</summary>
	private void WatchLayers()
	{
		if (_watched != null)
		{
			_watched.CollectionChanged -= OnLayersChanged;
		}
		_watched = Layers;
		if (_watched != null)
		{
			_watched.CollectionChanged += OnLayersChanged;
		}
		RefreshRows();
	}

	private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshRows();

	/// <summary>Puts each realised row's place and whether it is the last on its container.</summary>
	private void RefreshRows()
	{
		int count = list.ItemCount;
		for (int i = 0; i < count; i++)
		{
			if (list.ContainerFromIndex(i) is Control row)
			{
				row.SetValue(IndexProperty, i);
				row.SetValue(IsLastProperty, i == count - 1);
			}
		}
	}

	/// <summary>Appends a copy of the last layer (a default layer to an empty list), so a stack of alternating layers grows by copying and changing.</summary>
	private void OnAddLayer(object? sender, RoutedEventArgs e) => Layers?.Add(Layers.LastOrDefault()?.Clone() ?? new FilterLayer());

	/// <summary>Removes the row's layer (the button's data context).</summary>
	private void OnRemoveLayer(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { DataContext: FilterLayer layer })
		{
			Layers?.Remove(layer);
		}
	}

	/// <summary>Empties the list in one step, so a stack of another design starts from nothing; Undo brings the layers back.</summary>
	private void OnRemoveAllLayers(object? sender, RoutedEventArgs e) => Layers?.Clear();

	private void OnMoveLayerUp(object? sender, RoutedEventArgs e) => MoveBy((sender as Button)?.DataContext as FilterLayer, -1, "Up");

	private void OnMoveLayerDown(object? sender, RoutedEventArgs e) => MoveBy((sender as Button)?.DataContext as FilterLayer, 1, "Down");

	/// <summary>
	/// Moves <paramref name="layer"/> <paramref name="delta"/> places and gives the keyboard focus back to the same button
	/// on its row, which the list shows in its new place: a press of the button again moves it on. At either end of the
	/// list, where that button is disabled, the focus goes to the other one.
	/// </summary>
	private void MoveBy(FilterLayer? layer, int delta, string button)
	{
		if (layer == null || Layers is not { } layers)
		{
			return;
		}
		int from = layers.IndexOf(layer);
		int to = from + delta;
		if (from < 0 || to < 0 || to >= layers.Count)
		{
			return;
		}
		layers.Move(from, to);
		Dispatcher.UIThread.Post(() =>
		{
			if (list.ContainerFromItem(layer) is Control row)
			{
				List<Button> buttons = row.GetVisualDescendants().OfType<Button>().ToList();
				Button? target = buttons.FirstOrDefault(b => Equals(b.Tag, button) && b.IsEnabled) ?? buttons.FirstOrDefault(b => b.Tag is string && b.IsEnabled);
				target?.Focus();
			}
		}, DispatcherPriority.Loaded);
	}

	private void OnGripPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			return;
		}
		_pressed = (sender as Control)?.DataContext as FilterLayer;
		_press = e;
		_pressedAt = e.GetPosition(this);
	}

	private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
	{
		_pressed = null;
		_press = null;
	}

	/// <summary>Starts the drag of the pressed row once the pointer has moved a few pixels with the button held.</summary>
	private async void OnGripMoved(object? sender, PointerEventArgs e)
	{
		if (_pressed is not { } layer || _press is not { } press)
		{
			return;
		}
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			_pressed = null;
			_press = null;
			return;
		}
		Point moved = e.GetPosition(this) - _pressedAt;
		if (Math.Abs(moved.X) < DragDistance && Math.Abs(moved.Y) < DragDistance)
		{
			return;
		}
		_pressed = null;
		_press = null;
		_dragged = layer;
		var payload = new DataTransferItem();
		payload.Set(LayerFormat, "layer");
		var transfer = new DataTransfer();
		transfer.Add(payload);
		try
		{
			// the platform disposes the transfer when the drag ends (its contract); a drag the platform cannot run is
			// logged and dropped, the list stays usable
			await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move);
		}
		catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
		{
			Logger.Warn(ex, "The layer drag could not start");
		}
		finally
		{
			_dragged = null;
		}
	}

	/// <summary>A dragged layer over another row takes that row's place at once, so the list shows where it will land; anything else dragged here is left to the window (a file it opens).</summary>
	private void OnLayerDragOver(object? sender, DragEventArgs e)
	{
		if (_dragged is not { } dragged || !e.DataTransfer.Contains(LayerFormat) || Layers is not { } layers)
		{
			return;
		}
		e.DragEffects = DragDropEffects.Move;
		e.Handled = true;
		if ((e.Source as Control)?.DataContext is FilterLayer over && !ReferenceEquals(over, dragged))
		{
			int from = layers.IndexOf(dragged);
			int to = layers.IndexOf(over);
			if (from >= 0 && to >= 0)
			{
				layers.Move(from, to);
			}
		}
	}

	/// <summary>The row is already in its place (<see cref="OnLayerDragOver"/>); the drop only ends the drag.</summary>
	private void OnLayerDrop(object? sender, DragEventArgs e)
	{
		if (_dragged != null && e.DataTransfer.Contains(LayerFormat))
		{
			e.DragEffects = DragDropEffects.Move;
			e.Handled = true;
		}
	}
}
