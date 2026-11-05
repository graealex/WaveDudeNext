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
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>The choice accepted in the memory warning.</summary>
internal sealed record MemoryWarningChoice(PreviewQuality Quality, bool CacheAuto, bool Mute);

/// <summary>Warns before a run exceeds the RAM cache. Used on the UI thread.</summary>
internal sealed class MemoryWarningWindow : Window
{
	/// <summary>Creates the warning with the existing quality and cache choices.</summary>
	public MemoryWarningWindow(RunSpec spec, long budget, long autoBudget, PreviewQuality quality)
	{
		Width = 500;
		SizeToContent = SizeToContent.Height;
		CanResize = false;
		ShowInTaskbar = false;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		AutomationProperties.SetAutomationId(this, "MemoryWarningWindow");
		this.Bind(TitleProperty, this.GetResourceObservable("Title"));
		var model = new MemoryWarningModel(new MemoryWarningPlan(spec, budget, autoBudget, quality), spec.StoreBytes, budget, autoBudget, choice => Close(choice));
		DataContext = model;
		var layout = new StackPanel { Margin = new Thickness(20, 18, 20, 16), Spacing = 8 };
		var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
		message.Bind(TextBlock.TextProperty, new Binding(nameof(MemoryWarningModel.Message)));
		AutomationProperties.SetAutomationId(message, "MemoryWarningText");
		layout.Children.Add(message);
		var choices = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
		choices.Bind(TextBlock.TextProperty, choices.GetResourceObservable("MemoryWarningChoices"));
		layout.Children.Add(choices);
		CheckBox Box(string property, string id, string? resource, bool enabled)
		{
			var box = new CheckBox { IsEnabled = enabled };
			AutomationProperties.SetAutomationId(box, id);
			box.Bind(CheckBox.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay });
			if (resource != null)
			{
				box.Bind(ContentControl.ContentProperty, box.GetResourceObservable(resource));
			}
			return box;
		}
		var cache = Box(nameof(MemoryWarningModel.Cache), "MemoryWarningCache", null, model.Plan.CanUseAuto);
		cache.Bind(ContentControl.ContentProperty, new Binding(nameof(MemoryWarningModel.CacheCaption)));
		var resolution = Box(nameof(MemoryWarningModel.Resolution), "MemoryWarningResolution", "MemoryWarningResolution", model.Plan.CanReduceResolution);
		var frames = Box(nameof(MemoryWarningModel.Frames), "MemoryWarningFrames", "MemoryWarningFrames", model.Plan.CanReduceFrames);
		var samples = Box(nameof(MemoryWarningModel.Samples), "MemoryWarningSamples", "MemoryWarningSamples", model.Plan.CanUseIndices);
		layout.Children.Add(cache);
		layout.Children.Add(resolution);
		layout.Children.Add(frames);
		layout.Children.Add(samples);
		var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
		result.Bind(TextBlock.TextProperty, new Binding(nameof(MemoryWarningModel.Result)));
		AutomationProperties.SetAutomationId(result, "MemoryWarningResult");
		layout.Children.Add(result);
		layout.Children.Add(Box(nameof(MemoryWarningModel.Mute), "MemoryWarningMute", "MemoryWarningMute", true));
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
		var run = new Button { IsDefault = true, MinWidth = 90, Command = model.RunCommand };
		run.Bind(ContentControl.ContentProperty, run.GetResourceObservable("DialogRun"));
		AutomationProperties.SetAutomationId(run, "MemoryWarningRun");
		var cancel = new Button { IsCancel = true, MinWidth = 90, Command = model.CancelCommand };
		cancel.Bind(ContentControl.ContentProperty, cancel.GetResourceObservable("DialogCancel"));
		AutomationProperties.SetAutomationId(cancel, "MemoryWarningCancel");
		buttons.Children.Add(run);
		buttons.Children.Add(cancel);
		layout.Children.Add(buttons);
		Content = layout;
		Opened += (_, _) => (new[] { cache, resolution, frames, samples }.FirstOrDefault(box => box.IsEnabled) as Control ?? run).Focus();
	}
}

/// <summary>Bound choices and calculations for the memory warning.</summary>
internal sealed class MemoryWarningModel : INotifyPropertyChanged
{
	private readonly Action<MemoryWarningChoice?> _close;
	private bool _cache;
	private bool _resolution;
	private bool _frames;
	private bool _samples;
	private bool _mute;

	/// <summary>Creates the model and selects the proposed choices.</summary>
	public MemoryWarningModel(MemoryWarningPlan plan, long bytes, long budget, long autoBudget, Action<MemoryWarningChoice?> close)
	{
		Plan = plan;
		_close = close;
		(_cache, _resolution, _frames, _samples) = plan.Proposal();
		Message = string.Format(Text("MemoryWarningTextFormat"), FrameMemory.Gigabytes(bytes), FrameMemory.Gigabytes(budget));
		CacheCaption = string.Format(Text("MemoryWarningCacheFormat"), FrameMemory.Gigabytes(autoBudget));
		RunCommand = new MemoryWarningCommand(() => _close(new MemoryWarningChoice(Plan.Quality(Resolution, Frames, Samples), Cache, Mute)));
		CancelCommand = new MemoryWarningCommand(() => _close(null));
	}

	/// <summary>Raised when a bound property changes.</summary>
	public event PropertyChangedEventHandler? PropertyChanged;
	/// <summary>The run's memory alternatives.</summary>
	public MemoryWarningPlan Plan { get; }
	/// <summary>The warning text.</summary>
	public string Message { get; }
	/// <summary>The Auto cache caption.</summary>
	public string CacheCaption { get; }
	/// <summary>Whether to set the cache to Auto.</summary>
	public bool Cache { get => _cache; set => Set(ref _cache, value); }
	/// <summary>Whether to halve resolution.</summary>
	public bool Resolution { get => _resolution; set => Set(ref _resolution, value); }
	/// <summary>Whether to halve stored frames.</summary>
	public bool Frames { get => _frames; set => Set(ref _frames, value); }
	/// <summary>Whether to use palette-index samples.</summary>
	public bool Samples { get => _samples; set => Set(ref _samples, value); }
	/// <summary>Whether to suppress future warnings.</summary>
	public bool Mute { get => _mute; set => Set(ref _mute, value); }
	/// <summary>The estimated result of the current choices.</summary>
	public string Result
	{
		get
		{
			(long bytes, long budget) = Plan.Estimate(Cache, Resolution, Frames, Samples);
			return bytes <= budget ? string.Format(Text("MemoryWarningFitsFormat"), FrameMemory.Gigabytes(bytes))
				: string.Format(Text("MemoryWarningSpillsFormat"), FrameMemory.Gigabytes(bytes), FrameMemory.Gigabytes(bytes - budget));
		}
	}
	/// <summary>Accepts the selected choices.</summary>
	public ICommand RunCommand { get; }
	/// <summary>Cancels the warning.</summary>
	public ICommand CancelCommand { get; }

	private static string Text(string key) => (string)Application.Current!.Resources[key]!;
	private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
	{
		if (field == value)
		{
			return;
		}
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Result)));
	}
}

/// <summary>A command for a memory-warning action.</summary>
internal sealed class MemoryWarningCommand(Action execute) : ICommand
{
	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged { add { } remove { } }
	/// <inheritdoc/>
	public bool CanExecute(object? parameter) => true;
	/// <inheritdoc/>
	public void Execute(object? parameter) => execute();
}
