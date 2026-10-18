// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shell;
using Microsoft.Win32;
using NLog;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;
using WaveDudeNext.Rendering;
using WaveDudeNext.WPF.Dialogs;
using WaveDudeNext.WPF.Docking;
using WaveDudeNext.WPF.Localization;
using WaveDudeNext.WPF.Properties;
using WaveDudeNext.WPF.Theming;

namespace WaveDudeNext.WPF;

/// <summary>
/// Shell window: menu, status bar and the <see cref="DockHost"/> with the Project and
/// Properties panes around the preview. The window is its own data context: it owns the open
/// <see cref="Project"/>, its <see cref="Filename"/>, the tree's <see cref="SelectedItem"/> and the
/// current <see cref="Run"/> (see <c>MainWindow.Simulation.cs</c>). Window placement is bound two-way
/// to <see cref="Settings"/> in the XAML; the pane arrangement is stored there on closing.
/// </summary>
public partial class MainWindow : Window
{
	/// <summary>Extension of project files written by Save.</summary>
	public const string ProjectExtension = ".wdproj";
	private const int RecentFileCount = 10;

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Identifies the <see cref="Project"/> property.</summary>
	public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
		nameof(Project), typeof(Project), typeof(MainWindow), new PropertyMetadata(null, (d, e) => ((MainWindow)d).OnProjectReplaced((Project?)e.NewValue)));

	/// <summary>Identifies the <see cref="Filename"/> property.</summary>
	public static readonly DependencyProperty FilenameProperty = DependencyProperty.Register(
		nameof(Filename), typeof(string), typeof(MainWindow),
		new PropertyMetadata(null, (_, _) => CommandManager.InvalidateRequerySuggested()));

	/// <summary>Identifies the <see cref="IsDirty"/> property.</summary>
	public static readonly DependencyProperty IsDirtyProperty = DependencyProperty.Register(
		nameof(IsDirty), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

	/// <summary>Identifies the <see cref="SelectedItem"/> property.</summary>
	public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
		nameof(SelectedItem), typeof(object), typeof(MainWindow),
		new PropertyMetadata(null, (d, e) => ((MainWindow)d).OnSelectedItemChanged(e.NewValue)));

	public MainWindow()
	{
		Project = new Project();
		InitializeComponent();

		if (!TryLoadLayout() && !dockHost.LoadLayout(DefaultLayout()))
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
			else if (args.PropertyName == nameof(Settings.FrameMemoryPercent))
			{
				Logger.Info("RAM cache size: {Bound}, {Budget} right now (Simulation menu)",
					Settings.Default.FrameMemoryPercent <= FrameMemory.Automatic ? "automatic (what is free minus " + FrameMemory.Gigabytes(FrameMemory.Margin) + ")" : "at most " + Settings.Default.FrameMemoryPercent + " % of the RAM",
					FrameMemory.Gigabytes(FrameMemory.Budget(Settings.Default.FrameMemoryPercent)));
			}
			else if (args.PropertyName is nameof(Settings.PreviewResolution) or nameof(Settings.PreviewFrames) or nameof(Settings.PreviewSampleBytes))
			{
				Logger.Info("Preview quality: {Quality} (View menu)", PreviewQualitySetting);
				OnPreviewQualityChanged();
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
			ShutdownExport();
			ShutdownRun();
			ShutdownScratch();
		};
	}

	/// <summary>The open project. Never null: an empty project while nothing is loaded.</summary>
	public Project Project
	{
		get => (Project)GetValue(ProjectProperty);
		set => SetValue(ProjectProperty, value);
	}

	/// <summary>Full path the project was loaded from or saved to; null for an unsaved project.</summary>
	public string? Filename
	{
		get => (string?)GetValue(FilenameProperty);
		set => SetValue(FilenameProperty, value);
	}

	/// <summary>
	/// Whether the project was edited since it was opened, created or last saved (2026-10-14): the title
	/// carries an asterisk and New, Open, Close and Exit ask before discarding. Set by the watcher on any
	/// change, model-wide; cleared when the project is replaced or saved.
	/// </summary>
	public bool IsDirty
	{
		get => (bool)GetValue(IsDirtyProperty);
		set => SetValue(IsDirtyProperty, value);
	}

	/// <summary>The model object selected in the project tree (or on the preview's pictures), or null.</summary>
	public object? SelectedItem
	{
		get => GetValue(SelectedItemProperty);
		set => SetValue(SelectedItemProperty, value);
	}

	/// <summary>
	/// A selection made elsewhere (a marker or graph box on the preview) is mirrored in the tree, which then
	/// raises its own change with the same item. The tree node does not take the focus: the handle keeps it,
	/// so Tab can move on to the next handle (the tree stole it and broke the Tab cycle, 2026-10-14).
	/// </summary>
	private void OnSelectedItemChanged(object? item)
	{
		CommandManager.InvalidateRequerySuggested();
		if (item == null)
		{
			// deselected on the preview (a click on empty space): the tree lets go too
			projectTree.ClearSelection();
		}
		else if (!ReferenceEquals(projectTree.SelectedItem, item))
		{
			projectTree.Select(item, focus: false);
		}
	}

	/// <summary>
	/// Loads the project file at <paramref name="fileName"/> and makes it the open project.
	/// Failures are logged and shown to the user; the previous project stays open then.
	/// </summary>
	internal void OpenFile(string fileName)
	{
		if (string.Equals(Path.GetExtension(fileName), ColorSchemeStore.XmlExtension, StringComparison.OrdinalIgnoreCase))
		{
			ImportScheme(fileName);
			return;
		}
		if (LegacyProject.IsLegacyFile(fileName))
		{
			ImportLegacy(fileName);
			return;
		}
		try
		{
			fileName = Path.GetFullPath(fileName);
			Project loaded = Project.Load(fileName);
			DiscardRun();
			Project = loaded;
			Filename = fileName;
			_suggestedFileName = null;
			Logger.Info("Opened '{Path}'", fileName);
			RememberRecent(fileName);
			projectTree.SelectRoot();
			// the embedded image against its file: a changed file is taken (the project is then dirty) and watched from here on
			SyncSceneImage("changed since the project was saved");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error while opening '{Path}'", fileName);
			ShowError(string.Format((string)FindResource("OpenError"), fileName), ex);
		}
	}

	/// <summary>
	/// A project of the author's program (its <c>configuration.json</c>) becomes a new, unsaved project
	/// (2026-10-14): the scene image is embedded, what could not be carried over exactly is logged as a
	/// warning, and Save As proposes a <c>.wdproj</c> named after the project beside the original.
	/// </summary>
	private void ImportLegacy(string fileName)
	{
		try
		{
			var notes = new List<string>();
			Project imported = LegacyProject.Import(fileName, notes, out string projectName);
			DiscardRun();
			Project = imported;
			Filename = null;
			SyncSceneImage("imported");
			_suggestedFileName = LegacyProject.SuggestedFileName(fileName, projectName);
			foreach (string note in notes)
			{
				Logger.Warn(note);
			}
			Logger.Info("Imported the legacy project '{Name}'; save it as a WaveDude Next project (File > Save As)", projectName.Length > 0 ? projectName : Path.GetFileName(fileName));
			projectTree.SelectRoot();
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error while importing '{Path}'", fileName);
			ShowError(string.Format((string)FindResource("OpenError"), fileName), ex);
		}
	}

	/// <summary>The file name Save As proposes for an unsaved imported project; null otherwise.</summary>
	private string? _suggestedFileName;

	/// <summary>A scheme file (double-clicked, or given on the command line) is added to the open project's own schemes and selected.</summary>
	private void ImportScheme(string fileName)
	{
		try
		{
			ColorScheme scheme = ColorSchemeStore.LoadFile(fileName);
			scheme.FilePath = "";
			Project.AddColorScheme(scheme);
			projectTree.Select(scheme);
			Logger.Info("Colour scheme '{Name}' loaded from '{Path}' into the project", scheme.Name, fileName);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error while opening '{Path}'", fileName);
			ShowError(string.Format((string)FindResource("OpenError"), fileName), ex);
		}
	}

	/// <summary>Writes the open project to <paramref name="fileName"/>; false when that failed.</summary>
	private bool SaveFile(string fileName)
	{
		try
		{
			fileName = Path.GetFullPath(fileName);
			// an image file inside the project's folder is stored relative to it, so the project moves with its image
			Project.Scene.RelativizeImagePath(Path.GetDirectoryName(fileName));
			Project.Save(fileName);
			Filename = fileName;
			IsDirty = false;
			ArmSceneImageWatch();
			Logger.Info("Saved '{Path}'", fileName);
			RememberRecent(fileName);
			return true;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error while saving '{Path}'", fileName);
			ShowError(string.Format((string)FindResource("SaveError"), fileName), ex);
			return false;
		}
	}

	/// <summary>Puts the file at the top of the recent list and of the taskbar jump list.</summary>
	private static void RememberRecent(string fileName)
	{
		JumpList.AddToRecentCategory(fileName);

		// a fresh collection so the bound menu sees the change (StringCollection does not notify)
		var recent = new StringCollection { fileName };
		foreach (string? entry in Settings.Default.RecentFiles ?? [])
		{
			if (entry != null && !string.Equals(entry, fileName, StringComparison.OrdinalIgnoreCase) && recent.Count < RecentFileCount)
			{
				recent.Add(entry);
			}
		}
		Settings.Default.RecentFiles = recent;
		SaveSettings();
	}

	private void ShowError(string message, Exception ex) =>
		MessageWindow.Show(this, $"{message}\n{ex.Message}", MessageKind.Error, MessageButtons.Ok);

	/// <summary>
	/// Asks whether to save an edited project before it is discarded: Save saves (through Save As when it has
	/// no file yet), Don't save discards, Cancel keeps it. True when the caller may go on.
	/// </summary>
	private bool ConfirmDiscard()
	{
		if (!IsDirty)
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
			DefaultExt = ProjectExtension,
			AddExtension = true,
			FileName = _suggestedFileName ?? "",
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
		SaveSettings();
	}

	private void OnFileNew(object sender, ExecutedRoutedEventArgs e)
	{
		if (!ConfirmDiscard())
		{
			return;
		}
		DiscardRun();
		Project = new Project();
		Filename = null;
		_suggestedFileName = null;
		projectTree.SelectRoot();
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
			DefaultExt = ProjectExtension,
		};
		if (dialog.ShowDialog(this) == true)
		{
			OpenFile(dialog.FileName);
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
			DefaultExt = ProjectExtension,
			AddExtension = true,
			FileName = Filename ?? _suggestedFileName ?? "",
		};
		if (dialog.ShowDialog(this) == true)
		{
			SaveFile(dialog.FileName);
		}
	}

	/// <summary>The folder of the project file, or null for an unsaved project.</summary>
	private string? ProjectFolder => Filename != null ? Path.GetDirectoryName(Filename) : null;

	private void CanFileClose(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Filename != null;

	private void OnFileClose(object sender, ExecutedRoutedEventArgs e)
	{
		if (!ConfirmDiscard())
		{
			return;
		}
		DiscardRun();
		Project = new Project();
		Filename = null;
		_suggestedFileName = null;
		projectTree.SelectRoot();
	}

	private void OnFileExit(object sender, ExecutedRoutedEventArgs e) => Close();

	/// <summary>Shows the Messages pane if hidden, brings it to the front of its group, and gives it keyboard focus.</summary>
	private void OnShowMessages(object sender, ExecutedRoutedEventArgs e)
	{
		dockHost.Show(messagesDock);
		messagesPane.FocusList();
	}

	private void OnDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && ConfirmDiscard())
		{
			OpenFile(files[0]);
		}
	}

	private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => SelectedItem = e.NewValue;

	/// <summary>Re-reads the texts that are not DynamicResources: the dock pane titles and the theme menu's entries.</summary>
	private void ApplyLanguageTexts()
	{
		projectDock.Title = (string)FindResource("PaneProject");
		propertiesDock.Title = (string)FindResource("PaneProperties");
		messagesDock.Title = (string)FindResource("PaneMessages");
		themeMenu.Items.Refresh();
		// the title's "Untitled" is looked up by the converter; the busy text is built in code
		BindingOperations.GetMultiBindingExpression(this, TitleProperty)?.UpdateTarget();
		UpdateBusy();
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
		if (Settings.Default.MainWindow_State == WindowState.Minimized)
		{
			// never start minimized
			Settings.Default.MainWindow_State = WindowState.Normal;
		}
		SaveSettings();
	}

	private static void SaveSettings()
	{
		try
		{
			Settings.Default.Save();
			Logger.Debug("User settings saved");
		}
		catch (ConfigurationErrorsException ex)
		{
			// Losing window placement or the recent list is not worth interrupting the user.
			Logger.Warn(ex, "Could not save the user settings");
		}
	}

	/// <summary>
	/// The arrangement of a first start (agreed 2026-10-09): a left column of a quarter of the width with the
	/// Project tree in its upper third and the Properties pane below it, the preview in the rest; Messages hidden.
	/// </summary>
	private DockLayout DefaultLayout() => new()
	{
		Root = new DockLayout.SplitLayout
		{
			Orientation = Orientation.Horizontal,
			Children =
			{
				new DockLayout.SplitLayout
				{
					Orientation = Orientation.Vertical,
					Length = 1,
					Children =
					{
						new DockLayout.GroupLayout { Items = { projectDock.Id }, Active = projectDock.Id, Length = 1 },
						new DockLayout.GroupLayout { Items = { propertiesDock.Id }, Active = propertiesDock.Id, Length = 2 },
					},
				},
				new DockLayout.DocumentLayout { Length = 3 },
			},
		},
	};

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
