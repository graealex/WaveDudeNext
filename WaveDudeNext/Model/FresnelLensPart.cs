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
/// A Fresnel lens (the user's request, 2026-11-07): the lens of <see cref="LensPart"/> with each curved surface cut into
/// <see cref="Zones"/> zones from the axis to the rim, the arc restarted at the vertex plane at each zone's inner edge,
/// so the glass between the arcs is cut away and a thin plate bends light as the thick lens would (the user's choices:
/// both curved surfaces, a flat surface staying flat). The zones are divided as <see cref="Division"/> says: equal in
/// width, or equal in depth as a lighthouse lens's elements are (the user's request the same day), each zone then taking
/// the same share of the rim's sag, so they narrow outward. Within zone k, |y'| from its boundary y'ₖ to y'ₖ₊₁
/// (<see cref="LensPart.ZoneBound"/>), a surface lies at s(y') − s(y'ₖ) from its vertex; at a zone boundary a step across
/// the axis leads back to the vertex plane. The sag that counts for the fit
/// (<see cref="LensPart.FitRadii(int, int, double, double, int, ZoneDivision)"/>), the edge thickness and the vertex handles
/// is the deepest zone's. A new one is a plate of <see cref="DefaultPlateThickness"/>, <see cref="DefaultDiameter"/>
/// across with radii of <see cref="DefaultRadius"/> (<see cref="PrepareNew"/>; a loaded one has the file's values). The
/// lens's Concave box is not shown for it: a surface is hollowed by a negative radius (the user's choice).
/// </summary>
public sealed class FresnelLensPart : LensPart
{
	/// <summary>Default <see cref="Zones"/>.</summary>
	public const int DefaultZones = 4;
	/// <summary>Largest <see cref="Zones"/>.</summary>
	public const int MaxZones = 100;
	/// <summary>A new Fresnel lens's centre thickness in pixels (the user's choice, 2026-11-07): a thin plate.</summary>
	public const int DefaultPlateThickness = 20;
	/// <summary>A new Fresnel lens's diameter in pixels (the user's choice, 2026-11-07: about 500).</summary>
	public new const int DefaultDiameter = 500;
	/// <summary>A new Fresnel lens's radii in pixels, R1 and −R2: with four zones of equal width on a diameter of 500 the outer zone's sag is 9.2 px a surface, so the two fit in the plate of 20.</summary>
	public const double DefaultRadius = 1500.0;

	private int _zones = DefaultZones;
	private ZoneDivision _division = ZoneDivision.EqualWidth;

	/// <inheritdoc/>
	public override string NameStem => "Fresnel lens";

	/// <summary>How the zones are divided over the radius: equal in width, or equal in depth (each zone the same sag, the zones narrowing outward, as in a lighthouse lens).</summary>
	[XmlAttribute]
	[DefaultValue(ZoneDivision.EqualWidth)]
	[Category("Shape")]
	[DisplayName("Zone division")]
	[Description("How the zones are divided from the axis to the rim: equal in width, or equal in depth, each zone cut to the same depth and the zones narrowing outward, as the elements of a lighthouse lens are.")]
	public ZoneDivision Division
	{
		get => _division;
		set
		{
			if (SetProperty(ref _division, value))
			{
				OnPropertyChanged(nameof(ZoneDivision));
			}
		}
	}

	/// <inheritdoc/>
	public override ZoneDivision ZoneDivision => Enum.IsDefined(Division) ? Division : ZoneDivision.EqualWidth;

	/// <summary>Number of zones each curved surface is cut into from the axis to the rim, 1 to <see cref="MaxZones"/>, divided by <see cref="Division"/>; 1 is the plain lens.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultZones)]
	[Range(1, MaxZones)]
	[Category("Shape")]
	[Description("Number of zones each curved surface is cut into, from the axis to the rim, equal in width or in depth as the zone division says. At each zone's inner edge the arc starts again at the vertex plane, so the plate stays thin; 1 is a plain lens.")]
	public int Zones
	{
		get => _zones;
		set
		{
			if (SetProperty(ref _zones, value))
			{
				OnPropertyChanged(nameof(ZoneCount));
			}
		}
	}

	/// <inheritdoc/>
	public override int ZoneCount => Math.Clamp(Zones, 1, MaxZones);

	/// <summary>A new Fresnel lens is a thin plate of <see cref="DefaultPlateThickness"/>, <see cref="DefaultDiameter"/> across, its radii <see cref="DefaultRadius"/>.</summary>
	public override void PrepareNew()
	{
		Diameter = DefaultDiameter;
		R1 = DefaultRadius;
		R2 = -DefaultRadius;
		Thickness = DefaultPlateThickness;
	}

	/// <summary>The profile with its zones: each zone's arc, and at each zone boundary the step back to the vertex plane, down the first surface and up the second.</summary>
	public override (double X, double Y)[] Outline()
	{
		(double r1, double r2) = FittedRadii();
		double half = Math.Max(2, Diameter) / 2.0;
		double vertex = Math.Max(1, Thickness) / 2.0;
		List<(double X, double Y)> first = Profile(-vertex, r1, half, ZoneCount, ZoneDivision);
		List<(double X, double Y)> second = Profile(vertex, r2, half, ZoneCount, ZoneDivision);
		second.Reverse();
		return [.. first, .. second];
	}

	/// <summary>One surface's profile from −half to half: the zones in turn by their boundaries (<see cref="LensPart.ZoneBound"/>), each sampled along its arc from its own start at the vertex plane, so a zone's last point and the next zone's first make the step.</summary>
	private static List<(double X, double Y)> Profile(double vertex, double radius, double half, int zones, ZoneDivision division)
	{
		int steps = Math.Max(2, 32 / zones);
		var points = new List<(double X, double Y)>(2 * zones * (steps + 1));
		for (int k = -zones; k < zones; k++)
		{
			int zone = k < 0 ? -k - 1 : k;
			double inner = ZoneBound(radius, half, zones, division, zone);
			double outer = ZoneBound(radius, half, zones, division, zone + 1);
			double shift = SurfaceSag(radius, inner);
			// the negative side runs from the zone's outer edge in to its inner one
			(double from, double to) = k < 0 ? (-outer, -inner) : (inner, outer);
			for (int i = 0; i <= steps; i++)
			{
				double y = from + (to - from) * i / steps;
				points.Add((vertex + SurfaceSag(radius, y) - shift, y));
			}
		}
		return points;
	}

	/// <summary>The zones and the division first, so the fit of the radii reckons with the clamped count.</summary>
	public override void Normalize()
	{
		Zones = Math.Clamp(Zones, 1, MaxZones);
		Division = ZoneDivision;
		base.Normalize();
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<FresnelLensPart>();
}

/// <summary>How a <see cref="FresnelLensPart"/>'s zones are divided over the radius (2026-11-07, the user's request for both).</summary>
public enum ZoneDivision
{
	/// <summary>Every zone the same width from the axis to the rim; the outer zone is the deepest.</summary>
	EqualWidth,
	/// <summary>Every zone the same depth, the rim's sag shared out equally, so the zones narrow outward: the elements of a lighthouse lens.</summary>
	EqualDepth,
}
