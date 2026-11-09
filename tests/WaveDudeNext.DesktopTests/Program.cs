// SPDX-License-Identifier: GPL-3.0-or-later
// Linux headless integration checks of the actual Avalonia App and MainWindow.
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WaveDudeNext.Desktop;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;

internal static class Program
{
    static MainWindow window = null!;
    static int checks;
    static readonly List<string> results = [];
    static string output = "";
    [STAThread]
    static int Main(string[] args)
    {
        output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "WaveDudeNext-desktop-qa"));
        Directory.CreateDirectory(output);
        try
        {
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
            var files = new RecordingStorage();
            window = new MainWindow(storageProvider: files.Provider) { FontFamily = new Avalonia.Media.FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter") }; window.Show(); Pump();
            Check(window.IsVisible && window.Bounds.Width == 1240, "Actual App and MainWindow open at desktop size");
            Check(Objects.ItemCount == 5 && Project.Sources.Count == 1, "Initial scene and source populate object list");
            Screenshot("01-scene-editor.png");
            Edit("Width", "128"); Edit("Height", "96"); Click("Apply edits / preview scene");
            Check(Project.Scene.Width == 128 && Project.Scene.Height == 96, "Scene text editors apply actual model dimensions");
            Objects.SelectedIndex = 4; Pump(); Edit("X", "32"); Edit("Y", "48"); Click("Apply edits / preview scene");
            Check(Project.Sources[0].X == 32 && Project.Sources[0].Y == 48, "Source position editors move emitter inside resized scene");
            Objects.SelectedIndex = 1; Pump();
            Edit("Steps", "30000");
            // Display labels come from model metadata; match by the current serialized value if needed.
            Click("Apply edits / preview scene");
            Check(Project.Simulation.Steps == 30000, "Simulation property editor applies step count");
            Click("+ Source"); Check(Project.Sources.Count == 2, "Add Source button adds and selects source");
            Click("Delete"); Check(Project.Sources.Count == 1, "Delete button removes selected source");
            Click("+ Probe"); Check(Project.Probes.Count == 1, "Add Probe button adds probe");
            Click("+ Part"); Check(Project.Parts.Count == 1, "Add Part button adds selected optical part");
            Click("Delete"); Check(Project.Parts.Count == 0 && Project.Probes.Count == 1, "Delete part preserves other objects");
            Objects.SelectedIndex = 0; Pump();
            Edit("Width", "not-a-number"); Click("Apply edits / preview scene");
            Check(Status.Contains("Invalid Width") && Project.Scene.Width == 128, "Invalid numeric edit reports error without corrupting width");
            Objects.SelectedIndex = 1; Pump();
            Check(Objects.SelectedIndex == 0, "Invalid edit prevents leaving selected object");
            Edit("Width", "128"); Click("Apply edits / preview scene");
            var before = Project;
            Click("New"); var dialog = Dialog();
            Check(dialog.Title == "Unsaved changes", "New project prompts for unsaved edits");
            Screenshot("02-unsaved-confirmation.png", dialog);
            ClickIn(dialog, "Cancel"); Pump();
            Check(ReferenceEquals(Project, before) && window.IsVisible, "Cancel replacement retains project and edits");
            window.Close(); Pump(); dialog = Dialog(); ClickIn(dialog, "Cancel"); Pump();
            Check(window.IsVisible && ReferenceEquals(Project, before), "Cancel window close leaves window and project intact");
            VerifyProjectPickers(files);
            Click("Run / restart"); Until(() => Run != null && Run.State == RunState.Running, "run starts");
            var first = Run!;
            Click("Pause / resume"); Until(() => first.State == RunState.Paused, "run pauses");
            Check(first.State == RunState.Paused, "Pause button reaches Paused state");
            Click("Pause / resume"); Until(() => first.State == RunState.Running, "run resumes");
            Check(first.State == RunState.Running, "Resume button returns to Running state");
            Click("Stop"); Until(() => first.IsFinished, "run stops");
            Check(first.State == RunState.Cancelled, "Stop button cancels active run");
            // Use a bounded short run for deterministic completed frames and rendering.
            Objects.SelectedIndex = 1; Pump(); Edit("Steps", "100"); Click("Apply edits / preview scene");
            Click("Run / restart"); Until(() => Run != null && !ReferenceEquals(Run, first) && Run.IsFinished, "restart completes");
            Check(Run!.State == RunState.Completed && Run.Frames.Count > 1, "Restart creates new run and completes with frames");
            Pump(120);
            // Advance the timer handler explicitly: RunJobs does not wait on the headless dispatcher clock.
            typeof(MainWindow).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            var view = Field<ComboBox>("_view"); var timeline = Field<Slider>("_timeline");
            Field<CheckBox>("_follow").IsChecked = false;
            view.SelectedIndex = 1; timeline.Value = Run.Frames.Count - 1; Pump();
            Check(Field<TextBlock>("_frameLabel").Text!.Contains($"Frame {Run.Frames.Count}"), "Timeline selects stored field frame");
            Check(Field<int[]>("_lastPixels").Distinct().Count() > 10, "Field render contains non-uniform wave pixels");
            Screenshot("03-field-timeline.png");
            view.SelectedIndex = 2; Pump();
            Check(Field<int>("_lastFrame") == Run.Frames.Count - 1, "Intensity selection renders the selected stored frame");
            Check(Field<int[]>("_lastPixels").Distinct().Count() > 10, "Intensity render contains non-uniform wave pixels");
            Screenshot("04-intensity-view.png");
            view.SelectedIndex = 0; Pump();
            Check(Field<TextBlock>("_frameLabel").Text!.Contains("Scene"), "Scene view returns to geometry preview");
            Click("Play / pause frames"); Pump(180); Click("Play / pause frames");
            Check(!Field<bool>("_playing") && Field<CheckBox>("_follow").IsChecked == false, "Playback toggles and disables follow-run");
            files.SavePath = Path.Combine(output, "chosen-frame.png");
            Click("Export PNG…");
            Check(files.LastSave is { SuggestedFileName: "WaveDude-frame", DefaultExtension: "png", ShowOverwritePrompt: true }, "PNG picker proposes a stem and one default extension");
            Check(File.Exists(files.SavePath) && File.ReadAllBytes(files.SavePath).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG writes to the exact selected destination");
            Objects.SelectedIndex = Objects.ItemCount - 1; Pump();
            files.SavePath = Path.Combine(output, "chosen-probe.csv");
            Click("Probe CSV…");
            Check(files.LastSave?.DefaultExtension == "csv" && !files.LastSave.SuggestedFileName!.EndsWith(".csv", StringComparison.OrdinalIgnoreCase), "CSV picker proposes a stem and one default extension");
            Check(File.Exists(files.SavePath) && File.ReadAllText(files.SavePath).Contains(';'), "CSV writes data to the exact selected destination");
            for (var i = 0; i < 8; i++) Click("Run / restart");
            Until(() => !Field<bool>("_busy") && Run is { IsFinished: true }, "repeated restart settles");
            Check(Run!.Error == null && Run.State == RunState.Completed, "Rapid repeated restart clicks settle without failed run");
            for (var i = 0; i < 8; i++) { Click("Stop"); Click("Pause / resume"); }
            Check(Run.Error == null && !Status.StartsWith("Error:"), "Repeated Stop/Pause after completion are harmless");
            Click("New"); ClickIn(Dialog(), "Continue"); Pump();
            Check(!ReferenceEquals(Project, before) && Run == null && Objects.ItemCount == 5, "Confirmed replacement disposes run and restores clean project");
            window.Close(); Pump(); Check(!window.IsVisible, "Clean close completes without confirmation");
            results.Add($"PASS: {checks} assertions; Linux headless Avalonia with native Skia. This is not macOS execution.");
            File.WriteAllLines(Path.Combine(output, "desktop-ui-results.txt"), results);
            Console.WriteLine(results[^1]); return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex); results.Add("FAIL: " + ex); File.WriteAllLines(Path.Combine(output, "desktop-ui-results.txt"), results); return 1;
        }
    }
    static void VerifyProjectPickers(RecordingStorage files)
    {
        files.SavePath = null;
        Click("Save as…");
        Check(files.LastSave is { SuggestedFileName: "Untitled", DefaultExtension: "wdproj", ShowOverwritePrompt: true }, "Untitled Save As proposes a stem and one default extension");
        Check(Field<string?>("_path") == null && Field<bool>("_dirty"), "Cancelled Save As preserves path and dirty edits");
        // An intentionally doubled suffix is a valid user-selected name. Do not repair it.
        files.SavePath = Path.Combine(output, "chosen.name.wdproj.wdproj");
        Click("Save as…");
        Check(File.Exists(files.SavePath) && Field<string>("_path") == files.SavePath, "Save As honors the exact user-selected path, including a doubled suffix");
        Check(WaveDudeNext.Model.Project.Load(files.SavePath).Scene.Width == 128, "Saved project reloads with edited scene");
        files.SavePath = null; Click("Save as…");
        Check(files.LastSave?.SuggestedFileName == "chosen.name.wdproj", "Existing project suggestion removes only its final known suffix");
        files.OpenPath = Field<string>("_path"); var saved = Project; Click("Open…");
        Check(files.LastOpen is { AllowMultiple: false } && files.LastOpen.FileTypeFilter![0].Patterns!.Contains("*.wdproj"), "Open picker filters projects without suggesting or appending a suffix");
        Check(!ReferenceEquals(saved, Project) && Project.Scene.Width == 128 && Project.Probes.Count == 1, "Actual Open handler reopens the selected saved project");
        var count = files.SaveCalls; Click("Save");
        Check(files.SaveCalls == count && Field<string>("_path") == files.OpenPath, "Save reuses the selected path without another picker");
        // Continue the existing unsaved-change tests after the round trip.
        Objects.SelectedIndex = 1; Pump(); Edit("Steps", "30000"); Click("Apply edits / preview scene");
    }
    static T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    static Project Project => Field<Project>("_project");
    static SimulationRun? Run => Field<SimulationRun?>("_run");
    static ListBox Objects => Field<ListBox>("_objects");
    static string Status => Field<TextBlock>("_status").Text ?? "";
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message + " [status=" + Status + "]"); checks++; results.Add("PASS: " + message); Console.WriteLine(results[^1]); }
    static void Pump(int milliseconds = 25) { var sw = Stopwatch.StartNew(); do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); } while (sw.ElapsedMilliseconds < milliseconds); }
    static void Until(Func<bool> condition, string purpose) { var sw = Stopwatch.StartNew(); while (!condition()) { Pump(10); if (sw.Elapsed.TotalSeconds > 30) throw new TimeoutException(purpose + "; " + Status); } Pump(); }
    static void Click(string label) => ClickIn(window, label);
    static void ClickIn(Window owner, string label) { var b = owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)); b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
    static Window Dialog() { Pump(); return window.OwnedWindows.Single(w => w.IsVisible); }
    static void Edit(string label, string value)
    {
        var children = Field<StackPanel>("_properties").Children;
        for (var i = 0; i + 1 < children.Count; i++) if (children[i] is TextBlock t && string.Equals(t.Text, label, StringComparison.OrdinalIgnoreCase) && children[i + 1] is TextBox box) { box.Text = value; return; }
        throw new Exception("Missing editor " + label + "; labels=" + string.Join(", ", children.OfType<TextBlock>().Select(t => t.Text)));
    }
    static void Screenshot(string name, Window? target = null)
    {
        Pump(80); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Pump();
        using var image = (target ?? window).CaptureRenderedFrame() ?? throw new Exception("No headless Skia frame captured");
        image.Save(Path.Combine(output, name));
        Check(image.PixelSize.Width > 400 && image.PixelSize.Height > 200, "Native Skia UI screenshot: " + name);
    }
}
