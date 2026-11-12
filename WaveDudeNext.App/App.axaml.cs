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

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NLog;
using NLog.Config;
using NLog.Targets;
using WaveDudeNext.App.Localization;
using WaveDudeNext.App.Logging;
using WaveDudeNext.App.Theming;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;
using WaveDudeNext.UI.Rendering;
using WaveDudeNext.UI;

namespace WaveDudeNext.App;

/// <summary>
/// The application: logging, the settings, the theme and the language, the start-up arguments, then the main window,
/// in upstream's order. <see cref="SetupLogging"/> runs from <see cref="Program.Main"/> before Avalonia starts so the
/// first lines (the environment, the ICD, the schemes) land in the file.
/// </summary>
public partial class App : Application
{
	/// <summary>Logger of the application class; other classes get their own.</summary>
	public static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	private const string FileLayout =
		"${date:format=yyyy-MM-dd HH\\:mm\\:ss}\t${threadid}\t${level}\t${logger}\t${message}\t${onexception:EXCEPTION\\: ${exception:format=tostring}}";
	private const string DebugLayout =
		"${date:format=HH\\:mm\\:ss} ${threadid} ${level} ${logger:shortName=true}\t${message}\t${onexception:EXCEPTION\\: ${exception:format=tostring}}";
	private const string LogFileName = "WaveDudeNext.App.log";

	/// <summary>
	/// The folder the executable was started from: <see cref="Environment.ProcessPath"/>'s, not
	/// <see cref="AppContext.BaseDirectory"/> (upstream's choice: the two differ for a single-file publish).
	/// </summary>
	public static string ExecutableDirectory { get; } =
		Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } folder ? folder : AppContext.BaseDirectory;

	/// <summary>Path of the current log file: next to the executable when that folder is writable, else in the data folder. Set by <see cref="SetupLogging"/>.</summary>
	public static string LogFile { get; private set; } = Path.Combine(ExecutableDirectory, LogFileName);

	/// <summary>The project file the application was started with, opened once the window shows; null for none.</summary>
	public static string? StartupFile { get; private set; }

	/// <summary>
	/// Replaces the NLog configuration with the application's targets: a rolling file (20 MB, one archive) next to the
	/// executable or in the data folder, the status bar (Info and up), the Messages pane's buffer (everything in Debug
	/// builds, Debug and up in Release), and the debugger and console in Debug builds; unhandled exceptions of the
	/// AppDomain and of unobserved tasks go to the log. Numbers in the log always carry a decimal point. Call once,
	/// before the application object exists.
	/// </summary>
	public static void SetupLogging()
	{
		LogFile = Path.Combine(ResolveLogDirectory(), LogFileName);
		var config = new LoggingConfiguration();
		var fileTarget = new FileTarget("logfile")
		{
			FileName = LogFile,
			ArchiveFileName = Path.ChangeExtension(LogFile, "archive.log"),
			MaxArchiveFiles = 1,
			ArchiveAboveSize = 20 * 1024 * 1024,
			KeepFileOpen = true,
			Layout = FileLayout,
		};
		config.AddTarget(fileTarget);
		StatusBarTarget.Install(config);
#if DEBUG
		config.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget);
		config.AddRule(LogLevel.Trace, LogLevel.Fatal, new DebuggerTarget("debugger") { Layout = DebugLayout });
		config.AddRule(LogLevel.Trace, LogLevel.Fatal, new ColoredConsoleTarget("console") { Layout = DebugLayout });
#else
		// Trace is a Debug-build affair (upstream's rule): the presenter traces every frame it draws
		config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
#endif
		config.DefaultCultureInfo = CultureInfo.InvariantCulture;
		LogManager.Configuration = config;
		// the Messages pane's buffer joins once Avalonia runs (its flush timer needs the UI dispatcher): OnFrameworkInitializationCompleted

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			Logger.Fatal(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
		TaskScheduler.UnobservedTaskException += (_, args) =>
			Logger.Fatal(args.Exception, "TaskScheduler.UnobservedTaskException");
	}

	/// <summary>
	/// Reads the start-up arguments: <c>--zoom=&lt;factor&gt;</c> sets the UI zoom setting (0.5 to 8, a decimal point),
	/// <c>--greyscale-text</c> is accepted and ignored (upstream's figure switch; text rendering is the platform's here),
	/// an unknown switch is logged and ignored, and the first other argument is the project file to open.
	/// </summary>
	public static void ReadArguments(string[] args)
	{
		if (args.Length > 0)
		{
			Logger.Info("Startup arguments: {Args}", string.Join(" ", args));
		}
		foreach (string arg in args)
		{
			const string zoomSwitch = "--zoom=";
			if (arg.StartsWith(zoomSwitch, StringComparison.OrdinalIgnoreCase))
			{
				if (double.TryParse(arg.AsSpan(zoomSwitch.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out double zoom) && zoom is >= 0.5 and <= 8)
				{
					Settings.Default.UiZoom = zoom;
					Logger.Info("UI zoom {Zoom} ({Switch}): the setting, kept for the next start too", zoom, arg);
				}
				else
				{
					Logger.Warn("The start-up switch {Switch} is ignored: the zoom must be a number from 0.5 to 8", arg);
				}
			}
			else if (arg.Equals("--greyscale-text", StringComparison.OrdinalIgnoreCase))
			{
				Logger.Info("The switch {Switch} is accepted and has no effect in this port", arg);
			}
			else if (arg.StartsWith("--", StringComparison.Ordinal))
			{
				Logger.Warn("The start-up switch {Switch} is unknown and ignored", arg);
			}
			else
			{
				StartupFile ??= arg;
			}
		}
	}

	/// <inheritdoc />
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	/// <inheritdoc />
	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// everything in a Debug build, Debug and up in Release (upstream's rule), into the Messages pane's buffer
			MessagesTarget.Install(MessagesTarget.DefaultMinimum);
			Logger.Info("Application: '{Path}'", Environment.ProcessPath);
			Logger.Info("Application Version: {Version}", Program.DisplayVersion);
			Logger.Info("OS: '{OS}'", RuntimeInformation.OSDescription);
			Logger.Info("Runtime: '{Runtime}'", RuntimeInformation.FrameworkDescription);
			Logger.Info("Log file: '{Path}'", LogFile);
			// a double-click on a project or scheme file in Explorer then starts this exe with the path as its argument
			// (2026-11-12, the user's choice of three: the same classes as the WPF head's, so the head started last owns
			// the file types, as two installs of one head in different folders do; Windows alone, the Linux desktop
			// entry and the macOS bundle come with those ports)
			if (OperatingSystem.IsWindows() && Environment.ProcessPath is string executable)
			{
				FileAssociation.Register(ProjectDocument.Extension, FileAssociation.ProjectProgId, FileAssociation.ProjectDescription, executable);
				FileAssociation.Register(ColorSchemeStore.XmlExtension, FileAssociation.SchemeProgId, FileAssociation.SchemeDescription, executable);
			}
			FrameCache.Sweep();
			// the one start without a settings file chooses the preview quality for the machine (upstream's design)
			(MachineFacts Facts, PreviewTier Tier)? firstStart = Settings.IsFirstStart ? ChoosePreviewDefaults() : null;
			ColorSchemeCatalog.Instance.Load();
			ThemeController.Instance.Apply();
			// tooltips let the pointer through to what lies under them, as the WPF head's do (2026-11-10, the user's report)
			Controls.ToolTipPassThrough.Install();
			LanguageController.Instance.Apply();
			ReadArguments(desktop.Args ?? []);
			var window = new MainWindow();
			desktop.MainWindow = window;
			desktop.Exit += (_, args) =>
			{
				Logger.Info("Application exit, code {Code}", args.ApplicationExitCode);
				LogManager.Shutdown();
			};
			window.Opened += (_, _) =>
			{
				Logger.Info("Display scale {Percent} %, UI zoom {Zoom}", Math.Round(window.RenderScaling * 100), Settings.Default.UiZoom);
				// the last start-up line, and so the status bar's first text unless this is a first start or a file is opened below
				// the product's name from the string table, as the WPF head logs it; the window's title carries the project's name too
				Logger.Info("{Title} {Version} ready", window.TryFindResource("Title", out object? title) ? title : window.Title, Program.DisplayVersion);
				if (firstStart is { } first)
				{
					Logger.Info("First start on this machine: {Memory:0.0} GB of memory, {Threads} threads, {Gpu}, cache drive {Drive} {DriveKind}: the preview is set to {Quality} (View menu)",
						first.Facts.PhysicalBytes / (double)(1L << 30), first.Facts.Threads, first.Facts.Gpu.Length == 0 ? "no OpenCL GPU" : first.Facts.Gpu, first.Facts.CacheDrive,
						first.Facts.CacheDriveSeekPenalty switch { true => "with a seek penalty (a hard disk)", false => "without a seek penalty", null => "of unknown kind" },
						PreviewDefaults.Describe(first.Tier));
				}
				if (StartupFile != null)
				{
					// launched with a project file (the desktop entry's %f, "open with", the command line)
					_ = window.OpenFileAsync(StartupFile);
				}
			};
		}
		base.OnFrameworkInitializationCompleted();
	}

	/// <summary>The executable's folder if a file can be created there, else the data folder.</summary>
	private static string ResolveLogDirectory()
	{
		if (IsWritable(ExecutableDirectory))
		{
			return ExecutableDirectory;
		}
		string folder = Path.GetDirectoryName(Settings.FilePath)!;
		Directory.CreateDirectory(folder);
		return folder;
	}

	private static bool IsWritable(string directory)
	{
		try
		{
			string probe = Path.Combine(directory, Path.GetRandomFileName());
			using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
			{
			}
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// Reads the machine's facts, sets the preview quality of its tier (<see cref="PreviewDefaults"/>) and writes the
	/// settings file at once, so the choice is made once and deleting the file redoes it. Returns the facts and the tier for the log.
	/// </summary>
	private static (MachineFacts Facts, PreviewTier Tier) ChoosePreviewDefaults()
	{
		long started = Stopwatch.GetTimestamp();
		MachineFacts facts = MachineFacts.Read();
		PreviewTier tier = PreviewDefaults.TierOf(facts);
		Settings.Default.SetPreviewQuality(PreviewDefaults.QualityOf(tier), PreviewDefaults.DitherBitsOf(tier));
		Logger.Debug("First start: the machine's facts read in {Ms:0} ms, tier {Tier}", Stopwatch.GetElapsedTime(started).TotalMilliseconds, tier);
		try
		{
			Settings.Default.Save();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Logger.Warn(ex, "The settings file could not be written at the first start; the preview quality will be chosen again at the next start");
		}
		return (facts, tier);
	}
}
