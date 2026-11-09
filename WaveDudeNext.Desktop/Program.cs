// SPDX-License-Identifier: GPL-3.0-or-later
// Desktop port of WaveDude Next, Copyright (C) 2026 Alexander Graef.
// Based on Wave Dude 2D by Jeroen Vleggaar and WaveSimulator2D by 0x23.
// Distributed WITHOUT ANY WARRANTY; see ../LICENSE.md.
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace WaveDudeNext.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--smoke-test")) return DesktopSmokeTest.Run().GetAwaiter().GetResult();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args?.FirstOrDefault(a => a.EndsWith(".wdproj", StringComparison.OrdinalIgnoreCase)));
        base.OnFrameworkInitializationCompleted();
    }
}
