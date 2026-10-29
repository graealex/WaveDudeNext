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
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveDudeNext.WPF;

/// <summary>
/// One shipped example: the embedded resource, the file it came from, its number and its title ("Example07_Random_emitter_lattice.wdproj"
/// is number 7, "Random emitter lattice").
/// </summary>
/// <param name="Resource">The manifest resource name, "Examples/" and the file name.</param>
/// <param name="FileName">The project file's name, suggested when the example is saved.</param>
/// <param name="Number">The two-digit number leading the file name, the examples' order.</param>
/// <param name="Title">The file name after the number, underscores as spaces.</param>
public sealed record ExampleEntry(string Resource, string FileName, int Number, string Title)
{
	/// <summary>The menu entry's text: the number and the title.</summary>
	public string Header => $"{Number}. {Title}";

	/// <summary>The menu entry's automation id, "MenuExample" and the two-digit number.</summary>
	public string AutomationId => $"MenuExample{Number:00}";
}

/// <summary>
/// The examples shipped inside the application (2026-10-29, the user's request): the project files of the repository's
/// Examples folder, embedded as resources named "Examples/&lt;file&gt;" (the project file), listed under File > Examples
/// in file order, a two-digit number leading each name, and opened as unsaved projects that suggest their file name
/// when saved. Read once on first use; any thread.
/// </summary>
public static class Examples
{
	/// <summary>The resource names' prefix.</summary>
	public const string ResourcePrefix = "Examples/";

	private static readonly Regex NamePattern = new(@"^Example(\d+)_(.+)\.wdproj$", RegexOptions.CultureInvariant);

	/// <summary>Every shipped example in file order.</summary>
	public static IReadOnlyList<ExampleEntry> All { get; } = Load();

	/// <summary>Opens an example's project file; the caller disposes the stream.</summary>
	/// <exception cref="FileNotFoundException">No such resource.</exception>
	public static Stream Open(ExampleEntry example) =>
		typeof(Examples).Assembly.GetManifestResourceStream(example.Resource) ?? throw new FileNotFoundException("The example is not in the application.", example.Resource);

	private static List<ExampleEntry> Load()
	{
		var examples = new List<ExampleEntry>();
		foreach (string resource in typeof(Examples).Assembly.GetManifestResourceNames())
		{
			if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
			{
				continue;
			}
			string fileName = resource[ResourcePrefix.Length..];
			Match match = NamePattern.Match(fileName);
			// a file outside the pattern is listed last, under its own name
			int number = match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue;
			string title = (match.Success ? match.Groups[2].Value : Path.GetFileNameWithoutExtension(fileName)).Replace('_', ' ');
			examples.Add(new ExampleEntry(resource, fileName, number, title));
		}
		examples.Sort((a, b) => a.Number != b.Number ? a.Number.CompareTo(b.Number) : string.CompareOrdinal(a.FileName, b.FileName));
		return examples;
	}
}
