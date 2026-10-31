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


using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace WaveDudeNext.WPF.Help;

/// <summary>
/// MANUAL.md as a <see cref="FlowDocument"/> and its headings as a contents tree. The build turns the Markdown into the
/// Page <c>Help/Manual.xaml</c> (<c>Build/Documents.targets</c>), compiled to BAML: each heading's paragraph tagged
/// <c>h{level}#{anchor}</c>, each figure a canvas over its screenshot, the screenshots WPF resources of the assembly.
/// UI thread (the document is a WPF object).
/// </summary>
public static partial class ManualDocument
{
	private static readonly Uri Source = new("/WaveDudeNext.WPF;component/Help/Manual.xaml", UriKind.Relative);

	[GeneratedRegex(@"^h([1-6])#(.*)$")]
	private static partial Regex HeadingTag();

	/// <summary>
	/// A new copy of the manual (<see cref="Application.LoadComponent(Uri)"/>), its lines stacked at whole pixels: each
	/// paragraph's line height is its font's line spacing rounded up, so every block starts on a whole pixel and a
	/// screenshot that fits the column is drawn 1:1 at 100 % rather than resampled between pixels. A build without the
	/// page gets a one-paragraph document saying so.
	/// </summary>
	public static FlowDocument Load()
	{
		FlowDocument document;
		try
		{
			document = (FlowDocument)Application.LoadComponent(Source);
		}
		catch (IOException)
		{
			return new FlowDocument(new Paragraph(new Run("MANUAL.md is not compiled into this build.")));
		}
		foreach (Paragraph paragraph in Paragraphs(document.Blocks))
		{
			paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			paragraph.LineHeight = Math.Ceiling(paragraph.FontSize * paragraph.FontFamily.LineSpacing);
		}
		return document;
	}

	/// <summary>The document's headings as a tree, each under the nearest heading of a lower level before it.</summary>
	public static List<ManualHeading> Contents(FlowDocument document)
	{
		var roots = new List<ManualHeading>();
		var open = new Stack<ManualHeading>();
		foreach (Paragraph paragraph in Paragraphs(document.Blocks))
		{
			if (paragraph.Tag is not string tag || HeadingTag().Match(tag) is not { Success: true } match)
			{
				continue;
			}
			var heading = new ManualHeading(new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text,
				int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value, paragraph);
			while (open.Count > 0 && open.Peek().Level >= heading.Level)
			{
				open.Pop();
			}
			(open.Count > 0 ? open.Peek().Children : roots).Add(heading);
			open.Push(heading);
		}
		return roots;
	}

	/// <summary>Every paragraph in document order, those in lists and sections included.</summary>
	private static IEnumerable<Paragraph> Paragraphs(IEnumerable<Block> blocks)
	{
		foreach (Block block in blocks)
		{
			IEnumerable<Paragraph> inner = block switch
			{
				Paragraph paragraph => [paragraph],
				List list => list.ListItems.SelectMany(item => Paragraphs(item.Blocks)),
				Section section => Paragraphs(section.Blocks),
				_ => [],
			};
			foreach (Paragraph paragraph in inner)
			{
				yield return paragraph;
			}
		}
	}
}
