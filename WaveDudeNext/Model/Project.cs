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
using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;
using NLog;
using WaveDudeNext.Rendering;

namespace WaveDudeNext.Model;

/// <summary>
/// A wave simulation project: the scene, solver and display settings plus the sources, optical
/// parts and probes. Persisted as sparse XML through <see cref="XmlSerializer"/>: scalars are attributes,
/// sources, parts and probes are repeated <c>Source</c>, <c>Lens</c> … <c>GrinLens</c> and <c>Probe</c> elements
/// directly under the root, and values equal to their defaults are left out. Properties carry
/// <see cref="System.ComponentModel"/> metadata for property grids.
/// </summary>
[XmlRoot("Project")]
public sealed class Project : ObservableObject
{
	/// <summary>Format version written to new files; bump when the layout changes incompatibly.</summary>
	public const int CurrentVersion = 3;

	private int _version = CurrentVersion;
	private int _seed = NewSeed();
	private string _description = "";
	private Scene _scene = new();
	private Simulation _simulation = new();
	private Display _display = new();
	private ObservableCollection<Source> _sources = new();
	private ObservableCollection<OpticalPart> _parts = new();
	private ObservableCollection<Probe> _probes = new();
	private ObservableCollection<ColorScheme> _colorSchemes = new();
	/// <summary>Each project scheme's last known name, so a rename can repoint the layers that referred to the old one.</summary>
	private readonly Dictionary<ColorScheme, string> _schemeNames = new();

	/// <summary>A new project with the designer defaults; its scheme list is watched from the start.</summary>
	public Project()
	{
		HookSchemes(_colorSchemes);
	}

	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
	private static readonly XmlSerializer Serializer = new(typeof(Project));
	private static readonly XmlSerializerNamespaces NoNamespaces = new([XmlQualifiedName.Empty]);
	private static readonly XmlWriterSettings WriterSettings = new()
	{
		Indent = true,
		IndentChars = "\t",
		Encoding = new System.Text.UTF8Encoding(false),
	};

	/// <summary>Format version of the file this project was read from, or <see cref="CurrentVersion"/>.</summary>
	[XmlAttribute]
	[Category("General")]
	[DisplayName("Format version")]
	[Description("Version of the project file format.")]
	[ReadOnly(true)]
	public int Version
	{
		get => _version;
		set => SetProperty(ref _version, value);
	}

	/// <summary>
	/// Seed of every random decision in the project: emitter jitter and the engines' per-period
	/// gap randomisation. The same project file therefore always produces the same frames.
	/// A new project gets a fresh seed; use <see cref="Reseed"/> for a different variation.
	/// </summary>
	[XmlAttribute]
	[Category("General")]
	[DisplayName("Random seed")]
	[Description("Seed of all random decisions (emitter jitter, random gaps). The same seed reproduces the same frames.")]
	public int Seed
	{
		get => _seed;
		set => SetProperty(ref _seed, value);
	}

	/// <summary>Free-text notes about the project; omitted from the file when empty.</summary>
	[XmlElement]
	[DefaultValue("")]
	[Category("General")]
	[Description("Free-text notes about the project.")]
	public string Description
	{
		get => _description;
		set => SetProperty(ref _description, value);
	}

	/// <summary>The canvas: its size or image, damping border and image source options.</summary>
	[XmlElement]
	[Category("Scene")]
	[Description("The canvas: its size or image, damping border and image sources.")]
	public Scene Scene
	{
		get => _scene;
		set => SetProperty(ref _scene, value);
	}

	/// <summary>Run length and solver parameters.</summary>
	[XmlElement]
	[Category("Simulation")]
	[Description("Run length and solver parameters.")]
	public Simulation Simulation
	{
		get => _simulation;
		set => SetProperty(ref _simulation, value);
	}

	/// <summary>How the field and intensity outputs are rendered, previewed and exported.</summary>
	[XmlElement]
	[Category("Display")]
	[Description("How the field and intensity outputs are rendered, previewed and exported.")]
	public Display Display
	{
		get => _display;
		set => SetProperty(ref _display, value);
	}

	/// <summary>
	/// Wave sources, in the order they appear in the project tree. Observable so that views
	/// follow additions and removals; always present, empty in a new project.
	/// </summary>
	[XmlElement("Source")]
	[Browsable(false)]
	public ObservableCollection<Source> Sources
	{
		get => _sources;
		set => SetProperty(ref _sources, value);
	}

	/// <summary>
	/// Optical parts (lenses, mirrors, polygons, rectangles, ellipses, triangles, outlines, fibres, Bragg reflectors, GRIN lenses, XOR, AND and OR gates, zone plates), in the order they appear in
	/// the project tree, which is the order they are painted in: a later part paints over an earlier one. One element
	/// name per kind in the file (2026-10-18, the user's choice, so a schema can say what each element needs).
	/// Observable; always present, empty in a new project.
	/// </summary>
	[XmlElement("Lens", typeof(LensPart))]
	[XmlElement("Polygon", typeof(PolygonPart))]
	[XmlElement("Rectangle", typeof(RectanglePart))]
	[XmlElement("Ellipse", typeof(EllipsePart))]
	[XmlElement("Triangle", typeof(TrianglePart))]
	[XmlElement("Outline", typeof(OutlinePart))]
	[XmlElement("Fiber", typeof(FiberPart))]
	[XmlElement("BraggReflector", typeof(BraggReflectorPart))]
	[XmlElement("GrinLens", typeof(GrinLensPart))]
	[XmlElement("XorGate", typeof(XorGatePart))]
	[XmlElement("AndGate", typeof(AndGatePart))]
	[XmlElement("OrGate", typeof(OrGatePart))]
	[XmlElement("ZonePlate", typeof(ZonePlatePart))]
	[Browsable(false)]
	public ObservableCollection<OpticalPart> Parts
	{
		get => _parts;
		set => SetProperty(ref _parts, value);
	}

	/// <summary>
	/// Probes, in the order they appear in the project tree. Observable so that views follow
	/// additions and removals; always present, empty in a new project.
	/// </summary>
	[XmlElement("Probe")]
	[Browsable(false)]
	public ObservableCollection<Probe> Probes
	{
		get => _probes;
		set => SetProperty(ref _probes, value);
	}

	/// <summary>
	/// The project's own colour schemes (2026-10-13): edited copies and loaded files, stored in the project
	/// file so it stays self-contained. A display layer's scheme name finds these before the shared ones.
	/// </summary>
	[XmlElement("ColorScheme")]
	[Browsable(false)]
	public ObservableCollection<ColorScheme> ColorSchemes
	{
		get => _colorSchemes;
		set
		{
			ObservableCollection<ColorScheme> old = _colorSchemes;
			if (SetProperty(ref _colorSchemes, value))
			{
				UnhookSchemes(old);
				HookSchemes(value);
			}
		}
	}

	// ---------------------------------------------------------------- a renamed scheme takes its layers along (2026-10-15)

	private void HookSchemes(ObservableCollection<ColorScheme>? schemes)
	{
		if (schemes == null)
		{
			return;
		}
		schemes.CollectionChanged += OnSchemesChanged;
		foreach (ColorScheme scheme in schemes)
		{
			TrackScheme(scheme);
		}
	}

	private void UnhookSchemes(ObservableCollection<ColorScheme>? schemes)
	{
		if (schemes == null)
		{
			return;
		}
		schemes.CollectionChanged -= OnSchemesChanged;
		foreach (ColorScheme scheme in schemes)
		{
			UntrackScheme(scheme);
		}
	}

	private void TrackScheme(ColorScheme scheme)
	{
		if (_schemeNames.TryAdd(scheme, scheme.Name))
		{
			scheme.PropertyChanged += OnSchemeChanged;
		}
	}

	private void UntrackScheme(ColorScheme scheme)
	{
		if (_schemeNames.Remove(scheme))
		{
			scheme.PropertyChanged -= OnSchemeChanged;
		}
	}

	private void OnSchemesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
		{
			foreach (ColorScheme scheme in _schemeNames.Keys.ToList())
			{
				UntrackScheme(scheme);
			}
			foreach (ColorScheme scheme in _colorSchemes)
			{
				TrackScheme(scheme);
			}
			return;
		}
		if (e.OldItems != null)
		{
			foreach (ColorScheme scheme in e.OldItems)
			{
				UntrackScheme(scheme);
			}
		}
		if (e.NewItems != null)
		{
			foreach (ColorScheme scheme in e.NewItems)
			{
				TrackScheme(scheme);
			}
		}
	}

	/// <summary>
	/// A project scheme was renamed: every display layer that named it (case-insensitively) names it by its new name,
	/// so the preview keeps its colours and the file stays consistent. Done here, in the model, so every editor and
	/// the importer get it, not just the scheme view.
	/// </summary>
	private void OnSchemeChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(ColorScheme.Name) || sender is not ColorScheme scheme || !_schemeNames.TryGetValue(scheme, out string? oldName))
		{
			return;
		}
		string newName = scheme.Name;
		_schemeNames[scheme] = newName;
		if (string.Equals(oldName, newName, StringComparison.Ordinal))
		{
			return;
		}
		foreach (DisplayLayer layer in new[] { Display.Field, Display.Intensity })
		{
			if (string.Equals(layer.ColorScheme, oldName, StringComparison.OrdinalIgnoreCase))
			{
				layer.ColorScheme = newName;
			}
		}
	}

	/// <summary>Tells <see cref="XmlSerializer"/> to leave an all-default scene out of the file.</summary>
	public bool ShouldSerializeScene() => !Scene.IsDefault;

	/// <summary>Tells <see cref="XmlSerializer"/> to leave all-default solver settings out of the file.</summary>
	public bool ShouldSerializeSimulation() => !Simulation.IsDefault;

	/// <summary>Draws a new seed.</summary>
	public static int NewSeed() => Random.Shared.Next(1, int.MaxValue);

	/// <summary>Assigns a new <see cref="Seed"/> and regenerates everything derived from it.</summary>
	public void Reseed()
	{
		Seed = NewSeed();
		GenerateElements();
	}

	/// <summary>Regenerates the emitters of every source from the current settings and <see cref="Seed"/>.</summary>
	public void GenerateElements()
	{
		foreach (Source source in Sources)
		{
			source.GenerateElements(Seed);
		}
	}

	/// <summary>Appends a new source named "Source n" with the first free n, and returns it.</summary>
	public Source AddSource()
	{
		var source = new Source { Name = NumberedName("Source", Sources.Select(s => s.Name)) };
		source.GenerateElements(Seed);
		Sources.Add(source);
		return source;
	}

	/// <summary>
	/// Appends a new part of the concrete <see cref="OpticalPart"/> subclass <paramref name="type"/> (the "+ Part" menu's
	/// parameter), prepared as new (<see cref="OpticalPart.PrepareNew"/>), named by its <see cref="OpticalPart.NameStem"/>
	/// with the first free n ("Lens 1", "Bragg reflector 2"), centred on the canvas (the scene's size; an image scene's own
	/// size is not known here), and returns it.
	/// </summary>
	public OpticalPart AddPart(Type type)
	{
		if (!type.IsSubclassOf(typeof(OpticalPart)) || type.IsAbstract)
		{
			throw new ArgumentException($"{type.Name} is not a concrete optical part", nameof(type));
		}
		var part = (OpticalPart)Activator.CreateInstance(type)!;
		part.PrepareNew();
		part.Name = NumberedName(part.NameStem, Parts.Select(p => p.Name));
		part.X = Scene.Width / 2;
		part.Y = Scene.Height / 2;
		Parts.Add(part);
		return part;
	}

	/// <summary>Appends a new part of type <typeparamref name="T"/> as <see cref="AddPart(Type)"/> does, and returns it.</summary>
	public T AddPart<T>() where T : OpticalPart, new() => (T)AddPart(typeof(T));

	/// <summary>Appends a new probe named "Probe n" with the first free n, and returns it.</summary>
	public Probe AddProbe()
	{
		var probe = new Probe { Name = NumberedName("Probe", Probes.Select(p => p.Name)) };
		Probes.Add(probe);
		return probe;
	}

	/// <summary>Appends a new black-to-white scheme named "Scheme n" and returns it.</summary>
	public ColorScheme AddColorScheme()
	{
		var scheme = new ColorScheme(NumberedName("Scheme", ColorSchemes.Select(s => s.Name)), ColorScheme.Grey());
		ColorSchemes.Add(scheme);
		return scheme;
	}

	/// <summary>Appends a scheme that came from elsewhere (a file, a copy); its name gets a " copy" suffix when taken.</summary>
	public ColorScheme AddColorScheme(ColorScheme scheme)
	{
		if (ColorSchemes.Any(s => string.Equals(s.Name, scheme.Name, StringComparison.OrdinalIgnoreCase)))
		{
			scheme.Name = CopyName(scheme.Name, ColorSchemes.Select(s => s.Name));
		}
		ColorSchemes.Add(scheme);
		return scheme;
	}

	/// <summary>Inserts a copy of <paramref name="scheme"/> right after it, named "&lt;name&gt; copy", and returns the copy.</summary>
	public ColorScheme Duplicate(ColorScheme scheme)
	{
		ColorScheme copy = scheme.Clone(CopyName(scheme.Name, ColorSchemes.Select(s => s.Name)));
		ColorSchemes.Insert(ColorSchemes.IndexOf(scheme) + 1, copy);
		return copy;
	}

	/// <summary>Removes a scheme; false when it is not in the project.</summary>
	public bool Remove(ColorScheme scheme) => ColorSchemes.Remove(scheme);

	/// <summary>Moves a scheme to another index; false when nothing changes.</summary>
	public bool Move(ColorScheme scheme, int index) => Move(ColorSchemes, scheme, index);

	/// <summary>Inserts a copy of <paramref name="source"/> right after it, named "&lt;name&gt; copy", and returns the copy.</summary>
	public Source Duplicate(Source source)
	{
		Source copy = source.Clone();
		copy.Name = CopyName(source.Name, Sources.Select(s => s.Name));
		Sources.Insert(Sources.IndexOf(source) + 1, copy);
		return copy;
	}

	/// <summary>Inserts a copy of <paramref name="part"/> right after it, named "&lt;name&gt; copy", and returns the copy.</summary>
	public OpticalPart Duplicate(OpticalPart part)
	{
		OpticalPart copy = part.Clone();
		copy.Name = CopyName(part.Name, Parts.Select(p => p.Name));
		Parts.Insert(Parts.IndexOf(part) + 1, copy);
		return copy;
	}

	/// <summary>Inserts a copy of <paramref name="probe"/> right after it, named "&lt;name&gt; copy", and returns the copy.</summary>
	public Probe Duplicate(Probe probe)
	{
		Probe copy = probe.Clone();
		copy.Name = CopyName(probe.Name, Probes.Select(p => p.Name));
		Probes.Insert(Probes.IndexOf(probe) + 1, copy);
		return copy;
	}

	/// <summary>Removes the source; false when it was not in the project.</summary>
	public bool Remove(Source source) => Sources.Remove(source);

	/// <summary>Removes the part; false when it was not in the project.</summary>
	public bool Remove(OpticalPart part) => Parts.Remove(part);

	/// <summary>Removes the probe; false when it was not in the project.</summary>
	public bool Remove(Probe probe) => Probes.Remove(probe);

	/// <summary>Moves the source so that it ends up at <paramref name="index"/>; false when nothing changed.</summary>
	public bool Move(Source source, int index) => Move(Sources, source, index);

	/// <summary>Moves the part so that it ends up at <paramref name="index"/>; false when nothing changed.</summary>
	public bool Move(OpticalPart part, int index) => Move(Parts, part, index);

	/// <summary>Moves the probe so that it ends up at <paramref name="index"/>; false when nothing changed.</summary>
	public bool Move(Probe probe, int index) => Move(Probes, probe, index);

	private static bool Move<T>(ObservableCollection<T> list, T item, int index)
	{
		int current = list.IndexOf(item);
		if (current < 0 || index < 0 || index >= list.Count || index == current)
		{
			return false;
		}
		list.Move(current, index);
		return true;
	}

	/// <summary>"stem 1", "stem 2", ... skipping names already in <paramref name="taken"/> (case-insensitive).</summary>
	private static string NumberedName(string stem, IEnumerable<string> taken)
	{
		var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
		for (int n = 1; ; n++)
		{
			string candidate = $"{stem} {n}";
			if (!names.Contains(candidate))
			{
				return candidate;
			}
		}
	}

	/// <summary>"name copy", then "name copy 2", ... skipping names already in <paramref name="taken"/>.</summary>
	internal static string CopyName(string name, IEnumerable<string> taken)
	{
		var names = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
		string stem = $"{name} copy";
		if (!names.Contains(stem))
		{
			return stem;
		}
		for (int n = 2; ; n++)
		{
			string candidate = $"{stem} {n}";
			if (!names.Contains(candidate))
			{
				return candidate;
			}
		}
	}

	/// <summary>Writes the project as indented XML.</summary>
	public void Save(Stream stream)
	{
		using var writer = XmlWriter.Create(stream, WriterSettings);
		Serializer.Serialize(writer, this, NoNamespaces);
	}

	/// <summary>Writes the project to <paramref name="path"/>, replacing an existing file.</summary>
	public void Save(string path)
	{
		Logger.Info("Saving project to '{Path}' ({Sources} sources, {Parts} parts, {Probes} probes)", path, Sources.Count, Parts.Count, Probes.Count);
		using FileStream stream = File.Create(path);
		Save(stream);
	}

	/// <summary>
	/// Reads a project written by <see cref="Save(Stream)"/>. Values are clamped into range and
	/// the source emitters are generated from the settings and the seed.
	/// </summary>
	/// <exception cref="InvalidOperationException">The stream does not hold a project document.</exception>
	public static Project Load(Stream stream)
	{
		using var reader = XmlReader.Create(stream);
		if (Serializer.Deserialize(reader) is not Project project)
		{
			throw new InvalidOperationException("The document is not a WaveDude Next project.");
		}
		if (project.Version > CurrentVersion)
		{
			Logger.Warn("Project file version {Version} is newer than the supported {Supported}; unknown settings are ignored", project.Version, CurrentVersion);
		}
		if (project.Version < 2)
		{
			// version 1 (until 2026-10-19) knew only reflective edges; the open edges that are the default since must not
			// reach an old file, which keeps the walls it was made with
			project.Scene.Edges = EdgeMode.Reflective;
		}
		if (project.Version < 3 && !project.Scene.DampingEdgeRead)
		{
			// until version 3 (2026-10-22) a file without a damping edge meant none; the rim of 32 that is the default since
			// must not reach it either
			project.Scene.DampingEdge = 0;
		}
		project.Version = CurrentVersion;
		project.Normalize();
		Logger.Debug("Loaded project: version {Version}, seed {Seed}, {Sources} sources, {Parts} parts, {Probes} probes", project.Version, project.Seed, project.Sources.Count, project.Parts.Count, project.Probes.Count);
		return project;
	}

	/// <summary>Reads the project file at <paramref name="path"/>.</summary>
	public static Project Load(string path)
	{
		Logger.Info("Loading project from '{Path}'", path);
		using FileStream stream = File.OpenRead(path);
		return Load(stream);
	}

	/// <summary>Clamps every setting into its valid range and regenerates the source emitters.</summary>
	public void Normalize()
	{
		Scene.Normalize();
		Simulation.Normalize();
		Display.Normalize();
		foreach (Source source in Sources)
		{
			source.Normalize();
			source.GenerateElements(Seed);
		}
		foreach (OpticalPart part in Parts)
		{
			part.Normalize();
		}
		foreach (Probe probe in Probes)
		{
			probe.Normalize();
		}
		foreach (ColorScheme scheme in ColorSchemes)
		{
			scheme.Normalize();
			if (scheme.Name.Length == 0)
			{
				scheme.Name = NumberedName("Scheme", ColorSchemes.Select(s => s.Name));
			}
		}
	}
}
