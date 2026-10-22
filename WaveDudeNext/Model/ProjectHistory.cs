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
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>
/// Undo and redo for a <see cref="Project"/> (2026-10-22, the user's request: unlimited, the project file being nearly the
/// whole state): a list of snapshots of everything the file holds, one per step, with a current position. A snapshot is
/// the project's XML without the embedded scene image, and the image's bytes by reference; an unchanged image (the same
/// array, or equal bytes) is shared with the step before, so a step costs the XML alone. <see cref="Record"/> adds a step
/// when the project differs from the current one; <see cref="Undo"/> and <see cref="Redo"/> write a step back into the same
/// project object, property by property, so its watchers report the change as an edit and nothing is replaced. The caller
/// decides when a step ends (after a quiet moment, never in the middle of a drag). UI thread, like the model.
/// </summary>
public sealed class ProjectHistory
{
	/// <summary>The project file's serializer without the scene image (<see cref="Scene.ImageData"/>), which a snapshot keeps by reference.</summary>
	private static readonly XmlSerializer Serializer = CreateSerializer();

	/// <summary>The persisted top-level properties, written back in declaration order: the lists' contents, the other values themselves.</summary>
	private static readonly PropertyInfo[] Persisted = [.. typeof(Project).GetProperties(BindingFlags.Public | BindingFlags.Instance)
		.Where(property => property.CanRead && property.CanWrite && property.GetCustomAttribute<XmlIgnoreAttribute>() == null)];

	private readonly Project _project;
	private readonly List<Snapshot> _steps = [];
	private int _current;
	private int _saved;

	/// <summary>Starts the history of <paramref name="project"/> with its present state as the first step, taken as saved.</summary>
	public ProjectHistory(Project project)
	{
		_project = project;
		_steps.Add(Capture(null));
	}

	/// <summary>Whether there is a step to go back to.</summary>
	public bool CanUndo => _current > 0;

	/// <summary>Whether there is an undone step to go forward to.</summary>
	public bool CanRedo => _current < _steps.Count - 1;

	/// <summary>Whether the current step is the one last saved (or the first, for a project never saved), so the file holds it.</summary>
	public bool IsAtSaved => _current == _saved;

	/// <summary>The number of steps, the first included.</summary>
	public int Count => _steps.Count;

	/// <summary>The current step's index, 0 for the first.</summary>
	public int Position => _current;

	/// <summary>
	/// Ends a step: when the project differs from the current step, its state becomes a new step after it, and the undone
	/// steps beyond are dropped. Returns whether a step was added; a change that leaves the file as it was adds none.
	/// </summary>
	public bool Record()
	{
		Snapshot snapshot = Capture(_steps[_current].Image);
		if (snapshot == _steps[_current])
		{
			return false;
		}
		_steps.RemoveRange(_current + 1, _steps.Count - _current - 1);
		if (_saved > _current)
		{
			// the saved step was undone and is now replaced: no step matches the file any more
			_saved = -1;
		}
		_steps.Add(snapshot);
		_current++;
		return true;
	}

	/// <summary>Marks the current step as the one the file holds (after a save, with the saved state recorded first).</summary>
	public void MarkSaved() => _saved = _current;

	/// <summary>Writes the step before the current one back into the project; false when there is none.</summary>
	public bool Undo()
	{
		if (!CanUndo)
		{
			return false;
		}
		Apply(_steps[--_current]);
		return true;
	}

	/// <summary>Writes the step after the current one back into the project; false when there is none.</summary>
	public bool Redo()
	{
		if (!CanRedo)
		{
			return false;
		}
		Apply(_steps[++_current]);
		return true;
	}

	/// <summary>The project's state now; its image shares <paramref name="previous"/> when the bytes are the same.</summary>
	private Snapshot Capture(byte[]? previous)
	{
		var text = new StringWriter();
		using (var writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = false }))
		{
			Serializer.Serialize(writer, _project);
		}
		byte[]? image = _project.Scene.ImageData;
		if (image != null && previous != null && !ReferenceEquals(image, previous) && image.AsSpan().SequenceEqual(previous))
		{
			image = previous;
		}
		return new Snapshot(text.ToString(), image);
	}

	/// <summary>
	/// Writes <paramref name="snapshot"/> into the project: read into a project of its own (normalised, so the sources'
	/// emitters are generated again), then each persisted top-level property moved over: a list's contents replaced (the
	/// list objects stay, and with them what is bound to them), any other value set.
	/// </summary>
	private void Apply(Snapshot snapshot)
	{
		Project state;
		using (var reader = XmlReader.Create(new StringReader(snapshot.Xml)))
		{
			state = (Project)Serializer.Deserialize(reader)!;
		}
		state.Scene.ImageData = snapshot.Image;
		state.Normalize();
		foreach (PropertyInfo property in Persisted)
		{
			object? value = property.GetValue(state);
			if (value is IList items && property.GetValue(_project) is IList target)
			{
				object[] moved = [.. items.Cast<object>()];
				items.Clear();
				target.Clear();
				foreach (object item in moved)
				{
					target.Add(item);
				}
			}
			else
			{
				property.SetValue(_project, value);
			}
		}
	}

	private static XmlSerializer CreateSerializer()
	{
		var overrides = new XmlAttributeOverrides();
		overrides.Add(typeof(Scene), nameof(Scene.ImageData), new XmlAttributes { XmlIgnore = true });
		return new XmlSerializer(typeof(Project), overrides);
	}

	/// <summary>One step: the project's XML without the scene image, and the image's bytes (shared between steps while unchanged).</summary>
	private sealed record Snapshot(string Xml, byte[]? Image)
	{
		/// <summary>Equal when the XML is and the image is the same array (equal bytes were made the same array by <see cref="Capture"/>).</summary>
		public bool Equals(Snapshot? other) => other != null && ReferenceEquals(Image, other.Image) && Xml == other.Xml;

		/// <inheritdoc/>
		public override int GetHashCode() => Xml.GetHashCode();
	}
}
