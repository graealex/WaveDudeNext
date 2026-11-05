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

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using NLog;
using NLog.Config;
using NLog.Targets;
using WaveDudeNext.Model;
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
		var state = new WindowStateModel(OpenProject);
		Closed += (_, _) => state.Dispose();
		DataContext = state;
		this.Bind(TitleProperty, new Binding(nameof(WindowStateModel.Title)));
		var open = new MenuItem { Command = state.OpenCommand, InputGesture = new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.O, global::Avalonia.Input.KeyModifiers.Control) };
		open.Bind(MenuItem.HeaderProperty, open.GetResourceObservable("MenuFileOpen"));
		var file = new MenuItem { ItemsSource = new[] { open } };
		file.Bind(MenuItem.HeaderProperty, file.GetResourceObservable("MenuFile"));
		var menu = new Menu { ItemsSource = new[] { file } };
		var tree = new TreeView { Width = 250, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
		tree.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(WindowStateModel.Nodes)));
		var status = new TextBlock();
		status.Bind(TextBlock.TextProperty, new Binding(nameof(WindowStateModel.Status)));
		var timeline = new Slider { Minimum = 0, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10 };
		timeline.Bind(Slider.MaximumProperty, new Binding(nameof(WindowStateModel.LastFrame)));
		timeline.Bind(Slider.ValueProperty, new Binding(nameof(WindowStateModel.Frame)) { Mode = BindingMode.TwoWay });
		var position = new TextBlock();
		position.Bind(TextBlock.TextProperty, new Binding(nameof(WindowStateModel.Position)));
		var transport = new DockPanel();
		DockPanel.SetDock(position, Dock.Right);
		transport.Children.Add(position);
		transport.Children.Add(timeline);
		var field = new Image();
		field.Bind(Image.SourceProperty, new Binding(nameof(WindowStateModel.FieldImage)));
		var intensity = new Image();
		intensity.Bind(Image.SourceProperty, new Binding(nameof(WindowStateModel.IntensityImage)));
		var pictures = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
		Grid.SetColumn(intensity, 1);
		pictures.Children.Add(field);
		pictures.Children.Add(intensity);
		DockPanel.SetDock(tree, Dock.Left);
		DockPanel.SetDock(menu, Dock.Top);
		DockPanel.SetDock(status, Dock.Bottom);
		DockPanel.SetDock(transport, Dock.Bottom);
		Content = new DockPanel { Children = { menu, status, transport, tree, pictures } };
	}

	private async Task<(Project Project, string Name, string? Folder)?> OpenProject()
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
			return (Project.Load(stream), files[0].Name, path == null ? null : Path.GetDirectoryName(path));
		}
		catch (Exception ex)
		{
			string? path = files[0].TryGetLocalPath();
			Logger.Error(path == null ? ex : new UnreadableFileException(path, ex), "Error while opening '{Path}'", path ?? files[0].Name);
			throw;
		}
	}

	private static (Project Project, string Name, string? Folder) ImportLegacy(string path)
	{
		var notes = new List<string>();
		Project imported = LegacyProject.Import(path, notes, out string name);
		foreach (string note in notes)
		{
			Logger.Warn("{Note}", note);
		}
		return (imported, name.Length > 0 ? name : Path.GetFileName(path), Path.GetDirectoryName(path));
	}
}

/// <summary>Bound project state and commands, used on the UI thread.</summary>
internal sealed class WindowStateModel : INotifyPropertyChanged, IDisposable
{
	private readonly Func<Task<(Project Project, string Name, string? Folder)?>> _open;
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

	/// <summary>Creates state for a new project.</summary>
	public WindowStateModel(Func<Task<(Project Project, string Name, string? Folder)?>> open)
	{
		_open = open;
		OpenCommand = new OpenCommand(Open);
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
	/// <summary>The loaded project, retained for editing and simulation.</summary>
	public Project Project { get => _project; private set { _project = value; Changed(); } }
	/// <summary>Opens an existing project.</summary>
	public ICommand OpenCommand { get; }
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
			double frame = Math.Clamp(Math.Round(value), 0, LastFrame);
			if (_frame == frame) return;
			_frame = frame;
			Changed();
			Changed(nameof(Position));
			if (_requested != null) _scratch?.Request(_requested, (int)frame);
		}
	}
	/// <summary>The frame and its corresponding simulation step.</summary>
	public string Position => $"{(int)Frame} / {LastFrame}   |   {(int)Frame * (_requested?.VisualizeEvery ?? 1)}";

	/// <summary>Stops the scratch worker and releases the pictures.</summary>
	public void Dispose()
	{
		_disposed = true;
		_scratch?.Dispose();
		_fieldImage?.Dispose();
		_intensityImage?.Dispose();
	}

	private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	private async Task Open()
	{
		try
		{
			if (await _open() is { } result)
			{
				_requested = null;
				SetProject(result.Project);
				Title = result.Name + " - WaveDude Next";
				Status = "";
				RequestPreview(result.Folder);
			}
		}
		catch (Exception ex)
		{
			Status = ex.Message;
		}
	}


	private void RequestPreview(string? folder)
	{
		ScenePixels? scene = SceneComposer.Compose(Project, folder);
		RunSpec spec = RunSpec.Create(Project, scene, preferGpu: false, trackIntensity: true);
		_requested = spec;
		LastFrame = Math.Max(0, spec.FrameCount - 1);
		_frame = 0;
		Changed(nameof(Frame));
		Changed(nameof(Position));
		if (_scratch == null)
		{
			_scratch = new ScratchRunner(SynchronizationContext.Current ?? throw new InvalidOperationException("No UI synchronisation context is available."));
			_scratch.Completed += ScratchCompleted;
		}
		_scratch.Request(spec, 0);
	}

	private void ScratchCompleted(object? sender, ScratchResult result)
	{
		if (_disposed || !ReferenceEquals(result.Spec, _requested) || result.Frame != (int)Frame)
		{
			return;
		}
		var schemes = Project.ColorSchemes.Concat(ColorSchemeStore.LoadBuiltIn(null)).ToArray();
		ColorLut Lut(DisplayLayer layer)
		{
			ColorScheme? scheme = ColorSchemeStore.Find(schemes, layer.ColorScheme);
			return scheme == null ? ColorLut.Grey : ColorLut.Build(scheme, layer.BlackLevel);
		}
		int width = result.Snapshot.Width;
		int height = result.Snapshot.Height;
		WriteableBitmap MakePicture(Half[] samples, ColorLut lut, SampleKind kind, float brightness)
		{
			var pixels = new int[checked(width * height)];
			FrameRenderer.Render(samples, width, height, lut, kind, brightness, pixels);
			var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
			using var locked = bitmap.Lock();
			for (int y = 0; y < height; y++)
			{
				Marshal.Copy(pixels, y * width, IntPtr.Add(locked.Address, y * locked.RowBytes), width);
			}
			return bitmap;
		}
		WriteableBitmap field = MakePicture(result.Snapshot.Field.ToArray(), Lut(Project.Display.Field), SampleKind.Field, (float)Project.Display.Field.Brightness);
		WriteableBitmap intensity = MakePicture(result.Snapshot.Intensity.ToArray(), Lut(Project.Display.Intensity), SampleKind.IntensityRoot, (float)Project.Display.Intensity.Brightness);
		WriteableBitmap? oldField = FieldImage;
		WriteableBitmap? oldIntensity = IntensityImage;
		FieldImage = field;
		IntensityImage = intensity;
		oldField?.Dispose();
		oldIntensity?.Dispose();
	}

	private void SetProject(Project project)
	{
		Project = project;
		static TreeViewItem Group(string key, IEnumerable<string> names)
		{
			var item = new TreeViewItem { ItemsSource = names.Select(value => new TreeViewItem { Header = value }).ToArray() };
			item.Bind(TreeViewItem.HeaderProperty, item.GetResourceObservable(key));
			return item;
		}
		Nodes =
		[
			Group("TreeScene", []),
			Group("TreeSimulation", []),
			Group("TreeDisplay", []),
			Group("TreeSources", project.Sources.Select(item => item.Name)),
			Group("TreeParts", project.Parts.Select(item => item.Name)),
			Group("TreeProbes", project.Probes.Select(item => item.Name)),
			Group("TreeColorSchemes", project.ColorSchemes.Select(item => item.Name)),
		];
	}
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
