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
/// An ellipse of <see cref="Length"/> along the axis and <see cref="Width"/> across, centred; a circle when the two are
/// equal, which <see cref="Linked"/> keeps (2026-10-20, the user's request). Painted and outlined as a polygon of enough vertices for the chord error to
/// stay far below a pixel: a vertex every three pixels of circumference, 24 at least, 180 at most.
/// </summary>
public sealed class EllipsePart : ShapePart
{
	/// <summary>Default <see cref="Length"/> and <see cref="Width"/> in pixels.</summary>
	public const int DefaultSize = 60;

	/// <summary>Fewest vertices of the outline.</summary>
	public const int MinVertices = 24;

	/// <summary>Most vertices of the outline: a 1000 px circle still has a chord sag under 0.2 px.</summary>
	public const int MaxVertices = 180;

	private int _length = DefaultSize;
	private int _width = DefaultSize;
	private bool _linked;

	/// <inheritdoc/>
	public override string NameStem => "Ellipse";

	/// <summary>Diameter along the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Diameter along the axis in pixels.")]
	public int Length
	{
		get => _length;
		set
		{
			if (SetProperty(ref _length, value) && Linked)
			{
				Width = value;
			}
		}
	}

	/// <summary>Diameter across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Diameter across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value) && Linked)
			{
				Length = value;
			}
		}
	}

	/// <summary>Keeps <see cref="Width"/> equal to <see cref="Length"/> (a circle): a change of either sets the other, and the size grip moves both (2026-10-20, the user's request: the chain between width and height of the image editors).</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Shape")]
	[DisplayName("Linked")]
	[Description("Keep the width equal to the length.")]
	public bool Linked
	{
		get => _linked;
		set
		{
			if (SetProperty(ref _linked, value) && value)
			{
				Width = Length;
			}
		}
	}

	/// <summary>How many vertices the outline gets: one per three pixels of circumference (Ramanujan's estimate), within the limits.</summary>
	public int VertexCount
	{
		get
		{
			double a = Math.Max(1, Length) / 2.0;
			double b = Math.Max(1, Width) / 2.0;
			double h = (a - b) * (a - b) / ((a + b) * (a + b));
			double circumference = Math.PI * (a + b) * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
			return Math.Clamp((int)Math.Round(circumference / 3.0), MinVertices, MaxVertices);
		}
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		double a = Math.Max(1, Length) / 2.0;
		double b = Math.Max(1, Width) / 2.0;
		int n = VertexCount;
		var vertices = new (double X, double Y)[n];
		for (int k = 0; k < n; k++)
		{
			double theta = 2.0 * Math.PI * k / n;
			vertices[k] = (a * Math.Cos(theta), b * Math.Sin(theta));
		}
		return vertices;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(1, Width);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<EllipsePart>();
}
