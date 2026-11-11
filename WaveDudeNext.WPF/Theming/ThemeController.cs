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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using NLog;
using WaveDudeNext.UI.Theming;
using WaveDudeNext.WPF.Commands;

namespace WaveDudeNext.WPF.Theming;

/// <summary>
/// Application theme state. Switches between the colour tables in <c>Themes/Dark.xaml</c> and
/// <c>Themes/Light.xaml</c> and keeps native window title bars in step. Bind menus to
/// <see cref="Options"/>, <see cref="Current"/> and <see cref="SelectCommand"/>. The System theme follows the
/// desktop's app mode (Settings > Colours > default app mode, the registry's <c>AppsUseLightTheme</c>), live since
/// 2026-11-11 (the user's request): Windows sends every top-level window WM_SETTINGCHANGE "ImmersiveColorSet" at a
/// switch, heard on the main window's handle, and the theme is applied again when the mode read differs from the
/// one applied; Light and Dark stand whatever the desktop does. The Avalonia head does the same through its framework,
/// whose hidden message window hears the same message.
/// </summary>
/// <remarks>
/// The colour table is the first merged dictionary of the application resources; every style in
/// <c>Themes/Controls.xaml</c> reads its brushes from it through <c>DynamicResource</c>, so
/// replacing that one dictionary restyles the whole application live.
/// </remarks>
public sealed class ThemeController : INotifyPropertyChanged
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
	private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
	private const int DwmwaUseImmersiveDarkMode = 20;
	private const int WmSettingChange = 0x001A;
	private const string ImmersiveColorSet = "ImmersiveColorSet";
	private const string DarkSource = "/WaveDudeNext.WPF;component/Themes/Dark.xaml";
	private const string LightSource = "/WaveDudeNext.WPF;component/Themes/Light.xaml";

	private AppTheme _current = AppTheme.System;
	private bool _appliedDark;
	private bool _listening;

	private ThemeController()
	{
		SelectCommand = new RelayCommand<AppTheme>(theme => Current = theme);
	}

	/// <summary>The single theme state of the application.</summary>
	public static ThemeController Instance { get; } = new();

	/// <summary>All selectable themes, in menu order.</summary>
	public IReadOnlyList<AppTheme> Options { get; } = Enum.GetValues<AppTheme>();

	/// <summary>Sets <see cref="Current"/> to its parameter.</summary>
	public ICommand SelectCommand { get; }

	/// <summary>The theme selected by the user. Setting it restyles the application.</summary>
	public AppTheme Current
	{
		get => _current;
		set
		{
			if (_current == value)
			{
				return;
			}
			_current = value;
			Logger.Info("Theme changed to {Theme} (dark: {IsDark})", value, IsDark);
			ApplyResources();
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsDark));
		}
	}

	/// <summary>True when the effective theme is dark, resolving <see cref="AppTheme.System"/>.</summary>
	public bool IsDark => _current switch
	{
		AppTheme.Dark => true,
		AppTheme.Light => false,
		_ => !SystemUsesLightTheme(),
	};

	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Re-applies the resources for <see cref="Current"/>; call once at startup.</summary>
	public void Refresh()
	{
		Logger.Debug("Theme: {Theme} (dark: {IsDark})", _current, IsDark);
		ApplyResources();
	}

	/// <summary>
	/// Makes the native title bar of <paramref name="window"/> follow the effective theme.
	/// Call from <see cref="Window.SourceInitialized"/>; a no-op until the window has a handle.
	/// </summary>
	public void ApplyTitleBar(Window window)
	{
		IntPtr hwnd = new WindowInteropHelper(window).Handle;
		if (hwnd == IntPtr.Zero)
		{
			return;
		}
		int dark = IsDark ? 1 : 0;
		_ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
		// the first window with a handle is the main window, which lives as long as the application: its messages tell of
		// a change of the desktop's app mode (2026-11-11, the user's request, so the System theme follows it live, as the
		// Avalonia head's does by its framework)
		if (!_listening && HwndSource.FromHwnd(hwnd) is { } source)
		{
			source.AddHook(OnWindowMessage);
			_listening = true;
		}
	}

	/// <summary>
	/// Windows says a setting changed: for the desktop's colours ("ImmersiveColorSet", sent with WM_SETTINGCHANGE when the
	/// app mode or the accent is switched) the System theme re-reads the app mode and restyles when it has changed; the
	/// fixed themes ignore it.
	/// </summary>
	private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (message == WmSettingChange && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == ImmersiveColorSet)
		{
			OnDesktopColorsChanged();
		}
		return IntPtr.Zero;
	}

	private void OnDesktopColorsChanged()
	{
		if (_current != AppTheme.System || IsDark == _appliedDark)
		{
			return;
		}
		Logger.Info("The desktop's app mode is {Mode} now; the System theme follows", IsDark ? "dark" : "light");
		ApplyResources();
		OnPropertyChanged(nameof(IsDark));
	}

	private void ApplyResources()
	{
		_appliedDark = IsDark;
		var table = new ResourceDictionary { Source = new Uri(_appliedDark ? DarkSource : LightSource, UriKind.Relative) };
		var merged = Application.Current.Resources.MergedDictionaries;
		int index = -1;
		for (int i = 0; i < merged.Count; i++)
		{
			string? source = merged[i].Source?.OriginalString;
			if (source != null && (source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)
				|| source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)))
			{
				index = i;
				break;
			}
		}
		if (index >= 0)
		{
			merged[index] = table;
		}
		else
		{
			merged.Insert(0, table);
		}
		foreach (Window window in Application.Current.Windows)
		{
			ApplyTitleBar(window);
		}
	}

	private static bool SystemUsesLightTheme()
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
		return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
