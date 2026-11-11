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
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NLog;
using WaveDudeNext.App.Commands;
using WaveDudeNext.App.Dialogs;
using WaveDudeNext.App.Docking;
using WaveDudeNext.App.Localization;
using WaveDudeNext.App.Platform;
using WaveDudeNext.App.Theming;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;

namespace WaveDudeNext.App;

/// <summary>
/// The main window of the Avalonia head: the menu bar, the dock host with the Project and Properties panes around the
/// preview, the status bar. Like the WPF window, it is its own data context and holds the application's models of
/// <c>WaveDudeNext.UI</c>: the open project as a <see cref="Document"/>, what is selected as a <see cref="Selection"/>, the
/// current run as a <see cref="RunHost"/>, the live-editing rules as a <see cref="LivePreview"/>, the exports, the
/// brightness fit; the markup binds the panes to them, the menus to <see cref="Commands"/>, whose members are a line each
/// into a model. The window keeps what only it can do: the dialogs, the dock layout and the placement in the settings,
/// the zoom, F10 for the menu bar, a dropped file. The five interfaces the models need are the Avalonia pieces under
/// Platform/.
/// </summary>
public partial class MainWindow : Window
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	private readonly ProjectItems _items;
	private readonly ObjectClipboard _clipboard;
	private readonly UndoHistory _history;
	private readonly ExportPaths _exportPaths;
	private readonly EditBoundaries _boundaries;
	private bool _closeConfirmed;

	/// <summary>Creates the window: the models and their wiring as the WPF window has them, the markup, the commands, the menus, the layout and the placement from the settings, the zoom.</summary>
	public MainWindow()
	{
		var decoder = new SkiaImageDecoder();
		SceneImage = new SceneImageFile(Document, decoder, new AvaloniaSynchronizationContext(DispatcherPriority.Normal));
		// the embedded image replaced by its changed file is an edit complete in itself: it is finished when it arrives
		SceneImage.Reembedded += (_, _) => OnEditFinished();
		// the scene is composed when the UI thread is idle, so the changes of one drag step make one composition
		SceneComposer = new SceneComposer(Document, decoder, new AvaloniaSynchronizationContext(DispatcherPriority.Background));
		// back from the image editor: the scene image's file is looked at, should an event of its change have gone missing
		Activated += (_, _) => SceneImage.CheckFile();
		_exportPaths = new ExportPaths(Document);
		_items = new ProjectItems(Document, Selection);
		_clipboard = new ObjectClipboard(Document, Selection, _items, new AvaloniaClipboard(this));
		_history = new UndoHistory(Document, Selection);
		_history.PropertyChanged += (_, _) => Commands.Requery();
		// a step written back by Undo or Redo: the colours of the project as it is now (see OnProjectChanged)
		_history.Restored += (_, _) => Preview.RefreshColors();
		// the run's progress lands on the UI thread at Loaded priority, as in the WPF head (2026-11-04): ahead of queued input
		RunHost = new RunHost(Document, SceneComposer, new AvaloniaMemoryWarning(this), new AvaloniaSynchronizationContext(DispatcherPriority.Loaded));
		RunHost.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName is nameof(RunHost.Run) or nameof(RunHost.IsStarting))
			{
				Commands.Requery();
			}
		};
		RunHost.RunStateChanged += (_, _) => Commands.Requery();
		Live = new LivePreview(Document, RunHost, Preview, Busy, SceneComposer, new AvaloniaSynchronizationContext(DispatcherPriority.Normal));
		Exporter = new Exporter(Document, RunHost, Preview, Busy, SceneComposer, _exportPaths, new AvaloniaExportDialogs(this), new SkiaFrameDecorators(), new AvaloniaSynchronizationContext(DispatcherPriority.Normal));
		Exporter.PropertyChanged += (_, _) => Commands.Requery();
		Exporter.Failed += OnExportFailed;
		// the auto re-run held back during the export may be due now
		Exporter.Finished += (_, _) => Live.RunFinished();
		AutoBrightness = new AutoBrightness(Document, RunHost, Preview, Busy, SceneComposer, new AvaloniaSynchronizationContext(DispatcherPriority.Normal));
		AutoBrightness.PropertyChanged += (_, _) => Commands.Requery();
		// a fit that computed its frame arrived after the command that asked for it: an edit complete in itself
		AutoBrightness.Fitted += (_, _) => OnEditFinished();
		// the document's moments, wired before anything is loaded; its first, empty project is taken as a replacement was
		Document.ProjectReplacing += (_, _) => DiscardRun();
		Document.ProjectReplaced += (_, _) => OnProjectReplaced();
		Document.Changed += OnProjectChanged;
		Document.Opened += OnDocumentOpened;
		Document.Saved += (_, _) =>
		{
			_history.MarkSaved();
			SceneImage.Watch();
		};
		Document.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(ProjectDocument.Filename))
			{
				// File > Close is offered for a project with a file
				Commands.Requery();
			}
		};
		Selection.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(Selection.Current))
			{
				// most commands act on the current item
				Commands.Requery();
			}
		};
		Preview.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(PreviewState.IsPlaying))
			{
				Commands.Requery();
			}
		};
		OnProjectReplaced();
		DataContext = this;
		InitializeComponent();
		// an edit has finished when the window's input says so: the undo step ends, the stale run is redone
		_boundaries = new EditBoundaries(this, OnEditFinished);
		WireCommands();
		BuildMenus();
		AttachStatusBar();
		RestoreLayout();
		RestorePlacement();
		Settings.Default.PropertyChanged += OnSettingChanged;
		LanguageController.Instance.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(LanguageController.Current))
			{
				ApplyLanguageTexts();
			}
		};
		Closing += OnClosing;
		WindowZoom.Attach(this);
		KeyDown += OnWindowKeyDown;
		AddHandler(DragDrop.DropEvent, OnDrop);
	}

	/// <summary>The open project, its file and whether it was edited; the markup binds the project and the title there.</summary>
	public ProjectDocument Document { get; } = new();

	/// <summary>The embedded scene image and its file on disk.</summary>
	public SceneImageFile SceneImage { get; }

	/// <summary>The scene pixels the preview, the runs and the exports take.</summary>
	public SceneComposer SceneComposer { get; }

	/// <summary>The current item and the selected list; the tree, the preview and the properties pane bind them.</summary>
	public Selection Selection { get; } = new();

	/// <summary>What the application is busy with, for the status bar and the preview's dimming.</summary>
	public BusyState Busy { get; } = new();

	/// <summary>The preview as the application sees it: the position, playback, the canvas shown.</summary>
	public PreviewState Preview { get; } = new();

	/// <summary>The current run and its store.</summary>
	public RunHost RunHost { get; }

	/// <summary>The live-editing rules: the stale run, the scratch frame, the automatic re-run.</summary>
	public LivePreview Live { get; }

	/// <summary>The exports.</summary>
	public Exporter Exporter { get; }

	/// <summary>The pictures' Auto brightness.</summary>
	public AutoBrightness AutoBrightness { get; }

	/// <summary>The commands the menus, the panes and the overlays bind; each a line into a model (<see cref="WireCommands"/>).</summary>
	public AppCommands Commands { get; } = new();

	/// <summary>An edit has finished (the window's input said so, or an edit complete in itself arrived): the undo step ends, the stale run is redone.</summary>
	private void OnEditFinished()
	{
		_history.EndStep();
		Live.EditFinished();
	}

	/// <summary>Opens a file of any kind the document reads (a project, a Wave Dude 2D configuration, a colour scheme); an error is shown.</summary>
	internal async Task OpenFileAsync(string fileName)
	{
		if (!Document.TryOpen(fileName, out Exception? error))
		{
			await ShowErrorAsync(string.Format(Text("OpenError"), fileName), error);
		}
	}

	/// <summary>A new project: the selection, the scene and the export paths start over, the undo history with it, and the live rules last, so their first frame follows the new scene.</summary>
	private void OnProjectReplaced()
	{
		Selection.Items.Clear();
		SceneComposer.Reset();
		_exportPaths.Forget();
		SceneComposer.Update();
		_history.Start(Document.Project);
		Live.ProjectReplaced();
		Commands.Requery();
	}

	private void OnDocumentOpened(object? sender, OpenedEventArgs e)
	{
		if (e.Kind == OpenedKind.ProjectFile)
		{
			_exportPaths.Restore();
		}
		Selection.Reveal(e.Item);
		if (e.Kind == OpenedKind.ProjectFile)
		{
			SceneImage.Sync("changed since the project was saved");
		}
		else if (e.Kind == OpenedKind.ImportedProject)
		{
			SceneImage.Sync("imported");
		}
	}

	/// <summary>The project is being replaced: an export reading the run's store ends with what it has, then the run goes.</summary>
	private void DiscardRun()
	{
		if (Exporter.IsWriting)
		{
			Logger.Warn("Export cancelled: its run is being discarded");
			Exporter.Cancel();
		}
		_ = RunHost.Discard();
	}

	/// <summary>Avalonia's Closing cannot wait for the save question: it is cancelled, the question asked, and the window closed again with the answer in hand.</summary>
	private async void OnClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_closeConfirmed)
		{
			return;
		}
		e.Cancel = true;
		if (!await ConfirmDiscardAsync())
		{
			return;
		}
		_closeConfirmed = true;
		Settings.Default.Layout = dockHost.SaveLayout();
		SavePlacement();
		Exporter.Shutdown();
		RunHost.Shutdown();
		Live.Shutdown();
		SceneImage.Stop();
		Settings.Default.TrySave();
		Close();
	}

	// ---- the dialogs the window shows

	/// <summary>The save question for an edited project: true when the work may go on (saved, or discarded), false for Cancel or a failed save.</summary>
	private async Task<bool> ConfirmDiscardAsync()
	{
		if (!Document.IsDirty)
		{
			return true;
		}
		if (Environment.GetEnvironmentVariable("WAVEDUDE_DISCARD_EDITS") == "1")
		{
			// a UI harness cannot answer the question by posted keys: it discards
			Logger.Debug("Edits discarded without asking (WAVEDUDE_DISCARD_EDITS)");
			return true;
		}
		string name = Document.Filename != null ? Path.GetFileName(Document.Filename) : Text("Untitled");
		MessageAnswer answer = await MessageWindow.Show(this, string.Format(Text("SaveChangesFormat"), name), MessageKind.Question, MessageButtons.SaveDiscardCancel);
		return answer switch
		{
			MessageAnswer.Save => await SaveAsync(),
			MessageAnswer.Discard => true,
			_ => false,
		};
	}

	/// <summary>Shows an error with the exception's own words on a second line.</summary>
	private Task ShowErrorAsync(string message, Exception? error) =>
		MessageWindow.Show(this, error == null ? message : message + Environment.NewLine + (error is RunSpecException { Key: { } key } && this.TryFindResource(key, out object? reason) && reason is string text ? text : error.Message), MessageKind.Error, MessageButtons.Ok);

	/// <summary>Picks one file to open; null when cancelled. A null title leaves the platform's own.</summary>
	private async Task<string?> PickOpenFileAsync(string? title, string filter, string? startFolder)
	{
		IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = title,
			AllowMultiple = false,
			FileTypeFilter = AvaloniaExportDialogs.FileTypesFrom(filter),
			SuggestedStartLocation = await StartFolderAsync(startFolder),
		});
		return files.Count > 0 ? files[0].TryGetLocalPath() : null;
	}

	/// <summary>Picks a file to write; null when cancelled.</summary>
	private async Task<string?> PickSaveFileAsync(string? title, string filter, string suggestedName, string? defaultExtension, string? startFolder)
	{
		IReadOnlyList<FilePickerFileType> types = AvaloniaExportDialogs.FileTypesFrom(filter);
		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = title,
			SuggestedFileName = suggestedName,
			DefaultExtension = defaultExtension?.TrimStart('.'),
			FileTypeChoices = types,
			SuggestedFileType = types.Count > 0 ? types[0] : null,
			ShowOverwritePrompt = true,
			SuggestedStartLocation = await StartFolderAsync(startFolder),
		});
		return file?.TryGetLocalPath();
	}

	private async Task<IStorageFolder?> StartFolderAsync(string? folder) =>
		folder != null && Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;

	/// <summary>A string of the table by key; the key itself when the table lacks it.</summary>
	private string Text(string key) => this.TryFindResource(key, out object? value) && value is string text ? text : key;

	// ---- the file commands

	private async Task NewAsync()
	{
		if (await ConfirmDiscardAsync())
		{
			Document.New();
		}
	}

	private async Task OpenAsync()
	{
		string? file = await PickOpenFileAsync(null, Text("OpenFileDialogFilter"), Document.Folder);
		if (file != null && await ConfirmDiscardAsync())
		{
			await OpenFileAsync(file);
		}
	}

	private async Task OpenRecentAsync(string path)
	{
		if (await ConfirmDiscardAsync())
		{
			await OpenFileAsync(path);
		}
	}

	private async Task OpenExampleAsync(ExampleEntry example)
	{
		if (await ConfirmDiscardAsync() && !Document.TryOpenExample(example, out Exception? error))
		{
			await ShowErrorAsync(string.Format(Text("OpenError"), example.FileName), error);
		}
	}

	/// <summary>Save, or Save As for a project without a file; false when the user cancelled or the save failed.</summary>
	private Task<bool> SaveAsync() => Document.Filename != null ? SaveFileAsync(Document.Filename) : SaveAsAsync();

	private async Task<bool> SaveAsAsync()
	{
		string? file = await PickSaveFileAsync(null, Text("SaveFileDialogFilter"), Document.Filename != null ? Path.GetFileName(Document.Filename) : Document.SuggestedFileName ?? "", ProjectDocument.Extension, Document.Folder);
		return file != null && await SaveFileAsync(file);
	}

	private async Task<bool> SaveFileAsync(string fileName)
	{
		if (Document.TrySave(fileName, out Exception? error))
		{
			return true;
		}
		await ShowErrorAsync(string.Format(Text("SaveError"), fileName), error);
		return false;
	}

	private async Task CloseProjectAsync()
	{
		if (await ConfirmDiscardAsync())
		{
			Document.New();
		}
	}

	private async Task BrowseSceneImageAsync()
	{
		string? file = await PickOpenFileAsync(Text("TipBrowseSceneImage"), Text("SceneImageFilter"), Document.Folder);
		if (file != null)
		{
			SceneImage.TryEmbed(file);
		}
	}

	private async Task SaveSceneImageAsync()
	{
		if (!SceneImage.CanSave)
		{
			return;
		}
		string extension = SceneImage.SaveExtension;
		// the embedded file's own type; an unknown one gets the picker's list
		string filter = extension.Length > 0 ? string.Format(Text("SceneImageSaveFilterFormat"), extension) : Text("SceneImageFilter");
		string? file = await PickSaveFileAsync(Text("TipSaveSceneImage"), filter, SceneImage.SaveFileName, extension.Length > 0 ? extension : null, SceneImage.SaveFolder);
		if (file == null)
		{
			Logger.Debug("Scene image save cancelled in the Save dialog");
			return;
		}
		if (!SceneImage.TrySave(file, out Exception? error) && error != null)
		{
			await ShowErrorAsync(string.Format(Text("SceneImageSaveError"), file), error);
		}
	}

	// ---- the keys and the drop the window handles itself

	/// <summary>The menu bar is out of the Tab cycle: F10 is the way in (the WPF head's rule). Menu.Open is Avalonia's answer to Alt and F10: the first header takes the keyboard focus, Down opens it, Left and Right move between headers.</summary>
	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (!e.Handled && e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None)
		{
			menu.Open();
			e.Handled = true;
		}
	}

	/// <summary>A file dropped anywhere on the window opens it (after the save question), as the WPF window does.</summary>
	private async void OnDrop(object? sender, DragEventArgs e)
	{
		string? file = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).FirstOrDefault(path => path != null);
		if (file != null && await ConfirmDiscardAsync())
		{
			await OpenFileAsync(file);
		}
	}

	// ---- the dock layout and the window placement

	/// <summary>The saved dock layout, else the default arrangement the markup declares; failing both, the two panes at their sides.</summary>
	private void RestoreLayout()
	{
		bool restored = Settings.Default.Layout is { } saved && dockHost.LoadLayout(saved);
		if (!restored && dockHost.DefaultLayout != null)
		{
			restored = dockHost.LoadLayout(dockHost.DefaultLayout);
		}
		if (!restored)
		{
			Logger.Warn("No dock layout could be loaded: the Project and Properties panes take their default sides");
			foreach (DockItem item in dockHost.Items)
			{
				if (item.Id is "project" or "properties")
				{
					dockHost.Show(item);
				}
			}
		}
	}

	/// <summary>The window's size, position and state as the last session left them (the size at zoom 1, which the zoom multiplies).</summary>
	private void RestorePlacement()
	{
		Settings settings = Settings.Default;
		Width = settings.WindowWidth;
		Height = settings.WindowHeight;
		if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
		{
			WindowStartupLocation = WindowStartupLocation.Manual;
			Position = new PixelPoint((int)Math.Round(settings.WindowLeft), (int)Math.Round(settings.WindowTop));
		}
		if (settings.WindowState == WindowMode.Maximized)
		{
			WindowState = WindowState.Maximized;
		}
	}

	/// <summary>Never saved as minimised; a maximised window keeps its normal size for the next start.</summary>
	private void SavePlacement()
	{
		Settings settings = Settings.Default;
		settings.WindowState = WindowState == WindowState.Maximized ? WindowMode.Maximized : WindowMode.Normal;
		if (WindowState == WindowState.Normal)
		{
			settings.WindowWidth = Width / WindowZoom.Scale;
			settings.WindowHeight = Height / WindowZoom.Scale;
			settings.WindowLeft = Position.X;
			settings.WindowTop = Position.Y;
		}
	}
}
