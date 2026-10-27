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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.Model;

/// <summary>What changed in a watched project: the object and the property, or a list when items were added or removed.</summary>
/// <param name="Item">The object that changed (a <see cref="Scene"/>, <see cref="Source"/>, …) or the collection.</param>
/// <param name="Property">The property name, or null for a collection change.</param>
/// <param name="Values">The value before and after when the edit went through <see cref="ObservableObject.SetProperty{T}"/>; null for a derived property or a list.</param>
public sealed record ProjectChange(object Item, string? Property, PropertyValueChangedEventArgs? Values = null)
{
	/// <summary>The value before the edit as the log shows it (<see cref="Format"/>); "" without <see cref="Values"/>.</summary>
	public string OldValueText => Values == null ? "" : Format(Values.OldValue);

	/// <summary>The value set as the log shows it (<see cref="Format"/>); "" without <see cref="Values"/>.</summary>
	public string NewValueText => Values == null ? "" : Format(Values.NewValue);

	/// <summary>A value as the log shows it: numbers and enums invariant, a string in quotes, a byte array by its length, null as "null", anything else by its text.</summary>
	public static string Format(object? value) => value switch
	{
		null => "null",
		string text => "'" + text + "'",
		byte[] bytes => "byte[" + bytes.Length.ToString(CultureInfo.InvariantCulture) + "]",
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? "",
	};

	/// <summary>
	/// Whether the change alters the frames a run computes: the scene, the solver settings, the seed, a source or an
	/// optical part (painted into the scene). Display settings, probes (see <see cref="AffectsProbes"/>) and the
	/// description do not.
	/// </summary>
	public bool AffectsSimulation => Item switch
	{
		Scene when Property == nameof(Scene.ImagePath) => false,
		Scene or Simulation or Source or SourceArray or OpticalPart or PartPoint or FiberNode => true,
		Project => Property is nameof(Project.Seed) or nameof(Project.Scene) or nameof(Project.Simulation) or nameof(Project.Sources) or nameof(Project.Parts),
		ObservableCollection<ColorScheme> or ObservableCollection<Probe> => false,
		INotifyCollectionChanged => true,
		_ => false,
	};

	/// <summary>
	/// Whether the change alters what a run records for its probes, and nothing else: what a probe measures
	/// (<see cref="Probe.AffectsMeasurement"/>) or the probe list. A probe never changes the picture (the
	/// user's rule, 2026-10-14), so a run's frames stay valid and only its recordings go stale. The window makes one
	/// exception: an active probe the run does not record (switched on or added) is treated as a simulation edit (2026-10-18).
	/// </summary>
	public bool AffectsProbes => Item switch
	{
		Probe => Probe.AffectsMeasurement(Property),
		Project => Property == nameof(Project.Probes),
		ObservableCollection<Probe> => true,
		_ => false,
	};
}

/// <summary>
/// Raises one event for any change anywhere in a <see cref="Project"/>: the nested settings objects, the
/// source, part and probe lists and their items, a part's point list included (re-subscribing when objects are
/// replaced or items come and go).
/// The model has no aggregate notification of its own; this is it. UI thread, like the model.
/// </summary>
public sealed class ProjectWatcher : IDisposable
{
	private readonly Project _project;
	private readonly HashSet<INotifyPropertyChanged> _subscribed = [];
	private readonly HashSet<INotifyCollectionChanged> _collections = [];

	/// <param name="project">The project to watch for its lifetime (or until <see cref="Dispose"/>).</param>
	public ProjectWatcher(Project project)
	{
		_project = project;
		project.PropertyChanged += OnProjectPropertyChanged;
		Resubscribe();
	}

	/// <summary>Something in the project changed.</summary>
	public event EventHandler<ProjectChange>? Changed;

	/// <summary>Stops watching.</summary>
	public void Dispose()
	{
		_project.PropertyChanged -= OnProjectPropertyChanged;
		Unsubscribe();
	}

	private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(Project.Scene) or nameof(Project.Simulation) or nameof(Project.Display) or nameof(Project.Sources) or nameof(Project.Parts) or nameof(Project.Probes) or nameof(Project.ColorSchemes))
		{
			Resubscribe();
		}
		Changed?.Invoke(this, new ProjectChange(_project, e.PropertyName, e as PropertyValueChangedEventArgs));
	}

	private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is Source && e.PropertyName == nameof(Source.Array) || sender is Display && e.PropertyName is nameof(Display.Field) or nameof(Display.Intensity) or nameof(Display.Video)
			|| sender is OutlinePart && e.PropertyName == nameof(OutlinePart.Points) || sender is FiberPart && e.PropertyName == nameof(FiberPart.Nodes))
		{
			Resubscribe();
		}
		if (sender != null)
		{
			Changed?.Invoke(this, new ProjectChange(sender, e.PropertyName, e as PropertyValueChangedEventArgs));
		}
	}

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		Resubscribe();
		// the item is the collection itself, so a handler can tell which list changed
		Changed?.Invoke(this, new ProjectChange(sender ?? e, null));
	}

	private void Resubscribe()
	{
		Unsubscribe();
		Subscribe(_project.Scene);
		Subscribe(_project.Simulation);
		Subscribe(_project.Display);
		Subscribe(_project.Display.Field);
		Subscribe(_project.Display.Intensity);
		Subscribe(_project.Display.Video);
		SubscribeCollection(_project.Sources);
		SubscribeCollection(_project.Probes);
		foreach (Source source in _project.Sources)
		{
			Subscribe(source);
			if (source.Array != null)
			{
				Subscribe(source.Array);
			}
		}
		SubscribeCollection(_project.Parts);
		foreach (OpticalPart part in _project.Parts)
		{
			Subscribe(part);
			switch (part)
			{
				case OutlinePart outline:
					SubscribeCollection(outline.Points);
					foreach (PartPoint point in outline.Points)
					{
						Subscribe(point);
					}
					break;
				case FiberPart fiber:
					SubscribeCollection(fiber.Nodes);
					foreach (FiberNode node in fiber.Nodes)
					{
						Subscribe(node);
					}
					break;
			}
		}
		foreach (Probe probe in _project.Probes)
		{
			Subscribe(probe);
		}
		SubscribeCollection(_project.ColorSchemes);
		foreach (ColorScheme scheme in _project.ColorSchemes)
		{
			Subscribe(scheme);
		}
	}

	private void Subscribe(INotifyPropertyChanged item)
	{
		if (_subscribed.Add(item))
		{
			item.PropertyChanged += OnItemPropertyChanged;
		}
	}

	private void SubscribeCollection(INotifyCollectionChanged collection)
	{
		if (_collections.Add(collection))
		{
			collection.CollectionChanged += OnCollectionChanged;
		}
	}

	private void Unsubscribe()
	{
		foreach (INotifyPropertyChanged item in _subscribed)
		{
			item.PropertyChanged -= OnItemPropertyChanged;
		}
		_subscribed.Clear();
		foreach (INotifyCollectionChanged collection in _collections)
		{
			collection.CollectionChanged -= OnCollectionChanged;
		}
		_collections.Clear();
	}
}
