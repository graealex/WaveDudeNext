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

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using NLog;

namespace WaveDudeNext.WPF;

/// <summary>
/// Registers the project file extension with Windows Explorer, so that a double-click on a
/// <c>.wdproj</c> or <c>.wdscheme</c> file (or "Open with") starts this executable with the file as its first
/// argument; <see cref="App.OnStartup"/> hands that argument to <see cref="MainWindow.OpenFile"/>.
/// </summary>
/// <remarks>
/// Written the way the shell documents file types, a ProgId that the extension points at:
/// <code>
/// classes\.wdproj                             (Default) = WaveDudeNext.Project
/// classes\WaveDudeNext.Project                    (Default) = WaveDude Next project   (Explorer's "Type" column)
/// classes\WaveDudeNext.Project\DefaultIcon        (Default) = "exe",0            (the executable's own icon)
/// classes\WaveDudeNext.Project\shell\open\command (Default) = "exe" "%1"
/// </code>
/// <c>classes</c> is <c>HKEY_CLASSES_ROOT</c> (all users; only an elevated process may write it)
/// and otherwise <c>HKEY_CURRENT_USER\Software\Classes</c> (this user, no elevation needed), the
/// same order as DTCODataSimulator. Entries that already point at this executable are left alone,
/// so the Explorer refresh is only triggered when something changed (first run, a build in another
/// folder). The user's own "Open with" choice for the extension is removed, since Explorer lets
/// that override the ProgId. Nothing here throws: a failure is logged and the application starts
/// without the association.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class FileAssociation
{
	/// <summary>Registry class name of a project file; the extension key points at it.</summary>
	public const string ProjectProgId = "WaveDudeNext.Project";

	/// <summary>What Explorer shows as a project file's type.</summary>
	public const string ProjectDescription = "WaveDude Next project";

	/// <summary>Registry class name of a colour scheme file (<c>.wdscheme</c>, 2026-10-13).</summary>
	public const string SchemeProgId = "WaveDudeNext.ColorScheme";

	/// <summary>What Explorer shows as a scheme file's type.</summary>
	public const string SchemeDescription = "WaveDude Next colour scheme";

	/// <summary><c>SHChangeNotify</c> event: the file associations changed, flush the icon and type caches.</summary>
	private const uint SHCNE_ASSOCCHANGED = 0x08000000;

	/// <summary><c>SHChangeNotify</c> flag: the item parameters are ID lists (both null here).</summary>
	private const uint SHCNF_IDLIST = 0x0000;

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>
	/// Makes files with <paramref name="extension"/> (leading dot included) open with
	/// <paramref name="executable"/> under the class <paramref name="progId"/> shown as
	/// <paramref name="description"/>, machine-wide when this process may write there, else for the
	/// current user. Call once per file type at start-up; it is cheap when nothing has changed.
	/// </summary>
	public static void Register(string extension, string progId, string description, string executable)
	{
		try
		{
			Apply(Registry.ClassesRoot, "HKEY_CLASSES_ROOT", extension, progId, description, executable);
			return;
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
		{
			Logger.Debug("No write access to HKEY_CLASSES_ROOT ({Message}); registering {Extension} for the current user only", ex.Message, extension);
		}

		try
		{
			using RegistryKey? classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
			if (classes == null)
			{
				Logger.Warn("HKEY_CURRENT_USER\\Software\\Classes does not exist; {Extension} not registered", extension);
				return;
			}
			Apply(classes, @"HKEY_CURRENT_USER\Software\Classes", extension, progId, description, executable);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
		{
			Logger.Warn(ex, "Could not register {Extension} for the current user", extension);
		}
	}

	/// <summary>Writes the four keys under <paramref name="classes"/> unless they already hold these values.</summary>
	private static void Apply(RegistryKey classes, string classesName, string extension, string progId, string description, string executable)
	{
		string command = $"\"{executable}\" \"%1\"";
		string icon = $"\"{executable}\",0";
		if (IsCurrent(classes, extension, progId, command, icon))
		{
			// HKEY_CLASSES_ROOT is the merged view, so a per-user registration satisfies this check too
			Logger.Debug("{Extension} already opens with '{Executable}' (seen through {Classes})", extension, executable, classesName);
			return;
		}

		using (RegistryKey extensionKey = classes.CreateSubKey(extension))
		{
			extensionKey.SetValue("", progId);
		}
		using (RegistryKey progIdKey = classes.CreateSubKey(progId))
		{
			progIdKey.SetValue("", description);
			using RegistryKey iconKey = progIdKey.CreateSubKey("DefaultIcon");
			iconKey.SetValue("", icon);
			using RegistryKey commandKey = progIdKey.CreateSubKey(@"shell\open\command");
			commandKey.SetValue("", command);
		}

		ClearUserChoice(extension);
		SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
		Logger.Info("Registered {Extension} under {Classes} to open with '{Executable}'", extension, classesName, executable);
	}

	/// <summary>True when the extension, the open command and the icon already read as expected (read access only).</summary>
	private static bool IsCurrent(RegistryKey classes, string extension, string progId, string command, string icon) =>
		ReadDefault(classes, extension) == progId
		&& ReadDefault(classes, $@"{progId}\shell\open\command") == command
		&& ReadDefault(classes, $@"{progId}\DefaultIcon") == icon;

	private static string? ReadDefault(RegistryKey classes, string subKey)
	{
		using RegistryKey? key = classes.OpenSubKey(subKey);
		return key?.GetValue("") as string;
	}

	/// <summary>
	/// Removes the per-user "Open with" choice Explorer stores for the extension, which would
	/// otherwise keep winning over the ProgId. Deleting the key is allowed where changing its
	/// values is not; a refusal is logged and ignored.
	/// </summary>
	private static void ClearUserChoice(string extension)
	{
		try
		{
			using RegistryKey? fileExtension = Registry.CurrentUser.OpenSubKey(
				$@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}", writable: true);
			fileExtension?.DeleteSubKey("UserChoice", throwOnMissingSubKey: false);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
		{
			Logger.Debug("Could not clear the user's 'Open with' choice for {Extension}: {Message}", extension, ex.Message);
		}
	}

	[DllImport("shell32.dll")]
	private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
