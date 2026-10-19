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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace WaveDudeNext.WPF.Docking;

/// <summary>
/// Visual for one <see cref="DockGroup"/>: title bar with position menu and close button, the
/// active item's content, and a tab strip when the group holds more than one item. The template
/// lives in <c>Themes/Controls.xaml</c>, binds to <see cref="Group"/> through the templated
/// parent and names the parts used here; its menu items use <see cref="DockCommands"/>, which
/// this control binds. The data context is left untouched so hosted content inherits the
/// window's.
/// </summary>
public sealed class DockGroupControl : Control
{
	public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
		nameof(IsActive), typeof(bool), typeof(DockGroupControl), new PropertyMetadata(false));

	private Border? _titleBar;
	private ListBox? _tabs;
	private ContentPresenter? _content;
	private Button? _menuButton;

	internal DockGroupControl(DockHost host, DockGroup group)
	{
		Host = host;
		Group = group;
		Focusable = false;
		CommandBindings.Add(new CommandBinding(DockCommands.MoveToSide, OnMoveToSide, CanMoveToSide));
		CommandBindings.Add(new CommandBinding(DockCommands.Close, OnClose, CanClose));
	}

	/// <summary>Host this control was built by.</summary>
	public DockHost Host { get; }

	/// <summary>Group this control shows.</summary>
	public DockGroup Group { get; }

	/// <summary>True for the group that last received mouse or keyboard focus.</summary>
	public bool IsActive
	{
		get => (bool)GetValue(IsActiveProperty);
		set => SetValue(IsActiveProperty, value);
	}

	/// <inheritdoc/>
	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		if (_titleBar != null)
		{
			_titleBar.MouseLeftButtonDown -= OnTitleBarMouseDown;
		}
		if (_tabs != null)
		{
			_tabs.PreviewMouseLeftButtonDown -= OnTabMouseDown;
		}
		if (_menuButton != null)
		{
			_menuButton.Click -= OnMenuButtonClick;
		}

		_titleBar = GetTemplateChild("PART_TitleBar") as Border;
		_tabs = GetTemplateChild("PART_Tabs") as ListBox;
		_content = GetTemplateChild("PART_Content") as ContentPresenter;
		_menuButton = GetTemplateChild("PART_MenuButton") as Button;

		if (_titleBar != null)
		{
			_titleBar.MouseLeftButtonDown += OnTitleBarMouseDown;
		}
		if (_tabs != null)
		{
			_tabs.PreviewMouseLeftButtonDown += OnTabMouseDown;
		}
		if (_menuButton != null)
		{
			_menuButton.Click += OnMenuButtonClick;
		}
	}

	/// <summary>Releases the hosted content so a new control can take it after a rebuild.</summary>
	internal void Detach()
	{
		if (_content != null)
		{
			BindingOperations.ClearBinding(_content, ContentPresenter.ContentProperty);
			_content.Content = null;
		}
	}

	private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
	{
		Host.SetActiveGroup(this);
		Host.DragManager.Arm(this, null, e.GetPosition(Host));
	}

	private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (_tabs == null)
		{
			return;
		}
		if (ItemsControl.ContainerFromElement(_tabs, e.OriginalSource as DependencyObject) is ListBoxItem { Content: DockItem item })
		{
			Host.DragManager.Arm(this, item, e.GetPosition(Host));
		}
	}

	/// <summary>The position menu is a context menu opened by a left click on the title bar button.</summary>
	private void OnMenuButtonClick(object sender, RoutedEventArgs e)
	{
		if (_menuButton?.ContextMenu is { } menu)
		{
			menu.PlacementTarget = _menuButton;
			menu.Placement = PlacementMode.Bottom;
			menu.IsOpen = true;
		}
	}

	private void CanMoveToSide(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = e.Parameter is DockSide && Group.ActiveItem != null;

	private void OnMoveToSide(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is DockSide side && Group.ActiveItem is { } item)
		{
			Host.DockToEdge(item, side);
		}
	}

	private void CanClose(object sender, CanExecuteRoutedEventArgs e) =>
		e.CanExecute = Group.ActiveItem is { CanClose: true };

	private void OnClose(object sender, ExecutedRoutedEventArgs e)
	{
		if (Group.ActiveItem is { } item)
		{
			Host.Hide(item);
		}
	}
}
