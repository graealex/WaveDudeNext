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
using System.Xml;
using System.Xml.Serialization;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.Model;

/// <summary>
/// Copy and paste of project objects (2026-10-23, the user's request and choices): sources, parts, probes and colour
/// schemes, and the Scene, Simulation and Display settings. The clipboard text is an XML fragment of the objects' own
/// elements, as the project file writes them (<c>&lt;Source …/&gt;&lt;Fiber …&gt;…&lt;/Fiber&gt;</c>, defaults left out) and
/// nothing else: no wrapper, seed or version. The element names come from the <see cref="XmlElementAttribute"/>s on
/// <see cref="Project"/>, so the fragment cannot drift from the file. <see cref="Paste"/> puts the objects into a project as
/// Duplicate does: behind the current item when that is in the same list, else at the end, at the same canvas position, a
/// name with " copy" only when the name is taken; a settings object replaces the project's, pasted onto its own row or the
/// project root (2026-10-23, the user's addition).
/// UI thread, like the model.
/// </summary>
public static class ProjectClipboard
{
	/// <summary>The clipboard format of the objects (the same text is offered as plain text too).</summary>
	public const string Format = "WaveDudeNext.Objects";

	/// <summary>The settings objects of a project: replaced as a whole by a paste onto their row or the project root.</summary>
	private static readonly (Type Type, Func<Project, object> Get, Action<Project, object> Set)[] SettingsObjects =
	[
		(typeof(Scene), project => project.Scene, (project, value) => project.Scene = (Scene)value),
		(typeof(Simulation), project => project.Simulation, (project, value) => project.Simulation = (Simulation)value),
		(typeof(Display), project => project.Display, (project, value) => project.Display = (Display)value),
	];

	/// <summary>Each copyable type's element name in the project file, and back.</summary>
	private static readonly Dictionary<Type, string> ElementOf = ElementNames();
	private static readonly Dictionary<string, Type> TypeOf = ElementOf.ToDictionary(entry => entry.Value, entry => entry.Key);

	/// <summary>One serializer per type, made once: a serializer with its own root makes a new assembly each time it is created.</summary>
	private static readonly Dictionary<Type, XmlSerializer> Serializers = [];
	private static readonly XmlSerializerNamespaces NoNamespaces = new([XmlQualifiedName.Empty]);

	/// <summary>Whether <paramref name="item"/> of <paramref name="project"/> can be copied: a list item or a settings object.</summary>
	public static bool CanCopy(Project project, object item) =>
		item is INamed || SettingsObjects.Any(entry => ReferenceEquals(entry.Get(project), item));

	/// <summary>The clipboard text for <paramref name="items"/> of <paramref name="project"/>: their elements in tree order; items that cannot be copied are left out.</summary>
	public static string Write(Project project, IEnumerable<object> items)
	{
		var chosen = items.ToHashSet(ReferenceEqualityComparer.Instance);
		IEnumerable<object> ordered = SettingsObjects.Select(entry => entry.Get(project))
			.Concat(project.Sources).Concat(project.Parts).Concat(project.Probes).Concat(project.ColorSchemes)
			.Where(chosen.Contains);
		// one writer per element: the serializer starts a document, which a fragment writer refuses; the elements are then
		// joined line by line into the fragment
		var settings = new XmlWriterSettings { Indent = true, IndentChars = "\t", OmitXmlDeclaration = true };
		return string.Join(Environment.NewLine, ordered.Select(item =>
		{
			var text = new StringWriter();
			using (var writer = XmlWriter.Create(text, settings))
			{
				// written as it is: serialising does not change the object
				SerializerOf(item.GetType()).Serialize(writer, item, NoNamespaces);
			}
			return text.ToString();
		}));
	}

	/// <summary>The objects in clipboard text made by <see cref="Write"/>, normalised; null for text holding anything else or nothing.</summary>
	public static Clip? Read(string text)
	{
		var objects = new List<object>();
		try
		{
			using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment });
			while (reader.MoveToContent() == XmlNodeType.Element)
			{
				if (!TypeOf.TryGetValue(reader.LocalName, out Type? type))
				{
					return null;
				}
				object item = SerializerOf(type).Deserialize(reader)!;
				Normalize(item);
				objects.Add(item);
			}
		}
		catch (Exception e) when (e is XmlException or InvalidOperationException)
		{
			return null;
		}
		return objects.Count > 0 ? new Clip(objects) : null;
	}

	/// <summary>
	/// Puts the objects of <paramref name="clip"/> into <paramref name="project"/>: each list's items behind
	/// <paramref name="current"/> when it is in that list (the later ones behind the earlier), else at its end; a settings
	/// object replaces the project's when <paramref name="current"/> is the project's object of that kind or the project itself. Returns what was
	/// put in, in that order. With <paramref name="before"/> (a drop on the upper half of a row) the items go in front of
	/// <paramref name="current"/> instead. A clip is used once: its objects become the project's.
	/// </summary>
	public static IReadOnlyList<object> Paste(Project project, Clip clip, object? current, bool before = false)
	{
		var pasted = new List<object>();
		foreach ((Type type, Func<Project, object> get, Action<Project, object> set) in SettingsObjects)
		{
			if (Accepts(project, get, current) && clip.Objects.FirstOrDefault(type.IsInstanceOfType) is { } value)
			{
				set(project, value);
				pasted.Add(value);
			}
		}
		Insert(project.Sources, clip.Objects.OfType<Source>(), current, before, pasted);
		Insert(project.Parts, clip.Objects.OfType<OpticalPart>(), current, before, pasted);
		Insert(project.Probes, clip.Objects.OfType<Probe>(), current, before, pasted);
		Insert(project.ColorSchemes, clip.Objects.OfType<ColorScheme>(), current, before, pasted);
		foreach (Source source in pasted.OfType<Source>())
		{
			// the emitters follow the seed of the project they are in now
			source.GenerateElements(project.Seed);
		}
		return pasted;
	}

	/// <summary>
	/// Moves pasted objects as a group so that the first movable one's first anchor (a source's or part's position, a
	/// probe's point A) lands on canvas pixel (<paramref name="x"/>, <paramref name="y"/>), held inside a canvas of
	/// <paramref name="width"/> × <paramref name="height"/> as a group drag is (<see cref="Movable.Limit"/>): a drop onto the
	/// picture (2026-10-23, the user's choice). Schemes and settings objects do not move.
	/// </summary>
	public static void PlaceAt(IEnumerable<object> pasted, int x, int y, int width, int height)
	{
		List<IMovable> movables = [.. pasted.OfType<IMovable>()];
		if (movables.Count == 0 || !movables[0].Anchors.Any())
		{
			return;
		}
		(int anchorX, int anchorY) = movables[0].Anchors.First();
		(int dx, int dy) = Movable.Limit(movables, x - anchorX, y - anchorY, width, height);
		Movable.MoveAll(movables, dx, dy);
	}

	/// <summary>Puts <paramref name="items"/> into <paramref name="list"/> behind (or before) <paramref name="current"/> when that is one of its items, else at its end, renamed where the name is taken.</summary>
	private static void Insert<T>(IList<T> list, IEnumerable<T> items, object? current, bool before, List<object> pasted)
		where T : class, INamed
	{
		int at = current is T anchor && list.IndexOf(anchor) is >= 0 and var index ? (before ? index : index + 1) : list.Count;
		foreach (T item in items)
		{
			if (list.Any(other => string.Equals(other.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
			{
				item.Name = Project.CopyName(item.Name, list.Select(other => other.Name));
			}
			list.Insert(at++, item);
			pasted.Add(item);
		}
	}

	/// <summary>Whether a settings object pastes with <paramref name="current"/> the current item: onto its own row, or onto the project root.</summary>
	private static bool Accepts(Project project, Func<Project, object> get, object? current) =>
		ReferenceEquals(get(project), current) || ReferenceEquals(project, current);

	/// <summary>Clamps a read object into range, as loading a project does.</summary>
	private static void Normalize(object item)
	{
		switch (item)
		{
			case Scene scene: scene.Normalize(); break;
			case Simulation simulation: simulation.Normalize(); break;
			case Display display: display.Normalize(); break;
			case Source source: source.Normalize(); break;
			case OpticalPart part: part.Normalize(); break;
			case Probe probe: probe.Normalize(); break;
			case ColorScheme scheme: scheme.Normalize(); break;
		}
	}

	private static XmlSerializer SerializerOf(Type type)
	{
		if (!Serializers.TryGetValue(type, out XmlSerializer? serializer))
		{
			serializer = new XmlSerializer(type, new XmlRootAttribute(ElementOf[type]));
			Serializers[type] = serializer;
		}
		return serializer;
	}

	/// <summary>The element name of every type a project's elements hold, read from <see cref="Project"/>'s own attributes.</summary>
	private static Dictionary<Type, string> ElementNames()
	{
		var names = new Dictionary<Type, string>();
		foreach (PropertyInfo property in typeof(Project).GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			foreach (XmlElementAttribute element in property.GetCustomAttributes<XmlElementAttribute>())
			{
				Type type = element.Type ?? (property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments()[0] : property.PropertyType);
				names[type] = string.IsNullOrEmpty(element.ElementName) ? property.Name : element.ElementName;
			}
		}
		// the description is a plain string, not an object one copies
		names.Remove(typeof(string));
		return names;
	}

	/// <summary>Objects read from the clipboard, in the order they were written.</summary>
	/// <param name="Objects">Sources, parts, probes, schemes and settings objects, not in any project yet.</param>
	public sealed record Clip(IReadOnlyList<object> Objects)
	{
		/// <summary>Whether the clip holds list items (sources, parts, probes, schemes).</summary>
		public bool HasItems => Objects.Any(item => item is INamed);

		/// <summary>Whether the clip can be pasted with <paramref name="current"/> the project's current item: it has list items, or a settings object of the current row's kind, or of any kind onto the project root.</summary>
		public bool CanPaste(Project project, object? current) =>
			HasItems || SettingsObjects.Any(entry => Accepts(project, entry.Get, current) && Objects.Any(entry.Type.IsInstanceOfType));
	}
}
