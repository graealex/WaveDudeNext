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
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NLog;
using NLog.Config;
using NLog.Targets;
using WaveDudeNext.Model;
using WaveDudeNext.Engine;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.Probes;
using WaveDudeNext.Export;

namespace WaveDudeNext.CrossPlatform;

/// <summary>The desktop application's entry point.</summary>
internal static class Program
{
	/// <summary>In-memory log shared by the window and NLog. Entries are consumed on the UI thread.</summary>
	internal static MessagesTarget Messages { get; } = new();
	/// <summary>The user's application-data directory, created before any cache or settings path is used.</summary>
	internal static string DataDirectory
	{
		get
		{
			string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
			if (!Path.IsPathFullyQualified(root)) throw new IOException("The local application data directory is unavailable.");
			return Path.Combine(root, "WaveDudeNext");
		}
	}

	[STAThread]
	private static void Main(string[] args)
	{
		string folder = DataDirectory;
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
#if DEBUG
		configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, Messages);
#else
		configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, Messages);
#endif
		LogManager.Configuration = configuration;
		AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.With(new global::Avalonia.Vulkan.VulkanOptions { VulkanInstanceCreationOptions = new() { ApplicationName = "WaveDude Next", VulkanVersion = new Version(1, 3) } })
			.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Glx, X11RenderingMode.Software] })
			.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Vulkan, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software] })
			.StartWithClassicDesktopLifetime(args);
	}
}

/// <summary>The cross-platform desktop application.</summary>
internal sealed class App : Application
{
	/// <summary>Loads the initial control theme.</summary>
	public override void Initialize()
	{
		Styles.Add(new FluentTheme());
		Resources["PictureHandle"] = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#FFC8C8C8"));
		Resources["PictureSelection"] = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#FFFFE000"));
		ApplyLanguage("");
	}

	/// <summary>Applies the selected UI language, or the system language for an empty code. UI thread only.</summary>
	internal void ApplyLanguage(string code)
	{
		string language = (code.Length == 0 ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : code).ToUpperInvariant();
		if (language is not ("EN" or "DE" or "FR" or "NL")) language = "EN";
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
	private ManualWindow? _manual;
	/// <summary>Creates the project menu and tree.</summary>
	public MainWindow()
	{
		Width = 1000;
		Height = 700;
		var state = new WindowStateModel(OpenProject, SaveProjectAs, ConfirmSave, PickSceneImage, SaveSceneImage, (spec, budget, autoBudget, quality) => new MemoryWarningWindow(spec, budget, autoBudget, quality).ShowDialog<MemoryWarningChoice?>(this), ChooseProbeCsv, ChooseExport, ConfirmExportFolder);
		var messages = new MessagesPane(Program.Messages) { Height = 220, IsVisible = false };
		Program.Messages.StatusMessage += entry => state.ReportLogMessage(entry.Message);
		void ShowMessages() { messages.IsVisible = true; Dispatcher.UIThread.Post(messages.FocusList, DispatcherPriority.Background); }
		bool CanCopy() => state.SelectedItem is { } item && ProjectClipboard.CanCopy(state.Project, item);
		bool CanCut() => state.SelectedItem is WaveDudeNext.Model.INamed && CanCopy();
		async Task CopyObject(bool cut)
		{
			object? item = state.SelectedItem;
			Project project = state.Project;
			if (Clipboard == null || item == null || !CanCopy() || cut && !CanCut()) return;
			try
			{
				string text = ProjectClipboard.Write(project, [item]);
				await Clipboard.SetTextAsync(text);
				if (cut && ReferenceEquals(state.Project, project) && ReferenceEquals(state.SelectedItem, item)) state.RemoveSelected();
				Logger.Debug("Copied project object {Name}, cut={Cut}", (item as WaveDudeNext.Model.INamed)?.Name ?? item.GetType().Name, cut);
			}
			catch (Exception ex) { Logger.Warn(ex, "Could not write project object to clipboard"); }
		}
		async Task PasteObject()
		{
			if (Clipboard == null) return;
			Project project = state.Project;
			object? target = state.SelectedItem;
			try
			{
				string? text = await Clipboard.TryGetTextAsync();
				if (!ReferenceEquals(state.Project, project) || !ReferenceEquals(state.SelectedItem, target) || text == null) return;
				if (ProjectClipboard.Read(text) is not { } clip || !clip.CanPaste(project, target)) return;
				IReadOnlyList<object> pasted = ProjectClipboard.Paste(project, clip, target);
				if (pasted.Count > 0)
				{
					state.RefreshTree();
					state.SelectItem(pasted[^1]);
					Logger.Debug("Pasted {Count} project object(s)", pasted.Count);
				}
			}
			catch (Exception ex) { Logger.Warn(ex, "Could not paste project object from clipboard"); }
		}
		var copyObject = new OpenCommand(() => CopyObject(false), CanCopy);
		var cutObject = new OpenCommand(() => CopyObject(true), CanCut);
		var pasteObject = new OpenCommand(PasteObject);
		state.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(WindowStateModel.SelectedItem)) { copyObject.Refresh(); cutObject.Refresh(); } };
		DragDrop.SetAllowDrop(this, true);
		AddHandler(DragDrop.DragOverEvent, (_, e) =>
		{
			e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
			e.Handled = true;
		});
		AddHandler(DragDrop.DropEvent, async (_, e) =>
		{
			IStorageFile? file = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();
			e.Handled = true;
			if (file != null) await state.OpenFile(() => LoadProject(file));
		});
		AddHandler(InputElement.PointerPressedEvent, (_, _) => state.PointerDown = true, RoutingStrategies.Tunnel);
		AddHandler(InputElement.PointerReleasedEvent, (_, _) => state.PointerDown = false, RoutingStrategies.Tunnel);
		AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => state.PointerDown = false, RoutingStrategies.Tunnel);
		Opened += (_, _) =>
		{
			state.Initialise();
			Dispatcher.UIThread.Post(DescribeViewportInterop, DispatcherPriority.Background);
		};
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
		var newProject = new MenuItem { Command = state.NewCommand, InputGesture = new KeyGesture(Key.N, KeyModifiers.Control) };
		newProject.Bind(MenuItem.HeaderProperty, newProject.GetResourceObservable("MenuFileNew"));
		var open = new MenuItem { Command = state.OpenCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.O, global::Avalonia.Input.KeyModifiers.Control) };
		open.Bind(MenuItem.HeaderProperty, open.GetResourceObservable("MenuFileOpen"));
		var save = new MenuItem { Command = state.SaveCommand, InputGesture = new KeyGesture(Key.S, KeyModifiers.Control) };
		save.Bind(MenuItem.HeaderProperty, save.GetResourceObservable("MenuFileSave"));
		var saveAs = new MenuItem { Command = state.SaveAsCommand, InputGesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift) };
		saveAs.Bind(MenuItem.HeaderProperty, saveAs.GetResourceObservable("MenuFileSaveAs"));
		MenuItem ExportItem(string key, ExportRequest request)
		{
			var item = new MenuItem { Command = state.ExportImagesCommand, CommandParameter = request };
			item.Bind(MenuItem.HeaderProperty, item.GetResourceObservable(key));
			return item;
		}
		var export = new MenuItem { ItemsSource = new[]
		{
			ExportItem("MenuFileExportField", new(true, false, false)), ExportItem("MenuFileExportIntensity", new(false, true, false)),
			ExportItem("MenuFileExportBoth", new(true, true, false)), ExportItem("MenuFileExportFieldFrame", new(true, false, true)),
			ExportItem("MenuFileExportIntensityFrame", new(false, true, true)),
		} };
		export.Bind(MenuItem.HeaderProperty, export.GetResourceObservable("MenuFileExport"));
		var cancelExport = new MenuItem { Command = state.CancelExportCommand, InputGesture = new KeyGesture(Key.Escape) };
		cancelExport.Bind(MenuItem.HeaderProperty, cancelExport.GetResourceObservable("TipExportCancel"));
		var file = new MenuItem { ItemsSource = new[] { newProject, open, save, saveAs, export, cancelExport } };
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
		MenuItem Toggle(string key, string property)
		{
			var item = new MenuItem { ToggleType = MenuItemToggleType.CheckBox };
			item.Bind(MenuItem.HeaderProperty, item.GetResourceObservable(key));
			item.Bind(MenuItem.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay });
			return item;
		}
		var simulation = new MenuItem();
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
		var undo = ActionItem("MenuEditUndo", state.UndoCommand);
		undo.InputGesture = new KeyGesture(Key.Z, KeyModifiers.Control);
		var redo = ActionItem("MenuEditRedo", state.RedoCommand);
		redo.InputGesture = new KeyGesture(Key.Y, KeyModifiers.Control);
		var edit = new MenuItem { ItemsSource = new[] { undo, redo, ActionItem("MenuEditCut", cutObject), ActionItem("MenuEditCopy", copyObject), ActionItem("MenuEditPaste", pasteObject), ActionItem("MenuProjectAddSource", state.AddSourceCommand), ActionItem("MenuProjectAddProbe", state.AddProbeCommand), parts, ActionItem("MenuProjectRemove", state.RemoveCommand), ActionItem("MenuExportProbeCsv", state.ExportProbeCsvCommand) } };
		edit.Bind(MenuItem.HeaderProperty, edit.GetResourceObservable("MenuEdit"));
		MenuItem QualityMenu(string title, string property, ICommand command, params (string Key, int Value)[] choices)
		{
			var items = choices.Select(choice =>
			{
				var item = ActionItem(choice.Key, command, choice.Value);
				item.ToggleType = MenuItemToggleType.Radio;
				item.Bind(MenuItem.IsCheckedProperty, new Binding(property) { Mode = BindingMode.OneWay, Converter = new FuncValueConverter<int, bool>(value => value == choice.Value) });
				return item;
			}).ToArray();
			var group = new MenuItem { ItemsSource = items };
			group.Bind(MenuItem.HeaderProperty, group.GetResourceObservable(title));
			return group;
		}
		var cacheChoices = new List<MenuItem> { ActionItem("MenuSimulationFrameMemoryAuto", state.MemoryCommand, 0) };
		for (int percent = 10; percent <= 100; percent += 10)
		{
			int value = percent;
			cacheChoices.Add(new MenuItem { Header = $"{value}%", Command = state.MemoryCommand, CommandParameter = value });
		}
		for (int i = 0; i < cacheChoices.Count; i++)
		{
			int value = i * 10;
			cacheChoices[i].ToggleType = MenuItemToggleType.Radio;
			cacheChoices[i].Bind(MenuItem.IsCheckedProperty, new Binding(nameof(WindowStateModel.FrameMemoryPercent)) { Mode = BindingMode.OneWay, Converter = new FuncValueConverter<int, bool>(percent => percent == value) });
		}
		var cacheMenu = new MenuItem { ItemsSource = cacheChoices };
		cacheMenu.Bind(MenuItem.HeaderProperty, cacheMenu.GetResourceObservable("MenuSimulationFrameMemory"));
		simulation.ItemsSource = new object[] { run, pause, resume, stop, play, pausePlayback, Toggle("MenuSimulationUseVulkan", nameof(WindowStateModel.UseGpu)), Toggle("MenuSimulationAutoRerun", nameof(WindowStateModel.AutoRerun)), Toggle("MenuSimulationWarnMemory", nameof(WindowStateModel.WarnWhenRamInsufficient)), cacheMenu };
		MenuItem LanguageMenu()
		{
			MenuItem Option(string code, string name)
			{
				var item = new MenuItem { Header = name, Command = state.LanguageCommand, CommandParameter = code, ToggleType = MenuItemToggleType.Radio };
				if (code.Length == 0) item.Bind(HeaderedItemsControl.HeaderProperty, item.GetResourceObservable("LanguageSystem"));
				item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(WindowStateModel.Language)) { Converter = new FuncValueConverter<string, bool>(value => value == code) });
				AutomationProperties.SetAutomationId(item, "Language" + (code.Length == 0 ? "System" : code.ToUpperInvariant()));
				return item;
			}
			var menu = new MenuItem { ItemsSource = new[] { Option("", "System"), Option("en", "English"), Option("de", "Deutsch"), Option("fr", "Français"), Option("nl", "Nederlands") } };
			menu.Bind(HeaderedItemsControl.HeaderProperty, menu.GetResourceObservable("MenuViewLanguage"));
			return menu;
		}
		MenuItem ThemeMenu()
		{
			MenuItem Option(string theme)
			{
				var item = new MenuItem { Command = state.ThemeCommand, CommandParameter = theme, ToggleType = MenuItemToggleType.Radio };
				item.Bind(HeaderedItemsControl.HeaderProperty, item.GetResourceObservable("Enum.AppTheme." + theme));
				item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(WindowStateModel.Theme)) { Converter = new FuncValueConverter<string, bool>(value => value == theme) });
				AutomationProperties.SetAutomationId(item, "Theme" + theme);
				return item;
			}
			var menu = new MenuItem { ItemsSource = new[] { Option("System"), Option("Light"), Option("Dark") } };
			menu.Bind(HeaderedItemsControl.HeaderProperty, menu.GetResourceObservable("MenuViewTheme"));
			return menu;
		}
		var view = new MenuItem { ItemsSource = new[]
		{
			ActionItem("PaneMessages", new ControlCommand(() => { if (messages.IsVisible) messages.IsVisible = false; else ShowMessages(); }, () => true)),
			ThemeMenu(),
			LanguageMenu(),
			QualityMenu("MenuViewResolution", nameof(WindowStateModel.PreviewResolution), state.ResolutionCommand, ("MenuViewResolutionFull", 1), ("MenuViewResolutionHalf", 2), ("MenuViewResolutionQuarter", 4)),
			QualityMenu("MenuViewFrames", nameof(WindowStateModel.PreviewFrames), state.FramesCommand, ("MenuViewFramesFull", 1), ("MenuViewFramesHalf", 2), ("MenuViewFramesQuarter", 4)),
			QualityMenu("MenuViewSamples", nameof(WindowStateModel.PreviewSampleBytes), state.SamplesCommand, ("MenuViewSamples16", 2), ("MenuViewSamples8", 1)),
		} };
		view.Bind(MenuItem.HeaderProperty, view.GetResourceObservable("MenuView"));
		var about = new MenuItem { Command = new OpenCommand(() => new AboutWindow(state.UseGpu).ShowDialog(this)) };
		about.Bind(MenuItem.HeaderProperty, about.GetResourceObservable("MenuHelpAbout"));
		AutomationProperties.SetAutomationId(about, "MenuHelpAbout");
		var manual = new MenuItem { Command = new ControlCommand(ShowManual, () => true), InputGesture = new KeyGesture(Key.F1) };
		manual.Bind(MenuItem.HeaderProperty, manual.GetResourceObservable("MenuHelpManual"));
		AutomationProperties.SetAutomationId(manual, "MenuHelpManual");
		var help = new MenuItem { ItemsSource = new[] { about, manual } };
		help.Bind(MenuItem.HeaderProperty, help.GetResourceObservable("MenuHelp"));
		var menu = new Menu { ItemsSource = new[] { file, edit, view, simulation, help } };
		var tree = new TreeView { Width = 250, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
		tree.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WindowStateModel.Nodes)));
		tree.Bind(TreeView.SelectedItemProperty, new Binding(nameof(WindowStateModel.SelectedNode)) { Mode = BindingMode.TwoWay });
		tree.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Delete), Command = state.RemoveCommand });
		KeyModifiers clipboardModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
		tree.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.C, clipboardModifier), Command = copyObject });
		tree.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.X, clipboardModifier), Command = cutObject });
		tree.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.V, clipboardModifier), Command = pasteObject });
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
		status.DoubleTapped += (_, _) => ShowMessages();
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
		var loop = new CheckBox { Content = "↻" };
		loop.Bind(ToolTip.TipProperty, loop.GetResourceObservable("TipPreviewLoop"));
		loop.Bind(AutomationProperties.NameProperty, loop.GetResourceObservable("TipPreviewLoop"));
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
		Control Picture(string layer, string source, string title, FrameLayer plane)
		{
			var image = new Image();
			image.Bind(Image.SourceProperty, new Binding(source));
			var overlay = new Image { IsHitTestVisible = false };
			overlay.Bind(Image.SourceProperty, new Binding(layer == "Field" ? nameof(WindowStateModel.FieldOverlay) : nameof(WindowStateModel.IntensityOverlay)));
			var edge = new PictureEdgeLine { IsHitTestVisible = false };
			edge.Bind(PictureEdgeLine.CanvasWidthProperty, new Binding(nameof(WindowStateModel.CanvasWidth)));
			edge.Bind(PictureEdgeLine.CanvasHeightProperty, new Binding(nameof(WindowStateModel.CanvasHeight)));
			edge.Bind(PictureEdgeLine.EdgeBrushProperty, new Binding(nameof(WindowStateModel.EdgeBrush)));
			var handles = new SceneHandles(state, plane == FrameLayer.Field ? ProbeLayer.Field : ProbeLayer.Intensity);
			handles.Bind(SceneHandles.HandleBrushProperty, handles.GetResourceObservable("PictureHandle"));
			handles.Bind(SceneHandles.SelectionBrushProperty, handles.GetResourceObservable("PictureSelection"));
			var graphs = new ProbeGraphs(state, plane == FrameLayer.Field ? ProbeLayer.Field : ProbeLayer.Intensity);
			var pictureLayers = new Grid { Children = { image, overlay, edge, handles, graphs } };
			var name = new TextBlock { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
			name.Bind(TextBlock.TextProperty, name.GetResourceObservable(title));
			var brightness = new NumericUpDown { Minimum = 0.0001m, Maximum = 100000m, Increment = 0.1m, Width = 90 };
			brightness.Bind(NumericUpDown.ValueProperty, new Binding($"Project.Display.{layer}.Brightness") { Mode = BindingMode.TwoWay });
			brightness.Bind(AutomationProperties.NameProperty, brightness.GetResourceObservable("Label.DisplayLayer.Brightness"));
			AutomationProperties.SetAutomationId(brightness, $"{layer}Brightness");
			var auto = new Button { Command = state.AutoBrightnessCommand, CommandParameter = plane };
			auto.Bind(ContentControl.ContentProperty, auto.GetResourceObservable("ButtonAutoBrightness"));
			auto.Bind(ToolTip.TipProperty, auto.GetResourceObservable("TipAutoBrightness"));
			AutomationProperties.SetAutomationId(auto, $"{layer}AutoBrightness");
			var header = new DockPanel { Children = { name, auto, brightness } };
			DockPanel.SetDock(name, Dock.Left);
			DockPanel.SetDock(auto, Dock.Right);
			DockPanel.SetDock(brightness, Dock.Right);
			DockPanel.SetDock(header, Dock.Top);
			var picture = new DockPanel { Children = { header, pictureLayers } };
			picture.Bind(IsVisibleProperty, new Binding($"Project.Display.{layer}.Preview"));
			return picture;
		}
		Control field = Picture("Field", nameof(WindowStateModel.FieldImage), "Label.Display.Field", FrameLayer.Field);
		Control intensity = Picture("Intensity", nameof(WindowStateModel.IntensityImage), "Label.Display.Intensity", FrameLayer.Intensity);
		var pictures = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Focusable = true };
		Grid.SetColumn(intensity, 1);
		pictures.Children.Add(field);
		pictures.Children.Add(intensity);
		DockPanel.SetDock(tree, Dock.Left);
		DockPanel.SetDock(propertyPane, Dock.Right);
		DockPanel.SetDock(menu, Dock.Top);
		DockPanel.SetDock(status, Dock.Bottom);
		DockPanel.SetDock(messages, Dock.Bottom);
		DockPanel.SetDock(transport, Dock.Bottom);
		var preview = new PreviewPanel(state) { Children = { transport, pictures } };
		Content = new DockPanel { Children = { menu, status, messages, tree, propertyPane, preview } };
	}

	private void ShowManual()
	{
		if (_manual == null)
		{
			_manual = new ManualWindow();
			_manual.Closed += (_, _) => _manual = null;
			_manual.Show(this);
		}
		else _manual.Activate();
	}

	private async void DescribeViewportInterop()
	{
		try
		{
			CompositionVisual? visual = ElementComposition.GetElementVisual(this);
			if (visual == null) { Logger.Debug("Viewport compositor is not available for GPU interop discovery"); return; }
			ICompositionGpuInterop? interop = await visual.Compositor.TryGetCompositionGpuInterop();
			if (interop == null) { Logger.Debug("Viewport compositor does not expose GPU interop"); return; }
			byte[]? uuid = interop.DeviceUuid;
			string images = string.Join(", ", interop.SupportedImageHandleTypes.Select(type => $"{type} ({interop.GetSynchronizationCapabilities(type)})"));
			string semaphores = string.Join(", ", interop.SupportedSemaphoreTypes);
			string uuidText = uuid == null ? "unavailable" : Convert.ToHexString(uuid);
			string luidText = interop.DeviceLuid == null ? "unavailable" : Convert.ToHexString(interop.DeviceLuid);
			Logger.Debug("Viewport compositor GPU interop: lost {Lost}, UUID {Uuid}, LUID {Luid}, image handles [{Images}], semaphore handles [{Semaphores}]", interop.IsLost, uuidText, luidText, images, semaphores);
			if (uuid is not { Length: 16 }) return;
			IReadOnlyList<VulkanDeviceInfo> devices = await Task.Run(VulkanDeviceInfo.FindAll);
			VulkanDeviceInfo? match = devices.FirstOrDefault(device => device.DeviceUuid.AsSpan().SequenceEqual(uuid));
			if (match == null) Logger.Debug("No Vulkan 1.3 compute device matches the viewport compositor UUID {Uuid}", uuidText);
			else Logger.Debug("Viewport compositor matches Vulkan device {Device}; software {Software}", match.Name, match.Software);
		}
		catch (Exception ex) { Logger.Debug(ex, "Viewport GPU interop discovery failed"); }
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
		return await LoadProject(files[0]);
	}

	private static async Task<(Project Project, string Name, string? Folder, string? Path)> LoadProject(IStorageFile file)
	{
		try
		{
			string? path = file.TryGetLocalPath();
			if (LegacyProject.IsLegacyFile(file.Name))
			{
				if (path == null)
				{
					throw new IOException("A legacy project must be opened from a local folder so its scene image can be found.");
				}
				return ImportLegacy(path);
			}
			await using Stream stream = await file.OpenReadAsync();
			return (Project.Load(stream), file.Name, path == null ? null : Path.GetDirectoryName(path), path);
		}
		catch (Exception ex)
		{
			string? path = file.TryGetLocalPath();
			Logger.Error(path == null ? ex : new UnreadableFileException(path, ex), "Error while opening '{Path}'", path ?? file.Name);
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

	private async Task<(byte[] Data, string Name, string? Path)?> PickSceneImage()
	{
		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = (string)Application.Current!.Resources["TipBrowseSceneImage"]!,
			AllowMultiple = false,
			FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.bmp", "*.jpg", "*.jpeg", "*.tif", "*.tiff", "*.gif"] }],
		});
		if (files.Count == 0) return null;
		await using Stream stream = await files[0].OpenReadAsync();
		using var bytes = new MemoryStream();
		await stream.CopyToAsync(bytes);
		return (bytes.ToArray(), files[0].Name, files[0].TryGetLocalPath());
	}

	private async Task SaveSceneImage(string name, byte[] data)
	{
		string extension = Path.GetExtension(name);
		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = (string)Application.Current!.Resources["TipSaveSceneImage"]!,
			SuggestedFileName = name,
			DefaultExtension = extension.TrimStart('.'),
		});
		if (file == null) return;
		await using Stream stream = await file.OpenWriteAsync();
		if (stream.CanSeek) stream.SetLength(0);
		await stream.WriteAsync(data);
	}

	private async Task<string?> ChooseProbeCsv(string name)
	{
		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = (string)Application.Current!.Resources["ExportDialogTitleProbeCsv"]!,
			SuggestedFileName = name,
			DefaultExtension = "csv",
			FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
		});
		if (file == null) return null;
		return file.TryGetLocalPath() ?? throw new IOException("The probe data must be saved to a local folder.");
	}

	private async Task<ExportSelection?> ChooseExport(ExportRequest request, string name)
	{
		ExportFormat[] formats = request.Still ? ExportFormats.Stills : ExportFormats.All;
		string filter = (string)Application.Current!.Resources[request.Still ? "ExportDialogFilterStill" : "ExportDialogFilter"]!;
		string[] entries = filter.Split('|');
		var dialog = new Window { Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
		dialog.Title = (string)Application.Current.Resources[request.Still ? "ExportDialogTitleFrame" : request.Field && request.Intensity ? "ExportDialogTitleBoth" : request.Field ? "ExportDialogTitleField" : "ExportDialogTitleIntensity"]!;
		var choice = new ComboBox { ItemsSource = formats.Select((_, i) => entries[i * 2]).ToArray(), SelectedIndex = 0, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
		AutomationProperties.SetAutomationId(choice, "ExportFormat");
		var accept = new Button { Command = new ControlCommand(() => dialog.Close(choice.SelectedIndex), () => choice.SelectedIndex >= 0) };
		accept.Bind(ContentControl.ContentProperty, accept.GetResourceObservable("DialogSave"));
		var cancel = new Button { Command = new ControlCommand(() => dialog.Close(-1), () => true), IsCancel = true };
		cancel.Bind(ContentControl.ContentProperty, cancel.GetResourceObservable("DialogCancel"));
		var buttons = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Children = { accept, cancel } };
		dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { choice, buttons } };
		dialog.Opened += (_, _) => choice.Focus();
		int? selected = await dialog.ShowDialog<int?>(this);
		if (selected is not int index || index < 0 || index >= formats.Length) return null;
		ExportFormat format = formats[index];
		string extension = ExportFormats.Extension(format);
		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = dialog.Title, SuggestedFileName = name + extension, DefaultExtension = extension.TrimStart('.'),
			FileTypeChoices = [new FilePickerFileType(entries[index * 2]) { Patterns = ExportFormats.Extensions(format).Select(ext => "*" + ext).ToArray() }],
		});
		if (file == null) return null;
		string path = file.TryGetLocalPath() ?? throw new IOException("The export must be saved to a local folder.");
		if (!ExportFormats.Accepts(format, path)) path = Path.ChangeExtension(path, extension);
		return new ExportSelection(path, format);
	}

	private Task<bool> ConfirmExportFolder(string folder)
	{
		var dialog = new Window { Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
		var message = new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, (string)Application.Current!.Resources["ExportFolderExistsFormat"]!, folder), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
		var yes = new Button { Command = new ControlCommand(() => dialog.Close(true), () => true) };
		yes.Bind(ContentControl.ContentProperty, yes.GetResourceObservable("DialogYes"));
		var no = new Button { Command = new ControlCommand(() => dialog.Close(false), () => true), IsCancel = true };
		no.Bind(ContentControl.ContentProperty, no.GetResourceObservable("DialogNo"));
		var buttons = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Children = { yes, no } };
		dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { message, buttons } };
		dialog.Opened += (_, _) => no.Focus();
		return dialog.ShowDialog<bool>(this);
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

/// <summary>Picture layers and whether the export contains only the currently shown frame.</summary>
internal sealed record ExportRequest(bool Field, bool Intensity, bool Still);

/// <summary>The selected destination and encoder.</summary>
internal sealed record ExportSelection(string Path, ExportFormat Format);

/// <summary>Bound project state and commands, used on the UI thread.</summary>
internal sealed class WindowStateModel : INotifyPropertyChanged, IDisposable
{
	private readonly Func<Task<(Project Project, string Name, string? Folder, string? Path)?>> _open;
	private readonly Func<string?, Task<string?>> _saveAs;
	private readonly Func<string, Task<SaveDecision>> _confirmSave;
	private readonly Func<Task<(byte[] Data, string Name, string? Path)?>> _chooseImage;
	private readonly Func<string, byte[], Task> _saveImage;
	private readonly Func<RunSpec, long, long, PreviewQuality, Task<MemoryWarningChoice?>> _warn;
	private readonly Func<string, Task<string?>> _chooseProbeCsv;
	private readonly Func<ExportRequest, string, Task<ExportSelection?>> _chooseExport;
	private readonly Func<string, Task<bool>> _confirmExportFolder;
	private ExportJob? _exportJob;
	private readonly SimulationSettings _settings;
	private string _title = "WaveDude Next";
	private string _status = "";
	private IReadOnlyList<TreeViewItem> _nodes = [];
	private Project _project = new();
	private ScratchRunner? _scratch;
	private RunSpec? _requested;
	private WriteableBitmap? _fieldImage;
	private WriteableBitmap? _intensityImage;
	private WriteableBitmap? _fieldOverlay;
	private WriteableBitmap? _intensityOverlay;
	private bool _disposed;
	private double _frame;
	private int _lastFrame;
	private int _canvasWidth;
	private int _canvasHeight;
	private SimulationRun? _run;
	private string? _folder;
	private PreviewQuality _quality = PreviewQuality.Full;
	private bool _following;
	private readonly ControlCommand _pauseRun;
	private readonly ControlCommand _stopRun;
	private PresentationJob? _pendingPresentation;
	private CopiedFrameSource? _lastScratch;
	private ScratchResult? _lastScratchResult;
	private ScratchResult? _shownScratchResult;
	private ScratchResult? _probeScratchResult;
	private SimulationRun? _pausedForScratch;
	private bool _probesStale;
	private int _scratchGeneration;
	private RunSpec? _lastScratchSpec;
	private int _lastScratchFrame;
	private IFrameSource? _shownSource;
	private int _shownFrame;
	private ScratchRunner? _fitRunner;
	private SimulationRun? _pausedForFit;
	private FrameLayer _fitLayer;
	private int _fitGeneration;
	private bool _presenting;
	private long _presentationVersion;
	private ScenePixels? _scenePixels;
	private ScenePixels? _edgeCacheScene;
	private byte[]? _edgeCache;
	private OutlineJob? _pendingOutline;
	private bool _outlining;
	private long _outlineVersion;
	private TreeViewItem? _selectedNode;
	private int _selectionVersion;
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
	private FileSystemWatcher? _imageWatcher;
	private DispatcherTimer? _imageDelay;
	private int _imageGeneration;
	private bool _sceneImageFileMissing;
	private ProjectHistory? _history;
	private DispatcherTimer? _recordDelay;
	private bool _recordPending;
	private bool _restoring;

	/// <summary>Creates state for a new project.</summary>
	public WindowStateModel(Func<Task<(Project Project, string Name, string? Folder, string? Path)?>> open, Func<string?, Task<string?>> saveAs, Func<string, Task<SaveDecision>> confirmSave, Func<Task<(byte[] Data, string Name, string? Path)?>> chooseImage, Func<string, byte[], Task> saveImage, Func<RunSpec, long, long, PreviewQuality, Task<MemoryWarningChoice?>> warn, Func<string, Task<string?>> chooseProbeCsv, Func<ExportRequest, string, Task<ExportSelection?>> chooseExport, Func<string, Task<bool>> confirmExportFolder)
	{
		_open = open;
		_saveAs = saveAs;
		_confirmSave = confirmSave;
		_chooseImage = chooseImage;
		_saveImage = saveImage;
		_warn = warn;
		_chooseProbeCsv = chooseProbeCsv;
		_chooseExport = chooseExport;
		_confirmExportFolder = confirmExportFolder;
		_settings = SimulationSettings.Load(Path.Combine(Program.DataDirectory, "Settings.xml"));
		if (Application.Current is App app) app.ApplyLanguage(_settings.Language);
		ApplyTheme(_settings.Theme);
		_quality = _settings.Quality;
		OpenCommand = new OpenCommand(Open);
		NewCommand = new OpenCommand(NewProject);
		SaveCommand = new OpenCommand(() => Save(false));
		SaveAsCommand = new OpenCommand(() => Save(true));
		BrowseImageCommand = new OpenCommand(BrowseSceneImage);
		SaveImageCommand = new OpenCommand(SaveEmbeddedImage);
		ExportProbeCsvCommand = new AsyncParameterCommand(ExportProbeCsv, parameter => (parameter ?? SelectedItem) is Probe { Active: true } && Project.Simulation.Steps > 0);
		ExportImagesCommand = new AsyncParameterCommand(ExportImages, parameter => parameter is ExportRequest && Project.Simulation.Steps > 0);
		CancelExportCommand = new ControlCommand(() => _exportJob?.Cancel(), () => _exportJob?.IsActive == true);
		RemoveImageCommand = new ControlCommand(RemoveSceneImage, () => Project.Scene.ImageData != null || Project.Scene.Image.Length > 0 || Project.Scene.ImagePath.Length > 0);
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
		UndoCommand = new ControlCommand(() => MoveHistory(false), () => _history != null && (_history.CanUndo || _recordPending));
		RedoCommand = new ControlCommand(() => MoveHistory(true), () => _history?.CanRedo == true && !_recordPending);
		ResolutionCommand = new ParameterCommand(value => { if (value is int n) SetQuality(_quality with { ResolutionDivisor = n }); });
		FramesCommand = new ParameterCommand(value => { if (value is int n) SetQuality(_quality with { FrameDivisor = n }); });
		SamplesCommand = new ParameterCommand(value => { if (value is int n) SetQuality(_quality with { Format = n == 2 ? FrameFormat.Half : FrameFormat.Index }); });
		LanguageCommand = new ParameterCommand(value => { if (value is string code) SetLanguage(code); });
		ThemeCommand = new ParameterCommand(value => { if (value is string theme) SetTheme(theme); });
		MemoryCommand = new ParameterCommand(value => { if (value is int n && (n == 0 || n is >= 10 and <= 100 && n % 10 == 0)) { _settings.FrameMemoryPercent = n; _settings.Save(); Changed(nameof(FrameMemoryPercent)); } });
		AutoBrightnessCommand = new ParameterCommand(value => { if (value is FrameLayer layer) FitBrightness(layer); });
		SetProject(_project);
	}

	/// <summary>Raised when a bound property changes.</summary>
	public event PropertyChangedEventHandler? PropertyChanged;
	/// <summary>Raised on the UI thread when project items shown over a picture change.</summary>
	internal event EventHandler<ProjectChange>? SceneVisualChanged;
	/// <summary>Raised on the UI thread when the displayed frame or its probe recordings change.</summary>
	internal event EventHandler? ProbeGraphsChanged;
	/// <summary>Returns the recording for the frame currently displayed, and the simulation length in steps.</summary>
	internal ProbeGraphData ProbeData(Probe probe, out int totalSteps)
	{
		ScratchResult? shown = _shownScratchResult;
		if (_probesStale && _run is { } displayedRun && ReferenceEquals(_shownSource, displayedRun.Frames) && _probeScratchResult is { } probeScratch && probeScratch.Frame == _shownFrame)
			shown = probeScratch;
		if (shown is { } scratch)
		{
			totalSteps = Math.Max(1, scratch.Spec.Steps);
			for (int i = 0; i < scratch.Spec.Probes.Length && i < scratch.Traces.Length; i++)
				if (ReferenceEquals(scratch.Spec.Probes[i].Origin, probe)) return ProbeGraphData.At(scratch.Traces[i], scratch.Frame * scratch.Spec.VisualizeEvery, 0);
			return default;
		}
		SimulationRun? run = _run;
		totalSteps = Math.Max(1, run?.Spec.Steps ?? Project.Simulation.Steps);
		if (run == null || !ReferenceEquals(_shownSource, run.Frames)) return default;
		for (int i = 0; i < run.Spec.Probes.Length && i < run.Traces.Length; i++)
			if (ReferenceEquals(run.Spec.Probes[i].Origin, probe)) return ProbeGraphData.At(run.Traces[i], _shownFrame * run.Spec.VisualizeEvery, _shownFrame);
		return default;
	}
	/// <summary>The window title.</summary>
	public string Title { get => _title; private set { _title = value; Changed(); } }
	/// <summary>The current status message.</summary>
	public string Status { get => _status; private set { _status = value; Changed(); } }
	/// <summary>The UI language code, or an empty string for the system language.</summary>
	public string Language => _settings.Language;
	/// <summary>Selects and saves a UI language.</summary>
	public ICommand LanguageCommand { get; }
	/// <summary>The selected UI theme.</summary>
	public string Theme => _settings.Theme;
	/// <summary>Selects and saves a UI theme.</summary>
	public ICommand ThemeCommand { get; }
	private static void ApplyTheme(string theme)
	{
		if (Application.Current is { } app) app.RequestedThemeVariant = theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
	}
	private void SetTheme(string theme)
	{
		if (theme is not ("System" or "Light" or "Dark") || theme == _settings.Theme) return;
		_settings.Theme = theme;
		ApplyTheme(theme);
		_settings.Save();
		Changed(nameof(Theme));
		LogManager.GetCurrentClassLogger().Info("Theme changed to {Theme}", theme);
	}
	private void SetLanguage(string code)
	{
		if (code is not ("" or "en" or "de" or "fr" or "nl") || code == _settings.Language) return;
		_settings.Language = code;
		if (Application.Current is App app) app.ApplyLanguage(code);
		_settings.Save();
		Changed(nameof(Language));
		LogManager.GetCurrentClassLogger().Info("Language changed to '{Language}'", code.Length == 0 ? "system" : code);
	}
	/// <summary>Shows a log message of Info level or higher on the UI thread.</summary>
	internal void ReportLogMessage(string message) => Status = message;
	/// <summary>The displayed project nodes.</summary>
	public IReadOnlyList<TreeViewItem> Nodes { get => _nodes; private set { _nodes = value; Changed(); } }
	/// <summary>The selected tree row.</summary>
	public TreeViewItem? SelectedNode
	{
		get => _selectedNode;
		set
		{
			if (ReferenceEquals(_selectedNode, value)) return;
			if (value != null) _selectionVersion++;
			_selectedNode = value;
			Changed();
			Changed(nameof(SelectedItem));
			PropertyRows = BuildPropertyRows(value?.Tag);
			((ControlCommand)RemoveCommand).Refresh();
			((AsyncParameterCommand)ExportProbeCsvCommand).Refresh();
		}
	}
	/// <summary>The selected project object, if any.</summary>
	public object? SelectedItem => SelectedNode?.Tag;
	/// <summary>The selected object's visible scalar properties.</summary>
	public IReadOnlyList<Control> PropertyRows { get => _propertyRows; private set { _propertyRows = value; Changed(); } }
	/// <summary>The loaded project, retained for editing and simulation.</summary>
	public Project Project { get => _project; private set { _project = value; Changed(); } }
	/// <summary>Opens an existing project.</summary>
	public ICommand OpenCommand { get; }
	/// <summary>Creates a new project after handling unsaved changes.</summary>
	public ICommand NewCommand { get; }
	/// <summary>Saves the current project.</summary>
	public ICommand SaveCommand { get; }
	/// <summary>Chooses a new path and saves the current project.</summary>
	public ICommand SaveAsCommand { get; }
	/// <summary>Embeds a chosen scene image in the project.</summary>
	public ICommand BrowseImageCommand { get; }
	/// <summary>Saves the embedded scene image as a separate file.</summary>
	public ICommand SaveImageCommand { get; }
	/// <summary>Exports the selected active probe's recording as CSV.</summary>
	public ICommand ExportProbeCsvCommand { get; }
	/// <summary>Exports the requested picture layers or a still frame.</summary>
	public ICommand ExportImagesCommand { get; }
	/// <summary>Cancels an active export after its current frame.</summary>
	public ICommand CancelExportCommand { get; }
	/// <summary>Removes the image while preserving its canvas size.</summary>
	public ICommand RemoveImageCommand { get; }
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
	/// <summary>Restores the preceding project edit.</summary>
	public ICommand UndoCommand { get; }
	/// <summary>Restores the next project edit.</summary>
	public ICommand RedoCommand { get; }
	/// <summary>Sets the preview resolution divisor.</summary>
	public ICommand ResolutionCommand { get; }
	/// <summary>Sets the preview frame divisor.</summary>
	public ICommand FramesCommand { get; }
	/// <summary>Sets the preview sample size.</summary>
	public ICommand SamplesCommand { get; }
	/// <summary>Sets the RAM cache size as a percentage of physical memory, or zero for automatic.</summary>
	public ICommand MemoryCommand { get; }
	/// <summary>Fits a picture's brightness to its current frame.</summary>
	public ICommand AutoBrightnessCommand { get; }
	/// <summary>Whether to use a Vulkan GPU when available.</summary>
	public bool UseGpu { get => _settings.UseGpu; set { if (_settings.UseGpu == value) return; _settings.UseGpu = value; _settings.Save(); Changed(); } }
	/// <summary>Whether an edit automatically reruns a stale simulation.</summary>
	public bool AutoRerun { get => _settings.AutoRerun; set { if (_settings.AutoRerun == value) return; _settings.AutoRerun = value; _settings.Save(); if (value && _runStale) ScheduleRerun(); if (!value) _rerunDelay?.Stop(); Changed(); } }
	/// <summary>Whether to warn before spilling frames to disk.</summary>
	public bool WarnWhenRamInsufficient { get => _settings.WarnWhenRamInsufficient; set { if (_settings.WarnWhenRamInsufficient == value) return; _settings.WarnWhenRamInsufficient = value; _settings.Save(); Changed(); } }
	/// <summary>Preview resolution divisor.</summary>
	public int PreviewResolution => _quality.ResolutionDivisor;
	/// <summary>Preview frame divisor.</summary>
	public int PreviewFrames => _quality.FrameDivisor;
	/// <summary>Preview bytes per sample.</summary>
	public int PreviewSampleBytes => _quality.Format == FrameFormat.Half ? 2 : 1;
	/// <summary>The RAM cache limit as a percentage of physical memory, or zero for automatic.</summary>
	public int FrameMemoryPercent => _settings.FrameMemoryPercent;
	/// <summary>Whether a pointer button is held during a project edit.</summary>
	public bool PointerDown { get; set; }
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
	/// <summary>Whether the embedded image's named source file is missing.</summary>
	public bool SceneImageFileMissing { get => _sceneImageFileMissing; private set { if (_sceneImageFileMissing != value) { _sceneImageFileMissing = value; Changed(); } } }
	/// <summary>Whether the scene contains an embedded image to save.</summary>
	public bool HasEmbeddedImage => Project.Scene.ImageData != null;
	/// <summary>The latest field picture.</summary>
	public WriteableBitmap? FieldImage { get => _fieldImage; private set { _fieldImage = value; Changed(); } }
	/// <summary>The latest intensity picture.</summary>
	public WriteableBitmap? IntensityImage { get => _intensityImage; private set { _intensityImage = value; Changed(); } }
	/// <summary>The field picture's translucent scene outline.</summary>
	public WriteableBitmap? FieldOverlay { get => _fieldOverlay; private set { _fieldOverlay = value; Changed(); } }
	/// <summary>The intensity picture's translucent scene outline.</summary>
	public WriteableBitmap? IntensityOverlay { get => _intensityOverlay; private set { _intensityOverlay = value; Changed(); } }
	/// <summary>Width of the displayed canvas in cells.</summary>
	public int CanvasWidth => _canvasWidth > 0 ? _canvasWidth : Project.Scene.Width;
	/// <summary>Height of the displayed canvas in cells.</summary>
	public int CanvasHeight => _canvasHeight > 0 ? _canvasHeight : Project.Scene.Height;
	/// <summary>Colour of the simulated boundary around the preview.</summary>
	public global::Avalonia.Media.IBrush EdgeBrush => Project.Scene.HasRim ? global::Avalonia.Media.Brushes.Blue : Project.Scene.Edges == EdgeMode.Reflective ? global::Avalonia.Media.Brushes.Red : global::Avalonia.Media.Brushes.Transparent;
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
		_exportJob?.Cancel();
		_editGeneration++;
		_scratchDelay?.Stop();
		_rerunDelay?.Stop();
		_recordDelay?.Stop();
		_watcher?.Dispose();
		DisarmImageWatch();
		CancelFit();
		StopPlayback();
		_playTimer?.Dispose();
		InvalidatePresentation();
		_outlineVersion++;
		_pendingOutline = null;
		DiscardRun();
		_scratch?.Dispose();
		_fieldImage?.Dispose();
		_intensityImage?.Dispose();
		_fieldOverlay?.Dispose();
		_intensityOverlay?.Dispose();
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
			if (_probesStale && !IsPlaying && !_following) ScheduleScratch();
		}
		else if (_requested != null) _scratch?.Request(_requested, (int)frame);
	}

	private async Task StartRun(bool automatic = false, PreviewQuality? qualityOverride = null)
	{
		if (_requested == null) return;
		CancelFit();
		try
		{
			RunSpec original = _requested;
			PreviewQuality quality = qualityOverride ?? _quality;
			RunSpec spec = RunSpec.Create(Project, SceneComposer.Compose(Project, _folder), preferGpu: UseGpu, trackIntensity: true, quality: quality, preferVulkan: true);
			long budget = FrameMemory.Budget(_settings.FrameMemoryPercent, _run?.Store.RamBytes ?? 0);
			int ramFrames = Math.Min(spec.StoredFrameCount, FrameStore.FramesInBudget(spec.Shape, spec.Layers, budget));
			if (ramFrames < spec.StoredFrameCount && WarnWhenRamInsufficient)
			{
				long autoBudget = FrameMemory.Budget(FrameMemory.Automatic, _run?.Store.RamBytes ?? 0);
				MemoryWarningChoice? choice = await _warn(spec, budget, autoBudget, quality);
				if (choice == null || _disposed || !ReferenceEquals(original, _requested)) return;
				quality = choice.Quality;
				if (qualityOverride != null && (quality.FrameDivisor != 1 || quality.ResolutionDivisor != 1 || qualityOverride.Format == FrameFormat.Half && quality.Format != FrameFormat.Half || quality.IntensityRoot != qualityOverride.IntensityRoot))
				{
					LogManager.GetCurrentClassLogger().Warn("Probe export requires a full-resolution run with every frame");
					Status = Application.Current?.Resources.TryGetValue("ExportError", out object? text) == true ? text as string ?? "Export failed" : "Export failed";
					return;
				}
				if (qualityOverride == null) { _quality = quality; _settings.Quality = quality; }
				if (choice.Mute) _settings.WarnWhenRamInsufficient = false;
				if (choice.CacheAuto) _settings.FrameMemoryPercent = FrameMemory.Automatic;
				_settings.Save();
				NotifyQuality();
				Changed(nameof(WarnWhenRamInsufficient));
				Changed(nameof(FrameMemoryPercent));
				budget = FrameMemory.Budget(_settings.FrameMemoryPercent, _run?.Store.RamBytes ?? 0);
				spec = RunSpec.Create(Project, SceneComposer.Compose(Project, _folder), preferGpu: UseGpu, trackIntensity: true, quality: quality, preferVulkan: true);
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
			_probesStale = false;
			_probeScratchResult = null;
			_scratchWaiting = false;
			_rerunDelay?.Stop();
			_following = !automatic;
			run.PropertyChanged += RunChanged;
			run.ProbesUpdated += RunProbesUpdated;
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
		_scratchDelay?.Stop();
		if (_scratchWaiting && _scratch is { } pendingScratch)
		{
			_scratch = null;
			pendingScratch.Completed -= ScratchCompleted;
			await Task.Run(pendingScratch.Dispose);
			_scratchWaiting = false;
			ResumeAfterScratch();
			if (_disposed || generation != _playGeneration || _run == null) return;
		}
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
		if (_probesStale && !_runStale && !_disposed) ScheduleScratch();
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

	private void RunProbesUpdated(object? sender, ProbeUpdate update)
	{
		if (!_disposed && ReferenceEquals(sender, _run)) ProbeGraphsChanged?.Invoke(this, EventArgs.Empty);
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
		old.ProbesUpdated -= RunProbesUpdated;
		old.Stop();
		Task exportDone = _exportJob is { } export && ReferenceEquals(export.Store, old.Store) ? export.Completion : Task.CompletedTask;
		return Task.WhenAll(old.Completion, exportDone).ContinueWith(_ => { old.Store.Dispose(); old.Dispose(); }, TaskScheduler.Default);
	}

	private async Task Open()
	{
		try
		{
			if (!await ConfirmDiscard()) return;
			if (await _open() is { } result)
			{
				ReplaceProject(result.Project, result.Name, result.Folder, result.Path);
			}
		}
		catch (Exception ex)
		{
			Status = ex.Message;
		}
	}

	/// <summary>Opens a dropped project after handling unsaved changes.</summary>
	internal async Task OpenFile(Func<Task<(Project Project, string Name, string? Folder, string? Path)>> load)
	{
		try
		{
			if (!await ConfirmDiscard()) return;
			var result = await load();
			ReplaceProject(result.Project, result.Name, result.Folder, result.Path);
		}
		catch (Exception ex) { Status = ex.Message; }
	}

	private async Task NewProject()
	{
		try
		{
			if (!await ConfirmDiscard()) return;
			string name = Application.Current?.Resources.TryGetValue("Untitled", out object? value) == true ? value as string ?? "Untitled" : "Untitled";
			ReplaceProject(new Project(), name, null, null);
		}
		catch (Exception ex) { Status = ex.Message; }
	}

	private void ReplaceProject(Project project, string name, string? folder, string? path)
	{
		CancelFit();
		_shownSource = null;
		_shownScratchResult = null;
		_lastScratchResult = null;
		_probeScratchResult = null;
		_probesStale = false;
		_editGeneration++;
		_scratchDelay?.Stop();
		_rerunDelay?.Stop();
		_runStale = false;
		_scratchWaiting = false;
		StopPlayback();
		_ = DiscardRun();
		_requested = null;
		_folder = folder;
		_path = path;
		SetProject(project);
		_projectName = name;
		_dirty = false;
		UpdateTitle();
		LocateSceneImage();
		ArmImageWatch();
		_ = ReloadSceneImage(_imageGeneration);
		Status = "";
		RequestPreview(_folder);
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
			ArmImageWatch();
			_projectName = Path.GetFileName(path);
			_dirty = false;
			_recordDelay?.Stop();
			_recordPending = false;
			_history?.Record();
			_history?.MarkSaved();
			RefreshHistoryCommands();
			UpdateTitle();
		}
		catch (Exception ex)
		{
			LogManager.GetCurrentClassLogger().Error(ex, "Error while saving project");
			Status = ex.Message;
		}
	}

	private void UpdateTitle() => Title = _projectName + (_dirty ? "*" : "") + " - WaveDude Next";

	private async Task BrowseSceneImage()
	{
		try
		{
			if (await _chooseImage() is not { } image || _disposed) return;
			using var stream = new MemoryStream(image.Data, writable: false);
			ScenePixels pixels = SceneComposer.Decode(stream);
			Project.Scene.ImageData = image.Data;
			Project.Scene.Image = image.Name;
			Project.Scene.ImagePath = image.Path == null ? "" : Scene.StoredImagePath(_folder, image.Path);
			Project.Scene.Mode = SceneMode.Image;
			LogManager.GetCurrentClassLogger().Info("Scene image '{Name}' embedded: {Width}×{Height}, {Bytes} bytes", image.Name, pixels.Width, pixels.Height, image.Data.Length);
		}
		catch (Exception ex)
		{
			LogManager.GetCurrentClassLogger().Error(ex, "The scene image could not be loaded");
			Status = ex.Message;
		}
	}

	private void RemoveSceneImage()
	{
		Scene scene = Project.Scene;
		if (scene.ImageData is { } bytes)
		{
			try
			{
				using var stream = new MemoryStream(bytes, writable: false);
				ScenePixels pixels = SceneComposer.Decode(stream);
				scene.Width = pixels.Width;
				scene.Height = pixels.Height;
			}
			catch (Exception ex) { LogManager.GetCurrentClassLogger().Debug(ex, "Could not read the scene image size"); }
		}
		scene.RemoveImage();
	}

	private async Task SaveEmbeddedImage()
	{
		if (Project.Scene.ImageData is not { } bytes) return;
		try { await _saveImage(Project.Scene.Image.Length > 0 ? Project.Scene.Image : "scene", bytes); }
		catch (Exception ex) { LogManager.GetCurrentClassLogger().Error(ex, "The scene image could not be saved"); Status = ex.Message; }
	}

	private async Task ExportProbeCsv(object? parameter)
	{
		if ((parameter ?? SelectedItem) is not Probe { Active: true } probe) return;
		try
		{
			int generation = _editGeneration;
			string name = ProbeCsv.FileName(Path.GetFileNameWithoutExtension(_projectName), probe);
			string? path = await _chooseProbeCsv(name);
			if (path == null || _disposed || generation != _editGeneration) return;
			SimulationRun? run = _run;
			bool usable = run != null && !_runStale && !_probesStale && run.State is not (RunState.Cancelled or RunState.Failed)
				&& run.Spec.Probes.Any(item => ReferenceEquals(item.Origin, probe)) && run.Spec.Quality.ResolutionDivisor == 1 && run.Spec.FrameStride == 1;
			if (!usable)
			{
				SimulationRun? previous = run;
				if (_requested == null) RequestPreview(_folder, resetPosition: false);
				await StartRun(automatic: true, qualityOverride: _quality with { ResolutionDivisor = 1, FrameDivisor = 1 });
				run = _run;
				if (run == null || ReferenceEquals(run, previous) || _runStale || _probesStale || generation != _editGeneration) return;
			}
			if (run == null) return;
			if (!run.IsFinished)
			{
				var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				void OnFinished(object? sender, EventArgs e) => finished.TrySetResult();
				run.Finished += OnFinished;
				try { if (!run.IsFinished) await finished.Task; }
				finally { run.Finished -= OnFinished; }
			}
			if (_disposed || generation != _editGeneration || !ReferenceEquals(run, _run) || run.State != RunState.Completed) return;
			int index = -1;
			for (int i = 0; i < run.Spec.Probes.Length; i++) if (ReferenceEquals(run.Spec.Probes[i].Origin, probe)) { index = i; break; }
			if (index < 0 || index >= run.Traces.Length || (probe.Kind == ProbeKind.Line && run.Spec.FrameStride != 1)) return;
			Probe copy = probe.Clone();
			int rows = await Task.Run(() => ProbeCsv.Write(path, copy, run.Traces[index], run.Spec.VisualizeEvery, run.Spec.FrameStride));
			LogManager.GetCurrentClassLogger().Info("Probe data written: {Rows} rows of '{Probe}' to {Path}", rows, copy.Name, path);
		}
		catch (Exception ex) { LogManager.GetCurrentClassLogger().Error(ex, "The probe data could not be written"); Status = ex.Message; }
	}

	private async Task ExportImages(object? parameter)
	{
		if (parameter is not ExportRequest request || (!request.Field && !request.Intensity) || _exportJob != null) return;
		try
		{
			int generation = _editGeneration;
			int stillFrame = Math.Max(0, (int)Frame);
			string suffix = request.Field && request.Intensity ? "" : request.Field ? "_field" : "_intensity";
			if (request.Still) suffix += "_" + stillFrame.ToString("D" + ImageSequenceSink.MinDigits, CultureInfo.InvariantCulture);
			ExportSelection? selection = await _chooseExport(request, Path.GetFileNameWithoutExtension(_projectName) + suffix);
			if (selection == null || _disposed || generation != _editGeneration) return;
			ExportFormat format = selection.Format;
			if (!ExportFormats.IsAvailable(format) || request.Still && !ExportFormats.IsSequence(format)) throw new NotSupportedException("The selected export format is not available.");
			var outputs = new List<(FrameLayer Layer, string Path)>();
			if (request.Field && request.Intensity)
			{
				string folder = Path.GetDirectoryName(selection.Path) ?? "", stem = Path.GetFileNameWithoutExtension(selection.Path);
				foreach (string known in new[] { "_field", "_intensity" }) if (stem.EndsWith(known, StringComparison.OrdinalIgnoreCase)) stem = stem[..^known.Length];
				outputs.Add((FrameLayer.Field, Path.Combine(folder, stem + "_field" + ExportFormats.Extension(format))));
				outputs.Add((FrameLayer.Intensity, Path.Combine(folder, stem + "_intensity" + ExportFormats.Extension(format))));
			}
			else outputs.Add((request.Field ? FrameLayer.Field : FrameLayer.Intensity, selection.Path));
			if (!request.Still && ExportFormats.IsSequence(format))
				foreach (var output in outputs)
				{
					string folder = ExportFormats.SequenceFolder(output.Path);
					if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && !await _confirmExportFolder(folder)) return;
					if (_disposed || generation != _editGeneration) return;
				}
			bool? requiredRoot = request.Intensity ? ExportFormats.IntensityRootFor(format) : null;
			SimulationRun? run = _run;
			bool usable = run != null && !_runStale && !_probesStale && run.State is not (RunState.Cancelled or RunState.Failed)
				&& run.Spec.Quality.IsFull && (requiredRoot == null || run.Store.IntensityRoot == requiredRoot);
			if (!usable)
			{
				SimulationRun? previous = run;
				if (_requested == null) RequestPreview(_folder, resetPosition: false);
				await StartRun(automatic: true, qualityOverride: PreviewQuality.Full with { IntensityRoot = requiredRoot ?? true });
				run = _run;
				if (run == null || ReferenceEquals(run, previous) || _runStale || _probesStale || generation != _editGeneration) return;
			}
			if (run == null) return;
			if (!run.IsFinished)
			{
				var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				void OnFinished(object? sender, EventArgs e) => finished.TrySetResult();
				run.Finished += OnFinished;
				try { if (!run.IsFinished) await finished.Task; }
				finally { run.Finished -= OnFinished; }
			}
			if (_disposed || generation != _editGeneration || !ReferenceEquals(run, _run) || run.State != RunState.Completed || !run.Spec.Quality.IsFull || requiredRoot != null && run.Store.IntensityRoot != requiredRoot) return;
			int every = run.Spec.VisualizeEvery, lastFrame = run.Spec.FrameCount - 1;
			int first = request.Still ? Math.Clamp(stillFrame, 0, lastFrame) : Math.Clamp(Project.Display.ExportFrom / every, 0, lastFrame);
			int last = request.Still ? first : Project.Display.ExportTo <= 0 ? lastFrame : Math.Clamp(Project.Display.ExportTo / every, first, lastFrame);
			ScenePixels? scene = SceneComposer.Compose(Project, _folder);
			byte[]? edges = scene != null && scene.Width == run.Spec.Width && scene.Height == run.Spec.Height && (Project.Display.Field.OverlayInExport || Project.Display.Intensity.OverlayInExport) ? GlowingEdges.Compute(scene.Bgra, scene.Width, scene.Height, scene.StrideBytes) : null;
			var decorations = new ExportDecorations(Project, run);
			var schemes = Project.ColorSchemes.Concat(ColorSchemeStore.LoadBuiltIn(null)).ToArray();
			var targets = new List<ExportTarget>();
			foreach (var output in outputs)
			{
				DisplayLayer layer = output.Layer == FrameLayer.Field ? Project.Display.Field : Project.Display.Intensity;
				ColorScheme? scheme = ColorSchemeStore.Find(schemes, layer.ColorScheme);
				ColorLut lut = scheme == null ? ColorLut.Grey : ColorLut.Build(scheme, layer.BlackLevel);
				var render = new ExportLayer(output.Layer, lut, (float)layer.Brightness, layer.OverlayInExport ? edges : null, layer.OverlayGain, decorations.For(output.Layer == FrameLayer.Field ? ProbeLayer.Field : ProbeLayer.Intensity));
				int width = run.Spec.Width, height = run.Spec.Height, fps = Project.Display.Video.Fps, quality = Project.Display.Video.Quality;
				string path = output.Path;
				Func<IFrameSink> open = request.Still ? () => ExportFormats.OpenStill(format, path, width, height, quality) : () => ExportFormats.Open(format, path, width, height, fps, quality, last - first + 1, first);
				targets.Add(new ExportTarget(render, !request.Still && ExportFormats.IsSequence(format) ? ExportFormats.SequenceFolder(path) : path, open));
			}
			var job = new ExportJob(run.Store, first, last, targets, SynchronizationContext.Current);
			_exportJob = job;
			void OnProgress(object? sender, PropertyChangedEventArgs e)
			{
				if (e.PropertyName == nameof(ExportJob.FramesDone) && ReferenceEquals(job, _exportJob) && Application.Current?.Resources.TryGetValue("ExportStatusFormat", out object? value) == true && value is string formatText)
					Status = string.Format(CultureInfo.CurrentCulture, formatText, job.FramesDone, job.FrameCount);
			}
			job.PropertyChanged += OnProgress;
			var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			job.Finished += (_, _) => done.TrySetResult();
			job.Start();
			((ControlCommand)CancelExportCommand).Refresh();
			await done.Task;
			job.PropertyChanged -= OnProgress;
			if (job.State == ExportState.Failed) throw job.Error ?? new IOException("The export failed.");
			Status = job.State.ToString();
			LogManager.GetCurrentClassLogger().Info("Export {State}: {Count} frames to {Outputs}", job.State, job.FramesDone, string.Join(", ", targets.Select(target => target.Output)));
		}
		catch (Exception ex) { LogManager.GetCurrentClassLogger().Error(ex, "The export failed"); Status = ex.Message; }
		finally { _exportJob = null; ((ControlCommand)CancelExportCommand).Refresh(); }
	}


	private void RequestPreview(string? folder, bool resetPosition = true)
	{
		ScenePixels? scene = SceneComposer.Compose(Project, folder);
		RunSpec spec = RunSpec.Create(Project, scene, preferGpu: UseGpu, trackIntensity: true, quality: _quality, preferVulkan: true);
		_requested = spec;
		_canvasWidth = spec.Width;
		_canvasHeight = spec.Height;
		Changed(nameof(CanvasWidth));
		Changed(nameof(CanvasHeight));
		QueueOutline(scene);
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
		if (_run is { IsActive: true, IsPauseRequested: false } running && _pausedForScratch == null)
		{
			running.Pause();
			_pausedForScratch = running;
		}
		_scratchWaiting = true;
		_scratchGeneration = _editGeneration;
		_scratch.Request(spec, (int)_frame);
	}

	private void ScratchCompleted(object? sender, ScratchResult result)
	{
		if (_disposed || _scratchGeneration != _editGeneration || !ReferenceEquals(result.Spec, _requested) || result.Frame != (int)Frame)
		{
			if (!_disposed && _probesStale && !IsPlaying && _scratchGeneration == _editGeneration && ReferenceEquals(result.Spec, _requested) && result.Frame != (int)Frame) ScheduleScratch();
			return;
		}
		_scratchWaiting = false;
		ResumeAfterScratch();
		if (_run != null && !_runStale)
		{
			_probeScratchResult = result;
			ProbeGraphsChanged?.Invoke(this, EventArgs.Empty);
			return;
		}
		_lastScratch = new CopiedFrameSource(result.Snapshot);
		_lastScratchResult = result;
		_lastScratchSpec = result.Spec;
		_lastScratchFrame = result.Frame;
		Present(_lastScratch, 0);
	}

	private void ResumeAfterScratch()
	{
		SimulationRun? paused = _pausedForScratch;
		_pausedForScratch = null;
		if (paused != null && ReferenceEquals(paused, _run) && paused.IsActive) paused.Resume();
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

	private void QueueOutline(ScenePixels? scene)
	{
		_scenePixels = scene;
		_pendingOutline = new OutlineJob(++_outlineVersion, scene, ReferenceEquals(scene, _edgeCacheScene) ? _edgeCache : null,
			Project.Display.Field.Overlay, Project.Display.Field.OverlayGain, Project.Display.Intensity.Overlay, Project.Display.Intensity.OverlayGain);
		if (!_outlining) DrainOutline();
	}

	private async void DrainOutline()
	{
		_outlining = true;
		while (_pendingOutline is { } job)
		{
			_pendingOutline = null;
			try
			{
				OutlineResult result = await Task.Run(() => RenderOutline(job));
				if (_disposed || job.Version != _outlineVersion) continue;
				_edgeCacheScene = job.Scene;
				_edgeCache = result.Edges;
				ShowOutlines(job.Scene, result.Field, result.Intensity);
			}
			catch (Exception ex)
			{
				LogManager.GetCurrentClassLogger().Error(ex, "Scene outline rendering failed");
				if (!_disposed && job.Version == _outlineVersion) Status = ex.Message;
			}
		}
		_outlining = false;
	}

	private static OutlineResult RenderOutline(OutlineJob job)
	{
		if (job.Scene is not { } scene) return new(null, null, null);
		byte[] edges = job.Edges ?? GlowingEdges.Compute(scene.Bgra, scene.Width, scene.Height, scene.StrideBytes);
		static byte[]? Layer(byte[] edges, bool enabled, double gain)
		{
			if (!enabled || gain <= 0) return null;
			var pixels = new byte[edges.Length];
			for (int i = 0; i < pixels.Length; i += 4)
			{
				int b = edges[i], g = edges[i + 1], r = edges[i + 2];
				int max = Math.Max(b, Math.Max(g, r));
				if (max == 0) continue;
				pixels[i] = (byte)(b * 255 / max);
				pixels[i + 1] = (byte)(g * 255 / max);
				pixels[i + 2] = (byte)(r * 255 / max);
				pixels[i + 3] = (byte)Math.Min(255.0, max * gain);
			}
			return pixels;
		}
		return new(edges, Layer(edges, job.FieldEnabled, job.FieldGain), Layer(edges, job.IntensityEnabled, job.IntensityGain));
	}

	private void ShowOutlines(ScenePixels? scene, byte[]? fieldPixels, byte[]? intensityPixels)
	{
		WriteableBitmap? MakePicture(byte[]? pixels)
		{
			if (scene == null || pixels == null) return null;
			var bitmap = new WriteableBitmap(new PixelSize(scene.Width, scene.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
			using var locked = bitmap.Lock();
			int stride = checked(scene.Width * 4);
			for (int y = 0; y < scene.Height; y++) Marshal.Copy(pixels, y * stride, IntPtr.Add(locked.Address, y * locked.RowBytes), stride);
			return bitmap;
		}
		WriteableBitmap? field = MakePicture(fieldPixels);
		WriteableBitmap? intensity = MakePicture(intensityPixels);
		WriteableBitmap? oldField = FieldOverlay;
		WriteableBitmap? oldIntensity = IntensityOverlay;
		FieldOverlay = field;
		IntensityOverlay = intensity;
		oldField?.Dispose();
		oldIntensity?.Dispose();
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
				{
					ShowPictures(job.Source.Width, job.Source.Height, pictures.Field, pictures.Intensity);
					_shownSource = job.Source;
					_shownFrame = job.Frame;
					_shownScratchResult = ReferenceEquals(job.Source, _lastScratch) ? _lastScratchResult : null;
					ProbeGraphsChanged?.Invoke(this, EventArgs.Empty);
				}
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
		_recordDelay?.Stop();
		_recordPending = false;
		DisarmImageWatch();
		_watcher?.Dispose();
		SelectedNode = null;
		Project = project;
		_history = new ProjectHistory(project);
		RefreshHistoryCommands();
		Changed(nameof(HasEmbeddedImage));
		_canvasWidth = 0;
		_canvasHeight = 0;
		Changed(nameof(CanvasWidth));
		Changed(nameof(CanvasHeight));
		Changed(nameof(EdgeBrush));
		_outlineVersion++;
		_pendingOutline = null;
		_scenePixels = null;
		_edgeCacheScene = null;
		_edgeCache = null;
		_watcher = new ProjectWatcher(project);
		_watcher.Changed += ProjectChanged;
		RefreshTree();
	}

	private void LocateSceneImage()
	{
		Scene scene = Project.Scene;
		if (_folder == null || scene.ImageData == null || scene.ImagePath.Length != 0 || scene.Image.Length == 0) return;
		foreach (string subfolder in new[] { "", "scene_images" })
		{
			string path = Path.Combine(_folder, subfolder, scene.Image);
			if (!File.Exists(path)) continue;
			scene.ImagePath = Scene.StoredImagePath(_folder, path);
			return;
		}
	}

	private void DisarmImageWatch()
	{
		_imageGeneration++;
		_imageDelay?.Stop();
		_imageWatcher?.Dispose();
		_imageWatcher = null;
	}

	private void ArmImageWatch()
	{
		DisarmImageWatch();
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(_folder);
		SceneImageFileMissing = path.Length > 0 && scene.ImageData != null && !File.Exists(path);
		if (path.Length == 0 || scene.ImageData == null) return;
		string? folder = Path.GetDirectoryName(path);
		if (folder == null || !Directory.Exists(folder)) return;
		try
		{
			var watcher = new FileSystemWatcher(folder, Path.GetFileName(path))
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
			};
			watcher.Changed += ImageFileChanged;
			watcher.Created += ImageFileChanged;
			watcher.Deleted += ImageFileChanged;
			watcher.Renamed += ImageFileChanged;
			_imageWatcher = watcher;
			watcher.EnableRaisingEvents = true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			LogManager.GetCurrentClassLogger().Debug(ex, "Cannot watch scene image '{Path}'", path);
		}
	}

	private void ImageFileChanged(object sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() =>
	{
		if (_disposed || !ReferenceEquals(sender, _imageWatcher)) return;
		_imageDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
		_imageDelay.Tick -= OnImageDelay;
		_imageDelay.Tick += OnImageDelay;
		_imageDelay.Stop();
		_imageDelay.Start();
	});

	private async void OnImageDelay(object? sender, EventArgs e)
	{
		_imageDelay!.Stop();
		await ReloadSceneImage(_imageGeneration);
	}

	private async Task ReloadSceneImage(int generation)
	{
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(_folder);
		if (path.Length == 0 || scene.ImageData == null) return;
		SceneImageFileMissing = !File.Exists(path);
		if (SceneImageFileMissing) return;
		try
		{
			byte[]? bytes = null;
			for (int attempt = 0; attempt < 8; attempt++)
			{
				try { bytes = await File.ReadAllBytesAsync(path); break; }
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(100); }
			}
			if (_disposed || generation != _imageGeneration || bytes == null || bytes.AsSpan().SequenceEqual(scene.ImageData)) return;
			using var stream = new MemoryStream(bytes, writable: false);
			ScenePixels pixels = SceneComposer.Decode(stream);
			scene.ImageData = bytes;
			scene.Image = Path.GetFileName(path);
			LogManager.GetCurrentClassLogger().Info("Scene image re-embedded from '{Path}': {Width}×{Height}", path, pixels.Width, pixels.Height);
		}
		catch (Exception ex) { LogManager.GetCurrentClassLogger().Warn(ex, "Cannot reload scene image '{Path}'", path); }
	}

	internal void RefreshTree()
	{
		object? selected = SelectedNode?.Tag;
		TreeViewItem Group(string key, object? value, IEnumerable<object> children)
		{
			var item = new TreeViewItem { Tag = value, ItemsSource = children.Select(child =>
			{
				var row = new TreeViewItem { Tag = child };
				row.Bind(HeaderedItemsControl.HeaderProperty, new Binding("Name") { Source = child });
				if (child is Probe probe) row.ContextMenu = ProbeCsvMenu(probe);
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

	internal ContextMenu ProbeCsvMenu(Probe probe)
	{
		var export = new MenuItem { Command = ExportProbeCsvCommand, CommandParameter = probe };
		export.Bind(HeaderedItemsControl.HeaderProperty, export.GetResourceObservable("MenuExportProbeCsv"));
		AutomationProperties.SetAutomationId(export, "ContextExportProbeCsv");
		return new ContextMenu { Items = { export } };
	}

	private (int X, int Y) CanvasCentre() => ((_canvasWidth > 0 ? _canvasWidth : Project.Scene.Width) / 2, (_canvasHeight > 0 ? _canvasHeight : Project.Scene.Height) / 2);

	internal void SelectItem(object? value)
	{
		int version = ++_selectionVersion;
		if (value == null) { SelectedNode = null; return; }
		foreach (TreeViewItem group in Nodes)
		{
			if (ReferenceEquals(group.Tag, value)) { group.IsSelected = true; SelectedNode = group; return; }
			if (group.ItemsSource is not IEnumerable<TreeViewItem> children) continue;
			foreach (TreeViewItem row in children)
			{
				if (!ReferenceEquals(row.Tag, value)) continue;
				group.IsExpanded = true;
				if (!row.IsAttachedToVisualTree())
				{
					// The tree replaces its containers on a collection edit. Selecting before the new row
					// joins the tree is lost when the old selection is detached; wait for attachment.
					EventHandler<VisualTreeAttachmentEventArgs>? attached = null;
					attached = (_, _) =>
					{
						row.AttachedToVisualTree -= attached;
						Dispatcher.UIThread.Post(() =>
						{
							if (_disposed || version != _selectionVersion || !row.IsAttachedToVisualTree()) return;
							row.IsSelected = true;
							SelectedNode = row;
						}, DispatcherPriority.Background);
					};
					row.AttachedToVisualTree += attached;
				}
				else { row.IsSelected = true; SelectedNode = row; }
				return;
			}
		}
		SelectedNode = null;
	}

	internal void RemoveSelected()
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
		SceneVisualChanged?.Invoke(this, change);
		if (change.Item is Probe && change.Property == nameof(Probe.Active) || change.Item is Simulation && change.Property == nameof(Simulation.Steps)) ((AsyncParameterCommand)ExportProbeCsvCommand).Refresh();
		if (change.Item is Scene)
		{
			if (change.Property is nameof(Scene.Edges) or nameof(Scene.HasRim) or nameof(Scene.DampingEdge)) Changed(nameof(EdgeBrush));
			if (change.Property == nameof(Scene.ImageData)) Changed(nameof(HasEmbeddedImage));
			((ControlCommand)RemoveImageCommand).Refresh();
			if (change.Property is nameof(Scene.ImagePath) or nameof(Scene.ImageData)) ArmImageWatch();
		}
		if (change.Item is INotifyCollectionChanged) RefreshTree();
		if (!_saving)
		{
			_dirty = true;
			UpdateTitle();
			if (!_restoring) ScheduleHistoryRecord();
		}
		if (change.Item is Source && change.Property == nameof(Source.Array) && ReferenceEquals(SelectedNode?.Tag, change.Item))
		{
			object selected = change.Item;
			Dispatcher.UIThread.Post(() => { if (!_disposed && ReferenceEquals(SelectedNode?.Tag, selected)) PropertyRows = BuildPropertyRows(selected); });
		}
		bool bakedBrightness = change.Item is DisplayLayer && change.Property == nameof(DisplayLayer.Brightness) && _quality.Format == FrameFormat.Index;
		bool unrecordedProbe = change.AffectsProbes && _run is { } run && Project.Probes.Any(probe => probe.Active && !run.Spec.Probes.Any(spec => ReferenceEquals(spec.Origin, probe)));
		if (change.AffectsProbes && !unrecordedProbe && !change.AffectsSimulation && !bakedBrightness)
		{
			_probesStale |= _run != null;
			_probeScratchResult = null;
			_lastEdit = change;
			_editGeneration++;
			if (!IsPlaying) ScheduleScratch();
			return;
		}
		if (!change.AffectsSimulation && !bakedBrightness && !unrecordedProbe)
		{
			if (change.Item is DisplayLayer && change.Property is nameof(DisplayLayer.Overlay) or nameof(DisplayLayer.OverlayGain)) QueueOutline(_scenePixels);
			if (change.Item is DisplayLayer or ColorScheme || ReferenceEquals(change.Item, Project.ColorSchemes)) RefreshDisplayedFrame();
			return;
		}
		InvalidateSimulation(change);
	}

	private void SetQuality(PreviewQuality quality)
	{
		if (quality == _quality) return;
		_quality = quality;
		_settings.Quality = quality;
		_settings.Save();
		NotifyQuality();
		InvalidateSimulation(null);
	}

	private void NotifyQuality()
	{
		Changed(nameof(PreviewResolution));
		Changed(nameof(PreviewFrames));
		Changed(nameof(PreviewSampleBytes));
	}

	private void InvalidateSimulation(ProjectChange? change)
	{
		CancelFit();
		_lastEdit = change;
		_editGeneration++;
		_runStale |= _run != null;
		StopPlayback();
		_following = false;
		_run?.Stop();
		_pausedForScratch = null;
		_requested = null;
		_scratchWaiting = false;
		InvalidatePresentation();
		ScheduleScratch();
		if (_runStale && AutoRerun) ScheduleRerun();
	}

	private void ScheduleScratch()
	{
		_scratchDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
		_scratchDelay.Tick -= OnScratchDelay;
		_scratchDelay.Tick += OnScratchDelay;
		_scratchDelay.Stop();
		_scratchDelay.Start();
	}

	private void FitBrightness(FrameLayer layer)
	{
		if (_disposed || _fitRunner != null || _requested == null) return;
		try
		{
			if (_shownSource is { Format: FrameFormat.Half } source && source.Has(layer) && _shownFrame < source.Count)
			{
				var samples = new Half[checked(source.Width * source.Height)];
				if (source.CopyLayer(_shownFrame, layer, samples))
				{
					ApplyBrightness(layer, BrightnessFit.For(samples, source.KindOf(layer)));
					return;
				}
			}
			RunSpec spec = RunSpec.Create(Project, SceneComposer.Compose(Project, _folder), preferGpu: UseGpu, trackIntensity: layer == FrameLayer.Intensity, quality: _quality with { Format = FrameFormat.Half }, preferVulkan: true);
			if (_run is { IsActive: true, IsPauseRequested: false } run) { run.Pause(); _pausedForFit = run; }
			_fitLayer = layer;
			_fitGeneration = _editGeneration;
			_fitRunner = new ScratchRunner(SynchronizationContext.Current ?? throw new InvalidOperationException("No UI synchronisation context is available."));
			_fitRunner.Completed += FitCompleted;
			_fitRunner.Request(spec, Math.Clamp((int)Frame, 0, spec.FrameCount - 1));
		}
		catch (Exception ex) { CancelFit(); LogManager.GetCurrentClassLogger().Warn(ex, "Could not fit brightness"); Status = ex.Message; }
	}

	private void FitCompleted(object? sender, ScratchResult result)
	{
		if (_disposed || !ReferenceEquals(sender, _fitRunner)) return;
		IFrameSource source = new SnapshotFrameSource(result.Snapshot);
		var samples = new Half[checked(source.Width * source.Height)];
		double? value = source.Has(_fitLayer) && source.CopyLayer(0, _fitLayer, samples) ? BrightnessFit.For(samples, source.KindOf(_fitLayer)) : null;
		FrameLayer layer = _fitLayer;
		bool current = _fitGeneration == _editGeneration;
		CancelFit();
		if (current) ApplyBrightness(layer, value);
	}

	private void CancelFit()
	{
		ScratchRunner? runner = _fitRunner;
		_fitRunner = null;
		if (runner != null)
		{
			runner.Completed -= FitCompleted;
			_ = Task.Run(runner.Dispose);
		}
		if (_pausedForFit is { } paused)
		{
			_pausedForFit = null;
			if (ReferenceEquals(paused, _run) && paused.IsActive) paused.Resume();
		}
	}

	private void ApplyBrightness(FrameLayer layer, double? value)
	{
		if (value is not { } brightness) return;
		DisplayLayer settings = layer == FrameLayer.Field ? Project.Display.Field : Project.Display.Intensity;
		LogManager.GetCurrentClassLogger().Info("Fitted {Layer} brightness: {Old} to {New}", layer, settings.Brightness, brightness);
		settings.Brightness = brightness;
	}

	private void ScheduleHistoryRecord()
	{
		_recordPending = true;
		_recordDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
		_recordDelay.Tick -= OnHistoryDelay;
		_recordDelay.Tick += OnHistoryDelay;
		_recordDelay.Stop();
		_recordDelay.Start();
		RefreshHistoryCommands();
	}

	private void OnHistoryDelay(object? sender, EventArgs e)
	{
		if (!PointerDown) RecordHistory();
	}

	private void RecordHistory()
	{
		_recordDelay?.Stop();
		if (!_recordPending || _history == null) return;
		_recordPending = false;
		try
		{
			_history.Record();
			_dirty = !_history.IsAtSaved;
			UpdateTitle();
		}
		catch (Exception ex) { LogManager.GetCurrentClassLogger().Error(ex, "Could not record the project edit"); Status = ex.Message; }
		RefreshHistoryCommands();
	}

	private void RefreshHistoryCommands()
	{
		((ControlCommand)UndoCommand).Refresh();
		((ControlCommand)RedoCommand).Refresh();
	}

	private void MoveHistory(bool redo)
	{
		RecordHistory();
		if (_history == null) return;
		object? previous = SelectedNode?.Tag;
		IList? list = previous switch { Source => Project.Sources, OpticalPart => Project.Parts, Probe => Project.Probes, ColorScheme => Project.ColorSchemes, _ => null };
		int index = previous != null && list != null ? list.IndexOf(previous) : -1;
		string? name = (previous as WaveDudeNext.Model.INamed)?.Name;
		try
		{
			_restoring = true;
			if (!(redo ? _history.Redo() : _history.Undo())) return;
		}
		catch (Exception ex) { LogManager.GetCurrentClassLogger().Error(ex, "Could not restore a project edit"); Status = ex.Message; return; }
		finally { _restoring = false; }
		RefreshTree();
		object? restored = previous switch
		{
			Scene => Project.Scene,
			Simulation => Project.Simulation,
			Display => Project.Display,
			_ when list != null => list.Cast<object>().FirstOrDefault(item => item is WaveDudeNext.Model.INamed named && named.Name == name) ?? (index >= 0 && index < list.Count ? list[index] : null),
			_ => null,
		};
		if (restored != null) SelectItem(restored);
		else SelectedNode = null;
		ArmImageWatch();
		_dirty = !_history.IsAtSaved;
		UpdateTitle();
		RefreshHistoryCommands();
	}

	private void RefreshDisplayedFrame()
	{
		if (_run is { } run && !_runStale && Frame < run.FrameCount) Present(run.Frames, (int)Frame);
		else if (_lastScratch != null && ReferenceEquals(_lastScratchSpec, _requested) && _lastScratchFrame == (int)Frame) Present(_lastScratch, 0);
	}

	private async void OnScratchDelay(object? sender, EventArgs e)
	{
		_scratchDelay!.Stop();
		int generation = _editGeneration;
		if (_runStale && _run is { IsActive: true } run) await run.Completion;
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
		if (_disposed || !_runStale || !AutoRerun) return;
		if (_scratchWaiting || _scratchDelay?.IsEnabled == true || _requested == null)
		{
			ScheduleRerun();
			return;
		}
		await StartRun(automatic: true);
	}

	private IReadOnlyList<Control> BuildPropertyRows(object? item)
	{
		if (item == null) return [];
		var rows = new List<Control>();
		foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(item))
		{
			Type type = property.PropertyType;
			if (!property.IsBrowsable) continue;
			var label = new TextBlock { Text = property.DisplayName, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
			string key = $"Label.{property.ComponentType.Name}.{property.Name}";
			if (Application.Current?.Resources.ContainsKey(key) == true) label.Bind(TextBlock.TextProperty, label.GetResourceObservable(key));
			if (type == typeof(DisplayLayer) || type == typeof(Video) || type == typeof(SourceArray))
			{
				if (property.GetValue(item) is { } nested)
				{
					var panel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
					foreach (Control child in BuildPropertyRows(nested)) panel.Children.Add(child);
					rows.Add(new Expander { Header = label, Content = panel, IsExpanded = true });
				}
				continue;
			}
			if (!(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))) continue;
			Control value;
			var binding = new Binding(property.Name) { Source = item, Mode = BindingMode.TwoWay };
			if (item is Scene && property.Name == nameof(Scene.Image))
			{
				var imageName = new TextBlock();
				imageName.Bind(TextBlock.TextProperty, new Binding(property.Name) { Source = item });
				var browse = new Button { Content = "…", Command = BrowseImageCommand };
				browse.Bind(ToolTip.TipProperty, browse.GetResourceObservable("TipBrowseSceneImage"));
				AutomationProperties.SetAutomationId(browse, "SceneImageBrowse");
				var save = new Button { Content = "↓", Command = SaveImageCommand };
				save.Bind(InputElement.IsEnabledProperty, new Binding(nameof(HasEmbeddedImage)) { Source = this });
				save.Bind(ToolTip.TipProperty, save.GetResourceObservable("TipSaveSceneImage"));
				AutomationProperties.SetAutomationId(save, "SceneImageSave");
				var remove = new Button { Content = "×", Command = RemoveImageCommand };
				remove.Bind(ToolTip.TipProperty, remove.GetResourceObservable("TipRemoveSceneImage"));
				AutomationProperties.SetAutomationId(remove, "SceneImageRemove");
				value = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Children = { imageName, browse, save, remove } };
			}
			else if (item is Scene && property.Name == nameof(Scene.ImagePath))
			{
				var text = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
				text.Bind(TextBlock.TextProperty, new Binding(property.Name) { Source = item, Mode = BindingMode.OneWay });
				var missing = new TextBlock();
				missing.Bind(TextBlock.TextProperty, missing.GetResourceObservable("SceneImageNotFound"));
				missing.Bind(Visual.IsVisibleProperty, new Binding(nameof(SceneImageFileMissing)) { Source = this });
				value = new StackPanel { Children = { text, missing } };
			}
			else if (!property.IsReadOnly && type == typeof(bool))
			{
				var check = new CheckBox();
				check.Bind(ToggleButton.IsCheckedProperty, binding);
				value = check;
			}
			else if (!property.IsReadOnly && type.IsEnum)
			{
				var choice = new ComboBox
				{
					ItemsSource = Enum.GetValues(type),
					ItemTemplate = new FuncDataTemplate<object>((value, _) =>
					{
						var text = new TextBlock { Text = value?.ToString() };
						string enumKey = $"Enum.{type.Name}.{value}";
						if (Application.Current?.Resources.ContainsKey(enumKey) == true) text.Bind(TextBlock.TextProperty, text.GetResourceObservable(enumKey));
						return text;
					}),
				};
				choice.Bind(SelectingItemsControl.SelectedItemProperty, binding);
				value = choice;
			}
			else if (!property.IsReadOnly && type == typeof(string))
			{
				var text = new TextBox();
				text.Bind(TextBox.TextProperty, binding);
				value = text;
			}
			else if (!property.IsReadOnly && IsNumber(type))
			{
				(decimal minimum, decimal maximum) = NumericBounds(type, property.Attributes[typeof(RangeAttribute)] as RangeAttribute);
				var number = new NumericUpDown
				{
					Minimum = minimum,
					Maximum = maximum,
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
			if (Application.Current?.Resources.ContainsKey(key) == true) value.Bind(AutomationProperties.NameProperty, value.GetResourceObservable(key));
			string hintKey = $"Hint.{property.ComponentType.Name}.{property.Name}";
			if (Application.Current?.Resources.ContainsKey(hintKey) == true)
			{
				label.Bind(ToolTip.TipProperty, label.GetResourceObservable(hintKey));
				value.Bind(ToolTip.TipProperty, value.GetResourceObservable(hintKey));
			}
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

	private static (decimal Minimum, decimal Maximum) NumericBounds(Type type, RangeAttribute? range)
	{
		if (range != null) return (Convert.ToDecimal(range.Minimum, CultureInfo.InvariantCulture), Convert.ToDecimal(range.Maximum, CultureInfo.InvariantCulture));
		return Type.GetTypeCode(type) switch
		{
			TypeCode.Byte => (byte.MinValue, byte.MaxValue),
			TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
			TypeCode.Int16 => (short.MinValue, short.MaxValue),
			TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
			TypeCode.Int32 => (int.MinValue, int.MaxValue),
			TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
			TypeCode.Int64 => (long.MinValue, long.MaxValue),
			TypeCode.UInt64 => (ulong.MinValue, ulong.MaxValue),
			_ => (decimal.MinValue, decimal.MaxValue),
		};
	}
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

/// <summary>A scene outline request captured on the UI thread.</summary>
internal sealed record OutlineJob(long Version, ScenePixels? Scene, byte[]? Edges, bool FieldEnabled, double FieldGain, bool IntensityEnabled, double IntensityGain);

/// <summary>Computed edge values and the two translucent layers.</summary>
internal sealed record OutlineResult(byte[]? Edges, byte[]? Field, byte[]? Intensity);

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
internal sealed class OpenCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
	private bool _running;
	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged;
	/// <inheritdoc/>
	public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);
	/// <inheritdoc/>
	public async void Execute(object? parameter)
	{
		if (!CanExecute(parameter)) return;
		_running = true;
		CanExecuteChanged?.Invoke(this, EventArgs.Empty);
		try { await execute(); }
		finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
	}
	/// <summary>Notifies controls that the command's availability changed.</summary>
	public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>An asynchronous parameterised command that admits one invocation at a time.</summary>
internal sealed class AsyncParameterCommand(Func<object?, Task> execute, Func<object?, bool> canExecute) : ICommand
{
	private bool _running;
	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged;
	/// <inheritdoc/>
	public bool CanExecute(object? parameter) => !_running && canExecute(parameter);
	/// <inheritdoc/>
	public async void Execute(object? parameter)
	{
		if (!CanExecute(parameter)) return;
		_running = true;
		Refresh();
		try { await execute(parameter); }
		finally { _running = false; Refresh(); }
	}
	/// <summary>Notifies controls that the command's availability changed.</summary>
	public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
