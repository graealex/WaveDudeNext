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

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using WaveDudeNext.App.Commands;
using WaveDudeNext.App.Help;
using WaveDudeNext.App.Localization;
using WaveDudeNext.App.Theming;
using WaveDudeNext.UI.Theming;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;

namespace WaveDudeNext.App;

/// <summary>
/// The commands, each a line into a model, as the WPF window's command handlers are (its routed commands and
/// CommandBindings; Avalonia has no routing, so they are <see cref="RelayCommand"/>s on <see cref="Commands"/>, which the
/// menus bind in the markup and the panes through their Commands property). Every command counts as an edit boundary
/// before and after it (<see cref="Platform.EditBoundaries.Command"/>). The menus whose entries come from data (recent
/// files, examples, panes, theme, zoom, language, the preview quality, the RAM cache) are filled here in code.
/// </summary>
public partial class MainWindow
{
	/// <summary>The choices of Simulation > RAM cache size: automatic and 10 % to 100 % of the RAM as an upper bound (2026-10-15, the user's design).</summary>
	public static int[] FrameMemoryOptions { get; } = [FrameMemory.Automatic, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

	private ManualWindow? _manual;

	/// <summary>Where on the shown canvas a new item lands: its centre (2026-10-26, Jeroen's report).</summary>
	private (int X, int Y) CanvasCentre => (Preview.CanvasWidth / 2, Preview.CanvasHeight / 2);

	/// <summary>Creates every command with its CanExecute, the WPF window's rules.</summary>
	private void WireCommands()
	{
		AppCommands c = Commands;
		// the project's objects (the WPF ProjectCommands): the parameter is the tree node acted on, or null for the selection
		c.AddSource = Command(_ => _items.AddSource(CanvasCentre.X, CanvasCentre.Y));
		c.AddPart = Command(parameter =>
		{
			if (parameter is Type kind)
			{
				_items.AddPart(kind, CanvasCentre.X, CanvasCentre.Y);
			}
		});
		c.AddProbe = Command(parameter => _items.AddProbe(CanvasCentre.X, CanvasCentre.Y, parameter is ProbeDetector detector ? detector : ProbeDetector.Standard));
		c.Add = Command(parameter =>
		{
			if (Selection.Target(parameter) is NamedCollection folder && !_items.HasKinds(folder.Items))
			{
				_items.AddTo(folder.Items, CanvasCentre.X, CanvasCentre.Y);
			}
		}, parameter => Selection.Target(parameter) is NamedCollection folder && _items.IsList(folder.Items));
		c.Duplicate = Command(_items.Duplicate, _items.CanEdit);
		c.Remove = Command(_items.Remove, _items.CanEdit);
		c.MoveUp = Command(parameter => _items.Move(parameter, -1), parameter => _items.CanMove(parameter, -1));
		c.MoveDown = Command(parameter => _items.Move(parameter, +1), parameter => _items.CanMove(parameter, +1));
		c.ToggleEnabled = Command(_items.ToggleEnabled, _items.CanToggleEnabled);
		c.ToggleSelected = Command(parameter =>
		{
			if (Selection.Target(parameter) is { } item && Selection.IsGroupable(item))
			{
				Selection.Toggle(item);
			}
		}, parameter => Selection.IsGroupable(Selection.Target(parameter)));
		c.SelectAll = Command(_ => Selection.SelectAll(Document.Project), _ => Selection.Groupable(Document.Project).Any());
		c.Cut = Command(_clipboard.Cut, _clipboard.CanCut);
		c.Copy = Command(_clipboard.Copy, _clipboard.CanCopy);
		c.Paste = Command(_clipboard.Paste, _clipboard.CanPaste);
		c.Delete = Command(_items.Remove, _items.CanEdit);
		c.ExportProbeCsv = Command(parameter => Fire(parameter is Probe probe ? Exporter.ExportProbeAsync(probe) : Task.CompletedTask), Exporter.CanExportProbe);
		c.BrowseSceneImage = Command(_ => Fire(BrowseSceneImageAsync()));
		c.SaveSceneImage = Command(_ => Fire(SaveSceneImageAsync()), _ => SceneImage.CanSave);
		c.RemoveSceneImage = Command(_ => SceneImage.Remove(), _ => SceneImage.CanRemove);
		c.RemoveStop = Command(parameter =>
		{
			if (parameter is ColorStop stop)
			{
				Document.Project.ColorSchemes.FirstOrDefault(scheme => scheme.Stops.Contains(stop))?.RemoveStop(stop);
			}
		});
		// the run (the WPF SimulationCommands)
		c.Run = Command(_ => Fire(RunAsync()), _ => !RunHost.IsActive && !RunHost.IsStarting && !Busy.IsExporting);
		c.TogglePause = Command(_ => RunHost.TogglePause(), _ => RunHost.IsActive);
		c.Stop = Command(_ => StopRunOrExport(), _ => RunHost.IsActive || Exporter.IsWriting);
		c.EnsureRun = Command(_ => Fire(Live.EnsureRunAsync()));
		c.PlayPause = Command(_ => Preview.IsPlaying = !Preview.IsPlaying, _ => previewPane.LastFrame > 0);
		// display and export
		c.AutoBrightness = Command(parameter =>
		{
			if (parameter is ProbeLayer layer)
			{
				AutoBrightness.Fit(layer);
			}
		}, parameter => parameter is ProbeLayer && AutoBrightness.CanFit);
		c.ExportField = Command(_ => Fire(Exporter.ExportAsync(field: true, intensity: false)), _ => Exporter.CanExport);
		c.ExportIntensity = Command(_ => Fire(Exporter.ExportAsync(field: false, intensity: true)), _ => Exporter.CanExport);
		c.ExportBoth = Command(_ => Fire(Exporter.ExportAsync(field: true, intensity: true)), _ => Exporter.CanExport);
		c.ExportFrame = Command(parameter => Fire(Exporter.ExportAsync(field: parameter is ProbeLayer.Field, intensity: parameter is ProbeLayer.Intensity, still: true)), parameter => parameter is ProbeLayer && Exporter.CanExport);
		c.ExportLast = Command(_ => Fire(Exporter.ExportLastAsync()), _ => Exporter.CanExportLast);
		// editing
		c.Undo = Command(_ => _history.Undo(), _ => _history.CanUndo);
		c.Redo = Command(_ => _history.Redo(), _ => _history.CanRedo);
		// the window's own: the file menu, Help
		c.New = Command(_ => Fire(NewAsync()));
		c.Open = Command(_ => Fire(OpenAsync()));
		c.Save = Command(_ => Fire(SaveAsync()));
		c.SaveAs = Command(_ => Fire(SaveAsAsync()));
		c.CloseProject = Command(_ => Fire(CloseProjectAsync()), _ => Document.Filename != null);
		c.Exit = Command(_ => Close());
		c.About = Command(_ => Fire(new AboutWindow().ShowDialog(this)));
		c.Manual = Command(_ => ShowManual());
		// the settings menus, bound in the markup as the WPF head's are: each entry's command carries its value, the check
		// marks follow the settings and the controllers through bindings (2026-11-11; the port had filled these menus in
		// code and set their check marks by hand)
		c.SetUiZoom = Command(parameter => SetDoubleSetting(parameter, factor => Settings.Default.UiZoom = factor));
		c.SetPreviewResolution = Command(parameter => SetIntSetting(parameter, divisor => Settings.Default.PreviewResolution = divisor));
		c.SetPreviewFrames = Command(parameter => SetIntSetting(parameter, divisor => Settings.Default.PreviewFrames = divisor));
		c.SetPreviewSamples = Command(parameter => SetIntSetting(parameter, bytes => Settings.Default.PreviewSampleBytes = bytes));
		c.SetPreviewDither = Command(parameter => SetIntSetting(parameter, bits => Settings.Default.PreviewDitherBits = bits));
		c.SetFrameMemory = Command(parameter => SetIntSetting(parameter, percent => Settings.Default.FrameMemoryPercent = percent));
		// the three toggles flip their setting by a command and show it through a one-way mark, as the panes' entries do: a
		// menu item's click runs its command in every path (a pointer, a key, a probe), while whether it flips its own mark
		// is the framework's affair
		c.ToggleUseGpu = Command(_ => ToggleSetting(() => Settings.Default.UseGpu = !Settings.Default.UseGpu));
		c.ToggleAutoRerun = Command(_ => ToggleSetting(() => Settings.Default.AutoRerun = !Settings.Default.AutoRerun));
		c.ToggleMemoryWarning = Command(_ => ToggleSetting(() => Settings.Default.WarnWhenRamInsufficient = !Settings.Default.WarnWhenRamInsufficient));
		c.TogglePane = dockHost.ToggleCommand;
		RegisterHotKeys(menu);
		// Ctrl+Shift+Z is Redo's second gesture (the WPF head has both); the menu shows Ctrl+Y
		KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift), Command = c.Redo });
		// Escape is Stop's second gesture, the usual "stop what you are doing" key and the one without a modifier (the WPF
		// head has both; 2026-11-11, found by RunCheck against this head); the menu shows Shift+F5. A text box lets an
		// Escape through only when it changed nothing (EditBehavior), as in WPF
		KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Escape), Command = c.Stop });
	}

	/// <summary>
	/// The menu entries' HotKeys as key bindings of the window: a HotKey on an entry inside the menu bar registers nothing
	/// before its menu has opened once (the entries join the visual tree with the popup), so every gesture the markup
	/// declares is registered here, at the window, with the entry's command and parameter; the markup stays the one place
	/// that names them.
	/// </summary>
	private void RegisterHotKeys(ItemsControl items)
	{
		foreach (object? entry in items.Items)
		{
			if (entry is not MenuItem item)
			{
				continue;
			}
			if (item.HotKey is { } gesture && item.Command is { } command)
			{
				var binding = new KeyBinding { Gesture = gesture, Command = command };
				if (item.CommandParameter != null)
				{
					binding.CommandParameter = item.CommandParameter;
				}
				KeyBindings.Add(binding);
			}
			RegisterHotKeys(item);
		}
	}

	/// <summary>A command whose action is an edit boundary before and after it, with its CanExecute.</summary>
	private RelayCommand Command(Action<object?> execute, Func<object?, bool>? canExecute = null) => new(_boundaries.Command(execute), canExecute);

	/// <summary>Starts a task from a command: an error the models did not expect reaches the UI thread's handler, as an exception in a handler did.</summary>
	private static async void Fire(Task task) => await task;

	/// <summary>F5: the error of a run the user asked for is shown.</summary>
	private async Task RunAsync()
	{
		try
		{
			await RunHost.StartAsync();
		}
		catch (Exception ex) when (ex is RunSpecException or IOException or UnauthorizedAccessException or NotSupportedException)
		{
			Logger.Error(ex, "The run could not start: {Message}", ex.Message);
			await ShowErrorAsync(Text("RunError"), ex);
		}
	}

	/// <summary>Cancels the export when one is running (it has the user's attention then), else stops the run.</summary>
	private void StopRunOrExport()
	{
		if (Exporter.IsWriting)
		{
			Exporter.Cancel();
		}
		else
		{
			RunHost.Stop();
		}
	}

	private void ShowManual()
	{
		_manual = ManualWindow.ShowOrActivate(this);
		_manual.Closed += (_, _) => _manual = null;
	}

	/// <summary>An export failed: the text of the key, with the error's words under it when there is one.</summary>
	private void OnExportFailed(object? sender, ExportFailure failure) => Fire(ShowErrorAsync(Text(failure.Key), failure.Error));

	// ---- the menus whose entries come from data

	/// <summary>A menu choice carried as a number in the command parameter (an int, or digits as text): stores it and saves the settings.</summary>
	private static void SetIntSetting(object? parameter, Action<int> apply)
	{
		int value;
		if (parameter is int number)
		{
			value = number;
		}
		else if (parameter is not string text || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			return;
		}
		apply(value);
		Settings.Default.TrySave();
	}

	/// <summary>A menu toggle: flips its setting and saves the settings.</summary>
	private static void ToggleSetting(Action flip)
	{
		flip();
		Settings.Default.TrySave();
	}

	/// <summary>A menu choice carried as a factor in the command parameter (a double, or text with a decimal point): stores it and saves the settings.</summary>
	private static void SetDoubleSetting(object? parameter, Action<double> apply)
	{
		double value;
		if (parameter is double number)
		{
			value = number;
		}
		else if (parameter is not string text || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
		{
			return;
		}
		apply(value);
		Settings.Default.TrySave();
	}

	/// <summary>Fills the two menus whose entries are files: the recent files and the examples. The others bind their entries in the markup.</summary>
	private void BuildMenus()
	{
		mnuRecent.SubmenuOpened += (_, _) => FillRecentMenu();
		FillRecentMenu();
		foreach (ExampleEntry example in Examples.All)
		{
			var item = new MenuItem { Header = example.Header, Command = Command(_ => Fire(OpenExampleAsync(example))) };
			// the project's description, as the WPF entries show it (2026-11-10, the user's request; the port had shown the file name)
			ToolTip.SetTip(item, example.Description);
			Avalonia.Automation.AutomationProperties.SetAutomationId(item, example.AutomationId);
			mnuExamples.Items.Add(item);
		}
		ApplyLanguageTexts();
	}

	private void FillRecentMenu()
	{
		mnuRecent.Items.Clear();
		foreach (string path in Settings.Default.RecentFiles)
		{
			mnuRecent.Items.Add(new MenuItem { Header = path, Command = Command(_ => Fire(OpenRecentAsync(path))) });
		}
		mnuRecent.IsEnabled = mnuRecent.Items.Count > 0;
	}

	/// <summary>Re-reads the texts that are not DynamicResources: the dock titles (plain properties).</summary>
	private void ApplyLanguageTexts()
	{
		foreach (Docking.DockItem dock in dockHost.Items)
		{
			dock.Title = Text(dock.Id switch { "project" => "PaneProject", "properties" => "PaneProperties", _ => "PaneMessages" });
		}
	}

	/// <summary>A setting changed, from a menu or the memory warning: logged as the WPF window logs it, the live rules told of a preview quality change.</summary>
	private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
	{
		switch (args.PropertyName)
		{
			case nameof(Settings.UseGpu):
				Logger.Info(Settings.Default.UseGpu ? "Use the GPU switched on (Simulation menu)" : "Use the GPU switched off (Simulation menu): runs use the CPU from now on");
				break;
			case nameof(Settings.WarnWhenRamInsufficient):
				Logger.Info(Settings.Default.WarnWhenRamInsufficient ? "Memory warning switched on (Simulation menu)" : "Memory warning switched off: a run whose frames do not fit in the RAM cache goes to the cache drive without asking");
				break;
			case nameof(Settings.FrameMemoryPercent):
				Logger.Info("RAM cache size: {Share} (Simulation menu or the memory warning)", Settings.Default.FrameMemoryPercent <= FrameMemory.Automatic ? "automatic" : Settings.Default.FrameMemoryPercent + " % of the RAM at most");
				break;
			case nameof(Settings.PreviewResolution) or nameof(Settings.PreviewFrames) or nameof(Settings.PreviewSampleBytes):
				Logger.Info("Preview quality: {Quality} (View menu or the memory warning)", RunHost.PreviewQualitySetting);
				Live.PreviewQualityChanged();
				break;
		}
	}
}
