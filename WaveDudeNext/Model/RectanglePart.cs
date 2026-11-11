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

/// <summary>A rectangle of <see cref="Length"/> along the axis and <see cref="Width"/> across, centred. A flat mirror is a long, narrow one.</summary>
public sealed class RectanglePart : ShapePart
{
	/// <summary>Default <see cref="Length"/> and <see cref="Width"/> in pixels.</summary>
	public const int DefaultSize = 60;

	private int _length = DefaultSize;
	private int _width = DefaultSize;
	private bool _linked;

	/// <inheritdoc/>
	public override string NameStem => "Rectangle";

	/// <summary>Extent along the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels.")]
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

	/// <summary>Extent across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSize)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels.")]
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

	/// <summary>Keeps <see cref="Width"/> equal to <see cref="Length"/> (a square): a change of either sets the other, and the size grip moves both (2026-10-20, the user's request: the chain between width and height of the image editors).</summary>
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

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		double l = Math.Max(1, Length) / 2.0;
		double w = Math.Max(1, Width) / 2.0;
		return [(-l, -w), (l, -w), (l, w), (-l, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(1, Width);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<RectanglePart>();
}
