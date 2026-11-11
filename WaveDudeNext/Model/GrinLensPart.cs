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
/// A gradient-index lens: a rectangle of <see cref="Length"/> along the axis and <see cref="Diameter"/> across whose
/// index falls from <see cref="CentreIndex"/> on the axis to <see cref="EdgeIndex"/> at the edge as
/// n(y') = n_c − (n_c − n_e)·(2y'/D)².
/// </summary>
public sealed class GrinLensPart : CoatedPart
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 100;
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 100;
	/// <summary>Default <see cref="CentreIndex"/>.</summary>
	public const double DefaultCentreIndex = 1.8;
	/// <summary>Default <see cref="EdgeIndex"/>.</summary>
	public const double DefaultEdgeIndex = 1.5;

	private int _length = DefaultLength;
	private int _diameter = DefaultDiameter;
	private double _centreIndex = DefaultCentreIndex;
	private double _edgeIndex = DefaultEdgeIndex;

	/// <inheritdoc/>
	public override string NameStem => "GRIN lens";

	/// <summary>Extent along the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels.")]
	public int Diameter
	{
		get => _diameter;
		set => SetProperty(ref _diameter, value);
	}

	/// <summary>Refractive index on the axis, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCentreIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Centre index")]
	[Description("Refractive index on the axis, 1.00 to 2.55 in steps of 0.01.")]
	public double CentreIndex
	{
		get => _centreIndex;
		set => SetProperty(ref _centreIndex, value);
	}

	/// <summary>Refractive index at the edge, 1.00 to 2.55; the profile between is parabolic.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultEdgeIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Edge index")]
	[Description("Refractive index at the edge, 1.00 to 2.55 in steps of 0.01. The profile between the axis and the edge is parabolic.")]
	public double EdgeIndex
	{
		get => _edgeIndex;
		set => SetProperty(ref _edgeIndex, value);
	}

	/// <inheritdoc/>
	public override double AxisReach => Length / 2.0;

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline()
	{
		double l = Math.Max(1, Length) / 2.0;
		double d = Math.Max(1, Diameter) / 2.0;
		return [(-l, -d), (l, -d), (l, d), (-l, d)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Diameter = Math.Max(2, Diameter);
		CentreIndex = NormalizeIndex(CentreIndex);
		EdgeIndex = NormalizeIndex(EdgeIndex);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<GrinLensPart>();
}
