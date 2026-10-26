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
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml;
using System.Xml.Serialization;
using NLog;

namespace WaveDudeNext.WPF.Docking;

/// <summary>
/// Serializable snapshot of a <see cref="DockHost"/> layout tree. Items are referenced by
/// <see cref="DockItem.Id"/>; unknown ids are ignored on load. Persisted with
/// <see cref="XmlSerializer"/>; a host's default arrangement is written in XAML (the node classes are top-level, as an XML
/// name cannot hold a nested type's "+"; children and item ids are content, 2026-10-26).
/// </summary>
[XmlRoot("DockLayout")]
public sealed class DockLayout
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
	private static readonly XmlSerializer Serializer = new(typeof(DockLayout));

	/// <summary>Root of the tree: a split, a group, or the document node.</summary>
	[XmlElement("Split", typeof(SplitLayout))]
	[XmlElement("Group", typeof(GroupLayout))]
	[XmlElement("Document", typeof(DocumentLayout))]
	public NodeLayout? Root { get; set; }

	/// <summary>Writes the layout as indented XML.</summary>
	public void Save(Stream stream)
	{
		using var writer = XmlWriter.Create(stream, new XmlWriterSettings
		{
			Indent = true,
			IndentChars = "\t",
			Encoding = new System.Text.UTF8Encoding(false),
		});
		Serializer.Serialize(writer, this, new XmlSerializerNamespaces([XmlQualifiedName.Empty]));
	}

	/// <summary>Reads a layout written by <see cref="Save"/>. Returns null on malformed input.</summary>
	public static DockLayout? Load(Stream stream)
	{
		try
		{
			using var reader = XmlReader.Create(stream);
			return Serializer.Deserialize(reader) as DockLayout;
		}
		catch (InvalidOperationException ex)
		{
			Logger.Warn(ex, "The layout file does not hold a dock layout");
			return null;
		}
		catch (XmlException ex)
		{
			Logger.Warn(ex, "The layout file is not well-formed XML");
			return null;
		}
	}
}

/// <summary>A node of a <see cref="DockLayout"/>: a split, a group or the document area; <see cref="Length"/> is its share in the parent split.</summary>
public abstract class NodeLayout
{
	[XmlAttribute]
	public double Length { get; set; } = 1.0;
}

/// <summary>A split of a <see cref="DockLayout"/>, its children in content order.</summary>
[ContentProperty(nameof(Children))]
public sealed class SplitLayout : NodeLayout
{
	[XmlAttribute]
	public Orientation Orientation { get; set; }

	[XmlElement("Split", typeof(SplitLayout))]
	[XmlElement("Group", typeof(GroupLayout))]
	[XmlElement("Document", typeof(DocumentLayout))]
	public List<NodeLayout> Children { get; set; } = new();
}

/// <summary>A tabbed group of a <see cref="DockLayout"/>: the item ids as content, the active one named.</summary>
[ContentProperty(nameof(Items))]
public sealed class GroupLayout : NodeLayout
{
	[XmlAttribute]
	public string? Active { get; set; }

	[XmlElement("Item")]
	public List<string> Items { get; set; } = new();
}

/// <summary>The document area of a <see cref="DockLayout"/>.</summary>
public sealed class DocumentLayout : NodeLayout
{
}
