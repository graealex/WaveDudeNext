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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WaveDudeNext.App.Commands;
using WaveDudeNext.Model;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Panes;

/// <summary>
/// The Properties pane: shows the editor view that matches the type of <see cref="SelectedObject"/>, mapped by the data
/// templates in the XAML, or a hint when nothing is selected. The views read <see cref="Project"/> (a source's wavelength
/// needs the solver settings, the scheme picker the project's own schemes), <see cref="Commands"/> (the scene image
/// row's buttons) and <see cref="SceneImageFileMissing"/> (the note beside the image path) through their ancestor, as
/// upstream's did through the window.
/// </summary>
public partial class PropertiesPane : UserControl
{
	/// <summary>Identifies the <see cref="SelectedObject"/> property.</summary>
	public static readonly StyledProperty<object?> SelectedObjectProperty =
		AvaloniaProperty.Register<PropertiesPane, object?>(nameof(SelectedObject));

	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly StyledProperty<Project?> ProjectProperty =
		AvaloniaProperty.Register<PropertiesPane, Project?>(nameof(Project));

	/// <summary>Identifies the <see cref="Commands"/> property.</summary>
	public static readonly StyledProperty<AppCommands> CommandsProperty =
		AvaloniaProperty.Register<PropertiesPane, AppCommands>(nameof(Commands), new AppCommands());

	/// <summary>Identifies the <see cref="SceneImageFileMissing"/> property.</summary>
	public static readonly StyledProperty<bool> SceneImageFileMissingProperty =
		AvaloniaProperty.Register<PropertiesPane, bool>(nameof(SceneImageFileMissing));

	/// <summary>Identifies the <see cref="RevealRequest"/> property.</summary>
	public static readonly StyledProperty<RevealRequest?> RevealRequestProperty =
		AvaloniaProperty.Register<PropertiesPane, RevealRequest?>(nameof(RevealRequest));

	/// <summary>Creates the pane showing the no-selection hint.</summary>
	public PropertiesPane()
	{
		InitializeComponent();
		UpdateContent();
	}

	/// <summary>The model object to edit, or null for the empty hint.</summary>
	public object? SelectedObject
	{
		get => GetValue(SelectedObjectProperty);
		set => SetValue(SelectedObjectProperty, value);
	}

	/// <summary>The project the selected object belongs to, for views that need its other parts (a source's wavelength needs the solver settings).</summary>
	public Project? Project
	{
		get => GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>The window's commands; the scene image row runs BrowseSceneImage, SaveSceneImage and RemoveSceneImage. Disabled commands until the window sets them.</summary>
	public AppCommands Commands
	{
		get => GetValue(CommandsProperty);
		set => SetValue(CommandsProperty, value);
	}

	/// <summary>Whether the scene image's file is not on disk (the window checks): the "(not found)" note beside the path.</summary>
	public bool SceneImageFileMissing
	{
		get => GetValue(SceneImageFileMissingProperty);
		set => SetValue(SceneImageFileMissingProperty, value);
	}

	/// <summary>
	/// The newest request to bring an item to the user (the selection's; <see cref="UI.RevealRequest"/>, bound by the
	/// window as the WPF pane binds it, 2026-11-11): one that asks for editing puts the caret into the first editor of the
	/// item's view, a new item's name.
	/// </summary>
	public RevealRequest? RevealRequest
	{
		get => GetValue(RevealRequestProperty);
		set => SetValue(RevealRequestProperty, value);
	}

	/// <summary>The control showing the current view, for the tests.</summary>
	internal ContentControl ViewHost => content;

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == SelectedObjectProperty)
		{
			UpdateContent();
		}
		else if (change.Property == RevealRequestProperty && change.GetNewValue<RevealRequest?>() is { Edit: true })
		{
			FocusFirstEditor();
		}
	}

	private void UpdateContent()
	{
		object? selected = SelectedObject;
		content.Content = selected;
		hint.IsVisible = selected == null;
	}

	/// <summary>Puts the caret into the first text box of the current view once it is built, selecting its text.</summary>
	public void FocusFirstEditor() =>
		Dispatcher.UIThread.Post(() =>
		{
			if (this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
			{
				box.Focus();
				box.SelectAll();
			}
		}, DispatcherPriority.Background);
}
