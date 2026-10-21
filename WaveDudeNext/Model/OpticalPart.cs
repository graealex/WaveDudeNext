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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>The kinds of optical part; one subclass of <see cref="OpticalPart"/> and one project file element per kind.</summary>
public enum PartKind
{
	/// <summary>A lens or a curved or flat mirror (<see cref="LensPart"/>).</summary>
	Lens,
	/// <summary>A regular polygon (<see cref="PolygonPart"/>).</summary>
	Polygon,
	/// <summary>A rectangle (<see cref="RectanglePart"/>).</summary>
	Rectangle,
	/// <summary>An ellipse or circle (<see cref="EllipsePart"/>).</summary>
	Ellipse,
	/// <summary>A triangle shaped by its base angles (<see cref="TrianglePart"/>).</summary>
	Triangle,
	/// <summary>A free outline from a point list (<see cref="OutlinePart"/>).</summary>
	Outline,
	/// <summary>A fibre with a core and a cladding along a path (<see cref="FiberPart"/>).</summary>
	Fiber,
	/// <summary>A stack of alternating layers (<see cref="BraggReflectorPart"/>).</summary>
	BraggReflector,
	/// <summary>A gradient-index lens (<see cref="GrinLensPart"/>).</summary>
	GrinLens,
}

/// <summary>
/// An optical part placed on the canvas: a lens or mirror, a shape (polygon, rectangle, triangle, outline), a fibre, a
/// Bragg reflector or a GRIN lens (Jeroen
/// Vleggaar's proposal, decided 2026-10-18). Parts are painted into the red channel of the scene pixels before a run
/// (<see cref="Parts.PartPainter"/>), so the engines, the outline and the export see them as part of the scene;
/// green and blue are never touched. Positions and lengths are canvas pixels; the angle is in degrees,
/// counter-clockwise on the screen, 0 pointing the part's axis along +x (the composite source's convention).
/// Refractive indices run from 1.00 to 2.55 in steps of 0.01 (red = index × 100). One subclass per kind; the kind is
/// chosen when the part is added and never changes.
/// </summary>
public abstract class OpticalPart : ObservableObject, IMovable
{
	/// <summary>Smallest refractive index a part can have.</summary>
	public const double MinIndex = 1.0;
	/// <summary>Largest refractive index a part can have (red 255).</summary>
	public const double MaxIndex = 2.55;
	/// <summary>Largest coating thickness in pixels.</summary>
	public const int MaxCoating = 50;
	/// <summary>Largest absorption (blue 255, the velocity damped away in one step).</summary>
	public const int MaxAbsorption = 255;
	/// <summary>Default absorption ramp in pixels.</summary>
	public const int DefaultAbsorptionRamp = 40;
	/// <summary>Largest absorption ramp in pixels.</summary>
	public const int MaxAbsorptionRamp = 400;

	private string _name = "";
	private bool _enabled = true;
	private int _x;
	private int _y;
	private double _angle;

	/// <summary>Name shown in the project tree and on the canvas; must be non-blank.</summary>
	[XmlAttribute]
	[Category("General")]
	[Description("Name shown in the project tree and on the canvas.")]
	public string Name
	{
		get => _name;
		set => SetProperty(ref _name, value);
	}

	/// <summary>Whether the part is painted into the scene; a disabled part has no effect on a run.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("General")]
	[Description("Paint the part into the scene; a disabled part has no effect on a run.")]
	public bool Enabled
	{
		get => _enabled;
		set => SetProperty(ref _enabled, value);
	}

	/// <summary>X of the part's centre in canvas pixels.</summary>
	[XmlAttribute]
	[Category("Position")]
	[Description("X of the part's centre in canvas pixels.")]
	[Range(0, int.MaxValue)]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y of the part's centre in canvas pixels.</summary>
	[XmlAttribute]
	[Category("Position")]
	[Description("Y of the part's centre in canvas pixels.")]
	[Range(0, int.MaxValue)]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>Rotation about the centre in degrees, counter-clockwise on the screen; 0 points the part's axis along +x.</summary>
	[XmlAttribute]
	[DefaultValue(0.0)]
	[Range(-180.0, 180.0, MinimumIsExclusive = true)]
	[Category("Position")]
	[DisplayName("Angle")]
	[Description("Rotation about the centre in degrees, counter-clockwise on the screen. 0 points the part's axis to the right.")]
	public double Angle
	{
		get => _angle;
		set => SetProperty(ref _angle, value);
	}

	/// <summary>The kind of part, for the tree row and the overlay; fixed per subclass.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract PartKind Kind { get; }

	/// <summary>Half-extent of the part along its axis in canvas pixels, so the rotation handle can sit beyond it. UI thread.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract double AxisReach { get; }

	/// <summary>
	/// The part's outline for drawing, as points in the part frame (relative to the centre, before rotation, canvas
	/// pixels): a closed polygon unless <see cref="OutlineClosed"/> says otherwise. Out-of-range values are guarded as
	/// the painter guards them, so the drawn outline is the painted one. UI thread.
	/// </summary>
	public abstract (double X, double Y)[] Outline();

	/// <summary>Whether <see cref="Outline"/> closes back to its first point (a fibre's path does not).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public virtual bool OutlineClosed => true;

	/// <summary>Maps a point of the part frame to canvas coordinates: the centre at (X, Y), the frame turned by the angle (counter-clockwise on the screen).</summary>
	public (double X, double Y) ToCanvas(double x, double y)
	{
		double radians = Angle * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		return (X + x * cos + y * sin, Y + y * cos - x * sin);
	}

	/// <summary>Maps canvas coordinates into the part frame (the inverse of <see cref="ToCanvas"/>).</summary>
	public (double X, double Y) ToFrame(double canvasX, double canvasY)
	{
		double radians = Angle * Math.PI / 180.0;
		double cos = Math.Cos(radians);
		double sin = Math.Sin(radians);
		double ox = canvasX - X;
		double oy = canvasY - Y;
		return (ox * cos - oy * sin, ox * sin + oy * cos);
	}

	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public IEnumerable<(int X, int Y)> Anchors => [(X, Y)];

	/// <inheritdoc/>
	public void MoveBy(int dx, int dy)
	{
		X += dx;
		Y += dy;
	}

	/// <summary>Clamps every value into its valid range; the angle is brought into (-180, 180].</summary>
	public virtual void Normalize()
	{
		Name ??= "";
		X = Math.Max(0, X);
		Y = Math.Max(0, Y);
		Angle = NormalizeAngle(Angle);
	}

	/// <summary>A deep copy with the same settings and no change subscribers.</summary>
	public abstract OpticalPart Clone();

	/// <summary>The name, for display in the project tree and property grids.</summary>
	public override string ToString() => Name;

	/// <summary>Brings an angle in degrees into (-180, 180]; a non-finite value becomes 0.</summary>
	protected static double NormalizeAngle(double degrees)
	{
		if (!double.IsFinite(degrees))
		{
			return 0.0;
		}
		if (degrees <= -180.0 || degrees > 180.0)
		{
			degrees -= 360.0 * Math.Ceiling((degrees - 180.0) / 360.0);
		}
		return degrees;
	}

	/// <summary>A refractive index clamped to <see cref="MinIndex"/>..<see cref="MaxIndex"/> and rounded to 0.01 (red = index × 100); a non-finite value becomes 1.</summary>
	protected static double NormalizeIndex(double index) =>
		double.IsFinite(index) ? Math.Round(Math.Clamp(index, MinIndex, MaxIndex), 2) : MinIndex;

	/// <summary>Deep-copies a point list.</summary>
	protected static ObservableCollection<PartPoint> ClonePoints(ObservableCollection<PartPoint> points) =>
		new(points.Select(p => new PartPoint { X = p.X, Y = p.Y }));
}

/// <summary>One vertex of an <see cref="OutlinePart"/> or of a <see cref="FiberPart"/> path, in canvas pixels relative to the part's centre, before rotation.</summary>
public sealed class PartPoint : ObservableObject
{
	private int _x;
	private int _y;

	/// <summary>X relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Point")]
	[Description("X relative to the part's centre in canvas pixels, before rotation.")]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Point")]
	[Description("Y relative to the part's centre in canvas pixels, before rotation.")]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>The coordinates, for display.</summary>
	public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// A lens, or a curved or flat mirror when <see cref="Mirror"/> is set: the strip of <see cref="Diameter"/> across
/// the axis between two spherical surfaces of radii <see cref="R1"/> (left, facing -x') and <see cref="R2"/>
/// (right), <see cref="Thickness"/> apart on the axis. A radius is signed: R1 &gt; 0 and R2 &lt; 0 bulge outward
/// (biconvex), 0 is flat. The diameter and the centre thickness are the lens's bulk; the radii fit them: a radius
/// too curved for them is raised to the smallest that fits (<see cref="FitRadii"/>), so the lens always fills its
/// diameter and the edge thickness is never negative (the user's point, 2026-10-18: a diameter of 2000 with the
/// default radii gave a tiny lens, since the surfaces crossed long before the rim).
/// </summary>
public sealed class LensPart : OpticalPart
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
	private int _coating;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionRamp;
	private int _diameter = DefaultDiameter;
	private double _r1 = DefaultR1;
	private double _r2 = DefaultR2;
	private int _thickness = DefaultThickness;

	/// <inheritdoc/>
	public override PartKind Kind => PartKind.Lens;

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

	/// <summary>Thickness in pixels of the anti-reflection band just inside the outline, with the index √n; 0 for none. A quarter-wave coating is λ / (4 √n) thick.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band just inside the outline, with the index √n; 0 for none. A quarter-wave coating is λ / (4 √n) pixels thick, λ being the wavelength the source form shows.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
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
			(double r1, double r2) = FitRadii(Diameter, Thickness, R1, R2);
			double half = Math.Max(2, Diameter) / 2.0;
			return Math.Max(1, Thickness) / 2.0 + Math.Max(Sag(r1, half), Sag(r2, half));
		}
	}

	/// <summary>Points per surface of the drawn outline.</summary>
	private const int OutlineSteps = 32;

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline()
	{
		(double r1, double r2) = FitRadii(Diameter, Thickness, R1, R2);
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
	private static double SurfaceSag(double radius, double y)
	{
		if (radius == 0.0)
		{
			return 0.0;
		}
		double inside = Math.Max(0.0, radius * radius - y * y);
		return radius - Math.CopySign(Math.Sqrt(inside), radius);
	}

	/// <summary>Thickness at the rim in pixels: the centre thickness less what the convex surfaces take, plus what the concave ones add; never negative after <see cref="FitRadii"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double EdgeThickness
	{
		get
		{
			(double r1, double r2) = FitRadii(Diameter, Thickness, R1, R2);
			double half = Math.Max(2, Diameter) / 2.0;
			return Math.Max(1, Thickness) - Math.CopySign(Sag(r1, half), r1) + Math.CopySign(Sag(r2, half), r2);
		}
	}

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
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2)
	{
		double half = Math.Max(2, diameter) / 2.0;
		r1 = GuardRadius(r1, half);
		r2 = GuardRadius(r2, half);
		double sag1 = Sag(r1, half);
		double sag2 = Sag(r2, half);
		double convex = (r1 > 0.0 ? sag1 : 0.0) + (r2 < 0.0 ? sag2 : 0.0);
		double budget = Math.Max(1, thickness) + (r1 < 0.0 ? sag1 : 0.0) + (r2 > 0.0 ? sag2 : 0.0);
		if (convex > budget)
		{
			double scale = budget / convex;
			if (r1 > 0.0)
			{
				r1 = RadiusForSag(half, sag1 * scale);
			}
			if (r2 < 0.0)
			{
				r2 = -RadiusForSag(half, sag2 * scale);
			}
		}
		return (r1, r2);
	}

	/// <summary>The radius whose sag at <paramref name="half"/> from the axis is <paramref name="sag"/>, (h² + s²) / 2s, rounded up to a whole pixel (a flatter surface still fits).</summary>
	private static double RadiusForSag(double half, double sag) => Math.Ceiling((half * half + sag * sag) / (2.0 * sag));

	/// <summary>A radius of at least <paramref name="half"/> in magnitude, its sign kept; a non-finite or zero one is flat.</summary>
	private static double GuardRadius(double radius, double half) =>
		!double.IsFinite(radius) || radius == 0.0 ? 0.0 : Math.Abs(radius) < half ? Math.CopySign(half, radius) : radius;

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		RefractiveIndex = NormalizeIndex(RefractiveIndex);
		Coating = Math.Clamp(Coating, 0, MaxCoating);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
		Diameter = Math.Max(2, Diameter);
		Thickness = Math.Max(1, Thickness);
		(double r1, double r2) = FitRadii(Diameter, Thickness, R1, R2);
		R1 = r1;
		R2 = r2;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<LensPart>();
}

/// <summary>
/// A part that is a plain shape of glass or mirror: the shared material (<see cref="Mirror"/>, <see cref="RefractiveIndex"/>,
/// <see cref="Coating"/>) over an outline each subclass generates (<see cref="Vertices"/>): a regular
/// <see cref="PolygonPart"/>, a <see cref="RectanglePart"/>, a <see cref="TrianglePart"/>, a free <see cref="OutlinePart"/>
/// (one class each, chosen when the part is added: the user's point, 2026-10-18, against a shape mode inside one class).
/// </summary>
public abstract class ShapePart : OpticalPart
{
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultIndex = 1.5;

	private bool _mirror;
	private double _refractiveIndex = DefaultIndex;
	private int _coating;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionRamp;

	/// <summary>Whether the part is a mirror (red 0, a wall no wave gets through) instead of glass; the index, the coating and the absorption are ignored then.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Material")]
	[Description("Make the part a mirror instead of glass. The refractive index, the coating and the absorption are ignored then.")]
	public bool Mirror
	{
		get => _mirror;
		set => SetProperty(ref _mirror, value);
	}

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

	/// <summary>Thickness in pixels of the anti-reflection band just inside the outline, with the index √n; 0 for none.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band just inside the outline, with the index √n; 0 for none. A quarter-wave coating is λ / (4 √n) pixels thick, λ being the wavelength the source form shows.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
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

	/// <summary>The outline's vertices relative to the centre, before rotation, in canvas pixels; fewer than three paint nothing. Out-of-range values are guarded, not written back.</summary>
	public abstract (double X, double Y)[] Vertices();

	/// <inheritdoc/>
	public override double AxisReach => Vertices().Select(v => Math.Abs(v.X)).DefaultIfEmpty(0.0).Max();

	/// <inheritdoc/>
	public override (double X, double Y)[] Outline() => Vertices();

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		RefractiveIndex = NormalizeIndex(RefractiveIndex);
		Coating = Math.Clamp(Coating, 0, MaxCoating);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
	}
}

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
	public override PartKind Kind => PartKind.Polygon;

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

/// <summary>A rectangle of <see cref="Length"/> along the axis and <see cref="Width"/> across, centred. A flat mirror is a long, narrow one.</summary>
public sealed class RectanglePart : ShapePart
{
	/// <summary>Default <see cref="Length"/> and <see cref="Width"/> in pixels.</summary>
	public const int DefaultSize = 60;

	private int _length = DefaultSize;
	private int _width = DefaultSize;
	private bool _linked;

	/// <inheritdoc/>
	public override PartKind Kind => PartKind.Rectangle;

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
	public override PartKind Kind => PartKind.Ellipse;

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
	public override PartKind Kind => PartKind.Triangle;

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

/// <summary>A free outline from <see cref="Points"/>, relative to the centre before rotation; fewer than three points paint nothing.</summary>
public sealed class OutlinePart : ShapePart
{
	private ObservableCollection<PartPoint> _points = [];

	/// <inheritdoc/>
	public override PartKind Kind => PartKind.Outline;

	/// <summary>The outline's vertices relative to the centre, before rotation; fewer than three points paint nothing.</summary>
	[XmlElement("Point")]
	[Category("Shape")]
	[DisplayName("Points")]
	[Description("The outline's vertices relative to the centre, before rotation. Fewer than three points paint nothing.")]
	public ObservableCollection<PartPoint> Points
	{
		get => _points;
		set => SetProperty(ref _points, value);
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices() => Points.Select(p => ((double)p.X, (double)p.Y)).ToArray();

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Points ??= [];
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<OutlinePart>();
		copy.Points = ClonePoints(Points);
		return copy;
	}
}

/// <summary>
/// A fibre: a core of <see cref="CoreDiameter"/> along the polyline <see cref="Points"/>, wrapped in a cladding of
/// <see cref="CladdingThickness"/>. The ends are round (the distance to the path decides). The angle rotates the
/// whole path about the centre.
/// </summary>
public sealed class FiberPart : OpticalPart
{
	/// <summary>Default <see cref="CoreIndex"/>.</summary>
	public const double DefaultCoreIndex = 1.5;
	/// <summary>Default <see cref="CoreDiameter"/> in pixels.</summary>
	public const int DefaultCoreDiameter = 20;
	/// <summary>Default <see cref="CladdingIndex"/>.</summary>
	public const double DefaultCladdingIndex = 1.45;
	/// <summary>Default <see cref="CladdingThickness"/> in pixels.</summary>
	public const int DefaultCladdingThickness = 10;
	/// <summary>Half the default path length in pixels: the path runs from (-100, 0) to (100, 0).</summary>
	public const int DefaultHalfLength = 100;

	private double _coreIndex = DefaultCoreIndex;
	private int _coreDiameter = DefaultCoreDiameter;
	private double _claddingIndex = DefaultCladdingIndex;
	private int _claddingThickness = DefaultCladdingThickness;
	private int _coating;
	private ObservableCollection<PartPoint> _points = [];

	/// <inheritdoc/>
	public override PartKind Kind => PartKind.Fiber;

	/// <summary>Refractive index of the core, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Core")]
	[DisplayName("Core index")]
	[Description("Refractive index of the core, 1.00 to 2.55 in steps of 0.01.")]
	public double CoreIndex
	{
		get => _coreIndex;
		set => SetProperty(ref _coreIndex, value);
	}

	/// <summary>Diameter of the core in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Core")]
	[DisplayName("Core diameter")]
	[Description("Diameter of the core in pixels.")]
	public int CoreDiameter
	{
		get => _coreDiameter;
		set => SetProperty(ref _coreDiameter, value);
	}

	/// <summary>Refractive index of the cladding, 1.00 to 2.55; below the core's for guiding.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Cladding")]
	[DisplayName("Cladding index")]
	[Description("Refractive index of the cladding, 1.00 to 2.55 in steps of 0.01. Below the core's index, the fibre guides.")]
	public double CladdingIndex
	{
		get => _claddingIndex;
		set => SetProperty(ref _claddingIndex, value);
	}

	/// <summary>Thickness of the cladding around the core in pixels; 0 for a bare core.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingThickness)]
	[Range(0, int.MaxValue)]
	[Category("Cladding")]
	[DisplayName("Cladding thickness")]
	[Description("Thickness of the cladding around the core in pixels; 0 for a bare core.")]
	public int CladdingThickness
	{
		get => _claddingThickness;
		set => SetProperty(ref _claddingThickness, value);
	}

	/// <summary>Thickness in pixels of the anti-reflection band just inside the outer boundary, with the index √n of the cladding (or the core when bare); 0 for none.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxCoating)]
	[Category("Cladding")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band just inside the outer boundary, with the index √n; 0 for none.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>The path's vertices relative to the centre, before rotation; fewer than two points paint nothing.</summary>
	[XmlElement("Point")]
	[Category("Shape")]
	[DisplayName("Points")]
	[Description("The path's vertices relative to the centre, before rotation. Fewer than two points paint nothing.")]
	public ObservableCollection<PartPoint> Points
	{
		get => _points;
		set => SetProperty(ref _points, value);
	}

	/// <summary>Outer radius in pixels: half the core diameter plus the cladding.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterRadius => CoreDiameter / 2.0 + CladdingThickness;

	/// <inheritdoc/>
	public override double AxisReach => (Points.Count > 0 ? Points.Max(p => Math.Abs(p.X)) : 0) + OuterRadius;

	/// <summary>The path itself; the core and cladding bands show through the scene outline.</summary>
	public override (double X, double Y)[] Outline() => Points.Select(p => ((double)p.X, (double)p.Y)).ToArray();

	/// <inheritdoc/>
	public override bool OutlineClosed => false;

	/// <summary>
	/// The default straight path, from (-100, 0) to (100, 0). Given to a new fibre by <see cref="Project.AddPart"/>, not by
	/// the constructor: <see cref="XmlSerializer"/> appends a file's points to the list it finds, so the constructor's list
	/// must be empty.
	/// </summary>
	public static ObservableCollection<PartPoint> DefaultPath() =>
		[new PartPoint { X = -DefaultHalfLength, Y = 0 }, new PartPoint { X = DefaultHalfLength, Y = 0 }];

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		CoreIndex = NormalizeIndex(CoreIndex);
		CladdingIndex = NormalizeIndex(CladdingIndex);
		CoreDiameter = Math.Max(2, CoreDiameter);
		CladdingThickness = Math.Max(0, CladdingThickness);
		Coating = Math.Clamp(Coating, 0, MaxCoating);
		Points ??= [];
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<FiberPart>();
		copy.Points = ClonePoints(Points);
		return copy;
	}
}

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
	public override PartKind Kind => PartKind.BraggReflector;

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

/// <summary>
/// A gradient-index lens: a rectangle of <see cref="Length"/> along the axis and <see cref="Diameter"/> across whose
/// index falls from <see cref="CentreIndex"/> on the axis to <see cref="EdgeIndex"/> at the edge as
/// n(y') = n_c − (n_c − n_e)·(2y'/D)².
/// </summary>
public sealed class GrinLensPart : OpticalPart
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
	private int _coating;

	/// <inheritdoc/>
	public override PartKind Kind => PartKind.GrinLens;

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

	/// <summary>Thickness in pixels of the anti-reflection band just inside the outline, with the index √n of the local index; 0 for none.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band just inside the outline, with the index √n; 0 for none.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
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
		Coating = Math.Clamp(Coating, 0, MaxCoating);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<GrinLensPart>();
}
