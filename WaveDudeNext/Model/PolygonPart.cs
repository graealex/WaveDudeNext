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
/// A regular polygon of <see cref="Sides"/> sides of <see cref="Side"/> pixels, a face centred on -x' (toward a source on
/// the left); for an odd count the first vertex lies on +x'.
/// </summary>
public sealed class PolygonPart : ShapePart
{
	/// <summary>Default <see cref="Sides"/>.</summary>
	public const int DefaultSides = 6;
	/// <summary>Smallest <see cref="Sides"/>.</summary>
	public const int MinSides = 3;
	/// <summary>Largest <see cref="Sides"/>.</summary>
	public const int MaxSides = 64;
	/// <summary>Default <see cref="Side"/> in pixels.</summary>
	public const int DefaultSide = 60;

	private int _sides = DefaultSides;
	private int _side = DefaultSide;

	/// <inheritdoc/>
	public override string NameStem => "Polygon";

	/// <summary>Number of sides, 3 to 64.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSides)]
	[Range(MinSides, MaxSides)]
	[Category("Shape")]
	[DisplayName("Number of sides")]
	[Description("Number of sides, 3 to 64.")]
	public int Sides
	{
		get => _sides;
		set => SetProperty(ref _sides, value);
	}

	/// <summary>Length of each side in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSide)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[DisplayName("Side length")]
	[Description("Length of each side in pixels.")]
	public int Side
	{
		get => _side;
		set => SetProperty(ref _side, value);
	}

	/// <summary>Circumradius in pixels: side / (2 sin(π / sides)).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Circumradius => Math.Max(2, Side) / (2.0 * Math.Sin(Math.PI / Math.Clamp(Sides, MinSides, MaxSides)));

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		int sides = Math.Clamp(Sides, MinSides, MaxSides);
		double radius = Circumradius;
		var vertices = new (double X, double Y)[sides];
		for (int k = 0; k < sides; k++)
		{
			double theta = Math.PI + (2 * k + 1) * Math.PI / sides;
			vertices[k] = (radius * Math.Cos(theta), radius * Math.Sin(theta));
		}
		return vertices;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Sides = Math.Clamp(Sides, MinSides, MaxSides);
		Side = Math.Max(2, Side);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<PolygonPart>();
}
