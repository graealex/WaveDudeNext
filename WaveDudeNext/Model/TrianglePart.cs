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

/// <summary>The presets of a <see cref="TrianglePart"/>: which base angles are fixed.</summary>
public enum TriangleType
{
	/// <summary>Both base angles 60°.</summary>
	Equilateral,
	/// <summary>Both base angles 45°: the right angle at the apex (the total-internal-reflection prism).</summary>
	RightIsosceles,
	/// <summary>Base angles 90° and 30°: the right angle at the first end of the base.</summary>
	ThirtySixtyNinety,
	/// <summary>Both base angles the same, typed once.</summary>
	Isosceles,
	/// <summary>Both base angles typed.</summary>
	Custom,
}

/// <summary>
/// A triangle on a <see cref="Base"/> across the axis (the face toward a source on the left, at -x'), the apex pointing
/// along +x', shaped by its two base angles <see cref="AngleA"/> (at the -y' end) and <see cref="AngleB"/> (at the +y'
/// end); the apex angle is what remains of 180°. A <see cref="Type"/> preset fixes the angles.
/// </summary>
public sealed class TrianglePart : ShapePart
{
	/// <summary>Default <see cref="Base"/> in pixels.</summary>
	public const int DefaultBase = 60;
	/// <summary>Largest base angle; the two must leave something for the apex.</summary>
	public const double MaxAngle = 178.0;

	private int _base = DefaultBase;
	private TriangleType _type = TriangleType.Equilateral;
	private double _angleA = 60.0;
	private double _angleB = 60.0;

	/// <inheritdoc/>
	public override string NameStem => "Triangle";

	/// <summary>Length of the base in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultBase)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Length of the base in pixels; the base lies across the axis, toward a source on the left.")]
	public int Base
	{
		get => _base;
		set => SetProperty(ref _base, value);
	}

	/// <summary>The preset: equilateral, right isosceles (45°), 30-60-90, isosceles with one typed angle, or custom with both typed. Choosing one sets the angles.</summary>
	[XmlAttribute]
	[DefaultValue(TriangleType.Equilateral)]
	[Category("Shape")]
	[Description("Equilateral, right isosceles (the 45° prism), 30-60-90, isosceles with one typed angle, or custom with both typed.")]
	public TriangleType Type
	{
		get => _type;
		set
		{
			if (SetProperty(ref _type, value))
			{
				ApplyPreset();
			}
		}
	}

	/// <summary>Base angle at the first end of the base (-y'), in degrees; typed for the isosceles and custom types.</summary>
	[XmlAttribute]
	[DefaultValue(60.0)]
	[Range(1.0, MaxAngle)]
	[Category("Shape")]
	[DisplayName("Angle A")]
	[Description("Base angle at the first end of the base, in degrees. Typed for the isosceles and custom types; the presets fix it.")]
	public double AngleA
	{
		get => _angleA;
		set
		{
			if (SetProperty(ref _angleA, value) && Type == TriangleType.Isosceles)
			{
				AngleB = value;
			}
		}
	}

	/// <summary>Base angle at the second end of the base (+y'), in degrees; typed for the custom type only.</summary>
	[XmlAttribute]
	[DefaultValue(60.0)]
	[Range(1.0, MaxAngle)]
	[Category("Shape")]
	[DisplayName("Angle B")]
	[Description("Base angle at the second end of the base, in degrees. Typed for the custom type; the others fix it.")]
	public double AngleB
	{
		get => _angleB;
		set => SetProperty(ref _angleB, value);
	}

	/// <summary>The angle at the apex in degrees: 180 minus the two base angles (as the painter uses them).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ApexAngle
	{
		get
		{
			(double a, double b) = EffectiveAngles();
			return 180.0 - a - b;
		}
	}

	/// <summary>The base angles the type gives, in degrees, within range and leaving at least 2° for the apex; the typed values for the custom type.</summary>
	public (double A, double B) EffectiveAngles()
	{
		double a = double.IsFinite(AngleA) ? Math.Clamp(AngleA, 1.0, MaxAngle) : 60.0;
		double b = double.IsFinite(AngleB) ? Math.Clamp(AngleB, 1.0, MaxAngle) : 60.0;
		switch (Type)
		{
			case TriangleType.Equilateral:
				return (60.0, 60.0);
			case TriangleType.RightIsosceles:
				return (45.0, 45.0);
			case TriangleType.ThirtySixtyNinety:
				return (90.0, 30.0);
			case TriangleType.Isosceles:
				a = Math.Min(a, 89.0);
				return (a, a);
			default:
				return (a, Math.Min(b, MaxAngle + 1.0 - a));
		}
	}

	/// <summary>Writes the type's angles into <see cref="AngleA"/> and <see cref="AngleB"/>.</summary>
	private void ApplyPreset()
	{
		(double a, double b) = EffectiveAngles();
		AngleA = a;
		AngleB = b;
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices()
	{
		(double a, double b) = EffectiveAngles();
		double alpha = a * Math.PI / 180.0;
		double beta = b * Math.PI / 180.0;
		double half = Math.Max(2, Base) / 2.0;
		// the height from the law of sines, the apex over the base at h / tan α from the first end
		double height = 2.0 * half * Math.Sin(alpha) * Math.Sin(beta) / Math.Sin(alpha + beta);
		double apexY = -half + height * Math.Cos(alpha) / Math.Sin(alpha);
		return [(-height / 2.0, -half), (-height / 2.0, half), (height / 2.0, apexY)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Base = Math.Max(2, Base);
		ApplyPreset();
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<TrianglePart>();
}
