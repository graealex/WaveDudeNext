// SPDX-License-Identifier: GPL-3.0-or-later
// Desktop port of WaveDude Next, Copyright (C) 2026 Alexander Graef.
// Based on Wave Dude 2D by Jeroen Vleggaar and WaveSimulator2D by 0x23.
// Distributed WITHOUT ANY WARRANTY; see ../LICENSE.md.
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.Desktop;

public sealed class MainWindow : Window
{
    private readonly IStorageProvider? _storageProvider;
    private IStorageProvider Files => _storageProvider ?? StorageProvider;
    private const long FrameRamBudget = 256L * 1024 * 1024;
    private Project _project = NewProject();
    private string? _path;
    private bool _dirty, _closing, _updating, _playing, _busy;
    private ObjectItem? _previousSelection;
    private SimulationRun? _run;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private WriteableBitmap? _bitmap;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 5) };
    private readonly TextBlock _frameLabel = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox _objects = new() { MinHeight = 130, MaxHeight = 220 };
    private readonly StackPanel _properties = new() { Spacing = 7, Margin = new Thickness(8) };
    private readonly List<Action> _commits = [];
    private readonly ComboBox _view = new() { ItemsSource = new[] { "Scene", "Field", "Intensity" }, SelectedIndex = 0, Width = 115 };
    private readonly ComboBox _partTypes = new() { Width = 165 };
    private readonly Slider _timeline = new() { Minimum = 0, Maximum = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly Slider _gain = new() { Minimum = -4, Maximum = 5, Value = Math.Log10(30), Width = 110 };
    private readonly CheckBox _follow = new() { Content = "Follow run", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly List<ColorScheme> _schemes = ColorSchemeStore.LoadBuiltIn(null);
    private int[]? _lastPixels;
    private int _lastWidth, _lastHeight, _lastFrame = -1;
    private readonly StackPanel _editor = new() { Spacing = 8 };

    private static Project NewProject()
    {
        var p = new Project();
        p.Scene.Width = 480;
        p.Scene.Height = 320;
        p.Simulation.Steps = 600;
        p.Simulation.VisualizeEvery = 3;
        p.AddSource();
        return p;
    }

    public MainWindow(string? startupPath = null, IStorageProvider? storageProvider = null)
    {
        _storageProvider = storageProvider;
        Title = "WaveDude Next • macOS preview 2";
        Width = 1240; Height = 820; MinWidth = 900; MinHeight = 620;
        var root = new DockPanel();
        var toolbar = new WrapPanel { Margin = new Thickness(8), Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Button("New", NewAsync));
        toolbar.Children.Add(Button("Open…", OpenAsync));
        toolbar.Children.Add(Button("Save", () => SaveAsync(false)));
        toolbar.Children.Add(Button("Save as…", () => SaveAsync(true)));
        var examples = new ComboBox { Width = 200, PlaceholderText = "Open an example…", Margin = new Thickness(3) };
        var names = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith("Examples/")).OrderBy(n => n).ToArray();
        examples.ItemsSource = names.Select(n => n[9..^7]).ToArray();
        examples.SelectionChanged += async (_, _) =>
        {
            if (examples.SelectedIndex < 0) return;
            var name = names[examples.SelectedIndex]; examples.SelectedIndex = -1;
            await Safe(async () => { if (!await CanReplace()) return; using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!; await ReplaceProject(Project.Load(stream), null); });
        };
        toolbar.Children.Add(examples);
        toolbar.Children.Add(Button("Import image…", ImportImage));
        toolbar.Children.Add(Button("Export PNG…", ExportPng));
        toolbar.Children.Add(Button("Probe CSV…", ExportProbeCsv));
        toolbar.Children.Add(Button("About", About));
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var bottom = new StackPanel { Margin = new Thickness(10), Spacing = 6 };
        var controls = new WrapPanel();
        controls.Children.Add(Button("Run / restart", StartRun));
        controls.Children.Add(Button("Pause / resume", () => { _run?.TogglePause(); return Task.CompletedTask; }));
        controls.Children.Add(Button("Stop", () => { _run?.Stop(); _playing = false; return Task.CompletedTask; }));
        controls.Children.Add(Button("Play / pause frames", () => { _playing = !_playing; _follow.IsChecked = false; return Task.CompletedTask; }));
        controls.Children.Add(_view); controls.Children.Add(new TextBlock { Text = "  Gain ", VerticalAlignment = VerticalAlignment.Center });
        controls.Children.Add(_gain); controls.Children.Add(_follow); controls.Children.Add(_frameLabel);
        bottom.Children.Add(controls); bottom.Children.Add(_timeline);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var left = new Border { Width = 310, Margin = new Thickness(8), Background = new SolidColorBrush(Color.Parse("#20252E")), Padding = new Thickness(8) };
        _editor.Children.Add(new TextBlock { Text = "PROJECT OBJECTS", FontWeight = FontWeight.Bold });
        _editor.Children.Add(_objects);
        var adds = new WrapPanel();
        adds.Children.Add(Button("+ Source", () => { CommitEdits(); var item = _project.AddSource(); Changed(); RefreshObjects(item); return Task.CompletedTask; }));
        adds.Children.Add(Button("+ Probe", () => { CommitEdits(); var item = _project.AddProbe(); Changed(); RefreshObjects(item); return Task.CompletedTask; }));
        adds.Children.Add(Button("Delete", DeleteObject));
        _editor.Children.Add(adds);
        var partRow = new WrapPanel();
        var types = typeof(OpticalPart).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(OpticalPart)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null).OrderBy(t => t.Name).ToArray();
        _partTypes.ItemsSource = types.Select(t => t.Name.Replace("Part", "")).ToArray(); _partTypes.SelectedIndex = 0;
        partRow.Children.Add(_partTypes);
        partRow.Children.Add(Button("+ Part", () => { CommitEdits(); var item = _project.AddPart(types[_partTypes.SelectedIndex]); Changed(); RefreshObjects(item); return Task.CompletedTask; }));
        _editor.Children.Add(partRow);
        _editor.Children.Add(new TextBlock { Text = "Edit values, then Apply. Runs use a snapshot.\nCoordinates are in simulation pixels.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        _editor.Children.Add(Button("Apply edits / preview scene", () => { CommitEdits(); RefreshObjects((_objects.SelectedItem as ObjectItem)?.Value); _view.SelectedIndex = 0; RenderScene(); return Task.CompletedTask; }));
        _editor.Children.Add(_properties);
        left.Child = new ScrollViewer { Content = _editor };
        DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        root.Children.Add(new Border { Background = Brushes.Black, Margin = new Thickness(8), Child = _image });
        Content = root;
        _objects.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            try { CommitEdits(); _previousSelection = _objects.SelectedItem as ObjectItem; RefreshProperties(); }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                _updating = true; _objects.SelectedItem = _previousSelection; _updating = false;
            }
        };
        _view.SelectionChanged += (_, _) => SafeSync(() =>
        {
            if (_view.SelectedIndex > 0)
            {
                _updating = true;
                _gain.Value = Math.Log10((_view.SelectedIndex == 1 ? _project.Display.Field : _project.Display.Intensity).Brightness);
                _updating = false;
            }
            _lastFrame = -1; RenderCurrent();
        });
        _gain.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || _updating || _view.SelectedIndex < 1) return;
            SafeSync(() =>
            {
                CommitEdits();
                var display = _view.SelectedIndex == 1 ? _project.Display.Field : _project.Display.Intensity;
                display.Brightness = Math.Pow(10, _gain.Value);
                if (ReferenceEquals((_objects.SelectedItem as ObjectItem)?.Value, display)) RefreshProperties();
                Changed(); _lastFrame = -1; RenderCurrent();
            });
        };
        _timeline.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !_updating) SafeSync(RenderCurrent); };
        _timer.Tick += (_, _) => SafeSync(Tick);
        Closing += async (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true;
            await Safe(async () => { if (!await CanReplace()) return; _timer.Stop(); await DisposeRun(); _bitmap?.Dispose(); _closing = true; Close(); });
        };
        Opened += async (_, _) =>
        {
            SafeSync(FrameCache.Sweep);
            RefreshObjects(); RenderScene(); _timer.Start();
            _status.Text = "CPU desktop preview • Open a .wdproj or example, edit objects, then Run. Windows-only video and advanced visual editors are not included.";
            if (startupPath != null) await Safe(async () => await ReplaceProject(Project.Load(startupPath), startupPath));
        };
    }

    private Button Button(string label, Func<Task> action)
    {
        var b = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(9, 5) };
        Avalonia.Automation.AutomationProperties.SetName(b, label);
        b.Click += async (_, _) => await Safe(action);
        return b;
    }
    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _status.Text = "Error: " + (ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex).Message; }
    }
    private void SafeSync(Action action)
    {
        try { action(); } catch (Exception ex) { _status.Text = "Error: " + ex.Message; }
    }
    private void Changed()
    {
        _dirty = true; Title = $"* {Path.GetFileName(_path) ?? "Untitled"} • WaveDude Next";
    }
    private sealed record ObjectItem(string Label, object Value) { public override string ToString() => Label; }
    private void RefreshObjects(object? selected = null)
    {
        _updating = true;
        var items = new List<ObjectItem> { new("Scene", _project.Scene), new("Simulation", _project.Simulation), new("Field display", _project.Display.Field), new("Intensity display", _project.Display.Intensity) };
        items.AddRange(_project.Sources.Select(s => new ObjectItem("Source: " + s.Name, s)));
        items.AddRange(_project.Parts.Select(p => new ObjectItem(p.GetType().Name.Replace("Part", "") + ": " + p.Name, p)));
        items.AddRange(_project.Probes.Select(p => new ObjectItem("Probe: " + p.Name, p)));
        _objects.ItemsSource = items;
        _objects.SelectedItem = items.FirstOrDefault(i => ReferenceEquals(i.Value, selected)) ?? items[0];
        _previousSelection = _objects.SelectedItem as ObjectItem;
        _updating = false; RefreshProperties();
    }
    private void RefreshProperties()
    {
        _properties.Children.Clear(); _commits.Clear();
        if (_objects.SelectedItem is not ObjectItem item) return;
        AddProperties(item.Value);
        if (item.Value is Source source)
        {
            var array = new CheckBox { Content = "Source array", IsChecked = source.Array != null };
            array.Click += (_, _) => SafeSync(() => { CommitEdits(); source.Array = array.IsChecked == true ? source.Array ?? new SourceArray() : null; Changed(); RefreshProperties(); });
            _properties.Children.Add(array);
            if (source.Array != null) AddProperties(source.Array);
        }
        _properties.Children.Add(new TextBlock { Text = "Scalar properties are supported. Complex vertex/path editors, probe graphs and drag handles are not yet ported.", FontSize = 11, TextWrapping = TextWrapping.Wrap });
    }
    private void AddProperties(object model)
    {
        foreach (var prop in model.GetType().GetProperties().Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false))
        {
            var type = prop.PropertyType;
            if (!(type.IsEnum || type == typeof(bool) || type == typeof(string) || type == typeof(int) || type == typeof(double) || type == typeof(float))) continue;
            var label = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? prop.Name;
            var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var title = new TextBlock { Text = label, FontSize = 12 };
            if (description != null) ToolTip.SetTip(title, description);
            _properties.Children.Add(title);
            var old = prop.GetValue(model);
            if (type == typeof(bool))
            {
                var control = new CheckBox { IsChecked = (bool?)old };
                _commits.Add(() => SetProperty(model, prop, control.IsChecked == true));
                _properties.Children.Add(control);
            }
            else if (type.IsEnum)
            {
                var control = new ComboBox { ItemsSource = Enum.GetValues(type), SelectedItem = old, HorizontalAlignment = HorizontalAlignment.Stretch };
                _commits.Add(() => SetProperty(model, prop, control.SelectedItem));
                _properties.Children.Add(control);
            }
            else
            {
                var control = new TextBox { Text = Convert.ToString(old, CultureInfo.InvariantCulture) };
                _commits.Add(() =>
                {
                    try
                    {
                        var value = type == typeof(string) ? control.Text ?? "" : Convert.ChangeType(control.Text, type, CultureInfo.InvariantCulture);
                        if (value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f)) throw new FormatException();
                        SetProperty(model, prop, value);
                    }
                    catch (Exception ex) { throw new ArgumentException($"Invalid {label}: {control.Text}", ex); }
                });
                _properties.Children.Add(control);
            }
        }
    }
    private void SetProperty(object model, PropertyInfo property, object? value)
    {
        if (!Equals(property.GetValue(model), value)) { property.SetValue(model, value); Changed(); }
    }
    private void CommitEdits() { foreach (var commit in _commits) commit(); _project.Normalize(); }

    private async Task<bool> Confirm(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 470, Height = 220, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 20 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12 };
        var no = new Button { Content = "Cancel" }; no.Click += (_, _) => dialog.Close(false);
        var yes = new Button { Content = "Continue" }; yes.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(no); buttons.Children.Add(yes); panel.Children.Add(buttons); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    private async Task<bool> CanReplace()
    {
        CommitEdits();
        return !_dirty || await Confirm("Unsaved changes", "Discard unsaved changes? Cancel to return and save your project first.");
    }
    private async Task NewAsync() { if (await CanReplace()) await ReplaceProject(NewProject(), null); }
    private static FilePickerFileType ProjectType => new("WaveDude project") { Patterns = ["*.wdproj"] };
    private async Task OpenAsync()
    {
        var files = await Files.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open WaveDude project", AllowMultiple = false, FileTypeFilter = [ProjectType] });
        if (files.Count == 0 || !await CanReplace()) return;
        var path = files[0].TryGetLocalPath() ?? throw new IOException("Choose a local project file.");
        await ReplaceProject(Project.Load(path), path);
    }
    private async Task ReplaceProject(Project project, string? path)
    {
        await DisposeRun(); _project = project; _path = path; _dirty = false;
        Title = $"{Path.GetFileName(path) ?? "Untitled example"} • WaveDude Next";
        RefreshObjects(); _view.SelectedIndex = 0; RenderScene(); _status.Text = "Project loaded. Run uses the CPU engine; changes take effect on the next run.";
    }
    // Native save panels add DefaultExtension themselves. Strip only the matching
    // suffix from the suggestion, never from the destination returned by the picker.
    private static string SuggestedStem(string name, string extension) =>
        name.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase) ? name[..^(extension.Length + 1)] : name;

    private async Task SaveAsync(bool saveAs)
    {
        CommitEdits();
        string? target = _path;
        if (saveAs || target == null)
        {
            var file = await Files.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save WaveDude project", SuggestedFileName = SuggestedStem(Path.GetFileName(_path) ?? "Untitled", "wdproj"), DefaultExtension = "wdproj", FileTypeChoices = [ProjectType], ShowOverwritePrompt = true });
            if (file == null) return;
            target = file.TryGetLocalPath() ?? throw new IOException("Choose a local destination.");
        }
        // Preserve linked image resolution across Save As without modifying embedded data.
        if (_project.Scene.ImageData == null && _project.Scene.Mode == SceneMode.Image)
            _project.Scene.ImagePath = _project.Scene.ResolveImagePath(Path.GetDirectoryName(_path));
        _project.Save(target); _path = target; _dirty = false;
        Title = $"{Path.GetFileName(target)} • WaveDude Next"; _status.Text = "Saved " + target;
    }
    private async Task ImportImage()
    {
        var files = await Files.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Embed a scene image", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.bmp", "*.jpg", "*.jpeg", "*.webp"] }] });
        if (files.Count == 0) return;
        using var stream = await files[0].OpenReadAsync(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
        var bytes = memory.ToArray(); var decoded = SceneImages.Decode(bytes);
        CommitEdits(); _project.Scene.ImageData = bytes; _project.Scene.Image = files[0].Name;
        _project.Scene.ImagePath = files[0].TryGetLocalPath() ?? "";
        _project.Scene.Width = decoded.Width; _project.Scene.Height = decoded.Height; _project.Scene.Mode = SceneMode.Image;
        Changed(); RefreshObjects(_project.Scene); _view.SelectedIndex = 0; RenderScene();
        _status.Text = "Image embedded. Red encodes refractive index, green sources, blue damping (upstream image convention).";
    }
    private Task DeleteObject()
    {
        if (_objects.SelectedItem is not ObjectItem item) return Task.CompletedTask;
        switch (item.Value) { case Source s: _project.Remove(s); break; case OpticalPart p: _project.Remove(p); break; case Probe p: _project.Remove(p); break; default: return Task.CompletedTask; }
        Changed(); RefreshObjects(); _view.SelectedIndex = 0; RenderScene(); return Task.CompletedTask;
    }
    private async Task StartRun()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            CommitEdits();
            _status.Text = "Preparing scene…";
            var scene = SceneImages.Build(_project, _path);
            var spec = RunSpec.Create(_project, scene, preferGpu: false, trackIntensity: true, quality: PreviewQuality.Full);
            if (spec.StoreBytes > 2L * 1024 * 1024 * 1024 && !await Confirm("Large simulation", $"This run stores about {spec.StoreBytes / (1024.0 * 1024 * 1024):F1} GiB of frames, mostly on temporary disk. Reduce Steps or increase Visualize every to use less space. Continue?")) return;
            await DisposeRun();
            var memoryFrames = Math.Min(spec.StoredFrameCount, FrameStore.FramesInBudget(spec.Shape, spec.Layers, FrameRamBudget));
            var diskFrames = spec.StoredFrameCount - memoryFrames;
            if (diskFrames > 0) FrameCache.CheckSpace(FrameStore.SizeOf(spec.Shape, spec.Layers, diskFrames));
            var store = FrameStore.Create(FrameCache.NewPath(), spec.Shape, spec.Layers, spec.StoredFrameCount, spec.CaptureEvery, ramBudget: FrameRamBudget);
            _run = new SimulationRun(spec, store, null);
            _lastFrame = -1; _playing = false; _follow.IsChecked = true; _view.SelectedIndex = 1;
            _run.Start();
        }
        finally { _busy = false; }
    }
    private async Task DisposeRun()
    {
        var run = _run; _run = null; _playing = false;
        if (run != null)
        {
            run.Stop(); await run.Completion;
            run.Dispose(); run.Store.Dispose();
        }
        _updating = true; _timeline.Value = 0; _timeline.Maximum = 1; _updating = false; _lastFrame = -1;
    }
    private void Tick()
    {
        if (_run == null) return;
        var frames = _run.Frames;
        _updating = true;
        _timeline.Maximum = Math.Max(1, frames.Count - 1);
        if (_playing && frames.Count > 0) _timeline.Value = ((int)_timeline.Value + 1) % frames.Count;
        else if (_follow.IsChecked == true && frames.Count > 0) _timeline.Value = frames.Count - 1;
        _updating = false;
        if (_view.SelectedIndex > 0) RenderCurrent();
        _status.Text = _run.Error != null ? "Simulation failed: " + _run.Error.Message : $"{_run.State} • {_run.StepsDone:N0} / {_run.TotalSteps:N0} steps • {frames.Count:N0} frames • {_run.Elapsed.TotalSeconds:F1}s • {_run.EngineDescription}";
    }
    private void RenderCurrent()
    {
        if (_view.SelectedIndex == 0) { RenderScene(); return; }
        var frames = _run?.Frames;
        if (frames == null || frames.Count == 0) return;
        var frame = Math.Clamp((int)_timeline.Value, 0, frames.Count - 1);
        if (frame == _lastFrame) return;
        var layer = _view.SelectedIndex == 2 ? FrameLayer.Intensity : FrameLayer.Field;
        if (!frames.Has(layer)) return;
        var display = layer == FrameLayer.Field ? _project.Display.Field : _project.Display.Intensity;
        var scheme = ColorSchemeStore.Find(_project.ColorSchemes.Concat(_schemes), display.ColorScheme);
        var lut = scheme == null ? ColorLut.Grey : ColorLut.Build(scheme, display.BlackLevel);
        var pixels = new int[frames.Width * frames.Height];
        var values = new Half[pixels.Length];
        if (!frames.CopyLayer(frame, layer, values.AsSpan())) return;
        FrameRenderer.Render(values, frames.Width, frames.Height, lut, frames.KindOf(layer), (float)display.Brightness, pixels);
        ShowPixels(pixels, frames.Width, frames.Height); _lastFrame = frame;
        _frameLabel.Text = $"  Frame {frame + 1:N0} / {frames.Count:N0}";
    }
    private void RenderScene()
    {
        var scene = SceneImages.Build(_project, _path);
        var width = scene?.Width ?? _project.Scene.Width;
        var height = scene?.Height ?? _project.Scene.Height;
        var pixels = new int[checked(width * height)];
        Array.Fill(pixels, unchecked((int)0xFF18202B));
        if (scene != null)
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = y * scene.StrideBytes + x * 4;
                    var r = scene.Bgra[i + 2]; var g = scene.Bgra[i + 1]; var b = scene.Bgra[i];
                    var grey = r == 0 ? 230 : Math.Clamp(35 + (r - 100), 20, 200);
                    pixels[y * width + x] = unchecked((int)0xFF000000) | grey << 16 | Math.Max(grey, (int)g) << 8 | Math.Max(grey, (int)b);
                }
        _project.GenerateElements();
        foreach (var source in _project.Sources.Where(s => s.Enabled))
            foreach (var point in source.Elements) Cross(pixels, width, height, point.X, point.Y, unchecked((int)0xFF37F8B1));
        foreach (var probe in _project.Probes) Cross(pixels, width, height, probe.AX, probe.AY, unchecked((int)0xFFFFC857));
        ShowPixels(pixels, width, height); _lastFrame = -1;
        _frameLabel.Text = $"  Scene {width} × {height}";
    }
    private static void Cross(int[] pixels, int width, int height, int x, int y, int color)
    {
        for (var d = -5; d <= 5; d++)
        {
            if ((uint)(x + d) < width && (uint)y < height) pixels[y * width + x + d] = color;
            if ((uint)x < width && (uint)(y + d) < height) pixels[(y + d) * width + x] = color;
        }
    }
    private void ShowPixels(int[] pixels, int width, int height)
    {
        if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
        {
            _image.Source = null; _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _image.Source = _bitmap;
        }
        using (var buffer = _bitmap.Lock())
            for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * width, IntPtr.Add(buffer.Address, y * buffer.RowBytes), width);
        _image.InvalidateVisual(); _lastPixels = pixels; _lastWidth = width; _lastHeight = height;
    }
    private async Task ExportPng()
    {
        RenderCurrent();
        if (_lastPixels == null) throw new InvalidOperationException("No image is available to export.");
        var bytes = SceneImages.EncodePng(_lastWidth, _lastHeight, _lastPixels);
        var file = await Files.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export the current view", SuggestedFileName = "WaveDude-frame", DefaultExtension = "png", FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }], ShowOverwritePrompt = true });
        if (file == null) return;
        await using var output = await file.OpenWriteAsync(); output.SetLength(0); await output.WriteAsync(bytes);
        _status.Text = "Exported " + file.Name;
    }
    private async Task ExportProbeCsv()
    {
        var run = _run ?? throw new InvalidOperationException("Run a simulation with an active probe first.");
        if (!run.Completion.IsCompleted) throw new InvalidOperationException("Wait for the run to finish, or Stop it, before exporting a probe.");
        if (_objects.SelectedItem is not ObjectItem { Value: Probe probe }) throw new InvalidOperationException("Select a probe in the project objects list first.");
        var index = -1;
        for (var i = 0; i < run.Spec.Probes.Length; i++)
            if (ReferenceEquals(run.Spec.Probes[i].Origin, probe)) { index = i; break; }
        if (index < 0) throw new InvalidOperationException("This probe was not active in the recorded run. Run again to record it.");
        var spec = run.Spec.Probes[index];
        var snapshot = new Probe { Name = spec.Name, Kind = spec.Kind, Measurement = spec.Measurement, AX = spec.AX, AY = spec.AY, BX = spec.BX, BY = spec.BY };
        using var csv = new StringWriter(CultureInfo.InvariantCulture);
        var rows = WaveDudeNext.Export.ProbeCsv.Write(csv, snapshot, run.Traces[index], run.Spec.VisualizeEvery, run.Spec.FrameStride);
        var file = await Files.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export selected probe", SuggestedFileName = SuggestedStem(WaveDudeNext.Export.ProbeCsv.FileName(Path.GetFileNameWithoutExtension(_path) ?? "Untitled", snapshot), "csv"), DefaultExtension = "csv", FileTypeChoices = [new FilePickerFileType("Probe CSV") { Patterns = ["*.csv"] }], ShowOverwritePrompt = true });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
        await using var writer = new StreamWriter(stream); await writer.WriteAsync(csv.ToString());
        _status.Text = $"Exported {rows:N0} probe rows to {file.Name} (semicolon-separated, invariant decimal points).";
    }
    private async Task About()
    {
        var dialog = new Window { Title = "About WaveDude Next", Width = 540, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 20 };
        panel.Children.Add(new TextBlock { Text = "WaveDude Next • experimental macOS desktop port (preview 2)", FontSize = 19, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Original WaveDude Next © 2026 Alexander Graef.\nBased on Wave Dude 2D by Jeroen Vleggaar (Huygens Optics) and WaveSimulator2D by 0x23 (Diffraction Limited).\n\nGPL-3.0-or-later. Distributed without warranty.\nhttps://github.com/graealex/WaveDudeNext\n\nThis Avalonia port uses the original CPU solver. It does not yet provide the full Windows interface, MP4 export, probe graph UI or advanced shape editors.", TextWrapping = TextWrapping.Wrap });
        dialog.Content = panel; await dialog.ShowDialog(this);
    }
}
