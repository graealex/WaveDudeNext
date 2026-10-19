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

using NLog;
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.WPF.Rendering;

/// <summary>
/// The colour schemes available to a project: the project's own (<see cref="Project.ColorSchemes"/>,
/// consulted first, so a project scheme shadows a built-in of the same name), then the user's folder
/// (<see cref="ColorSchemeStore.UserFolder"/>), then the ones built into the library; the last two
/// are loaded once. Also a cache of the palettes built from the shared schemes for the display
/// settings in use; a project scheme's palette is built on every request, since it may be mid-edit
/// and 255 entries cost nothing. UI thread only; the <c>int[]</c> palettes are immutable and may be
/// handed to rendering threads.
/// </summary>
public sealed class ColorSchemeCatalog
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>The single catalog of the application.</summary>
	public static ColorSchemeCatalog Instance { get; } = new();

	private readonly Dictionary<(string Name, double BlackLevel), int[]?> _luts = new();
	private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);
	private List<ColorScheme>? _schemes;

	private ColorSchemeCatalog()
	{
	}

	/// <summary>Reads the user's folder and the built-in resources; each unreadable one is logged as a warning. Called at start-up, or lazily on first use.</summary>
	public void Load()
	{
		var errors = new List<string>();
		List<ColorScheme> user = ColorSchemeStore.LoadFolder(ColorSchemeStore.UserFolder, errors);
		List<ColorScheme> builtIn = ColorSchemeStore.LoadBuiltIn(errors);
		_schemes = [.. user, .. builtIn];
		foreach (string error in errors)
		{
			Logger.Warn("Colour scheme not loaded: {Error}", error);
		}
		Logger.Info("{BuiltIn} built-in colour schemes, {User} from '{Folder}'", builtIn.Count, user.Count, ColorSchemeStore.UserFolder);
		_luts.Clear();
	}

	/// <summary>The shared schemes (user folder, then built-in), in file name order; not the project's.</summary>
	public IReadOnlyList<ColorScheme> Schemes
	{
		get
		{
			if (_schemes == null)
			{
				Load();
			}
			return _schemes!;
		}
	}

	/// <summary>The scheme a name refers to in <paramref name="project"/>: its own first, then the shared ones; null when unknown.</summary>
	public ColorScheme? Resolve(Project? project, string name) =>
		(project == null ? null : ColorSchemeStore.Find(project.ColorSchemes, name)) ?? ColorSchemeStore.Find(Schemes, name);

	/// <summary>
	/// The schemes a picker offers for one output: the project's own (all of them), then the shared ones
	/// whose <see cref="ColorScheme.Kind"/> is <paramref name="kind"/> or unspecified.
	/// </summary>
	public IEnumerable<ColorScheme> Choices(Project? project, ColorSchemeKind kind)
	{
		if (project != null)
		{
			foreach (ColorScheme scheme in project.ColorSchemes)
			{
				yield return scheme;
			}
		}
		foreach (ColorScheme scheme in Schemes)
		{
			if (scheme.Kind == kind || scheme.Kind == ColorSchemeKind.Unspecified)
			{
				yield return scheme;
			}
		}
	}

	/// <summary>
	/// The palette of the scheme called <paramref name="name"/> with the given black level; null (greyscale)
	/// when no such scheme exists, which is logged once per name. Shared schemes are cached; a project scheme is built fresh.
	/// </summary>
	public int[]? LutFor(Project? project, string name, double blackLevel)
	{
		if (project != null && ColorSchemeStore.Find(project.ColorSchemes, name) is { } own)
		{
			return ColorLut.Build(own, blackLevel: blackLevel).Argb;
		}
		(string, double) key = (name, blackLevel);
		if (_luts.TryGetValue(key, out int[]? lut))
		{
			return lut;
		}
		ColorScheme? scheme = ColorSchemeStore.Find(Schemes, name);
		if (scheme == null)
		{
			if (_warned.Add(name))
			{
				Logger.Warn("Colour scheme '{Name}' not found; showing greyscale", name);
			}
			lut = null;
		}
		else
		{
			lut = ColorLut.Build(scheme, blackLevel: blackLevel).Argb;
		}
		_luts[key] = lut;
		return lut;
	}
}
