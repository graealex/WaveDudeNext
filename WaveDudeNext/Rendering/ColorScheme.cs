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
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Xml.Serialization;
using WaveDudeNext.Model;

namespace WaveDudeNext.Rendering;

/// <summary>Which output a scheme is meant for; the picker offers a layer the schemes of its kind and the unspecified ones.</summary>
public enum ColorSchemeKind
{
	/// <summary>Not stated; offered for both outputs.</summary>
	Unspecified,
	/// <summary>Made for the wave field (symmetric around the middle entry, typically).</summary>
	Field,
	/// <summary>Made for the accumulated intensity (dark to bright, typically).</summary>
	Intensity,
}

/// <summary>
/// One stop of a <see cref="ColorScheme"/>: a colour at a position along the gradient, 0 (lowest
/// value) to 1 (highest). XML: <c>&lt;Stop At="0.125" Color="#3020F4" /&gt;</c>. Observable, so an
/// editor's change reaches the scheme (which forwards it) and the preview. UI thread.
/// </summary>
public sealed class ColorStop : ObservableObject
{
	private double _position;
	private Rgb _color;

	/// <summary>Black at position 0 (the serializer's starting point).</summary>
	public ColorStop()
	{
	}

	/// <summary>A stop at <paramref name="position"/> (0..1) with <paramref name="color"/>.</summary>
	public ColorStop(double position, Rgb color)
	{
		_position = position;
		_color = color;
	}

	/// <summary>Where along the gradient the colour sits: 0 at the lowest value, 1 at the highest (a fraction, not a percentage).</summary>
	[XmlAttribute("At")]
	[Category("Stop")]
	[Description("Position along the gradient, 0 at the lowest value to 1 at the highest.")]
	[Range(0.0, 1.0)]
	public double Position
	{
		get => _position;
		set => SetProperty(ref _position, value);
	}

	/// <summary>The colour at this position.</summary>
	[XmlIgnore]
	[Category("Stop")]
	[Description("Colour at this position, as #RRGGBB.")]
	public Rgb Color
	{
		get => _color;
		set => SetProperty(ref _color, value);
	}

	/// <summary>The colour as <c>#RRGGBB</c>, the XML attribute.</summary>
	[XmlAttribute("Color")]
	[Browsable(false)]
	public string ColorText
	{
		get => Color.ToString();
		set => Color = Rgb.Parse(value);
	}

	/// <summary>"0.125 #3020F4".</summary>
	public override string ToString() => Position.ToString("0.###", CultureInfo.InvariantCulture) + " " + Color;
}

/// <summary>
/// A colour scheme: colour stops at positions 0..1 along the value range, the gradient as specified: linear
/// between neighbouring stops, the end colours before the first stop and after the last (<see cref="Evaluate"/>).
/// The model holds no table (2026-10-23, the user's call): how many colours are taken from the gradient is the
/// renderer's business (<see cref="ColorLut"/>: 255 for a frame of 8-bit palette indices, one for every value a
/// 16-bit sample can hold). This is the consolidated form (2026-10-13): the author's files carried a 255-entry
/// table plus, for the ones made in his editor, 9 evenly spaced stops; every shipped table equals the interpolation
/// of its stops, so those schemes keep the 9 stops and the sampled colormaps keep 255 stops at i/254. Stops may sit
/// anywhere (2026-10-13, the user's call: positions are fractions of the gradient). Serialises to XML as attributes
/// plus one <c>Stop</c> element per stop, both standalone (<c>.wdscheme</c>) and inside a project file. Observable:
/// a stop's edit raises <c>Stops</c> on the scheme. UI thread; a renderer works on a copy (<see cref="SortedStops"/>).
/// </summary>
[XmlRoot("ColorScheme")]
public sealed class ColorScheme : ObservableObject
{
	/// <summary>Number of stops of the author's editable gradient: nine, evenly spaced.</summary>
	public const int StopCount = 9;

	private string _name = "";
	private string _description = "";
	private ColorSchemeKind _kind;
	private bool _replacing;

	/// <summary>
	/// An empty, nameless scheme: the serializer's starting point (it appends the file's stops to the
	/// list, so the list must start empty) and the base of <see cref="Clone"/>. Without stops the gradient
	/// is the grey ramp; <see cref="Normalize"/> makes that explicit.
	/// </summary>
	public ColorScheme()
	{
		Stops.CollectionChanged += OnStopsCollectionChanged;
	}

	/// <summary>A scheme of the given colours, spread evenly (at least one).</summary>
	public ColorScheme(string name, IEnumerable<Rgb> colors)
		: this()
	{
		Name = name;
		SetColors(colors);
	}

	/// <summary>Display name; the one a <c>DisplayLayer.ColorScheme</c> refers to (case-insensitive).</summary>
	[XmlAttribute]
	[Category("General")]
	[Description("Name the display settings refer to.")]
	public string Name
	{
		get => _name;
		set => SetProperty(ref _name, value ?? "");
	}

	/// <summary>Which output the scheme is meant for.</summary>
	[XmlAttribute]
	[DefaultValue(ColorSchemeKind.Unspecified)]
	[Category("General")]
	[Description("Output the scheme is meant for; the pickers list it for that output only.")]
	public ColorSchemeKind Kind
	{
		get => _kind;
		set => SetProperty(ref _kind, value);
	}

	/// <summary>Free text about the scheme, where it comes from ("matplotlib colormap viridis", "9-stop copy of …"); the author's "origin".</summary>
	[XmlAttribute]
	[DefaultValue("")]
	[Category("General")]
	[Description("Free text: what the scheme is, where it comes from.")]
	public string Description
	{
		get => _description;
		set => SetProperty(ref _description, value ?? "");
	}

	/// <summary>Full path of the file the scheme was loaded from or last saved to; "" for an unsaved, embedded or built-in scheme.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public string FilePath { get; set; } = "";

	/// <summary>Whether the scheme ships with the program (embedded in the library); such a scheme is not written back anywhere.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool IsBuiltIn { get; set; }

	/// <summary>
	/// The stops, each a colour at a position 0..1; kept in position order by <see cref="Normalize"/>
	/// (a renderer sorts a copy, so an editor may leave them unsorted while typing). Edit a stop, or add and
	/// remove through <see cref="AddStop"/> / <see cref="RemoveStop"/>; replace all with <see cref="SetStops"/>
	/// or <see cref="SetColors"/>. Every change raises <see cref="INotifyPropertyChanged.PropertyChanged"/> for <c>Stops</c>.
	/// </summary>
	[XmlElement("Stop")]
	[Browsable(false)]
	public ObservableCollection<ColorStop> Stops { get; } = [];

	/// <summary>Whether the scheme has the author's editor form: nine stops (at any positions).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool IsStopScheme => Stops.Count == StopCount;

	/// <summary>Whether the stops sit at k / (n−1), the layout of the author's files and of a sampled table.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool IsEvenlySpaced
	{
		get
		{
			int n = Stops.Count;
			for (int k = 0; k < n; k++)
			{
				if (Math.Abs(Stops[k].Position - (n == 1 ? 0.0 : k / (double)(n - 1))) > 1e-9)
				{
					return false;
				}
			}
			return true;
		}
	}

	/// <summary>The colours of the stops, in list order.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public IEnumerable<Rgb> Colors => Stops.Select(s => s.Color);

	/// <summary>The stops in position order as copies, which a renderer may keep and read on any thread; the grey ramp's when there are none.</summary>
	public List<ColorStop> SortedStops() =>
		Stops.Count > 0 ? [.. Stops.OrderBy(s => s.Position).Select(s => new ColorStop(s.Position, s.Color))] : Spread(Grey());

	/// <summary>The gradient's colour at <paramref name="position"/> (0..1), each channel rounded to the nearest level (half away from zero).</summary>
	public Rgb ColorAt(double position) => Round(Evaluate(SortedStops(), position));

	/// <summary>Replaces every stop with these colours spread evenly over 0..1 (one change notification); at least one.</summary>
	/// <exception cref="ArgumentException">No colours.</exception>
	public void SetColors(IEnumerable<Rgb> colors)
	{
		var list = colors.ToList();
		if (list.Count == 0)
		{
			throw new ArgumentException("A color scheme needs at least one color.");
		}
		SetStops(Spread(list));
	}

	/// <summary>Stops for these colours at k / (n−1), the author's layout; a single colour sits at 0.</summary>
	private static List<ColorStop> Spread(IReadOnlyList<Rgb> colors) =>
		[.. colors.Select((color, k) => new ColorStop(colors.Count == 1 ? 0.0 : k / (double)(colors.Count - 1), color))];

	/// <summary>Replaces every stop (copies are taken; one change notification); at least one.</summary>
	/// <exception cref="ArgumentException">No stops.</exception>
	public void SetStops(IEnumerable<ColorStop> stops)
	{
		var list = stops.Select(s => new ColorStop(s.Position, s.Color)).ToList();
		if (list.Count == 0)
		{
			throw new ArgumentException("A color scheme needs at least one stop.");
		}
		_replacing = true;
		try
		{
			Stops.Clear();
			foreach (ColorStop stop in list)
			{
				Stops.Add(stop);
			}
		}
		finally
		{
			_replacing = false;
		}
		OnStopsCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}

	/// <summary>
	/// Adds a stop in the middle of the widest gap between neighbouring stops (or after a single one),
	/// with the gradient's colour there, and returns it.
	/// </summary>
	public ColorStop AddStop()
	{
		List<ColorStop> sorted = [.. Stops.OrderBy(s => s.Position)];
		double at = 1.0;
		if (sorted.Count >= 2)
		{
			int widest = 0;
			for (int k = 1; k < sorted.Count - 1; k++)
			{
				if (sorted[k + 1].Position - sorted[k].Position > sorted[widest + 1].Position - sorted[widest].Position)
				{
					widest = k;
				}
			}
			at = (sorted[widest].Position + sorted[widest + 1].Position) / 2;
		}
		else if (sorted[0].Position >= 1.0)
		{
			at = 0.0;
		}
		var stop = new ColorStop(at, ColorAt(at));
		int index = Stops.Count(s => s.Position <= at);
		Stops.Insert(index, stop);
		return stop;
	}

	/// <summary>Removes a stop; false when it is not here or the last one.</summary>
	public bool RemoveStop(ColorStop stop) => Stops.Count > 1 && Stops.Remove(stop);

	/// <summary>
	/// Drops every stop that the straight line between its neighbours reproduces within
	/// <paramref name="tolerance"/> (the largest channel difference, 0..255, after the renderer's rounding):
	/// Ramer–Douglas–Peucker over the stops, so a sampled 255-stop colormap keeps only its bends. The first
	/// and last stop always stay. Used to slim the shipped colormaps (2026-10-14, tolerance 3: between 3
	/// and 22 stops each) and offered by the editor.
	/// </summary>
	public void Simplify(double tolerance)
	{
		List<ColorStop> kept = Simplify(Stops, tolerance);
		if (kept.Count < Stops.Count)
		{
			SetStops(kept);
		}
	}

	/// <summary>The stops that <see cref="Simplify(double)"/> keeps, as copies, in position order.</summary>
	public static List<ColorStop> Simplify(IReadOnlyList<ColorStop> stops, double tolerance)
	{
		List<ColorStop> sorted = [.. stops.OrderBy(s => s.Position)];
		var keep = new bool[sorted.Count];
		if (sorted.Count > 0)
		{
			keep[0] = true;
			keep[^1] = true;
			SimplifyRange(sorted, 0, sorted.Count - 1, tolerance, keep);
		}
		return sorted.Where((_, i) => keep[i]).Select(s => new ColorStop(s.Position, s.Color)).ToList();
	}

	private static void SimplifyRange(List<ColorStop> stops, int first, int last, double tolerance, bool[] keep)
	{
		if (last - first < 2)
		{
			return;
		}
		int worst = -1;
		double worstError = tolerance;
		for (int i = first + 1; i < last; i++)
		{
			double error = DeviationFromLine(stops[first], stops[last], stops[i]);
			if (error > worstError)
			{
				worst = i;
				worstError = error;
			}
		}
		if (worst < 0)
		{
			return;
		}
		keep[worst] = true;
		SimplifyRange(stops, first, worst, tolerance, keep);
		SimplifyRange(stops, worst, last, tolerance, keep);
	}

	/// <summary>How far a stop's colour is from the straight line between two others at its position: the largest channel difference after rounding.</summary>
	private static double DeviationFromLine(ColorStop a, ColorStop b, ColorStop p)
	{
		double span = b.Position - a.Position;
		double t = span <= 0 ? 0.0 : (p.Position - a.Position) / span;
		Rgb onLine = Lerp(a.Color, b.Color, t);
		return Math.Max(Math.Abs(onLine.R - p.Color.R), Math.Max(Math.Abs(onLine.G - p.Color.G), Math.Abs(onLine.B - p.Color.B)));
	}

	/// <summary>Clamps positions to 0..1 and sorts the stops; a scheme without stops becomes grey.</summary>
	public void Normalize()
	{
		if (Stops.Count == 0)
		{
			SetColors(Grey());
			return;
		}
		foreach (ColorStop stop in Stops)
		{
			stop.Position = Math.Clamp(stop.Position, 0.0, 1.0);
		}
		if (!Stops.Zip(Stops.Skip(1)).All(pair => pair.First.Position <= pair.Second.Position))
		{
			SetStops(Stops.OrderBy(s => s.Position).ToList());
		}
	}

	/// <summary>An independent copy under a new name (stops, kind, category and description; not the file path or the built-in flag).</summary>
	public ColorScheme Clone(string newName)
	{
		var copy = new ColorScheme { Name = newName, Kind = Kind, Description = Description };
		copy.SetStops(Stops);
		return copy;
	}

	/// <summary>A scheme of exactly <see cref="StopCount"/> evenly spaced stops (the author's editor form).</summary>
	/// <exception cref="ArgumentException">Not <see cref="StopCount"/> colours.</exception>
	public static ColorScheme FromStops(string schemeName, IReadOnlyList<Rgb> stops)
	{
		if (stops.Count != StopCount)
		{
			throw new ArgumentException("Exactly " + StopCount.ToString(CultureInfo.InvariantCulture) + " stops are required.");
		}
		return new ColorScheme(schemeName, stops)
		{
			Description = StopCount.ToString(CultureInfo.InvariantCulture) + "-stop gradient made in the color scheme editor",
		};
	}

	/// <summary>Nine colours from black to white, the default of a new scheme and the gradient of one without stops.</summary>
	public static Rgb[] Grey()
	{
		var stops = new Rgb[StopCount];
		for (int i = 0; i < StopCount; i++)
		{
			byte level = (byte)Math.Round(i * 255.0 / (StopCount - 1));
			stops[i] = new Rgb(level, level, level);
		}
		return stops;
	}

	/// <summary>
	/// The gradient at <paramref name="position"/>: between two stops the colour is interpolated linearly, before the
	/// first stop and after the last the end colours hold, at a stop's own position the first stop there (two stops at
	/// one position make a hard edge). Channels on the 0..255 scale, unrounded: how finely the gradient is sampled and
	/// how it is rounded are the renderer's business. Any thread, while <paramref name="sorted"/> is left alone.
	/// </summary>
	/// <param name="sorted">At least one stop, in position order (<see cref="SortedStops"/>).</param>
	/// <param name="position">Where along the gradient, 0..1; NaN gives the first stop's colour.</param>
	public static (double R, double G, double B) Evaluate(IReadOnlyList<ColorStop> sorted, double position)
	{
		if (!(position > sorted[0].Position))
		{
			return Channels(sorted[0].Color);
		}
		if (position >= sorted[^1].Position)
		{
			return Channels(sorted[^1].Color);
		}
		// the first stop at or beyond the position, by bisection: a sampled colormap may keep 255 stops
		int low = 1;
		int high = sorted.Count - 1;
		while (low < high)
		{
			int middle = (low + high) / 2;
			if (sorted[middle].Position < position)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}
		Rgb a = sorted[low - 1].Color;
		Rgb b = sorted[low].Color;
		double t = (position - sorted[low - 1].Position) / (sorted[low].Position - sorted[low - 1].Position);
		return (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
	}

	/// <summary>
	/// The gradient of these stops sampled at <paramref name="count"/> evenly spaced positions, entry i at
	/// i / (count−1) (<see cref="Evaluate"/>), each channel rounded half away from zero, as the author's Python did:
	/// a gradient brush's samples, the 8-bit palette, the author's JSON table.
	/// </summary>
	/// <exception cref="ArgumentException">No stops.</exception>
	public static Rgb[] Interpolate(IReadOnlyList<ColorStop> stops, int count)
	{
		if (stops.Count == 0)
		{
			throw new ArgumentException("A color scheme needs at least one stop.");
		}
		List<ColorStop> sorted = [.. stops.OrderBy(s => s.Position)];
		var result = new Rgb[count];
		for (int i = 0; i < count; i++)
		{
			result[i] = Round(Evaluate(sorted, i / (double)(count - 1)));
		}
		return result;
	}

	private static (double R, double G, double B) Channels(Rgb color) => (color.R, color.G, color.B);

	private static Rgb Round((double R, double G, double B) color) =>
		Rgb.Clamped(RoundChannel(color.R), RoundChannel(color.G), RoundChannel(color.B));

	private static int RoundChannel(double level) => (int)Math.Round(level, MidpointRounding.AwayFromZero);

	/// <summary>The straight line between two colours at <paramref name="t"/> (0..1), each channel rounded half away from zero.</summary>
	internal static Rgb Lerp(Rgb a, Rgb b, double t) =>
		Rgb.Clamped(LerpChannel(a.R, b.R, t), LerpChannel(a.G, b.G, t), LerpChannel(a.B, b.B, t));

	private static int LerpChannel(int a, int b, double t) => (int)Math.Round(a + (b - a) * t, MidpointRounding.AwayFromZero);

	private void OnStopsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.OldItems != null)
		{
			foreach (ColorStop stop in e.OldItems)
			{
				stop.PropertyChanged -= OnStopChanged;
			}
		}
		if (e.NewItems != null)
		{
			foreach (ColorStop stop in e.NewItems)
			{
				stop.PropertyChanged += OnStopChanged;
			}
		}
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			foreach (ColorStop stop in Stops)
			{
				stop.PropertyChanged -= OnStopChanged;
				stop.PropertyChanged += OnStopChanged;
			}
		}
		if (!_replacing)
		{
			Invalidate();
		}
	}

	private void OnStopChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();

	private void Invalidate()
	{
		OnPropertyChanged(nameof(Stops));
		OnPropertyChanged(nameof(IsStopScheme));
	}

	/// <summary>The name, for lists and the property grid.</summary>
	public override string ToString() => Name;
}
