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
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>One layer of an <see cref="InterferenceFilterPart"/>'s stack: a thickness along the axis and an index.</summary>
public sealed class FilterLayer : ObservableObject
{
	/// <summary>Default <see cref="Thickness"/> in pixels.</summary>
	public const double DefaultThickness = 2.0;
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultIndex = 1.5;
	/// <summary>Largest <see cref="Thickness"/> in pixels.</summary>
	public const double MaxThickness = 1000.0;

	private double _thickness = DefaultThickness;
	private double _refractiveIndex = DefaultIndex;

	/// <summary>Thickness of the layer along the part's axis in pixels, in steps of 0.01; a fractional boundary is a mixed index at its pixel (the painter samples 4 × 4).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(0.0, MaxThickness)]
	[Category("Layer")]
	[Description("Thickness of the layer in pixels, in steps of 0.01. A quarter-wave layer is λ / (4 n) pixels thick; a boundary inside a pixel gives that pixel a mixed index.")]
	public double Thickness
	{
		get => _thickness;
		set => SetProperty(ref _thickness, value);
	}

	/// <summary>Refractive index of the layer, 1.00 to 2.55 in steps of 0.01.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex)]
	[Range(OpticalPart.MinIndex, OpticalPart.MaxIndex)]
	[Category("Layer")]
	[DisplayName("Refractive index")]
	[Description("Refractive index of the layer, 1.00 to 2.55 in steps of 0.01.")]
	public double RefractiveIndex
	{
		get => _refractiveIndex;
		set => SetProperty(ref _refractiveIndex, value);
	}

	/// <summary>The thickness guarded as the painter takes it: finite, 0 to <see cref="MaxThickness"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedThickness => double.IsFinite(Thickness) ? Math.Clamp(Thickness, 0.0, MaxThickness) : 0.0;

	/// <summary>Clamps the thickness into 0..<see cref="MaxThickness"/> at 0.01 and the index into the part range.</summary>
	public void Normalize()
	{
		Thickness = Math.Round(GuardedThickness, 2);
		RefractiveIndex = double.IsFinite(RefractiveIndex) ? Math.Round(Math.Clamp(RefractiveIndex, OpticalPart.MinIndex, OpticalPart.MaxIndex), 2) : DefaultIndex;
	}

	/// <summary>A copy with the same values and no change subscribers.</summary>
	public FilterLayer Clone() => new() { Thickness = Thickness, RefractiveIndex = RefractiveIndex };

	/// <summary>The values, for display.</summary>
	public override string ToString() => $"{Thickness} px, n {RefractiveIndex}";
}

/// <summary>
/// An interference filter (the user's request, 2026-11-07): a carrier plate of <see cref="Length"/> along the axis and
/// <see cref="Width"/> across, the material and absorption of a <see cref="ShapePart"/>, with a stack of
/// <see cref="Layers"/> on its front face (−x', toward a source on the left), outside the plate, so the plate stays as
/// sized and the part grows by the stack (the user's choice). The first layer is the outermost, the one a wave from the
/// left meets first, the last lies on the plate. The part's centre is the plate's. A reflector (<see cref="ShapePart.Mirror"/>)
/// makes the plate a mirror and leaves the layers glass in front of it (the user's choice: a coated mirror). The plate's
/// AR coating lies on its back alone, the face away from the layers (<see cref="CoatedPart.BackCoated"/>; the front and
/// side flags are not used). A new filter gets a quarter-wave stack (<see cref="PrepareNew"/>).
/// </summary>
public sealed class InterferenceFilterPart : ShapePart
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>The wavelength in pixels a new filter's stack is cut for.</summary>
	public const double DefaultStackWavelength = 16.0;
	/// <summary>Layer pairs of a new filter's stack.</summary>
	public const int DefaultPairs = 5;
	/// <summary>The low index of a new filter's stack.</summary>
	public const double DefaultLowIndex = 1.5;
	/// <summary>The high index of a new filter's stack.</summary>
	public const double DefaultHighIndex = 2.0;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private ObservableCollection<FilterLayer> _layers = [];
	private HashSet<FilterLayer> _watched = [];

	/// <summary>A filter without layers: a file's layers or <see cref="PrepareNew"/>'s stack fill it.</summary>
	public InterferenceFilterPart()
	{
		_layers.CollectionChanged += OnLayersChanged;
	}

	/// <inheritdoc/>
	public override string NameStem => "Interference filter";

	/// <summary>Extent of the carrier plate along the axis in pixels, without the stack.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Thickness of the carrier plate along the axis in pixels; the layer stack lies on its front, outside it.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent of the plate and the stack across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Width of the plate and its layers across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set => SetProperty(ref _width, value);
	}

	/// <summary>The layers on the plate's front, the outermost first; the order is the order a wave from the left meets them.</summary>
	[XmlElement("Layer")]
	[Category("Layers")]
	[Description("The layers on the plate's front, the outermost first. Each has its thickness in pixels and its refractive index.")]
	public ObservableCollection<FilterLayer> Layers
	{
		get => _layers;
		set
		{
			ObservableCollection<FilterLayer> old = _layers;
			if (SetProperty(ref _layers, value))
			{
				if (old != null)
				{
					old.CollectionChanged -= OnLayersChanged;
				}
				foreach (FilterLayer layer in _watched)
				{
					layer.PropertyChanged -= OnLayerChanged;
				}
				_watched.Clear();
				if (value != null)
				{
					value.CollectionChanged += OnLayersChanged;
					foreach (FilterLayer layer in value)
					{
						Watch(layer);
					}
				}
				OnPropertyChanged(nameof(StackThickness));
			}
		}
	}

	private void Watch(FilterLayer layer)
	{
		if (_watched.Add(layer))
		{
			layer.PropertyChanged += OnLayerChanged;
		}
	}

	/// <summary>A layer came or went: the layers are watched for their thickness, which the stack's readout follows.</summary>
	private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		foreach (FilterLayer layer in _watched.Where(layer => !_layers.Contains(layer)).ToList())
		{
			layer.PropertyChanged -= OnLayerChanged;
			_watched.Remove(layer);
		}
		foreach (FilterLayer layer in _layers)
		{
			Watch(layer);
		}
		OnPropertyChanged(nameof(StackThickness));
	}

	private void OnLayerChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FilterLayer.Thickness))
		{
			OnPropertyChanged(nameof(StackThickness));
		}
	}

	/// <summary>The stack's thickness in pixels: the sum of the layers' (guarded) thicknesses.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double StackThickness => Layers.Sum(layer => layer.GuardedThickness);

	/// <summary>
	/// A new filter's stack (the user's choices, 2026-11-07): quarter-wave layers for <see cref="DefaultStackWavelength"/>,
	/// λ / (4 n) thick at 0.01, the usual design that begins and ends with the high index: <see cref="DefaultPairs"/> pairs
	/// of high then low index from the outside in, and one more high-index layer on the plate, eleven layers in all: by
	/// thin-film theory about 92 % reflected at the design wavelength on a plate of 1.50 in air, where the earlier ten
	/// with the low index outermost gave about 70 %.
	/// </summary>
	public override void PrepareNew()
	{
		for (int pair = 0; pair < DefaultPairs; pair++)
		{
			Layers.Add(QuarterWave(DefaultHighIndex));
			Layers.Add(QuarterWave(DefaultLowIndex));
		}
		Layers.Add(QuarterWave(DefaultHighIndex));
	}

	/// <summary>A quarter-wave layer of index <paramref name="index"/> for <see cref="DefaultStackWavelength"/>.</summary>
	private static FilterLayer QuarterWave(double index) =>
		new() { RefractiveIndex = index, Thickness = Math.Round(DefaultStackWavelength / (4.0 * index), 2) };

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + StackThickness;

	/// <summary>The whole part's outline, the stack's outer face to the plate's back: a rectangle.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double back = Math.Max(1, Length) / 2.0;
		double front = -back - StackThickness;
		double w = Math.Max(1, Width) / 2.0;
		return [(front, -w), (back, -w), (back, w), (front, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(1, Width);
		Layers ??= [];
		foreach (FilterLayer layer in Layers)
		{
			layer.Normalize();
		}
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<InterferenceFilterPart>();
		// the memberwise copy shares the original's set of watched layers: a fresh set, then the new list through the
		// setter, which watches it (the setter's unsubscribing from the shared list removes the copy's handler, never the original's)
		copy._watched = [];
		copy.Layers = new ObservableCollection<FilterLayer>(Layers.Select(layer => layer.Clone()));
		return copy;
	}
}
