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
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>The shape of one surface of a <see cref="LensPart"/> as it reads: bowing outward, flat, or bowing inward (<see cref="LensPart.ShapeOf"/>).</summary>
public enum SurfaceShape
{
	/// <summary>No bow worth a pixel: a radius of 0, or a sag at the rim under half a pixel.</summary>
	Flat,
	/// <summary>Bowing outward, away from the lens.</summary>
	Convex,
	/// <summary>Bowing inward, into the lens.</summary>
	Concave,
}

/// <summary>
/// A lens, or a curved or flat mirror when <see cref="Mirror"/> is set: the strip of <see cref="Diameter"/> across
/// the axis between two spherical surfaces of radii <see cref="R1"/> (left, facing -x') and <see cref="R2"/>
/// (right), <see cref="Thickness"/> apart on the axis. A radius is signed: R1 &gt; 0 and R2 &lt; 0 bulge outward
/// (biconvex), 0 is flat. The diameter and the centre thickness are the lens's bulk; the radii fit them: a radius
/// too curved for them is raised to the smallest that fits (<see cref="FitRadii(int, int, double, double, int)"/>), so the lens always fills its
/// diameter and the edge thickness is never negative (the user's point, 2026-10-18: a diameter of 2000 with the
/// default radii gave a tiny lens, since the surfaces crossed long before the rim).
/// </summary>
public class LensPart : CoatedPart
{
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultIndex = 1.5;
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 200;
	/// <summary>Default <see cref="R1"/> in pixels.</summary>
	public const double DefaultR1 = 300.0;
	/// <summary>Default <see cref="R2"/> in pixels.</summary>
	public const double DefaultR2 = -300.0;
	/// <summary>Default <see cref="Thickness"/> in pixels.</summary>
	public const int DefaultThickness = 40;

	private bool _mirror;
	private double _refractiveIndex = DefaultIndex;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionRamp;
	private int _diameter = DefaultDiameter;
	private double _r1 = DefaultR1;
	private double _r2 = DefaultR2;
	private int _thickness = DefaultThickness;

	/// <inheritdoc/>
	public override string NameStem => "Lens";

	/// <summary>
	/// How many zones of equal width each curved surface is cut into from the axis to the rim, the arc restarted at the
	/// vertex plane at each zone's inner edge (a <see cref="FresnelLensPart"/>, 2026-11-07): 1 for the plain lens, whose
	/// surfaces are whole arcs. The sag that counts for the fit, the reach, the edge thickness and the vertex handles is
	/// the outer zone's (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>), the deepest. Not stored here.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public virtual int ZoneCount => 1;

	/// <summary>How the zones are divided over the radius (<see cref="Model.ZoneDivision"/>): equal in width unless a Fresnel lens says otherwise. Not stored here.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public virtual ZoneDivision ZoneDivision => ZoneDivision.EqualWidth;

	/// <summary>Whether the part is a mirror (red 0, a wall no wave gets through) instead of a lens; the index, the coating and the absorption are ignored then.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Material")]
	[Description("Make the part a mirror instead of a lens. The refractive index, the coating and the absorption are ignored then.")]
	public bool Mirror
	{
		get => _mirror;
		set => SetProperty(ref _mirror, value);
	}

	/// <summary>
	/// <paramref name="name"/> with the word Lens replaced by Mirror when <paramref name="mirror"/> is set and Mirror by Lens
	/// when not, so "Lens 1" ticked as a mirror reads "Mirror 1" and unticked "Lens 1" again (2026-10-29, the user's design); a
	/// name with neither word is as given. The English stems, whatever the language, as <see cref="NameStem"/> is. The window
	/// applies it when the user ticks the box, not the setter, so a loaded, pasted or undone name stays what it was.
	/// </summary>
	public static string MirrorName(string name, bool mirror) =>
		mirror ? WordLens.Replace(name, "Mirror") : WordMirror.Replace(name, "Lens");

	private static readonly Regex WordLens = new(@"\bLens\b", RegexOptions.CultureInvariant);
	private static readonly Regex WordMirror = new(@"\bMirror\b", RegexOptions.CultureInvariant);

	/// <summary>Refractive index of the glass, 1.00 to 2.55 in steps of 0.01; not used by a mirror.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Refractive index")]
	[Description("Refractive index of the glass, 1.00 to 2.55 in steps of 0.01.")]
	public double RefractiveIndex
	{
		get => _refractiveIndex;
		set => SetProperty(ref _refractiveIndex, value);
	}

	/// <summary>
	/// How strongly the part absorbs, 0 (not at all) to 255, painted into the scene's blue channel (the engines damp a
	/// cell's velocity by 1 − blue / 255 each step) where it rises over <see cref="AbsorptionRamp"/>; a mirror ignores it.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxAbsorption)]
	[Category("Material")]
	[Description("How strongly the part absorbs waves, 0 (not at all) to 255, as the scene image's blue channel. It rises from nothing at the outline over the absorption ramp, so a wave enters the part instead of being reflected. A mirror ignores it.")]
	public int Absorption
	{
		get => _absorption;
		set => SetProperty(ref _absorption, value);
	}

	/// <summary>Depth in pixels over which the absorption rises from 0 at the outline to <see cref="Absorption"/>; at most half the part's thickness takes effect.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAbsorptionRamp)]
	[Range(1, MaxAbsorptionRamp)]
	[Category("Material")]
	[DisplayName("Absorption ramp")]
	[Description("Depth in pixels over which the absorption rises from nothing at the outline to its full value; the longer, the less of a wave is reflected. A part thinner than twice the ramp reaches its full absorption in its middle.")]
	public int AbsorptionRamp
	{
		get => _absorptionRamp;
		set => SetProperty(ref _absorptionRamp, value);
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

	/// <summary>Radius of the first surface (facing -x') in pixels: positive bulges outward, negative is concave, 0 is flat; raised when too curved for the diameter and thickness.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultR1)]
	[Category("Shape")]
	[DisplayName("R1")]
	[Description("Radius of the first surface in pixels: positive bulges outward, negative is concave, 0 is flat. A radius too curved for the diameter and the centre thickness is raised to the smallest that fits.")]
	public double R1
	{
		get => _r1;
		set
		{
			if (SetProperty(ref _r1, value))
			{
				OnPropertyChanged(nameof(IsConcave));
			}
		}
	}

	/// <summary>Radius of the second surface (facing +x') in pixels: negative bulges outward, positive is concave, 0 is flat; raised when too curved for the diameter and thickness.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultR2)]
	[Category("Shape")]
	[DisplayName("R2")]
	[Description("Radius of the second surface in pixels: negative bulges outward, positive is concave, 0 is flat. A radius too curved for the diameter and the centre thickness is raised to the smallest that fits.")]
	public double R2
	{
		get => _r2;
		set
		{
			if (SetProperty(ref _r2, value))
			{
				OnPropertyChanged(nameof(IsConcave));
			}
		}
	}

	/// <summary>
	/// Whether the lens diverges: the lensmaker's sum of curvatures, 1/R1 − 1/R2, is negative (a flat surface counts
	/// 0). Setting it to the other state turns the lens around by flipping the sign of both radii, so biconvex becomes
	/// biconcave and plano-convex plano-concave (the user's proposal, 2026-10-18). Not stored: the radii are.
	/// </summary>
	[XmlIgnore]
	[Category("Shape")]
	[DisplayName("Concave (diverging)")]
	[Description("Whether the lens diverges, its surfaces curving inward. Ticking it turns the lens around by flipping the sign of both radii; a flat surface stays flat.")]
	public bool IsConcave
	{
		get => (R1 == 0.0 ? 0.0 : 1.0 / R1) - (R2 == 0.0 ? 0.0 : 1.0 / R2) < 0.0;
		set
		{
			if (value != IsConcave)
			{
				R1 = -R1;
				R2 = -R2;
			}
		}
	}

	/// <summary>Distance between the two surfaces on the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[DisplayName("Centre thickness")]
	[Description("Distance between the two surfaces on the axis in pixels.")]
	public int Thickness
	{
		get => _thickness;
		set => SetProperty(ref _thickness, value);
	}

	/// <inheritdoc/>
	public override double AxisReach
	{
		get
		{
			(double r1, double r2) = FittedRadii();
			double half = Math.Max(2, Diameter) / 2.0;
			return Math.Max(1, Thickness) / 2.0 + Math.Max(ZoneSag(r1, half, ZoneCount, ZoneDivision), ZoneSag(r2, half, ZoneCount, ZoneDivision));
		}
	}

	/// <summary>Points per surface of the drawn outline.</summary>
	private const int OutlineSteps = 32;

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline()
	{
		(double r1, double r2) = FittedRadii();
		double half = Math.Max(2, Diameter) / 2.0;
		double vertex = Math.Max(1, Thickness) / 2.0;
		var points = new (double X, double Y)[2 * (OutlineSteps + 1)];
		for (int i = 0; i <= OutlineSteps; i++)
		{
			// down the first surface, then up the second
			double y = -half + 2.0 * half * i / OutlineSteps;
			points[i] = (-vertex + SurfaceSag(r1, y), y);
			points[2 * OutlineSteps + 1 - i] = (vertex + SurfaceSag(r2, y), y);
		}
		return points;
	}

	/// <summary>Where a surface of signed <paramref name="radius"/> lies along x' at <paramref name="y"/> from the axis, relative to its vertex: 0 for flat, positive toward +x'.</summary>
	public static double SurfaceSag(double radius, double y)
	{
		if (radius == 0.0)
		{
			return 0.0;
		}
		double inside = Math.Max(0.0, radius * radius - y * y);
		return radius - Math.CopySign(Math.Sqrt(inside), radius);
	}

	/// <summary>Thickness at the rim in pixels: the centre thickness less what the convex surfaces take, plus what the concave ones add; never negative after <see cref="FitRadii(int, int, double, double, int)"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double EdgeThickness
	{
		get
		{
			(double r1, double r2) = FittedRadii();
			double half = Math.Max(2, Diameter) / 2.0;
			return Math.Max(1, Thickness) - Math.CopySign(ZoneSag(r1, half, ZoneCount, ZoneDivision), r1) + Math.CopySign(ZoneSag(r2, half, ZoneCount, ZoneDivision), r2);
		}
	}

	/// <summary><see cref="ZoneSag(double, double, int, ZoneDivision)"/> for zones of equal width.</summary>
	public static double ZoneSag(double radius, double half, int zones) => ZoneSag(radius, half, zones, ZoneDivision.EqualWidth);

	/// <summary>
	/// How far the deepest of <paramref name="zones"/> zones of a surface of radius <paramref name="radius"/> bows out
	/// beyond its own start at the vertex plane, in pixels: the whole sag for one zone (<see cref="Sag"/>); for zones of
	/// equal width the outer zone's, the sag at <paramref name="half"/> less the sag at its inner edge, since the sag grows
	/// ever faster outward; for zones of equal depth every zone's, the whole sag over the count. The fit and the edge
	/// thickness reckon with it.
	/// </summary>
	public static double ZoneSag(double radius, double half, int zones, ZoneDivision division) =>
		zones <= 1 ? Sag(radius, half)
		: division == ZoneDivision.EqualDepth ? Sag(radius, half) / zones
		: Sag(radius, half) - Sag(radius, half * (zones - 1) / zones);

	/// <summary>
	/// Where zone boundary <paramref name="k"/> (0 the axis, <paramref name="zones"/> the rim) of a surface of radius
	/// <paramref name="radius"/> lies from the axis, in pixels: k / zones of <paramref name="half"/> for zones of equal
	/// width; for zones of equal depth where the sag reaches k / zones of the rim's, √(R² − (R − k·S/N)²), the zones
	/// narrowing outward (a flat surface is divided by width, having no sag).
	/// </summary>
	public static double ZoneBound(double radius, double half, int zones, ZoneDivision division, int k)
	{
		zones = Math.Max(1, zones);
		k = Math.Clamp(k, 0, zones);
		if (division == ZoneDivision.EqualWidth || radius == 0.0 || k == zones)
		{
			return half * k / zones;
		}
		double a = Math.Abs(radius);
		double along = a - Sag(radius, half) * k / zones;
		return Math.Sqrt(Math.Max(0.0, a * a - along * along));
	}

	/// <summary>The zone, 0 to <paramref name="zones"/> − 1, that <paramref name="across"/> pixels from the axis lies in, by the division's boundaries (<see cref="ZoneBound"/>).</summary>
	public static int ZoneIndex(double radius, double half, int zones, ZoneDivision division, double across)
	{
		zones = Math.Max(1, zones);
		double share = division == ZoneDivision.EqualWidth || radius == 0.0 || Sag(radius, half) <= 0.0
			? across / half
			: Sag(radius, Math.Min(across, half)) / Sag(radius, half);
		return Math.Clamp((int)(share * zones), 0, zones - 1);
	}

	/// <summary>Where the profile of a surface of signed <paramref name="radius"/> meets the rim, relative to its vertex, with <paramref name="zones"/> zones: <see cref="SurfaceSag"/> at the rim less the sag at the outer zone's inner edge.</summary>
	private static double ProfileSag(double radius, double half, int zones, ZoneDivision division) =>
		zones <= 1 ? SurfaceSag(radius, half) : SurfaceSag(radius, half) - SurfaceSag(radius, ZoneBound(radius, half, zones, division, zones - 1));

	/// <summary>The radii as this lens uses them: <see cref="FitRadii(int, int, double, double, int, ZoneDivision)"/> of its values, <see cref="ZoneCount"/> and <see cref="ZoneDivision"/>.</summary>
	protected (double R1, double R2) FittedRadii() => FitRadii(Diameter, Thickness, R1, R2, ZoneCount, ZoneDivision);

	/// <summary>
	/// The shape of a surface whose radius, signed so that positive bows outward, is <paramref name="outward"/> on a lens of
	/// <paramref name="diameter"/>: flat for 0 or when its sag at the rim is under half a pixel (the rule of
	/// <see cref="SetVertex(int, double, double, double)"/>; 2026-10-29, the user's report: Example 1's mirror of radii 14401
	/// on 104 px, a sag of a tenth of a pixel, showed as biconvex), else convex or concave by the sign. R1 is signed so; R2
	/// bows outward when negative, so it is passed negated.
	/// </summary>
	public static SurfaceShape ShapeOf(double outward, int diameter) =>
		!double.IsFinite(outward) || Sag(outward, Math.Max(2, diameter) / 2.0) < 0.5 ? SurfaceShape.Flat : outward > 0.0 ? SurfaceShape.Convex : SurfaceShape.Concave;

	/// <summary>How far a surface of radius <paramref name="radius"/> bows out beyond its vertex at <paramref name="half"/> (half the diameter) from the axis, in pixels; 0 for a flat surface.</summary>
	public static double Sag(double radius, double half)
	{
		if (radius == 0.0)
		{
			return 0.0;
		}
		double a = Math.Abs(radius);
		double h = Math.Min(a, half);
		return a - Math.Sqrt(a * a - h * h);
	}

	/// <summary>
	/// The radii as the lens uses them: a non-finite or zero radius is flat; a magnitude below half the diameter
	/// becomes half the diameter (the arc must span the rim); and when the convex surfaces (R1 &gt; 0, R2 &lt; 0)
	/// would bow out by more than the centre thickness plus what the concave ones add, they are flattened in
	/// proportion to the radius at which their sags just fit, so the surfaces meet at the rim and not before it.
	/// Shared by <see cref="Normalize"/>, the painter and the readouts, so all see the same lens.
	/// </summary>
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2) => FitRadii(diameter, thickness, r1, r2, 1);

	/// <summary>
	/// <see cref="FitRadii(int, int, double, double)"/> for a surface cut into <paramref name="zones"/> zones (a Fresnel lens):
	/// the sag that must fit is the outer zone's (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>), the deepest, so a thin plate holds radii a whole
	/// lens could not; and the outer zone's outer edge is where two surfaces would meet first, since within every zone and
	/// from zone to zone the convex surfaces' loss outgrows the concave ones' gain.
	/// </summary>
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2, int zones) =>
		FitRadii(diameter, thickness, r1, r2, zones, ZoneDivision.EqualWidth);

	/// <summary><see cref="FitRadii(int, int, double, double, int)"/> with the zones divided as <paramref name="division"/> says: the deepest zone's sag (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>) must fit.</summary>
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2, int zones, ZoneDivision division)
	{
		double half = Math.Max(2, diameter) / 2.0;
		zones = Math.Max(1, zones);
		r1 = GuardRadius(r1, half);
		r2 = GuardRadius(r2, half);
		double sag1 = ZoneSag(r1, half, zones, division);
		double sag2 = ZoneSag(r2, half, zones, division);
		double convex = (r1 > 0.0 ? sag1 : 0.0) + (r2 < 0.0 ? sag2 : 0.0);
		double budget = Math.Max(1, thickness) + (r1 < 0.0 ? sag1 : 0.0) + (r2 > 0.0 ? sag2 : 0.0);
		// a hair over the budget (the floating point of a stretched lens's sags) is no case for a refit
		if (convex > budget + 1e-9)
		{
			double scale = budget / convex;
			if (r1 > 0.0)
			{
				r1 = RadiusForSag(half, zones, division, sag1 * scale);
			}
			if (r2 < 0.0)
			{
				r2 = -RadiusForSag(half, zones, division, sag2 * scale);
			}
		}
		return (r1, r2);
	}

	/// <summary>The radius whose deepest zone's sag (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>) at <paramref name="half"/> from the axis with <paramref name="zones"/> zones is <paramref name="sag"/>, rounded up to a whole pixel (a flatter surface still fits).</summary>
	private static double RadiusForSag(double half, int zones, ZoneDivision division, double sag) => Math.Ceiling(RadiusOfZoneSag(half, zones, division, sag));

	/// <summary>
	/// The radius, at least <paramref name="half"/>, whose deepest zone's sag (<see cref="ZoneSag(double, double, int, ZoneDivision)"/>)
	/// is <paramref name="sag"/>: the sagitta (h² + s²) / 2s of the whole sag for one zone and for zones of equal depth
	/// (whose whole sag is the count times the zone's); for zones of equal width found by bisection, the outer zone's sag
	/// falling steadily from the hemisphere's at a radius of half toward 0 as the radius grows (a sag the hemisphere
	/// cannot reach gives half).
	/// </summary>
	private static double RadiusOfZoneSag(double half, int zones, ZoneDivision division, double sag)
	{
		if (zones <= 1 || division == ZoneDivision.EqualDepth)
		{
			double whole = Math.Min(half, sag * Math.Max(1, zones));
			return (half * half + whole * whole) / (2.0 * whole);
		}
		if (sag >= ZoneSag(half, half, zones))
		{
			return half;
		}
		double low = half, high = half * 2.0;
		while (ZoneSag(high, half, zones) > sag)
		{
			high *= 2.0;
		}
		for (int i = 0; i < 100 && high - low > 1e-9 * high; i++)
		{
			double mid = (low + high) / 2.0;
			if (ZoneSag(mid, half, zones) > sag)
			{
				low = mid;
			}
			else
			{
				high = mid;
			}
		}
		return high;
	}

	/// <summary>
	/// Sets the diameter and the centre thickness the size grip stands for by stretching the lens as a rectangle's or an
	/// ellipse's grip stretches theirs (2026-10-26, Jeroen's report and the user's rule after it: a grip dragged a little inward
	/// had the radii fitted flat and the lens was a rectangle; held sags made a stretched lens a slab): the diameter stretches
	/// the lens across, the profile along the axis held; the thickness stretches the whole profile along the axis, the sags
	/// and the edge with it, so a thinner lens is the same lens squashed and never a slab; pulled out diagonally the lens
	/// scales as a whole, its shape kept. The surfaces are refitted as circles through the stretched rim and vertex (a sag
	/// beyond the half diameter is a hemisphere's), the radii to a hundredth of a pixel (<see cref="RadiusOfSag"/>). Nothing
	/// stops the grip but a thickness of 1. Typed values are fitted as before.
	/// </summary>
	/// <param name="diameter">The diameter asked for, in pixels.</param>
	/// <param name="thickness">The centre thickness asked for, in pixels.</param>
	public void SetExtents(int diameter, int thickness)
	{
		(double r1, double r2) = FittedRadii();
		double half = Math.Max(2, Diameter) / 2.0;
		double grown = Math.Max(2, diameter) / 2.0;
		double along = Math.Max(1, thickness) / (double)Math.Max(1, Thickness);
		if (grown == half && along == 1.0)
		{
			return;
		}
		// the sags signed as the radii are: surface 1 convex is positive, surface 2 convex is negative (the profile's at the rim: the outer zone's)
		double sag1 = Math.Clamp(ProfileSag(r1, half, ZoneCount, ZoneDivision) * along, -grown, grown);
		double sag2 = Math.Clamp(ProfileSag(r2, half, ZoneCount, ZoneDivision) * along, -grown, grown);
		// the thicker value first, so no state in between has the radii fitted
		if (thickness > Thickness)
		{
			Thickness = Math.Max(1, thickness);
		}
		Diameter = Math.Max(2, diameter);
		R1 = RadiusOfSag(1, grown, sag1);
		R2 = RadiusOfSag(2, grown, sag2);
		Thickness = Math.Max(1, thickness);
	}

	/// <summary>
	/// The radius, signed as <paramref name="sag"/> is, whose surface <paramref name="surface"/> (1 or 2) has that sag at
	/// <paramref name="half"/> from the axis: the sagitta (h² + s²) / 2s; 0 for no sag. Rounded to a hundredth of a pixel
	/// (2026-10-28, Jeroen's report: a dragged vertex or grip left sixteen decimals in the box and the file), surface 1's
	/// towards +∞ and surface 2's towards −∞, which makes a convex surface a hair flatter and a concave one a hair deeper, never
	/// the other way, so the rounding never brings on the refit of <see cref="FitRadii(int, int, double, double, int)"/> for a lens whose surfaces meet at the rim.
	/// </summary>
	private double RadiusOfSag(int surface, double half, double sag)
	{
		if (sag == 0.0)
		{
			return 0.0;
		}
		// with zones the sag is the outer zone's (the profile at the rim), the radius found by bisection
		double hundredths = Math.CopySign(RadiusOfZoneSag(half, ZoneCount, ZoneDivision, Math.Abs(sag)), sag) * 100.0;
		return (surface == 1 ? Math.Ceiling(hundredths) : Math.Floor(hundredths)) / 100.0;
	}

	/// <summary>Where the vertex of surface <paramref name="surface"/> (1 the left, 2 the right) sits on the axis in the part frame: ∓<see cref="Thickness"/> / 2.</summary>
	public double VertexX(int surface) => (surface == 1 ? -1 : 1) * Math.Max(1, Thickness) / 2.0;

	/// <summary>Where surface <paramref name="surface"/> meets the rim, on the axis in the part frame: its vertex plus its sag at the half diameter, the radii as fitted.</summary>
	public double RimX(int surface)
	{
		(double r1, double r2) = FittedRadii();
		return VertexX(surface) + ProfileSag(surface == 1 ? r1 : r2, Math.Max(2, Diameter) / 2.0, ZoneCount, ZoneDivision);
	}

	/// <summary>
	/// Moves the vertex of surface <paramref name="surface"/> to <paramref name="x"/> on the axis (part frame), the surface's
	/// rim and the other surface held (2026-10-26, Jeroen's proposal, a handle on each vertex): the sag at the rim changes and
	/// with it the radius; pulled outward the surface bulges, pushed through the rim plane it is flat (within half a pixel),
	/// then concave, a hemisphere at most either way. The centre thickness follows, a whole pixel of at least 1, and the
	/// part's position moves to the new centre, rounded to a pixel. <paramref name="rim"/> and <paramref name="other"/> are
	/// where the rim and the other vertex are (a drag captures them as it starts, so the position's rounding never creeps
	/// into them).
	/// </summary>
	public void SetVertex(int surface, double x, double rim, double other)
	{
		double half = Math.Max(2, Diameter) / 2.0;
		int side = surface == 1 ? -1 : 1;
		int thickness = Math.Max(1, (int)Math.Round(side * (x - other)));
		x = other + side * thickness;
		double sag = Math.Clamp(side * (x - rim), -half, half);
		if (Math.Abs(sag) < 0.5)
		{
			sag = 0.0;
		}
		// the sagitta: surface 1 bulging left is R1 > 0, surface 2 bulging right is R2 < 0
		double radius = RadiusOfSag(surface, half, -side * sag);
		bool thicker = thickness > Thickness;
		if (thicker)
		{
			Thickness = thickness;
		}
		if (surface == 1)
		{
			R1 = radius;
		}
		else
		{
			R2 = radius;
		}
		if (!thicker)
		{
			Thickness = thickness;
		}
		(double cx, double cy) = ToCanvas((x + other) / 2.0, 0.0);
		X = (int)Math.Round(cx);
		Y = (int)Math.Round(cy);
	}

	/// <summary>Moves a vertex as <see cref="SetVertex(int, double, double, double)"/> does, the rim and the other vertex where they are now (a keyboard step).</summary>
	public void SetVertex(int surface, double x) => SetVertex(surface, x, RimX(surface), VertexX(surface == 1 ? 2 : 1));

	/// <summary>A radius of at least <paramref name="half"/> in magnitude, its sign kept; a non-finite or zero one is flat.</summary>
	private static double GuardRadius(double radius, double half) =>
		!double.IsFinite(radius) || radius == 0.0 ? 0.0 : Math.Abs(radius) < half ? Math.CopySign(half, radius) : radius;

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		RefractiveIndex = NormalizeIndex(RefractiveIndex);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
		Diameter = Math.Max(2, Diameter);
		Thickness = Math.Max(1, Thickness);
		(double r1, double r2) = FittedRadii();
		R1 = r1;
		R2 = r2;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<LensPart>();
}
