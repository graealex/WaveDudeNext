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

// Contributors: Vinicius Miguel

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Specialized;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NLog;
using NLog.Config;
using NLog.Targets;
using WaveDudeNext.Model;
using WaveDudeNext.Engine;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>The desktop application's entry point.</summary>
internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaveDudeNext");
		Directory.CreateDirectory(folder);
		var configuration = new LoggingConfiguration { DefaultCultureInfo = System.Globalization.CultureInfo.InvariantCulture };
		var file = new FileTarget("file")
		{
			FileName = Path.Combine(folder, "WaveDudeNext.log"),
			Layout = "${longdate} ${level:uppercase=true} ${message} ${exception:format=tostring}",
			ArchiveAboveSize = 20 * 1024 * 1024,
			MaxArchiveFiles = 1,
		};
		configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, file);
		LogManager.Configuration = configuration;
		AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
	}
}

/// <summary>The cross-platform desktop application.</summary>
internal sealed class App : Application
{
	/// <summary>Loads the initial control theme.</summary>
	public override void Initialize()
	{
		Styles.Add(new FluentTheme());
		string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToUpperInvariant();
		if (language is not ("DE" or "FR" or "NL")) language = "EN";
		using Stream stream = typeof(App).Assembly.GetManifestResourceStream($"Strings/Strings.{language}.xaml")
			?? throw new InvalidOperationException("The language resource is missing: " + language);
		XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
		foreach (XElement element in XDocument.Load(stream).Root!.Elements())
		{
			if (element.Attribute(xaml + "Key") is { } key && element.Name.LocalName == "String")
			{
				Resources[key.Value] = element.Value;
			}
		}
	}

	/// <summary>Creates the main window on the UI thread.</summary>
	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = new MainWindow();
		}
		base.OnFrameworkInitializationCompleted();
	}
}

/// <summary>The initial cross-platform window. Created and used on the UI thread.</summary>
internal sealed class MainWindow : Window
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	/// <summary>Creates the project menu and tree.</summary>
	public MainWindow()
	{
		Width = 1000;
		Height = 700;
		var state = new WindowStateModel(OpenProject, SaveProjectAs, ConfirmSave, (spec, budget, quality) => new MemoryWarningWindow(spec, budget, budget, quality).ShowDialog<MemoryWarningChoice?>(this));
		Opened += (_, _) => state.Initialise();
		bool closeApproved = false, confirmingClose = false;
		Closing += async (_, e) =>
		{
			if (closeApproved) return;
			e.Cancel = true;
			if (confirmingClose) return;
			confirmingClose = true;
			try
			{
				if (await state.ConfirmDiscard()) { closeApproved = true; Dispatcher.UIThread.Post(Close); }
			}
			finally { confirmingClose = false; }
		};
		Closed += (_, _) => state.Dispose();
		DataContext = state;
		this.Bind(TitleProperty, new Binding(nameof(WindowStateModel.Title)));
		var open = new MenuItem { Command = state.OpenCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.O, global::Avalonia.Input.KeyModifiers.Control) };
		open.Bind(MenuItem.HeaderProperty, open.GetResourceObservable("MenuFileOpen"));
		var save = new MenuItem { Command = state.SaveCommand, InputGesture = new KeyGesture(Key.S, KeyModifiers.Control) };
		save.Bind(MenuItem.HeaderProperty, save.GetResourceObservable("MenuFileSave"));
		var saveAs = new MenuItem { Command = state.SaveAsCommand, InputGesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift) };
		saveAs.Bind(MenuItem.HeaderProperty, saveAs.GetResourceObservable("MenuFileSaveAs"));
		var file = new MenuItem { ItemsSource = new[] { open, save, saveAs } };
		file.Bind(MenuItem.HeaderProperty, file.GetResourceObservable("MenuFile"));
		var run = new MenuItem { Command = state.RunCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.F5) };
		run.Bind(MenuItem.HeaderProperty, run.GetResourceObservable("MenuSimulationRun"));
		var pause = new MenuItem { Command = state.PauseRunCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.F6) };
		pause.Bind(MenuItem.HeaderProperty, pause.GetResourceObservable("MenuSimulationPause"));
		pause.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPauseRequested)) { Converter = BoolConverters.Not });
		var resume = new MenuItem { Command = state.PauseRunCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.F6) };
		resume.Bind(MenuItem.HeaderProperty, resume.GetResourceObservable("MenuSimulationResume"));
		resume.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPauseRequested)));
		var stop = new MenuItem { Command = state.StopRunCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.F5, global::Avalonia.Input.KeyModifiers.Shift) };
		stop.Bind(MenuItem.HeaderProperty, stop.GetResourceObservable("MenuSimulationStop"));
		var play = new MenuItem { Command = state.PlayPauseCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.F7) };
		play.Bind(MenuItem.HeaderProperty, play.GetResourceObservable("MenuSimulationPlay"));
		play.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPlaying)) { Converter = BoolConverters.Not });
		var pausePlayback = new MenuItem { Command = state.PlayPauseCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.F7) };
		pausePlayback.Bind(MenuItem.HeaderProperty, pausePlayback.GetResourceObservable("MenuSimulationPausePlayback"));
		pausePlayback.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPlaying)));
		var simulation = new MenuItem { ItemsSource = new[] { run, pause, resume, stop, play, pausePlayback } };
		simulation.Bind(MenuItem.HeaderProperty, simulation.GetResourceObservable("MenuSimulation"));
		MenuItem ActionItem(string key, ICommand command, object? parameter = null)
		{
			var item = new MenuItem { Command = command, CommandParameter = parameter };
			item.Bind(MenuItem.HeaderProperty, item.GetResourceObservable(key));
			return item;
		}
		var parts = new MenuItem { ItemsSource = new[]
		{
			ActionItem("MenuAddLens", state.AddPartCommand, typeof(LensPart)),
			ActionItem("MenuAddPolygon", state.AddPartCommand, typeof(PolygonPart)),
			ActionItem("MenuAddRectangle", state.AddPartCommand, typeof(RectanglePart)),
			ActionItem("MenuAddEllipse", state.AddPartCommand, typeof(EllipsePart)),
			ActionItem("MenuAddTriangle", state.AddPartCommand, typeof(TrianglePart)),
			ActionItem("MenuAddOutline", state.AddPartCommand, typeof(OutlinePart)),
			ActionItem("MenuAddFiber", state.AddPartCommand, typeof(FiberPart)),
			ActionItem("MenuAddBraggReflector", state.AddPartCommand, typeof(BraggReflectorPart)),
			ActionItem("MenuAddGrinLens", state.AddPartCommand, typeof(GrinLensPart)),
			ActionItem("MenuAddZonePlate", state.AddPartCommand, typeof(ZonePlatePart)),
			ActionItem("MenuAddXorGate", state.AddPartCommand, typeof(XorGatePart)),
			ActionItem("MenuAddAndGate", state.AddPartCommand, typeof(AndGatePart)),
			ActionItem("MenuAddOrGate", state.AddPartCommand, typeof(OrGatePart)),
		} };
		parts.Bind(MenuItem.HeaderProperty, parts.GetResourceObservable("MenuProjectAddPart"));
		var edit = new MenuItem { ItemsSource = new[] { ActionItem("MenuProjectAddSource", state.AddSourceCommand), ActionItem("MenuProjectAddProbe", state.AddProbeCommand), parts, ActionItem("MenuProjectRemove", state.RemoveCommand) } };
		edit.Bind(MenuItem.HeaderProperty, edit.GetResourceObservable("MenuEdit"));
		var menu = new Menu { ItemsSource = new[] { file, edit, simulation } };
		var tree = new TreeView { Width = 250, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
		tree.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WindowStateModel.Nodes)));
		tree.Bind(TreeView.SelectedItemProperty, new Binding(nameof(WindowStateModel.SelectedNode)) { Mode = BindingMode.TwoWay });
		tree.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Delete), Command = state.RemoveCommand });
		var properties = new ItemsControl();
		properties.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WindowStateModel.PropertyRows)));
		var propertyTitle = new TextBlock();
		propertyTitle.Bind(TextBlock.TextProperty, propertyTitle.GetResourceObservable("PaneProperties"));
		var propertyPane = new DockPanel { Width = 260 };
		DockPanel.SetDock(propertyTitle, Dock.Top);
		propertyPane.Children.Add(propertyTitle);
		propertyPane.Children.Add(new ScrollViewer { Content = properties });
		var status = new TextBlock();
		status.Bind(TextBlock.TextProperty, new Binding(nameof(WindowStateModel.Status)));
		var timeline = new Slider { Minimum = 0, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10 };
		timeline.Bind(Slider.MaximumProperty, new Binding(nameof(WindowStateModel.LastFrame)));
		timeline.Bind(Slider.ValueProperty, new Binding(nameof(WindowStateModel.Frame)) { Mode = BindingMode.TwoWay });
		var position = new TextBlock();
		position.Bind(TextBlock.TextProperty, new Binding(nameof(WindowStateModel.Position)));
		var transport = new DockPanel();
		static AccessText Caption(Control owner, string key)
		{
			var text = new AccessText();
			text.Bind(AccessText.TextProperty, owner.GetResourceObservable(key));
			return text;
		}
		var runButton = new Button { Command = state.RunCommand };
		runButton.Content = Caption(runButton, "DialogRun");
		AutomationProperties.SetAutomationId(runButton, "PreviewRun");
		var pauseButton = new Button { Command = state.PauseRunCommand };
		pauseButton.Content = Caption(pauseButton, "MenuSimulationPause");
		AutomationProperties.SetAutomationId(pauseButton, "PreviewPauseRun");
		pauseButton.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPauseRequested)) { Converter = BoolConverters.Not });
		var resumeButton = new Button { Command = state.PauseRunCommand };
		resumeButton.Content = Caption(resumeButton, "MenuSimulationResume");
		AutomationProperties.SetAutomationId(resumeButton, "PreviewResumeRun");
		resumeButton.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPauseRequested)));
		var playButton = new Button { Command = state.PlayPauseCommand };
		playButton.Content = Caption(playButton, "MenuSimulationPlay");
		AutomationProperties.SetAutomationId(playButton, "PreviewPlay");
		playButton.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPlaying)) { Converter = BoolConverters.Not });
		var pausePlaybackButton = new Button { Command = state.PlayPauseCommand };
		pausePlaybackButton.Content = Caption(pausePlaybackButton, "MenuSimulationPausePlayback");
		AutomationProperties.SetAutomationId(pausePlaybackButton, "PreviewPause");
		pausePlaybackButton.Bind(IsVisibleProperty, new Binding(nameof(WindowStateModel.IsPlaying)));
		Button TransportButton(string text, string tooltip, string id, ICommand command)
		{
			var button = new Button { Content = text, Command = command };
			button.Bind(ToolTip.TipProperty, button.GetResourceObservable(tooltip));
			button.Bind(AutomationProperties.NameProperty, button.GetResourceObservable(tooltip));
			AutomationProperties.SetAutomationId(button, id);
			DockPanel.SetDock(button, Dock.Left);
			return button;
		}
		var back = TransportButton("←", "TipPreviewStepBack", "PreviewStepBack", state.StepBackCommand);
		var forward = TransportButton("→", "TipPreviewStepForward", "PreviewStepForward", state.StepForwardCommand);
		var setIn = TransportButton("I", "TipPreviewSetIn", "PreviewSetIn", state.SetInCommand);
		var setOut = TransportButton("O", "TipPreviewSetOut", "PreviewSetOut", state.SetOutCommand);
		var clearRange = TransportButton("X", "TipPreviewClearRange", "PreviewClearRange", state.ClearRangeCommand);
		var live = TransportButton("F", "TipPreviewLive", "PreviewLive", state.LiveCommand);
		var loop = new CheckBox();
		loop.Bind(ContentControl.ContentProperty, loop.GetResourceObservable("TipPreviewLoop"));
		loop.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(WindowStateModel.IsLooping)) { Mode = BindingMode.TwoWay });
		AutomationProperties.SetAutomationId(loop, "PreviewLoop");
		var speed = new NumericUpDown { Minimum = 0.01m, Maximum = 100m, Increment = 0.25m, Width = 80 };
		speed.Bind(NumericUpDown.ValueProperty, new Binding(nameof(WindowStateModel.PlaybackSpeed)) { Mode = BindingMode.TwoWay });
		AutomationProperties.SetAutomationId(speed, "PreviewSpeed");
		var speedLabel = new TextBlock();
		speedLabel.Bind(TextBlock.TextProperty, speedLabel.GetResourceObservable("LabelPreviewSpeed"));
		DockPanel.SetDock(runButton, Dock.Left);
		DockPanel.SetDock(pauseButton, Dock.Left);
		DockPanel.SetDock(resumeButton, Dock.Left);
		DockPanel.SetDock(playButton, Dock.Left);
		DockPanel.SetDock(pausePlaybackButton, Dock.Left);
		DockPanel.SetDock(loop, Dock.Left);
		DockPanel.SetDock(speedLabel, Dock.Right);
		DockPanel.SetDock(speed, Dock.Right);
		transport.Children.Add(runButton);
		transport.Children.Add(pauseButton);
		transport.Children.Add(resumeButton);
		transport.Children.Add(playButton);
		transport.Children.Add(pausePlaybackButton);
		transport.Children.Add(back);
		transport.Children.Add(forward);
		transport.Children.Add(setIn);
		transport.Children.Add(setOut);
		transport.Children.Add(clearRange);
		transport.Children.Add(loop);
		transport.Children.Add(live);
		transport.Children.Add(speed);
		transport.Children.Add(speedLabel);
		DockPanel.SetDock(position, Dock.Right);
		transport.Children.Add(position);
		transport.Children.Add(timeline);
		var field = new Image();
		field.Bind(Image.SourceProperty, new Binding(nameof(WindowStateModel.FieldImage)));
		var intensity = new Image();
		intensity.Bind(Image.SourceProperty, new Binding(nameof(WindowStateModel.IntensityImage)));
		var pictures = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Focusable = true };
		Grid.SetColumn(intensity, 1);
		pictures.Children.Add(field);
		pictures.Children.Add(intensity);
		DockPanel.SetDock(tree, Dock.Left);
		DockPanel.SetDock(propertyPane, Dock.Right);
		DockPanel.SetDock(menu, Dock.Top);
		DockPanel.SetDock(status, Dock.Bottom);
		DockPanel.SetDock(transport, Dock.Bottom);
		var preview = new PreviewPanel(state) { Children = { transport, pictures } };
		Content = new DockPanel { Children = { menu, status, tree, propertyPane, preview } };
	}

	private async Task<(Project Project, string Name, string? Folder, string? Path)?> OpenProject()
	{
		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open project",
			AllowMultiple = false,
			FileTypeFilter =
			[
				new FilePickerFileType("WaveDude Next projects") { Patterns = ["*.wdproj"] },
				new FilePickerFileType("Wave Dude 2D projects") { Patterns = ["*.json"] },
			],
		});
		if (files.Count == 0)
		{
			return null;
		}
		try
		{
			string? path = files[0].TryGetLocalPath();
			if (LegacyProject.IsLegacyFile(files[0].Name))
			{
				if (path == null)
				{
					throw new IOException("A legacy project must be opened from a local folder so its scene image can be found.");
				}
				return ImportLegacy(path);
			}
			await using Stream stream = await files[0].OpenReadAsync();
			return (Project.Load(stream), files[0].Name, path == null ? null : Path.GetDirectoryName(path), path);
		}
		catch (Exception ex)
		{
			string? path = files[0].TryGetLocalPath();
			Logger.Error(path == null ? ex : new UnreadableFileException(path, ex), "Error while opening '{Path}'", path ?? files[0].Name);
			throw;
		}
	}

	private async Task<string?> SaveProjectAs(string? name)
	{
		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			SuggestedFileName = name,
			DefaultExtension = "wdproj",
			FileTypeChoices = [new FilePickerFileType("WaveDude Next projects") { Patterns = ["*.wdproj"] }],
		});
		if (file == null) return null;
		return file.TryGetLocalPath() ?? throw new IOException("The project must be saved to a local folder.");
	}

	private Task<SaveDecision> ConfirmSave(string name)
	{
		var dialog = new Window { Width = 440, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
		dialog.Bind(TitleProperty, dialog.GetResourceObservable("Title"));
		var message = new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, (string)Application.Current!.Resources["SaveChangesFormat"]!, name), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
		var buttons = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8 };
		void AddButton(string key, SaveDecision decision, bool isCancel = false)
		{
			var caption = new AccessText();
			caption.Bind(AccessText.TextProperty, caption.GetResourceObservable(key));
			buttons.Children.Add(new Button { Content = caption, IsCancel = isCancel, Command = new MemoryWarningCommand(() => dialog.Close(decision)) });
		}
		AddButton("DialogSave", SaveDecision.Save);
		AddButton("DialogDiscard", SaveDecision.Discard);
		AddButton("DialogCancel", SaveDecision.Cancel, true);
		dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { message, buttons } };
		return dialog.ShowDialog<SaveDecision>(this);
	}

	private static (Project Project, string Name, string? Folder, string? Path) ImportLegacy(string path)
	{
		var notes = new List<string>();
		Project imported = LegacyProject.Import(path, notes, out string name);
		foreach (string note in notes)
		{
			Logger.Warn("{Note}", note);
		}
		return (imported, name.Length > 0 ? name : Path.GetFileName(path), Path.GetDirectoryName(path), null);
	}
}

/// <summary>Bound project state and commands, used on the UI thread.</summary>
internal sealed class WindowStateModel : INotifyPropertyChanged, IDisposable
{
	private readonly Func<Task<(Project Project, string Name, string? Folder, string? Path)?>> _open;
	private readonly Func<string?, Task<string?>> _saveAs;
	private readonly Func<string, Task<SaveDecision>> _confirmSave;
	private readonly Func<RunSpec, long, PreviewQuality, Task<MemoryWarningChoice?>> _warn;
	private string _title = "WaveDude Next";
	private string _status = "";
	private IReadOnlyList<TreeViewItem> _nodes = [];
	private Project _project = new();
	private ScratchRunner? _scratch;
	private RunSpec? _requested;
	private WriteableBitmap? _fieldImage;
	private WriteableBitmap? _intensityImage;
	private bool _disposed;
	private double _frame;
	private int _lastFrame;
	private int _canvasWidth;
	private int _canvasHeight;
	private SimulationRun? _run;
	private string? _folder;
	private PreviewQuality _quality = PreviewQuality.Full;
	private bool _warnMemory = true;
	private bool _following;
	private readonly ControlCommand _pauseRun;
	private readonly ControlCommand _stopRun;
	private PresentationJob? _pendingPresentation;
	private bool _presenting;
	private long _presentationVersion;
	private TreeViewItem? _selectedNode;
	private IReadOnlyList<Control> _propertyRows = [];
	private readonly Stopwatch _playClock = new();
	private Timer? _playTimer;
	private double _playCarry;
	private bool _isPlaying;
	private int _tickPending;
	private int _playGeneration;
	private bool _isLooping;
	private decimal _playbackSpeed = 1m;
	private ProjectWatcher? _watcher;
	private DispatcherTimer? _scratchDelay;
	private DispatcherTimer? _rerunDelay;
	private bool _runStale;
	private bool _scratchWaiting;
	private int _editGeneration;
	private ProjectChange? _lastEdit;
	private string? _path;
	private string _projectName = "Untitled";
	private bool _dirty;
	private bool _saving;

	/// <summary>Creates state for a new project.</summary>
	public WindowStateModel(Func<Task<(Project Project, string Name, string? Folder, string? Path)?>> open, Func<string?, Task<string?>> saveAs, Func<string, Task<SaveDecision>> confirmSave, Func<RunSpec, long, PreviewQuality, Task<MemoryWarningChoice?>> warn)
	{
		_open = open;
		_saveAs = saveAs;
		_confirmSave = confirmSave;
		_warn = warn;
		OpenCommand = new OpenCommand(Open);
		SaveCommand = new OpenCommand(() => Save(false));
		SaveAsCommand = new OpenCommand(() => Save(true));
		RunCommand = new OpenCommand(() => StartRun());
		PlayPauseCommand = new OpenCommand(TogglePlayback);
		_pauseRun = new ControlCommand(ToggleRunPause, () => _run?.IsActive == true);
		_stopRun = new ControlCommand(() => _run?.Stop(), () => _run?.IsActive == true);
		StepBackCommand = new ControlCommand(() => Frame--, () => _requested != null);
		StepForwardCommand = new ControlCommand(() => Frame++, () => _requested != null);
		SetInCommand = new ControlCommand(SetInPoint, () => _requested != null && LastFrame > 0);
		SetOutCommand = new ControlCommand(SetOutPoint, () => _requested != null && LastFrame > 0);
		ClearRangeCommand = new ControlCommand(ClearRange, () => _requested != null && (Project.Display.ExportFrom != 0 || Project.Display.ExportTo != 0));
		LiveCommand = new ControlCommand(ToggleLive, () => _run?.IsActive == true);
		AddSourceCommand = new ControlCommand(() => { (int x, int y) = CanvasCentre(); SelectItem(Project.AddSource(x, y)); }, () => true);
		AddProbeCommand = new ControlCommand(() => { (int x, int y) = CanvasCentre(); SelectItem(Project.AddProbe(x, y)); }, () => true);
		AddPartCommand = new ParameterCommand(value => { if (value is Type type) { (int x, int y) = CanvasCentre(); SelectItem(Project.AddPart(type, x, y)); } });
		RemoveCommand = new ControlCommand(RemoveSelected, () => SelectedNode?.Tag is Source or OpticalPart or Probe or ColorScheme);
		SetProject(_project);
	}

	/// <summary>Raised when a bound property changes.</summary>
	public event PropertyChangedEventHandler? PropertyChanged;
	/// <summary>The window title.</summary>
	public string Title { get => _title; private set { _title = value; Changed(); } }
	/// <summary>The current status message.</summary>
	public string Status { get => _status; private set { _status = value; Changed(); } }
	/// <summary>The displayed project nodes.</summary>
	public IReadOnlyList<TreeViewItem> Nodes { get => _nodes; private set { _nodes = value; Changed(); } }
	/// <summary>The selected tree row.</summary>
	public TreeViewItem? SelectedNode
	{
		get => _selectedNode;
		set
		{
			if (ReferenceEquals(_selectedNode, value)) return;
			_selectedNode = value;
			Changed();
			PropertyRows = BuildPropertyRows(value?.Tag);
			((ControlCommand)RemoveCommand).Refresh();
		}
	}
	/// <summary>The selected object's visible scalar properties.</summary>
	public IReadOnlyList<Control> PropertyRows { get => _propertyRows; private set { _propertyRows = value; Changed(); } }
	/// <summary>The loaded project, retained for editing and simulation.</summary>
	public Project Project { get => _project; private set { _project = value; Changed(); } }
	/// <summary>Opens an existing project.</summary>
	public ICommand OpenCommand { get; }
	/// <summary>Saves the current project.</summary>
	public ICommand SaveCommand { get; }
	/// <summary>Chooses a new path and saves the current project.</summary>
	public ICommand SaveAsCommand { get; }
	/// <summary>Starts a simulation run.</summary>
	public ICommand RunCommand { get; }
	/// <summary>Starts or pauses playback of the timeline.</summary>
	public ICommand PlayPauseCommand { get; }
	/// <summary>Moves one timeline frame backwards.</summary>
	public ICommand StepBackCommand { get; }
	/// <summary>Moves one timeline frame forwards.</summary>
	public ICommand StepForwardCommand { get; }
	/// <summary>Sets the start of the export range to the current frame.</summary>
	public ICommand SetInCommand { get; }
	/// <summary>Sets the end of the export range to the current frame.</summary>
	public ICommand SetOutCommand { get; }
	/// <summary>Clears the export range.</summary>
	public ICommand ClearRangeCommand { get; }
	/// <summary>Starts or stops following the newest simulation frame.</summary>
	public ICommand LiveCommand { get; }
	/// <summary>Adds a source at the centre of the canvas.</summary>
	public ICommand AddSourceCommand { get; }
	/// <summary>Adds a probe at the centre of the canvas.</summary>
	public ICommand AddProbeCommand { get; }
	/// <summary>Adds an optical part of the supplied type.</summary>
	public ICommand AddPartCommand { get; }
	/// <summary>Removes the selected project item.</summary>
	public ICommand RemoveCommand { get; }
	/// <summary>Whether the timeline is playing.</summary>
	public bool IsPlaying { get => _isPlaying; private set { _isPlaying = value; Changed(); } }
	/// <summary>Whether playback wraps over the export range.</summary>
	public bool IsLooping { get => _isLooping; set { if (_isLooping != value) { _isLooping = value; Changed(); } } }
	/// <summary>The playback speed factor, from 0.01 to 100.</summary>
	public decimal PlaybackSpeed { get => _playbackSpeed; set { decimal speed = Math.Clamp(value, 0.01m, 100m); if (_playbackSpeed != speed) { _playbackSpeed = speed; Changed(); } } }
	/// <summary>Pauses or resumes an active simulation.</summary>
	public ICommand PauseRunCommand => _pauseRun;
	/// <summary>Stops an active simulation.</summary>
	public ICommand StopRunCommand => _stopRun;
	/// <summary>Whether the simulation has been asked to pause.</summary>
	public bool IsPauseRequested => _run?.IsPauseRequested == true;
	/// <summary>The latest field picture.</summary>
	public WriteableBitmap? FieldImage { get => _fieldImage; private set { _fieldImage = value; Changed(); } }
	/// <summary>The latest intensity picture.</summary>
	public WriteableBitmap? IntensityImage { get => _intensityImage; private set { _intensityImage = value; Changed(); } }
	/// <summary>The last frame on the project's timeline.</summary>
	public int LastFrame { get => _lastFrame; private set { _lastFrame = value; Changed(); } }
	/// <summary>The requested timeline frame.</summary>
	public double Frame
	{
		get => _frame;
		set
		{
			if (_frame == Math.Clamp(Math.Round(value), 0, LastFrame)) return;
			_following = false;
			SetFrame(value);
		}
	}
	/// <summary>The frame and its corresponding simulation step.</summary>
	public string Position => $"{(int)Frame} / {LastFrame}   |   {(int)Frame * (_requested?.VisualizeEvery ?? 1)}";

	/// <summary>Prepares the initial project's preview once the window is open.</summary>
	public void Initialise()
	{
		try { RequestPreview(null); }
		catch (Exception ex) { Status = ex.Message; }
	}

	/// <summary>Stops the scratch worker and releases the pictures.</summary>
	public void Dispose()
	{
		_disposed = true;
		_editGeneration++;
		_scratchDelay?.Stop();
		_rerunDelay?.Stop();
		_watcher?.Dispose();
		StopPlayback();
		_playTimer?.Dispose();
		InvalidatePresentation();
		DiscardRun();
		_scratch?.Dispose();
		_fieldImage?.Dispose();
		_intensityImage?.Dispose();
	}

	private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	private void SetFrame(double value)
	{
		double frame = Math.Clamp(Math.Round(value), 0, LastFrame);
		if (_frame != frame)
		{
			InvalidatePresentation();
			_frame = frame;
			Changed(nameof(Frame));
			Changed(nameof(Position));
		}
		if (_run is { } run && !_runStale)
		{
			if ((int)frame < run.FrameCount) Present(run.Frames, (int)frame);
		}
		else if (_requested != null) _scratch?.Request(_requested, (int)frame);
	}

	private async Task StartRun(bool automatic = false)
	{
		if (_requested == null) return;
		try
		{
			RunSpec original = _requested;
			RunSpec spec = original;
			long budget = FrameMemory.Budget(FrameMemory.Automatic, _run?.Store.RamBytes ?? 0);
			int ramFrames = Math.Min(spec.StoredFrameCount, FrameStore.FramesInBudget(spec.Shape, spec.Layers, budget));
			if (ramFrames < spec.StoredFrameCount && _warnMemory)
			{
				MemoryWarningChoice? choice = await _warn(spec, budget, _quality);
				if (choice == null || _disposed || !ReferenceEquals(original, _requested)) return;
				_quality = choice.Quality;
				_warnMemory = !choice.Mute;
				spec = RunSpec.Create(Project, SceneComposer.Compose(Project, _folder), preferGpu: true, trackIntensity: true, quality: _quality, preferVulkan: true);
				ramFrames = Math.Min(spec.StoredFrameCount, FrameStore.FramesInBudget(spec.Shape, spec.Layers, budget));
			}
			if (ramFrames < spec.StoredFrameCount)
				FrameCache.CheckSpace(FrameStore.SizeOf(spec.Shape, spec.Layers, spec.StoredFrameCount - ramFrames));
			Task memoryReleased = DiscardRun();
			ScratchRunner? scratch = _scratch;
			_scratch = null;
			if (scratch != null) await Task.Run(scratch.Dispose);
			if (_disposed || !ReferenceEquals(original, _requested)) return;
			_requested = spec;
			var store = FrameStore.Create(FrameCache.NewPath(), spec.Shape, spec.Layers, spec.StoredFrameCount, spec.CaptureEvery, budget, memoryReleased);
			var run = new SimulationRun(spec, store, SynchronizationContext.Current ?? throw new InvalidOperationException("No UI synchronisation context is available."), automatic);
			_run = run;
			_runStale = false;
			_scratchWaiting = false;
			_rerunDelay?.Stop();
			_following = !automatic;
			run.PropertyChanged += RunChanged;
			run.Start();
			UpdateRunControls();
		}
		catch (Exception ex)
		{
			Status = ex.Message;
		}
	}

	private async Task TogglePlayback()
	{
		if (IsPlaying) { StopPlayback(); return; }
		if (_requested == null || LastFrame == 0) return;
		int generation = ++_playGeneration;
		if (_run == null || _run.State is RunState.Cancelled or RunState.Failed)
		{
			SimulationRun? previous = _run;
			await StartRun();
			if (ReferenceEquals(previous, _run)) return;
		}
		if (_disposed || generation != _playGeneration || _run == null) return;
		_following = false;
		int divisor = _requested?.VisualizeEvery ?? 1;
		int start = Math.Clamp(Project.Display.ExportFrom / divisor, 0, LastFrame);
		int end = Project.Display.ExportTo == 0 ? LastFrame : Math.Clamp(Project.Display.ExportTo / divisor, start, LastFrame);
		if (Frame >= end) SetFrame(start);
		IsPlaying = true;
		_playCarry = 0;
		_playClock.Restart();
		_playTimer ??= new Timer(_ =>
		{
			if (Interlocked.Exchange(ref _tickPending, 1) == 0)
				Dispatcher.UIThread.Post(() => { Volatile.Write(ref _tickPending, 0); PlaybackTick(); }, DispatcherPriority.Render);
		});
		_playTimer.Change(0, 16);
	}

	private void StopPlayback()
	{
		_playGeneration++;
		_playTimer?.Change(Timeout.Infinite, Timeout.Infinite);
		_playClock.Stop();
		if (IsPlaying) IsPlaying = false;
	}

	private void PlaybackTick()
	{
		if (!IsPlaying || _run == null) return;
		_playCarry += _playClock.Elapsed.TotalSeconds * Project.Display.Video.Fps * (double)PlaybackSpeed;
		_playClock.Restart();
		int advance = (int)Math.Floor(_playCarry);
		if (advance == 0 || _presenting) return;
		_playCarry -= advance;
		int divisor = _requested?.VisualizeEvery ?? 1;
		int start = Math.Clamp(Project.Display.ExportFrom / divisor, 0, LastFrame);
		int end = Project.Display.ExportTo == 0 ? LastFrame : Math.Clamp(Project.Display.ExportTo / divisor, start, LastFrame);
		int available = Math.Min(end, _run.FrameCount - 1);
		if (available < 0) { _playCarry = 0; return; }
		int next = (int)Math.Min((long)Frame + advance, int.MaxValue);
		if (next < available) SetFrame(next);
		else if (_run.IsActive && available < end)
		{
			SetFrame(Math.Max(Frame, available));
			_playCarry = 0;
		}
		else if (IsLooping && available > start)
			SetFrame(start + (next - available) % (available - start));
		else { SetFrame(available); StopPlayback(); }
	}

	private void RunChanged(object? sender, PropertyChangedEventArgs args)
	{
		if (_disposed || sender is not SimulationRun run || !ReferenceEquals(run, _run)) return;
		if (args.PropertyName == nameof(SimulationRun.FrameCount) && _following && !_runStale && run.FrameCount > 0)
			SetFrame(run.FrameCount - 1);
		if (args.PropertyName == nameof(SimulationRun.State))
		{
			Status = run.Error?.Message ?? run.State.ToString();
			UpdateRunControls();
		}
	}

	private void ToggleRunPause()
	{
		_run?.TogglePause();
		UpdateRunControls();
	}

	private void UpdateRunControls()
	{
		Changed(nameof(IsPauseRequested));
		_pauseRun.Refresh();
		_stopRun.Refresh();
		((ControlCommand)LiveCommand).Refresh();
	}

	private void SetInPoint()
	{
		int step = (int)Frame * (_requested?.VisualizeEvery ?? 1);
		if (Project.Display.ExportTo != 0 && Project.Display.ExportTo <= step) Project.Display.ExportTo = 0;
		Project.Display.ExportFrom = step;
		((ControlCommand)ClearRangeCommand).Refresh();
	}

	private void SetOutPoint()
	{
		int step = Frame >= LastFrame ? 0 : (int)Frame * (_requested?.VisualizeEvery ?? 1);
		if (step != 0 && Project.Display.ExportFrom >= step) Project.Display.ExportFrom = 0;
		Project.Display.ExportTo = step;
		((ControlCommand)ClearRangeCommand).Refresh();
	}

	private void ClearRange()
	{
		Project.Display.ExportFrom = 0;
		Project.Display.ExportTo = 0;
		((ControlCommand)ClearRangeCommand).Refresh();
	}

	private void ToggleLive()
	{
		_following = !_following;
		if (_following && _run is { } run)
		{
			StopPlayback();
			if (run.FrameCount > 0) SetFrame(run.FrameCount - 1);
		}
	}

	private Task DiscardRun()
	{
		InvalidatePresentation();
		SimulationRun? old = _run;
		_run = null;
		UpdateRunControls();
		if (old == null) return Task.CompletedTask;
		old.PropertyChanged -= RunChanged;
		old.Stop();
		return old.Completion.ContinueWith(_ => { old.Store.Dispose(); old.Dispose(); }, TaskScheduler.Default);
	}

	private async Task Open()
	{
		try
		{
			if (!await ConfirmDiscard()) return;
			if (await _open() is { } result)
			{
				_editGeneration++;
				_scratchDelay?.Stop();
				_rerunDelay?.Stop();
				_runStale = false;
				_scratchWaiting = false;
				StopPlayback();
				_ = DiscardRun();
				_requested = null;
				_folder = result.Folder;
				_path = result.Path;
				SetProject(result.Project);
				_projectName = result.Name;
				_dirty = false;
				UpdateTitle();
				Status = "";
				RequestPreview(_folder);
			}
		}
		catch (Exception ex)
		{
			Status = ex.Message;
		}
	}

	/// <summary>Asks to save or discard unsaved edits before replacing or closing the project.</summary>
	public async Task<bool> ConfirmDiscard()
	{
		if (!_dirty) return true;
		SaveDecision decision = await _confirmSave(_projectName);
		if (decision == SaveDecision.Discard) return true;
		if (decision != SaveDecision.Save) return false;
		await Save(false);
		return !_dirty;
	}

	private async Task Save(bool choosePath)
	{
		try
		{
			string? path = choosePath || _path == null ? await _saveAs(_path == null ? _projectName : Path.GetFileName(_path)) : _path;
			if (path == null || _disposed) return;
			path = Path.GetFullPath(path);
			string? folder = Path.GetDirectoryName(path);
			string oldImagePath = Project.Scene.ImagePath;
			string imagePath = Project.Scene.ResolveImagePath(_folder);
			_saving = true;
			try
			{
				if (imagePath.Length > 0) Project.Scene.ImagePath = Scene.StoredImagePath(folder, imagePath);
				Project.Save(path);
			}
			catch
			{
				Project.Scene.ImagePath = oldImagePath;
				throw;
			}
			finally { _saving = false; }
			_path = path;
			_folder = folder;
			_projectName = Path.GetFileName(path);
			_dirty = false;
			UpdateTitle();
		}
		catch (Exception ex)
		{
			LogManager.GetCurrentClassLogger().Error(ex, "Error while saving project");
			Status = ex.Message;
		}
	}

	private void UpdateTitle() => Title = _projectName + (_dirty ? "*" : "") + " - WaveDude Next";


	private void RequestPreview(string? folder, bool resetPosition = true)
	{
		ScenePixels? scene = SceneComposer.Compose(Project, folder);
		RunSpec spec = RunSpec.Create(Project, scene, preferGpu: true, trackIntensity: true, quality: _quality, preferVulkan: true);
		_requested = spec;
		_canvasWidth = spec.Width;
		_canvasHeight = spec.Height;
		LastFrame = Math.Max(0, spec.FrameCount - 1);
		((ControlCommand)StepBackCommand).Refresh();
		((ControlCommand)StepForwardCommand).Refresh();
		((ControlCommand)SetInCommand).Refresh();
		((ControlCommand)SetOutCommand).Refresh();
		((ControlCommand)ClearRangeCommand).Refresh();
		_frame = resetPosition ? 0 : Math.Clamp(_frame, 0, LastFrame);
		Changed(nameof(Frame));
		Changed(nameof(Position));
		if (_scratch == null)
		{
			_scratch = new ScratchRunner(SynchronizationContext.Current ?? throw new InvalidOperationException("No UI synchronisation context is available."));
			_scratch.Completed += ScratchCompleted;
		}
		_scratchWaiting = true;
		_scratch.Request(spec, (int)_frame);
	}

	private void ScratchCompleted(object? sender, ScratchResult result)
	{
		if (_disposed || (_run != null && !_runStale) || !ReferenceEquals(result.Spec, _requested) || result.Frame != (int)Frame)
		{
			return;
		}
		_scratchWaiting = false;
		Present(new CopiedFrameSource(result.Snapshot), 0);
	}

	private void Present(IFrameSource source, int frame)
	{
		var schemes = Project.ColorSchemes.Concat(ColorSchemeStore.LoadBuiltIn(null)).ToArray();
		ColorLut Lut(DisplayLayer layer)
		{
			ColorScheme? scheme = ColorSchemeStore.Find(schemes, layer.ColorScheme);
			return scheme == null ? ColorLut.Grey : ColorLut.Build(scheme, layer.BlackLevel);
		}
		_pendingPresentation = new PresentationJob(++_presentationVersion, source, frame,
			Lut(Project.Display.Field), Lut(Project.Display.Intensity),
			(float)Project.Display.Field.Brightness, (float)Project.Display.Intensity.Brightness);
		if (!_presenting) DrainPresentation();
	}

	private void InvalidatePresentation()
	{
		_presentationVersion++;
		_pendingPresentation = null;
	}

	private async void DrainPresentation()
	{
		_presenting = true;
		while (_pendingPresentation is { } job)
		{
			_pendingPresentation = null;
			try
			{
				(int[] Field, int[] Intensity)? pixels = await Task.Run(() => Render(job));
				if (!_disposed && job.Version == _presentationVersion && pixels is { } pictures)
					ShowPictures(job.Source.Width, job.Source.Height, pictures.Field, pictures.Intensity);
			}
			catch (Exception ex)
			{
				LogManager.GetCurrentClassLogger().Error(ex, "Preview frame rendering failed");
				if (!_disposed && job.Version == _presentationVersion) Status = ex.Message;
			}
		}
		_presenting = false;
	}

	private static (int[] Field, int[] Intensity)? Render(PresentationJob job)
	{
		IFrameSource source = job.Source;
		int width = source.Width, height = source.Height;
		int[]? Plane(FrameLayer layer, ColorLut lut, float brightness)
		{
			var pixels = new int[checked(width * height)];
			if (source.Format == FrameFormat.Index)
			{
				var indices = new byte[pixels.Length];
				if (!source.CopyLayer(job.Frame, layer, indices)) return null;
				FrameRenderer.RenderIndexed(indices, width, height, lut, pixels);
			}
			else
			{
				var samples = new Half[pixels.Length];
				if (!source.CopyLayer(job.Frame, layer, samples)) return null;
				FrameRenderer.Render(samples, width, height, lut, source.KindOf(layer), brightness, pixels);
			}
			return pixels;
		}
		int[]? field = Plane(FrameLayer.Field, job.FieldLut, job.FieldBrightness);
		int[]? intensity = Plane(FrameLayer.Intensity, job.IntensityLut, job.IntensityBrightness);
		return field == null || intensity == null ? null : (field, intensity);
	}

	private void ShowPictures(int width, int height, int[] fieldPixels, int[] intensityPixels)
	{
		WriteableBitmap MakePicture(int[] pixels)
		{
			var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
			using var locked = bitmap.Lock();
			for (int y = 0; y < height; y++)
			{
				Marshal.Copy(pixels, y * width, IntPtr.Add(locked.Address, y * locked.RowBytes), width);
			}
			return bitmap;
		}
		WriteableBitmap field = MakePicture(fieldPixels);
		WriteableBitmap intensity = MakePicture(intensityPixels);
		WriteableBitmap? oldField = FieldImage;
		WriteableBitmap? oldIntensity = IntensityImage;
		FieldImage = field;
		IntensityImage = intensity;
		oldField?.Dispose();
		oldIntensity?.Dispose();
	}

	private void SetProject(Project project)
	{
		_watcher?.Dispose();
		SelectedNode = null;
		Project = project;
		_canvasWidth = 0;
		_canvasHeight = 0;
		_watcher = new ProjectWatcher(project);
		_watcher.Changed += ProjectChanged;
		RefreshTree();
	}

	private void RefreshTree()
	{
		object? selected = SelectedNode?.Tag;
		static TreeViewItem Group(string key, object? value, IEnumerable<object> children)
		{
			var item = new TreeViewItem { Tag = value, ItemsSource = children.Select(child =>
			{
				var row = new TreeViewItem { Tag = child };
				row.Bind(HeaderedItemsControl.HeaderProperty, new Binding("Name") { Source = child });
				return row;
			}).ToArray() };
			item.Bind(TreeViewItem.HeaderProperty, item.GetResourceObservable(key));
			return item;
		}
		Nodes =
		[
			Group("TreeScene", Project.Scene, []),
			Group("TreeSimulation", Project.Simulation, []),
			Group("TreeDisplay", Project.Display, []),
			Group("TreeSources", null, Project.Sources.Cast<object>()),
			Group("TreeParts", null, Project.Parts.Cast<object>()),
			Group("TreeProbes", null, Project.Probes.Cast<object>()),
			Group("TreeColorSchemes", null, Project.ColorSchemes.Cast<object>()),
		];
		if (selected != null) SelectItem(selected);
	}

	private (int X, int Y) CanvasCentre() => ((_canvasWidth > 0 ? _canvasWidth : Project.Scene.Width) / 2, (_canvasHeight > 0 ? _canvasHeight : Project.Scene.Height) / 2);

	private void SelectItem(object value)
	{
		foreach (TreeViewItem group in Nodes)
		{
			if (ReferenceEquals(group.Tag, value)) { group.IsSelected = true; SelectedNode = group; return; }
			if (group.ItemsSource is not IEnumerable<TreeViewItem> children) continue;
			foreach (TreeViewItem row in children)
			{
				if (!ReferenceEquals(row.Tag, value)) continue;
				group.IsExpanded = true;
				row.IsSelected = true;
				SelectedNode = row;
				return;
			}
		}
		SelectedNode = null;
	}

	private void RemoveSelected()
	{
		object? item = SelectedNode?.Tag;
		IList? list = item switch
		{
			Source => Project.Sources,
			OpticalPart => Project.Parts,
			Probe => Project.Probes,
			ColorScheme => Project.ColorSchemes,
			_ => null,
		};
		if (list == null || item == null) return;
		int index = list.IndexOf(item);
		int groupIndex = item switch { Source => 3, OpticalPart => 4, Probe => 5, _ => 6 };
		switch (item)
		{
			case Source source: Project.Remove(source); break;
			case OpticalPart part: Project.Remove(part); break;
			case Probe probe: Project.Remove(probe); break;
			case ColorScheme scheme: Project.Remove(scheme); break;
		}
		if (list.Count > 0) SelectItem(list[Math.Clamp(index, 0, list.Count - 1)]!);
		else { Nodes[groupIndex].IsSelected = true; SelectedNode = Nodes[groupIndex]; }
	}

	private void ProjectChanged(object? sender, ProjectChange change)
	{
		if (change.Item is INotifyCollectionChanged) RefreshTree();
		if (!_saving)
		{
			_dirty = true;
			UpdateTitle();
		}
		bool bakedBrightness = change.Item is DisplayLayer && change.Property == nameof(DisplayLayer.Brightness) && _quality.Format == FrameFormat.Index;
		bool unrecordedProbe = change.AffectsProbes && _run is { } run && Project.Probes.Any(probe => probe.Active && !run.Spec.Probes.Any(spec => ReferenceEquals(spec.Origin, probe)));
		if (!change.AffectsSimulation && !bakedBrightness && !unrecordedProbe) return;
		_lastEdit = change;
		_editGeneration++;
		_runStale |= _run != null;
		StopPlayback();
		_following = false;
		_run?.Stop();
		_requested = null;
		_scratchWaiting = false;
		InvalidatePresentation();
		_scratchDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
		_scratchDelay.Tick -= OnScratchDelay;
		_scratchDelay.Tick += OnScratchDelay;
		_scratchDelay.Stop();
		_scratchDelay.Start();
		if (_runStale) ScheduleRerun();
	}

	private async void OnScratchDelay(object? sender, EventArgs e)
	{
		_scratchDelay!.Stop();
		int generation = _editGeneration;
		if (_run is { IsActive: true } run) await run.Completion;
		if (_disposed || generation != _editGeneration) return;
		try
		{
			if (_lastEdit is { } edit)
				LogManager.GetCurrentClassLogger().Debug("Preview requested after {Item}.{Property} = {Value}", edit.Item, edit.Property ?? "list", edit.NewValueText);
			_lastEdit = null;
			RequestPreview(_folder, resetPosition: false);
		}
		catch (Exception ex) { Status = ex.Message; }
	}

	private void ScheduleRerun()
	{
		_rerunDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
		_rerunDelay.Tick -= OnRerunDelay;
		_rerunDelay.Tick += OnRerunDelay;
		_rerunDelay.Stop();
		_rerunDelay.Start();
	}

	private async void OnRerunDelay(object? sender, EventArgs e)
	{
		_rerunDelay!.Stop();
		if (_disposed || !_runStale) return;
		if (_scratchWaiting || _scratchDelay?.IsEnabled == true || _requested == null)
		{
			ScheduleRerun();
			return;
		}
		await StartRun(automatic: true);
	}

	private static IReadOnlyList<Control> BuildPropertyRows(object? item)
	{
		if (item == null) return [];
		var rows = new List<Control>();
		foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(item))
		{
			Type type = property.PropertyType;
			if (!property.IsBrowsable || !(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))) continue;
			var label = new TextBlock { Text = property.DisplayName, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
			string key = $"Label.{property.ComponentType.Name}.{property.Name}";
			if (Application.Current?.Resources.ContainsKey(key) == true) label.Bind(TextBlock.TextProperty, label.GetResourceObservable(key));
			Control value;
			var binding = new Binding(property.Name) { Source = item, Mode = BindingMode.TwoWay };
			if (!property.IsReadOnly && type == typeof(bool))
			{
				var check = new CheckBox();
				check.Bind(ToggleButton.IsCheckedProperty, binding);
				value = check;
			}
			else if (!property.IsReadOnly && type.IsEnum)
			{
				var choice = new ComboBox { ItemsSource = Enum.GetValues(type) };
				choice.Bind(SelectingItemsControl.SelectedItemProperty, binding);
				value = choice;
			}
			else if (!property.IsReadOnly && type == typeof(string))
			{
				var text = new TextBox();
				text.Bind(TextBox.TextProperty, binding);
				value = text;
			}
			else if (!property.IsReadOnly && property.Attributes[typeof(RangeAttribute)] is RangeAttribute range && IsNumber(type))
			{
				var number = new NumericUpDown
				{
					Minimum = Convert.ToDecimal(range.Minimum, CultureInfo.InvariantCulture),
					Maximum = Convert.ToDecimal(range.Maximum, CultureInfo.InvariantCulture),
					Increment = type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? 0.1m : 1m,
				};
				number.Bind(NumericUpDown.ValueProperty, binding);
				value = number;
			}
			else
			{
				var text = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
				text.Bind(TextBlock.TextProperty, new Binding(property.Name) { Source = item, Mode = BindingMode.OneWay });
				value = text;
			}
			AutomationProperties.SetAutomationId(value, $"Property{property.ComponentType.Name}{property.Name}");
			var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
			Grid.SetColumn(value, 1);
			row.Children.Add(label);
			row.Children.Add(value);
			rows.Add(row);
		}
		return rows;
	}

	private static bool IsNumber(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
		|| type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
		|| type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}

/// <summary>The user's answer to an unsaved-changes prompt.</summary>
internal enum SaveDecision { Cancel, Save, Discard }

/// <summary>The preview's keyboard transport scope; editable text retains its keys.</summary>
internal sealed class PreviewPanel(WindowStateModel state) : DockPanel
{
	/// <inheritdoc/>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox) return;
		ICommand? command = e.Key switch
		{
			Key.Space => state.PlayPauseCommand,
			Key.OemComma => state.StepBackCommand,
			Key.OemPeriod => state.StepForwardCommand,
			Key.I => state.SetInCommand,
			Key.O => state.SetOutCommand,
			Key.X => state.ClearRangeCommand,
			Key.F => state.LiveCommand,
			_ => null,
		};
		if (command != null)
		{
			if (command.CanExecute(null)) command.Execute(null);
			e.Handled = true;
		}
		else if (e.Key == Key.L)
		{
			state.IsLooping = !state.IsLooping;
			e.Handled = true;
		}
	}
}

/// <summary>Immutable UI-thread settings and source for one background render.</summary>
internal sealed record PresentationJob(long Version, IFrameSource Source, int Frame, ColorLut FieldLut, ColorLut IntensityLut, float FieldBrightness, float IntensityBrightness);

/// <summary>A scratch frame copied before its runner can reuse it.</summary>
internal sealed class CopiedFrameSource : IFrameSource
{
	private readonly byte[] _field;
	private readonly byte[] _intensity;
	/// <summary>Copies both planes on the thread receiving the scratch result.</summary>
	public CopiedFrameSource(FrameSnapshot snapshot)
	{
		Shape = snapshot.Shape;
		_field = snapshot.FieldBytes.ToArray();
		_intensity = snapshot.IntensityBytes.ToArray();
	}
	/// <inheritdoc/>
	public FrameShape Shape { get; }
	/// <inheritdoc/>
	public int Count => 1;
	/// <inheritdoc/>
	public bool Has(FrameLayer layer) => layer == FrameLayer.Field || _intensity.Length > 0;
	/// <inheritdoc/>
	public bool CopyLayer(int frame, FrameLayer layer, Span<byte> destination)
	{
		if (frame != 0 || !Has(layer)) return false;
		(layer == FrameLayer.Field ? _field : _intensity).CopyTo(destination);
		return true;
	}
	/// <inheritdoc/>
	public bool CopyLayer(int frame, FrameLayer layer, Span<Half> destination)
	{
		if (Shape.Format != FrameFormat.Half) throw new InvalidOperationException("The snapshot's samples are palette indices, not halves.");
		return CopyLayer(frame, layer, MemoryMarshal.AsBytes(destination));
	}
}

/// <summary>A UI command whose availability follows the current run.</summary>
internal sealed class ControlCommand(Action execute, Func<bool> canExecute) : ICommand
{
	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged;
	/// <inheritdoc/>
	public bool CanExecute(object? parameter) => canExecute();
	/// <inheritdoc/>
	public void Execute(object? parameter) { if (canExecute()) execute(); }
	/// <summary>Notifies controls that command availability may have changed.</summary>
	public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A UI command that passes its parameter to an action.</summary>
internal sealed class ParameterCommand(Action<object?> execute) : ICommand
{
	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged { add { } remove { } }
	/// <inheritdoc/>
	public bool CanExecute(object? parameter) => true;
	/// <inheritdoc/>
	public void Execute(object? parameter) => execute(parameter);
}

/// <summary>An asynchronous UI command that admits one invocation at a time.</summary>
internal sealed class OpenCommand(Func<Task> execute) : ICommand
{
	private bool _running;
	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged;
	/// <inheritdoc/>
	public bool CanExecute(object? parameter) => !_running;
	/// <inheritdoc/>
	public async void Execute(object? parameter)
	{
		if (_running) return;
		_running = true;
		CanExecuteChanged?.Invoke(this, EventArgs.Empty);
		try { await execute(); }
		finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
	}
}
