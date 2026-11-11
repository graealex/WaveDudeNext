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
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using NLog;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;
using WaveDudeNext.Rendering;
using WaveDudeNext.UI;
using WaveDudeNext.UI.Docking;
using WaveDudeNext.WPF.Dialogs;
using WaveDudeNext.WPF.Docking;
using WaveDudeNext.WPF.Localization;
using WaveDudeNext.WPF.Platform;
using WaveDudeNext.WPF.Theming;

namespace WaveDudeNext.WPF;

/// <summary>
/// Shell window: menu, status bar and the <see cref="DockHost"/> with the Project and
/// Properties panes around the preview. The window is its own data context: it holds the open project as a
/// <see cref="Document"/> (the project, its file name and whether it was edited; the XAML binds them there), what is
/// selected in it as a <see cref="Selection"/>, the current run as a <see cref="RunHost"/> and the live-editing rules as a
/// <see cref="LivePreview"/>, and shows the dialogs the file commands need. Window placement is bound two-way to <see cref="Settings"/> in the
/// XAML; the pane arrangement is stored there on closing.
/// </summary>
public partial class MainWindow : Window
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	public MainWindow()
	{
		var decoder = new WpfImageDecoder();
		SceneImage = new SceneImageFile(Document, decoder, new DispatcherSynchronizationContext(Dispatcher));
		// the embedded image replaced by its changed file is an edit complete in itself: it is finished when it arrives
		SceneImage.Reembedded += (_, _) => OnEditFinished();
		// the scene is composed when the UI thread is idle, so the changes of one drag step make one composition
		SceneComposer = new SceneComposer(Document, decoder, new DispatcherSynchronizationContext(Dispatcher, DispatcherPriority.Background));
		// back from the image editor: the scene image's file is looked at, should an event of its change have gone missing
		Activated += (_, _) => SceneImage.CheckFile();
		_exportPaths = new ExportPaths(Document);
		_items = new ProjectItems(Document, Selection);
		_clipboard = new ObjectClipboard(Document, Selection, _items, new WpfClipboard());
		_history = new UndoHistory(Document, Selection);
		_history.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
		// a step written back by Undo or Redo: the colours of the project as it is now (see OnProjectChanged)
		_history.Restored += (_, _) => Preview.RefreshColors();
		// an edit has finished when the window's input says so: the undo step ends, the stale run is redone
		_ = new EditBoundaries(this, OnEditFinished);
		// the run's progress lands on the UI thread at Loaded priority (2026-11-04): the lowest priority the dispatcher
		// still processes ahead of queued input. Background and Input priority are its "background" range, which it
		// serves only when no input is pending, else through a 1 ms Win32 timer, a message Windows delivers after every
		// queued input message; a mouse reporting a thousand times a second kept input pending the whole time, so the
		// heartbeat, the Live follow with it and the run's final state waited until the hand came off the mouse (a
		// flood of 4 s left the status at the step where it started and the pictures still). The 2026-10-16 choice of
		// Background had cured the opposite, every click and key delivered after the run, when the progress was posted
		// at normal priority as separate posts, each the moment the last was handled; since the one paced heartbeat
		// (at most 30 a second, a few milliseconds each) input has its turn between two of them at any priority
		RunHost = new RunHost(Document, SceneComposer, new WpfMemoryWarning(this), new DispatcherSynchronizationContext(Dispatcher, DispatcherPriority.Loaded));
		RunHost.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(RunHost.Run))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		};
		RunHost.RunStateChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
		Live = new LivePreview(Document, RunHost, Preview, Busy, SceneComposer, new DispatcherSynchronizationContext(Dispatcher));
		Exporter = new Exporter(Document, RunHost, Preview, Busy, SceneComposer, _exportPaths, new WpfExportDialogs(this), new WpfFrameDecorators(), new DispatcherSynchronizationContext(Dispatcher));
		Exporter.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
		Exporter.Failed += OnExportFailed;
		// the auto re-run held back during the export may be due now
		Exporter.Finished += (_, _) => Live.RunFinished();
		AutoBrightness = new AutoBrightness(Document, RunHost, Preview, Busy, SceneComposer, new DispatcherSynchronizationContext(Dispatcher));
		AutoBrightness.PropertyChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
		// a fit that computed its frame arrived after the command that asked for it: an edit complete in itself
		AutoBrightness.Fitted += (_, _) => OnEditFinished();
		// the document's moments, wired before anything is loaded; its first, empty project is taken as a replacement was
		Document.ProjectReplacing += (_, _) => DiscardRun();
		Document.ProjectReplaced += (_, _) => OnProjectReplaced(Document.Project);
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
				CommandManager.InvalidateRequerySuggested();
			}
		};
		Selection.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(Selection.Current))
			{
				// most commands act on the current item
				CommandManager.InvalidateRequerySuggested();
			}
		};
		OnProjectReplaced(Document.Project);
		InitializeComponent();

		if (!TryLoadLayout() && (dockHost.DefaultLayout == null || !dockHost.LoadLayout(dockHost.DefaultLayout)))
		{
			// Messages stays hidden until asked for (View > Panes, status bar double-click)
			dockHost.Show(projectDock);
			dockHost.Show(propertiesDock);
		}

		SourceInitialized += (_, _) => ThemeController.Instance.ApplyTitleBar(this);
		// texts taken once at construction follow a language change here: the dock titles (plain properties) and the
		// theme entries (converted headers); everything else is a DynamicResource (2026-10-14)
		LanguageController.Instance.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(LanguageController.Current))
			{
				ApplyLanguageTexts();
			}
		};
		// the engine choice is only ever written through the Simulation menu; say so in the log with a time
		// stamp, so a run on the CPU can be traced back to the click that caused it (2026-10-10)
		Settings.Default.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(Settings.UseGpu))
			{
				Logger.Info(Settings.Default.UseGpu ? "Use the GPU switched on (Simulation menu)" : "Use the GPU switched off (Simulation menu): runs use the CPU from now on");
			}
			else if (args.PropertyName == nameof(Settings.WarnWhenRamInsufficient))
			{
				Logger.Info(Settings.Default.WarnWhenRamInsufficient ? "Memory warning switched on (Simulation menu)" : "Memory warning switched off: a run whose frames do not fit in the RAM cache goes to the cache file without asking");
			}
			else if (args.PropertyName == nameof(Settings.FrameMemoryPercent))
			{
				int percent = Settings.Default.FrameMemoryPercent;
				double budget = FrameMemory.ToGigabytes(FrameMemory.Budget(percent));
				if (percent <= FrameMemory.Automatic)
				{
					Logger.Info("RAM cache size: automatic (what is free minus {Margin:0.0} GB), {Budget:0.0} GB right now (Simulation menu or the memory warning)", FrameMemory.ToGigabytes(FrameMemory.Margin), budget);
				}
				else
				{
					Logger.Info("RAM cache size: at most {Percent} % of the RAM, {Budget:0.0} GB right now (Simulation menu)", percent, budget);
				}
			}
			else if (args.PropertyName is nameof(Settings.PreviewResolution) or nameof(Settings.PreviewFrames) or nameof(Settings.PreviewSampleBytes))
			{
				Logger.Info("Preview quality: {Quality} (View menu or the memory warning)", RunHost.PreviewQualitySetting);
				Live.PreviewQualityChanged();
			}
			else if (args.PropertyName == nameof(Settings.PreviewDitherBits))
			{
				Logger.Info("Preview dithering {State} (View menu)", Settings.Default.PreviewDitherBits == 0 ? "off" : "from " + Settings.Default.PreviewDitherBits + " bits per channel");
			}
			else if (args.PropertyName == nameof(Settings.UiZoom))
			{
				Logger.Info("UI zoom {Zoom} (View menu)", Settings.Default.UiZoom);
			}
			else if (args.PropertyName == nameof(Settings.RecentFiles) && Settings.Default.RecentFiles is [string newest, ..])
			{
				// the taskbar's jump list follows the recent files: the list is a fresh one on every change, the newest first
				JumpList.AddToRecentCategory(newest);
			}
		};
		Closing += (_, e) =>
		{
			if (!ConfirmDiscard())
			{
				e.Cancel = true;
				return;
			}
			Settings.Default.Layout = dockHost.SaveLayout();
			Exporter.Shutdown();
			RunHost.Shutdown();
			Live.Shutdown();
			SceneImage.Stop();
		};
	}

	/// <summary>
	/// The open project as a document: the project, the file it came from, whether it was edited, and what New, Open,
	/// Save and the examples do with them. The XAML binds its properties; the window reacts to its events (wired in the
	/// constructor) and shows the dialogs.
	/// </summary>
	public ProjectDocument Document { get; } = new();

	/// <summary>The open project (<see cref="ProjectDocument.Project"/>), for the window's own code. Never null.</summary>
	private Project Project => Document.Project;

	/// <summary>The project's file (<see cref="ProjectDocument.Filename"/>), for the window's own code; null for an unsaved project.</summary>
	private string? Filename => Document.Filename;

	/// <summary>The name an unsaved project came with (<see cref="ProjectDocument.SuggestedFileName"/>), for the window's own code.</summary>
	private string? SuggestedFileName => Document.SuggestedFileName;

	/// <summary>The folder of the project file (<see cref="ProjectDocument.Folder"/>), or null for an unsaved project.</summary>
	private string? ProjectFolder => Document.Folder;

	/// <summary>
	/// The scene image and its file: embedding, dropping and writing it out, and the watch that keeps the embedded image
	/// in step with the file. The scene form binds whether the file is missing; the commands' dialogs are the window's.
	/// </summary>
	public SceneImageFile SceneImage { get; }

	/// <summary>The scene as pixels, the image with the parts painted in: the preview binds <see cref="UI.SceneComposer.Pixels"/>, the runs take <see cref="UI.SceneComposer.Load"/>.</summary>
	public SceneComposer SceneComposer { get; }

	/// <summary>
	/// What is selected: the current item, which the properties pane shows, and the items selected with it. The tree, the
	/// preview and the properties pane bind it in the XAML, so a selection made in one shows in the others; the commands
	/// read and set it.
	/// </summary>
	public Selection Selection { get; } = new();

	/// <summary>
	/// What the application is busy with (an export, its wait for a run, a frame computed for the preview): the status
	/// bar's indicator and the preview's dimming bind it in the XAML; the export flow and the scratch requests tell it.
	/// </summary>
	public BusyState Busy { get; } = new();

	/// <summary>
	/// The preview as the application sees it: the position and whether it plays (both ways), what the pictures show and
	/// the canvas they show (told by the pane), the colour changes the pane follows. The preview pane binds it in the XAML;
	/// the scratch requests follow the position, the rules stop playback, the brightness fit and the new items read it.
	/// </summary>
	public PreviewState Preview { get; } = new();

	/// <summary>
	/// The current run and its frame store, and whether an edit since made it or its probe recordings stale. The preview
	/// and the status bar bind it in the XAML; the simulation commands are one line each into it.
	/// </summary>
	public RunHost RunHost { get; }

	/// <summary>
	/// The live-editing rules: what an edit, a scrub or the end of a run means for the run, the scratch frame and the
	/// automatic re-run. The preview binds the scratch frame in the XAML; the edit hub hands every change to it.
	/// </summary>
	public LivePreview Live { get; }

	/// <summary>
	/// Carries the exports out: the dialog, the run, the job, Export last, a probe's data. The export commands are one line
	/// each into it; the window shows what fails.
	/// </summary>
	public Exporter Exporter { get; }

	/// <summary>The pictures' Auto brightness: a one-shot fit to the frame shown, computed in 16-bit first for an 8-bit preview. The command is one line into it.</summary>
	public AutoBrightness AutoBrightness { get; }

	/// <summary>
	/// An edit has finished, by the window's input (<see cref="EditBoundaries"/>) or by arriving on its own (the scene
	/// image re-embedded, a brightness fit computed): the undo history closes its step, and the live rules redo a stale run.
	/// </summary>
	private void OnEditFinished()
	{
		_history.EndStep();
		Live.EditFinished();
	}

	/// <summary>
	/// Opens the file at <paramref name="fileName"/>, a project, a project of Wave Dude 2D or a colour scheme
	/// (<see cref="ProjectDocument.TryOpen"/>). A failure is shown to the user; the previous project stays open then.
	/// </summary>
	internal void OpenFile(string fileName)
	{
		if (!Document.TryOpen(fileName, out Exception? error))
		{
			ShowError(string.Format((string)FindResource("OpenError"), fileName), error);
		}
	}

	/// <summary>
	/// Something was opened (<see cref="ProjectDocument.Opened"/>): it is selected and brought to the user, a project of
	/// any origin at the tree's root, a colour scheme at its row; a project from its file gets the export paths the settings keep for it; and the embedded
	/// scene image is compared with its file, a changed file taken (the project is then dirty) and watched from here on.
	/// </summary>
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

	/// <summary>Writes the open project to <paramref name="fileName"/>; false when that failed, which is shown to the user.</summary>
	private bool SaveFile(string fileName)
	{
		if (Document.TrySave(fileName, out Exception? error))
		{
			return true;
		}
		ShowError(string.Format((string)FindResource("SaveError"), fileName), error);
		return false;
	}

	/// <summary>Shows an error: what could not be done, and the reason, in the user's language where the error names a text of the string tables.</summary>
	private void ShowError(string message, Exception ex) =>
		MessageWindow.Show(this, $"{message}\n{(ex is RunSpecException { Key: { } key } && TryFindResource(key) is string reason ? reason : ex.Message)}", MessageKind.Error, MessageButtons.Ok);

	/// <summary>
	/// Asks whether to save an edited project before it is discarded: Save saves (through Save As when it has
	/// no file yet), Don't save discards, Cancel keeps it. True when the caller may go on.
	/// </summary>
	private bool ConfirmDiscard()
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
		string name = Filename != null ? Path.GetFileName(Filename) : (string)FindResource("Untitled");
		MessageAnswer answer = MessageWindow.Show(this, string.Format((string)FindResource("SaveChangesFormat"), name),
			MessageKind.Question, MessageButtons.SaveDiscardCancel);
		return answer switch
		{
			MessageAnswer.Save => SaveInteractively(),
			MessageAnswer.Discard => true,
			_ => false,
		};
	}

	/// <summary>Save, or Save As for a project without a file; false when the user cancelled or the save failed.</summary>
	private bool SaveInteractively()
	{
		if (Filename != null)
		{
			return SaveFile(Filename);
		}
		var dialog = new SaveFileDialog
		{
			Filter = (string)FindResource("SaveFileDialogFilter"),
			DefaultExt = ProjectDocument.Extension,
			AddExtension = true,
			FileName = SuggestedFileName ?? "",
		};
		return dialog.ShowDialog(this) == true && SaveFile(dialog.FileName);
	}

	/// <summary>Simulation > RAM cache size: the chosen share of the RAM, applied to the next run.</summary>
	private void OnSetFrameMemory(object sender, ExecutedRoutedEventArgs e) => SetIntSetting(e, percent => Settings.Default.FrameMemoryPercent = percent);

	/// <summary>View > Preview resolution: the canvas divisor (1, 2, 4), applied to the next scratch frame and run.</summary>
	private void OnSetPreviewResolution(object sender, ExecutedRoutedEventArgs e) => SetIntSetting(e, divisor => Settings.Default.PreviewResolution = divisor);

	/// <summary>View > Preview frames: the multiplier of the project's visualisation interval (1, 2, 4).</summary>
	private void OnSetPreviewFrames(object sender, ExecutedRoutedEventArgs e) => SetIntSetting(e, divisor => Settings.Default.PreviewFrames = divisor);

	/// <summary>View > Preview samples: the bytes per sample (2 halves, 1 palette indices).</summary>
	private void OnSetPreviewSamples(object sender, ExecutedRoutedEventArgs e) => SetIntSetting(e, bytes => Settings.Default.PreviewSampleBytes = bytes);

	/// <summary>View > Preview dithering: the bits per channel the 16-bit pictures are dithered from (0 none, 10, 12, 14, 16); the pictures redraw.</summary>
	private void OnSetPreviewDither(object sender, ExecutedRoutedEventArgs e) => SetIntSetting(e, bits => Settings.Default.PreviewDitherBits = bits);

	/// <summary>View > UI zoom: the factor of every window's content, from the parameter as text with a decimal point (0.5 to 3); applied live by WindowZoom.</summary>
	private void OnSetUiZoom(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is string text && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double factor))
		{
			Settings.Default.UiZoom = factor;
			Settings.Default.TrySave();
		}
	}

	/// <summary>A menu choice carried as a number in the command parameter (an int, or digits as text): stores it and saves the settings.</summary>
	private static void SetIntSetting(ExecutedRoutedEventArgs e, Action<int> apply)
	{
		int value;
		if (e.Parameter is int number)
		{
			value = number;
		}
		else if (e.Parameter is not string text || !int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
		{
			return;
		}
		apply(value);
		Settings.Default.TrySave();
	}

	private void OnFileNew(object sender, ExecutedRoutedEventArgs e)
	{
		if (ConfirmDiscard())
		{
			Document.New();
		}
	}

	private void OnFileOpen(object sender, ExecutedRoutedEventArgs e)
	{
		if (!ConfirmDiscard())
		{
			return;
		}
		if (e.Parameter is string recent)
		{
			OpenFile(recent);
			return;
		}
		var dialog = new OpenFileDialog
		{
			Filter = (string)FindResource("OpenFileDialogFilter"),
			DefaultExt = ProjectDocument.Extension,
		};
		if (dialog.ShowDialog(this) == true)
		{
			OpenFile(dialog.FileName);
		}
	}

	/// <summary>
	/// File > Examples > one of the shipped examples (2026-10-29): the embedded project becomes the open project, unsaved
	/// and without a file, its own file name suggested when it is saved; a failure leaves the previous project open.
	/// </summary>
	private void OnFileOpenExample(object sender, ExecutedRoutedEventArgs e)
	{
		if (e.Parameter is ExampleEntry example && ConfirmDiscard() && !Document.TryOpenExample(example, out Exception? error))
		{
			ShowError(string.Format((string)FindResource("OpenError"), example.FileName), error);
		}
	}

	private void OnFileSave(object sender, ExecutedRoutedEventArgs e)
	{
		if (Filename == null)
		{
			OnFileSaveAs(sender, e);
		}
		else
		{
			SaveFile(Filename);
		}
	}

	private void OnFileSaveAs(object sender, ExecutedRoutedEventArgs e)
	{
		var dialog = new SaveFileDialog
		{
			Filter = (string)FindResource("SaveFileDialogFilter"),
			DefaultExt = ProjectDocument.Extension,
			AddExtension = true,
			FileName = Filename ?? SuggestedFileName ?? "",
		};
		if (dialog.ShowDialog(this) == true)
		{
			SaveFile(dialog.FileName);
		}
	}

	private void CanFileClose(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Filename != null;

	private void OnFileClose(object sender, ExecutedRoutedEventArgs e)
	{
		if (ConfirmDiscard())
		{
			Document.New();
		}
	}

	private void OnFileExit(object sender, ExecutedRoutedEventArgs e) => Close();

	/// <summary>Shows the Messages pane if hidden, brings it to the front of its group, and gives it keyboard focus.</summary>
	private void OnShowMessages(object sender, ExecutedRoutedEventArgs e)
	{
		dockHost.Show(messagesDock);
		messagesPane.FocusList();
	}

	/// <summary>
	/// The window takes files dropped on it (a project, a scheme, Jeroen's configuration file); anything else is refused
	/// where nothing inside took it (the tree takes project objects, the text boxes text), so the cursor shows what a drop
	/// does. Without this a dragged tree object showed as allowed over the pictures and was then ignored (2026-10-23).
	/// </summary>
	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private void OnDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && ConfirmDiscard())
		{
			OpenFile(files[0]);
		}
	}

	/// <summary>Re-reads the texts that are not DynamicResources: the dock pane titles and the theme menu's entries.</summary>
	private void ApplyLanguageTexts()
	{
		projectDock.Title = (string)FindResource("PaneProject");
		propertiesDock.Title = (string)FindResource("PaneProperties");
		messagesDock.Title = (string)FindResource("PaneMessages");
		themeMenu.Items.Refresh();
		// the title's "Untitled" is looked up by the converter; the status bar's texts take the language as an input
		BindingOperations.GetMultiBindingExpression(this, TitleProperty)?.UpdateTarget();
	}

	/// <summary>
	/// A plain F10 focuses the menu bar (its first header; Left/Right move along it, Down opens a menu,
	/// Escape leaves). WPF enters menu mode itself for a real F10 or Alt, which arrive as system keys and
	/// need the real keyboard state; a harness posting messages has neither, so this is its way into the
	/// menus (2026-10-14), and it does no harm for a person.
	/// </summary>
	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		base.OnPreviewKeyDown(e);
		if (!e.Handled && e.Key == Key.F10 && !mainMenu.IsKeyboardFocusWithin
			&& mainMenu.ItemContainerGenerator.ContainerFromIndex(0) is MenuItem first)
		{
			first.Focus();
			e.Handled = true;
		}
	}

	/// <summary>Escape on a focused menu header (not inside an open menu) returns the focus to the preview.</summary>
	private void OnMenuPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape && e.OriginalSource is MenuItem { Role: MenuItemRole.TopLevelHeader, IsSubmenuOpen: false })
		{
			previewPane.Focus();
			e.Handled = true;
		}
	}

	/// <inheritdoc/>
	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		// the manual is not owned by this window, and the application ends with its last window
		_manual?.Close();
		if (Settings.Default.WindowState == WindowMode.Minimized)
		{
			// never start minimized
			Settings.Default.WindowState = WindowMode.Normal;
		}
		Settings.Default.TrySave();
	}

	private bool TryLoadLayout()
	{
		DockLayout? layout = Settings.Default.Layout;
		if (layout == null)
		{
			Logger.Debug("No saved layout, using the default arrangement");
			return false;
		}
		bool applied = dockHost.LoadLayout(layout);
		Logger.Debug("Saved layout {Result}", applied ? "restored" : "rejected, using the default arrangement");
		return applied;
	}
}
