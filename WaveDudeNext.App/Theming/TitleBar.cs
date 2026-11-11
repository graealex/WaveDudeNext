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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using NLog;

namespace WaveDudeNext.App.Theming;

/// <summary>
/// The native title bar follows the theme on Windows 10 (2026-11-11, the user's report: the inactive title bar stayed
/// white whatever the theme, where the WPF head's is grey in the dark theme and white in the light one, as Explorer's
/// is). Avalonia 12.1.5 sets <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> from a window's <see cref="StyledElement.ActualThemeVariant"/>
/// only on builds from 22000, Windows 11 (its Win32 <c>WindowImpl.SetFrameThemeVariant</c>), so on the Windows 10 builds
/// before it this does the same at <see cref="Attach"/> and at every change of the variant: attribute 20 from build
/// 19041 (20H1), 19 on 17763 to 19040 (1809 to 1909), where Windows kept it undocumented; nothing on older builds, which
/// have no dark title bars, and nothing on Windows 11, whose Avalonia does it. The WPF head's <c>ThemeController.ApplyTitleBar</c>
/// sets attribute 20 on every window at each theme change. UI thread only.
/// </summary>
internal static class TitleBar
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>The attribute this build of Windows takes, 0 where there is nothing for this class to do.</summary>
	private static readonly int Attribute = OperatingSystem.IsWindows() ? Environment.OSVersion.Version.Build switch
	{
		>= 22000 => 0,
		>= 19041 => 20,
		>= 17763 => 19,
		_ => 0,
	} : 0;

	/// <summary>Makes the title bar of <paramref name="window"/> follow its theme variant, now and at each change; a no-op except on Windows 10 from 1809.</summary>
	public static void Attach(Window window)
	{
		// the platform check stands beside the attribute's, which already contains it, so the analyzer sees the guard
		if (!OperatingSystem.IsWindows() || Attribute == 0)
		{
			return;
		}
		Apply(window);
		window.PropertyChanged += (sender, e) =>
		{
			if (OperatingSystem.IsWindows() && e.Property == TopLevel.ActualThemeVariantProperty)
			{
				Apply((Window)sender!);
			}
		};
	}

	[SupportedOSPlatform("windows")]
	private static void Apply(Window window)
	{
		if (window.TryGetPlatformHandle() is not { Handle: var hwnd } || hwnd == IntPtr.Zero)
		{
			return;
		}
		int dark = window.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
		int result = DwmSetWindowAttribute(hwnd, Attribute, ref dark, sizeof(int));
		Logger.Debug("Title bar of {Window}: {Variant} (DWM attribute {Attribute}, result 0x{Result:X8})", window.GetType().Name, dark == 1 ? "dark" : "light", Attribute, result);
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
