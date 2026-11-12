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

using System.Reflection;
using System.Runtime.InteropServices;
using NLog;

namespace WaveDudeNext.Engine;

/// <summary>
/// Finds the OpenCL loader (the ICD loader the graphics driver installs) for <see cref="OpenClApi"/>'s imports on
/// each operating system (2026-11-12, for the Linux and macOS ports). The imports are declared against the name
/// <see cref="ImportName"/>, which the runtime turns into <c>OpenCL.dll</c> on Windows, where the driver installs it,
/// but into <c>libOpenCL.so</c> on Linux, a name only the development package provides (the driver ships
/// <c>libOpenCL.so.1</c>), and into <c>libOpenCL.dylib</c> on macOS, where the loader is a framework. So
/// <see cref="OpenClApi"/>'s first use installs a resolver on this assembly that tries <see cref="Candidates"/> in
/// order and keeps the handle; <see cref="Library"/> names the one loaded, for the log and the checks. When none loads,
/// the runtime's own probing runs and its <see cref="DllNotFoundException"/> reaches <see cref="WaveEngineFactory"/>,
/// which names the candidates in its reason for the CPU engine. Any thread; the first calls may resolve at once, each
/// loading (the OS counts the references), which is harmless.
/// </summary>
public static class OpenClLoader
{
	/// <summary>The import name the OpenCL functions are declared against.</summary>
	public const string ImportName = "OpenCL";

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private static readonly object Gate = new();

	private static int _installed;

	private static IntPtr _handle;

	/// <summary>The library names tried for the loader on this operating system, in order: <c>OpenCL.dll</c> on Windows, the OpenCL framework on macOS, <c>libOpenCL.so.1</c> then <c>libOpenCL.so</c> elsewhere.</summary>
	public static IReadOnlyList<string> Candidates { get; } =
		OperatingSystem.IsWindows() ? ["OpenCL.dll"]
		: OperatingSystem.IsMacOS() ? ["/System/Library/Frameworks/OpenCL.framework/OpenCL"]
		: ["libOpenCL.so.1", "libOpenCL.so"];

	/// <summary>The library loaded for the imports, one of <see cref="Candidates"/>; empty before the first OpenCL call and when none could be loaded.</summary>
	public static string Library { get; private set; } = "";

	/// <summary>Installs the resolver on the library's assembly once; <see cref="OpenClApi"/> calls it before its first import.</summary>
	internal static void Install()
	{
		if (Interlocked.Exchange(ref _installed, 1) == 0)
		{
			NativeLibrary.SetDllImportResolver(typeof(OpenClLoader).Assembly, Resolve);
		}
	}

	private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (libraryName != ImportName)
		{
			return IntPtr.Zero;
		}
		lock (Gate)
		{
			if (_handle != IntPtr.Zero)
			{
				return _handle;
			}
			foreach (string candidate in Candidates)
			{
				if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
				{
					_handle = handle;
					Library = candidate;
					Logger.Debug("OpenCL loader: {Library}", candidate);
					return handle;
				}
			}
			Logger.Debug("OpenCL loader: none of {Candidates} could be loaded", string.Join(", ", Candidates));
			return IntPtr.Zero;
		}
	}
}
