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

using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>
/// The project file's document type definition (2026-10-31, the user's request: a text editor that reads the DTD inside
/// the file completes element and attribute names and enumeration values, and marks a misspelt one). Built once per
/// process from the same serialization attributes <see cref="XmlSerializer"/> reads (<see cref="XmlRootAttribute"/>,
/// <see cref="XmlElementAttribute"/>, <see cref="XmlAttributeAttribute"/>, <see cref="XmlIgnoreAttribute"/>), so it cannot
/// drift from what <see cref="Project.Save(Stream)"/> writes: one <c>ELEMENT</c> per element name with its children in the
/// written order, each attribute an <c>ATTLIST</c> line, a bool or an enum an enumerated type, anything else <c>CDATA</c>.
/// No DTD defaults, so a reader that does parse the DTD never adds a value the file does not hold; an attribute the
/// serializer always writes (a value type without a <see cref="DefaultValueAttribute"/>) is <c>#REQUIRED</c>, the rest
/// <c>#IMPLIED</c>. A DTD declares an element name once, so a name must hold one type wherever it appears; two types under
/// one name fail the build of the text. <see cref="Project.Load(Stream)"/> skips the DTD without reading it. Thread-safe:
/// the text is built on first use and shared.
/// </summary>
public static class ProjectSchema
{
	/// <summary>
	/// Whether a comment with the setting's description (<see cref="DescriptionAttribute"/>), default
	/// (<see cref="DefaultValueAttribute"/>) and range (<see cref="RangeAttribute"/>) precedes each declaration, for a reader of
	/// the file: about three times the text, the declarations the same either way. The comment code is <see cref="Comment"/>
	/// and the methods below it; false leaves the comments out without touching it (2026-10-31, the user's choice after
	/// seeing them: the declarations alone).
	/// </summary>
	public const bool Comments = false;

	/// <summary>The root element's name, <c>Project</c>, from <see cref="Project"/>'s <see cref="XmlRootAttribute"/>.</summary>
	public static readonly string RootName = typeof(Project).GetCustomAttribute<XmlRootAttribute>()?.ElementName is { Length: > 0 } root ? root : nameof(Project);

	private static readonly string NewLine = Environment.NewLine;
	private static readonly Lazy<string> Subset = new(Build);

	/// <summary>
	/// The internal subset for <see cref="XmlWriter.WriteDocType(string, string?, string?, string?)"/>: the declarations, one
	/// per line, each indented by a tab, after a line break and ending in one, so the closing <c>]&gt;</c> stands on a line of
	/// its own.
	/// </summary>
	public static string InternalSubset => Subset.Value;

	/// <summary>One element of the file: its name, the type it holds (null for text content: a string, or bytes as base64) and the description its comment shows.</summary>
	private sealed record Element(string Name, Type? Type, string? Description);

	private static string Build()
	{
		var elements = new List<Element>();
		Declare(RootName, typeof(Project), typeof(Project).GetCustomAttribute<DescriptionAttribute>()?.Description, elements);
		var text = new StringBuilder(NewLine);
		foreach (Element element in elements)
		{
			Comment(text, Comments ? element.Description : null);
			text.Append("\t<!ELEMENT ").Append(element.Name).Append(' ').Append(element.Type == null ? "(#PCDATA)" : ContentModel(element.Type)).Append('>').Append(NewLine);
			if (element.Type == null)
			{
				continue;
			}
			foreach (PropertyInfo property in Members(element.Type))
			{
				if (property.GetCustomAttribute<XmlAttributeAttribute>() is not { } attribute)
				{
					continue;
				}
				Comment(text, Comments ? Describe(property) : null);
				text.Append("\t<!ATTLIST ").Append(element.Name).Append(' ').Append(attribute.AttributeName is { Length: > 0 } name ? name : property.Name)
					.Append(' ').Append(AttributeType(property.PropertyType)).Append(IsRequired(element.Type, property) ? " #REQUIRED>" : " #IMPLIED>").Append(NewLine);
			}
		}
		return text.ToString();
	}

	/// <summary>
	/// Adds the element <paramref name="name"/> holding <paramref name="type"/> to <paramref name="elements"/>, then every
	/// element its persisted members reach, depth first; a name already there must hold the same type and is not added again.
	/// </summary>
	private static void Declare(string name, Type type, string? description, List<Element> elements)
	{
		Type? content = IsText(type) ? null : type;
		if (elements.Find(element => element.Name == name) is { } known)
		{
			if (known.Type != content)
			{
				throw new InvalidOperationException($"Element '{name}' holds both {known.Type?.Name ?? "text"} and {content?.Name ?? "text"}; a DTD declares an element name once.");
			}
			return;
		}
		elements.Add(new Element(name, content, description));
		if (content == null)
		{
			return;
		}
		foreach (PropertyInfo property in Members(content))
		{
			if (property.IsDefined(typeof(XmlAttributeAttribute)))
			{
				continue;
			}
			string? about = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
			XmlElementAttribute[] names = [.. property.GetCustomAttributes<XmlElementAttribute>()];
			if (names.Length == 0)
			{
				Declare(property.Name, ItemType(property.PropertyType), about, elements);
			}
			foreach (XmlElementAttribute element in names)
			{
				Type held = element.Type ?? ItemType(property.PropertyType);
				// one property with several names (the part kinds) describes the list, not a kind: each kind's class speaks for itself
				Declare(ElementName(property, element), held, names.Length == 1 ? about : held.GetCustomAttribute<DescriptionAttribute>()?.Description, elements);
			}
		}
	}

	/// <summary>The content model of an element holding <paramref name="type"/>: its child elements in the written order, a list's name starred, a single one optional; <c>EMPTY</c> without any.</summary>
	private static string ContentModel(Type type)
	{
		var children = new List<string>();
		foreach (PropertyInfo property in Members(type))
		{
			if (property.IsDefined(typeof(XmlAttributeAttribute)))
			{
				continue;
			}
			XmlElementAttribute[] names = [.. property.GetCustomAttributes<XmlElementAttribute>()];
			string child = names.Length switch
			{
				0 => property.Name,
				1 => ElementName(property, names[0]),
				_ => "(" + string.Join("|", names.Select(element => ElementName(property, element))) + ")",
			};
			children.Add(child + (IsMany(property.PropertyType) ? "*" : "?"));
		}
		return children.Count == 0 ? "EMPTY" : "(" + string.Join(", ", children) + ")";
	}

	/// <summary>The element name <paramref name="element"/> gives <paramref name="property"/>, the property's own without one.</summary>
	private static string ElementName(PropertyInfo property, XmlElementAttribute element) => element.ElementName is { Length: > 0 } name ? name : property.Name;

	/// <summary>Whether a value of <paramref name="type"/> is written as text (a string, or bytes as base64), not as an element with content of its own.</summary>
	private static bool IsText(Type type) => type == typeof(string) || type == typeof(byte[]);

	/// <summary>Whether <paramref name="type"/> is a list, written as repeated elements.</summary>
	private static bool IsMany(Type type) => !IsText(type) && typeof(IEnumerable).IsAssignableFrom(type);

	/// <summary>The type a member of <paramref name="type"/> writes: a list's item type, else the type itself.</summary>
	private static Type ItemType(Type type) => IsMany(type) && type.IsGenericType ? type.GetGenericArguments()[0] : type;

	/// <summary>
	/// The persisted members of <paramref name="type"/> in the serializer's order: public properties with a public getter and a
	/// public setter (a list needs none), not <see cref="XmlIgnoreAttribute"/>d, a base class's before a derived class's, each
	/// class's in declaration order.
	/// </summary>
	private static IEnumerable<PropertyInfo> Members(Type type) => type
		.GetProperties(BindingFlags.Public | BindingFlags.Instance)
		.Where(property => property.GetMethod?.IsPublic == true && (property.SetMethod?.IsPublic == true || IsMany(property.PropertyType))
			&& property.GetIndexParameters().Length == 0 && !property.IsDefined(typeof(XmlIgnoreAttribute)))
		.OrderBy(property => Depth(property.DeclaringType!))
		.ThenBy(property => property.MetadataToken);

	private static int Depth(Type type) => type.BaseType == null ? 0 : Depth(type.BaseType) + 1;

	/// <summary>The attribute type: the two values of a bool, an enum's members as the serializer writes them, <c>CDATA</c> for the rest.</summary>
	private static string AttributeType(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (type == typeof(bool))
		{
			return "(true|false)";
		}
		if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute)))
		{
			return "(" + string.Join("|", type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(EnumName)) + ")";
		}
		return "CDATA";
	}

	/// <summary>The name the serializer writes for an enum member: its <see cref="XmlEnumAttribute"/>'s, else its own.</summary>
	private static string EnumName(FieldInfo member) => member.GetCustomAttribute<XmlEnumAttribute>()?.Name is { Length: > 0 } name ? name : member.Name;

	/// <summary>
	/// Whether the serializer writes the attribute into every file: a value type without a <see cref="DefaultValueAttribute"/>
	/// and without a <c>ShouldSerialize</c> method; a string is left out when null, so it stays optional.
	/// </summary>
	private static bool IsRequired(Type type, PropertyInfo property) =>
		property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null
		&& !property.IsDefined(typeof(DefaultValueAttribute)) && type.GetMethod("ShouldSerialize" + property.Name) == null;

	// ---------------------------------------------------------------- the comments (Comments): the declarations use none of this

	/// <summary>Appends a comment line holding <paramref name="content"/>; nothing for none. A double hyphen cannot stand inside a comment and becomes a single one.</summary>
	private static void Comment(StringBuilder text, string? content)
	{
		if (!string.IsNullOrEmpty(content))
		{
			text.Append("\t<!-- ").Append(content.Replace("--", "-")).Append(" -->").Append(NewLine);
		}
	}

	/// <summary>The comment of an attribute: the property's description, its default and its range, each a sentence; null without any.</summary>
	private static string? Describe(PropertyInfo property)
	{
		var text = new StringBuilder();
		if (property.GetCustomAttribute<DescriptionAttribute>()?.Description is { Length: > 0 } description)
		{
			text.Append(description.TrimEnd('.', ' ')).Append('.');
		}
		if (property.GetCustomAttribute<DefaultValueAttribute>() is { } fallback)
		{
			text.Append(" Default ").Append(Format(fallback.Value)).Append('.');
		}
		if (property.GetCustomAttribute<RangeAttribute>() is { } range)
		{
			text.Append(' ').Append(Describe(range)).Append('.');
		}
		return text.Length == 0 ? null : text.ToString().Trim();
	}

	/// <summary>A range as a phrase: "Range 16 to 8192", "Range above -180 to 180" (an exclusive bound), "At least 0", "At most 100"; a bound at the type's extreme is no bound.</summary>
	private static string Describe(RangeAttribute range)
	{
		string min = Format(range.Minimum), max = Format(range.Maximum);
		return (IsExtreme(range.Minimum), IsExtreme(range.Maximum)) switch
		{
			(true, true) => "Any value",
			(false, true) => (range.MinimumIsExclusive ? "Above " : "At least ") + min,
			(true, false) => (range.MaximumIsExclusive ? "Below " : "At most ") + max,
			_ => "Range " + (range.MinimumIsExclusive ? "above " : "") + min + " to " + (range.MaximumIsExclusive ? "below " : "") + max,
		};
	}

	/// <summary>Whether a bound is its type's extreme, which the model uses for "no bound".</summary>
	private static bool IsExtreme(object? bound) =>
		bound is int.MaxValue or int.MinValue or double.MaxValue or double.MinValue or double.PositiveInfinity or double.NegativeInfinity;

	/// <summary>A default or a bound as the file writes the value: invariant numbers, lower-case bools, an enum member's name; "empty" for an empty string.</summary>
	private static string Format(object? value) => value switch
	{
		null => "none",
		"" => "empty",
		string text => text,
		bool flag => XmlConvert.ToString(flag),
		int number => XmlConvert.ToString(number),
		long number => XmlConvert.ToString(number),
		double number => XmlConvert.ToString(number),
		float number => XmlConvert.ToString(number),
		Enum member => member.GetType().GetField(member.ToString()) is { } field ? EnumName(field) : member.ToString(),
		_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
	};
}
