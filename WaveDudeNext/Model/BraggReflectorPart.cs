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

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>
/// A Bragg reflector: <see cref="Pairs"/> pairs of layers of <see cref="Thickness1"/> and <see cref="Thickness2"/>
/// pixels with the indices <see cref="Index1"/> and <see cref="Index2"/>, stacked along +x', the stack centred on
/// the part's centre and <see cref="Width"/> across. Thicknesses are pixels as typed; a quarter-wave layer is
/// λ / (4 n) pixels.
/// </summary>
public sealed class BraggReflectorPart : OpticalPart
{
	/// <summary>Default <see cref="Pairs"/>.</summary>
	public const int DefaultPairs = 10;
	/// <summary>Largest <see cref="Pairs"/>.</summary>
	public const int MaxPairs = 200;
	/// <summary>Default layer thickness in pixels.</summary>
	public const int DefaultThickness = 10;
	/// <summary>Default <see cref="Index1"/>.</summary>
	public const double DefaultIndex1 = 1.5;
	/// <summary>Default <see cref="Index2"/>.</summary>
	public const double DefaultIndex2 = 2.0;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 200;

	private int _pairs = DefaultPairs;
	private int _thickness1 = DefaultThickness;
	private int _thickness2 = DefaultThickness;
	private double _index1 = DefaultIndex1;
	private double _index2 = DefaultIndex2;
	private int _width = DefaultWidth;

	/// <inheritdoc/>
	public override string NameStem => "Bragg reflector";

	/// <summary>Number of layer pairs, 1 to 200.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPairs)]
	[Range(1, MaxPairs)]
	[Category("Layers")]
	[DisplayName("Layer pairs")]
	[Description("Number of layer pairs, 1 to 200.")]
	public int Pairs
	{
		get => _pairs;
		set => SetProperty(ref _pairs, value);
	}

	/// <summary>Thickness of the first layer of each pair in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Layers")]
	[DisplayName("Thickness 1")]
	[Description("Thickness of the first layer of each pair in pixels. A quarter-wave layer is λ / (4 n) pixels thick.")]
	public int Thickness1
	{
		get => _thickness1;
		set => SetProperty(ref _thickness1, value);
	}

	/// <summary>Thickness of the second layer of each pair in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Layers")]
	[DisplayName("Thickness 2")]
	[Description("Thickness of the second layer of each pair in pixels. A quarter-wave layer is λ / (4 n) pixels thick.")]
	public int Thickness2
	{
		get => _thickness2;
		set => SetProperty(ref _thickness2, value);
	}

	/// <summary>Refractive index of the first layer of each pair, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex1)]
	[Range(MinIndex, MaxIndex)]
	[Category("Layers")]
	[DisplayName("Index 1")]
	[Description("Refractive index of the first layer of each pair, 1.00 to 2.55 in steps of 0.01.")]
	public double Index1
	{
		get => _index1;
		set => SetProperty(ref _index1, value);
	}

	/// <summary>Refractive index of the second layer of each pair, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex2)]
	[Range(MinIndex, MaxIndex)]
	[Category("Layers")]
	[DisplayName("Index 2")]
	[Description("Refractive index of the second layer of each pair, 1.00 to 2.55 in steps of 0.01.")]
	public double Index2
	{
		get => _index2;
		set => SetProperty(ref _index2, value);
	}

	/// <summary>Extent of the stack across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent of the stack across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set => SetProperty(ref _width, value);
	}

	/// <summary>Total thickness of the stack along the axis in pixels.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int Length => Pairs * (Thickness1 + Thickness2);

	/// <inheritdoc/>
	public override double AxisReach => Length / 2.0;

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline()
	{
		double l = Math.Clamp(Pairs, 1, MaxPairs) * (Math.Max(1, Thickness1) + Math.Max(1, Thickness2)) / 2.0;
		double w = Math.Max(1, Width) / 2.0;
		return [(-l, -w), (l, -w), (l, w), (-l, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Pairs = Math.Clamp(Pairs, 1, MaxPairs);
		Thickness1 = Math.Max(1, Thickness1);
		Thickness2 = Math.Max(1, Thickness2);
		Index1 = NormalizeIndex(Index1);
		Index2 = NormalizeIndex(Index2);
		Width = Math.Max(2, Width);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<BraggReflectorPart>();
}
