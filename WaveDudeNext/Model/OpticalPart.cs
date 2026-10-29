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
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using WaveDudeNext.Parts;

namespace WaveDudeNext.Model;

/// <summary>
/// An optical part placed on the canvas: a lens or mirror, a shape (polygon, rectangle, ellipse, triangle, outline), a
/// fibre, a Bragg reflector, a GRIN lens, a logic gate or a zone plate (Jeroen Vleggaar's proposal, decided 2026-10-18;
/// OpticalParts.md is the reference). Parts are painted into the scene pixels before a run
/// (<see cref="Parts.PartPainter"/>), so the engines, the outline and the export see them as part of the scene: the
/// index into red, an absorber's damping into blue; green is never touched. Positions and lengths are canvas pixels; the
/// angle is in degrees, counter-clockwise on the screen, 0 pointing the part's axis along +x (the composite source's
/// convention). Refractive indices run from 1.00 to 2.55 in steps of 0.01 (red = index × 100). The class is the kind,
/// chosen when the part is added (<see cref="Project.AddPart(Type)"/>) and never changed.
/// </summary>
public abstract class OpticalPart : ObservableObject, IMovable, INamed
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

	/// <summary>The English stem of a new part's name ("Lens", "Bragg reflector", …), numbered by <see cref="Project.AddPart(Type)"/>; fixed per subclass.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract string NameStem { get; }

	/// <summary>
	/// Gives a part made by <see cref="Project.AddPart(Type)"/> what a new part starts with and a loaded one must not get
	/// (a fibre's default path: <see cref="XmlSerializer"/> appends a file's items to the list it finds). Does nothing by default.
	/// </summary>
	public virtual void PrepareNew()
	{
	}

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

/// <summary>One vertex of an <see cref="OutlinePart"/>, in canvas pixels relative to the part's centre, before rotation.</summary>
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
/// How the index runs through an anti-reflection band (2026-10-29, the user's request: the author's drawn scenes give
/// a glass face a graded ramp, and the parts had only the layer).
/// </summary>
public enum CoatingProfile
{
	/// <summary>A band at the index √n throughout: a quarter-wave layer when it is λ / (4 √n) thick, the least reflecting then.</summary>
	Layer,
	/// <summary>
	/// The index rises over the band from the medium outside to the glass as a raised cosine, (1 − cos πt) / 2 of the
	/// depth over the thickness (<see cref="Parts.PartShape.GradedProfile"/>): broadband, and the thicker the better; the
	/// figures are under "Coatings" in OpticalParts.md.
	/// </summary>
	Graded,
}

/// <summary>
/// A refractive part with an anti-reflection band along its outline (a lens, a shape, a GRIN lens; 2026-10-29, the
/// user's request): the band's <see cref="Coating"/> thickness, its <see cref="CoatingProfile"/>, and which faces carry
/// it. A face is classed by its outward normal in the part frame: within 45° of −x', the side a source on the left
/// meets, it is the front, within 45° of +x' the back, otherwise a side; so a lens's first surface, second surface and
/// rim, and a rectangle's four faces when its length runs along the beam. Every face is coated by default.
/// </summary>
public abstract class CoatedPart : OpticalPart
{
	/// <summary>Default <see cref="CoatingProfile"/>.</summary>
	public const CoatingProfile DefaultCoatingProfile = CoatingProfile.Layer;

	private int _coating;
	private CoatingProfile _coatingProfile = DefaultCoatingProfile;
	private bool _frontCoated = true;
	private bool _backCoated = true;
	private bool _sidesCoated = true;

	/// <summary>Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) thick.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>How the index runs through the band: a layer at √n, or a graded rise from the medium outside to the glass.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoatingProfile)]
	[Category("Material")]
	[DisplayName("Coating profile")]
	[Description("How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.")]
	public CoatingProfile CoatingProfile
	{
		get => _coatingProfile;
		set => SetProperty(ref _coatingProfile, value);
	}

	/// <summary>Whether the front faces carry the band: those whose outward normal lies within 45° of −x', the side a source on the left meets.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the front")]
	[Description("Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.")]
	public bool FrontCoated
	{
		get => _frontCoated;
		set => SetProperty(ref _frontCoated, value);
	}

	/// <summary>Whether the back faces carry the band: those whose outward normal lies within 45° of +x'.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the back")]
	[Description("Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.")]
	public bool BackCoated
	{
		get => _backCoated;
		set => SetProperty(ref _backCoated, value);
	}

	/// <summary>Whether the other faces carry the band: a lens's rim, a slab's narrow ends.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the sides")]
	[Description("Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.")]
	public bool SidesCoated
	{
		get => _sidesCoated;
		set => SetProperty(ref _sidesCoated, value);
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Coating = Math.Clamp(Coating, 0, MaxCoating);
		if (!Enum.IsDefined(CoatingProfile))
		{
			CoatingProfile = DefaultCoatingProfile;
		}
	}
}

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
/// too curved for them is raised to the smallest that fits (<see cref="FitRadii"/>), so the lens always fills its
/// diameter and the edge thickness is never negative (the user's point, 2026-10-18: a diameter of 2000 with the
/// default radii gave a tiny lens, since the surfaces crossed long before the rim).
/// </summary>
public sealed class LensPart : CoatedPart
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
	public static (double R1, double R2) FitRadii(int diameter, int thickness, double r1, double r2)
	{
		double half = Math.Max(2, diameter) / 2.0;
		r1 = GuardRadius(r1, half);
		r2 = GuardRadius(r2, half);
		double sag1 = Sag(r1, half);
		double sag2 = Sag(r2, half);
		double convex = (r1 > 0.0 ? sag1 : 0.0) + (r2 < 0.0 ? sag2 : 0.0);
		double budget = Math.Max(1, thickness) + (r1 < 0.0 ? sag1 : 0.0) + (r2 > 0.0 ? sag2 : 0.0);
		// a hair over the budget (the floating point of a stretched lens's sags) is no case for a refit
		if (convex > budget + 1e-9)
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
		(double r1, double r2) = FitRadii(Diameter, Thickness, R1, R2);
		double half = Math.Max(2, Diameter) / 2.0;
		double grown = Math.Max(2, diameter) / 2.0;
		double along = Math.Max(1, thickness) / (double)Math.Max(1, Thickness);
		if (grown == half && along == 1.0)
		{
			return;
		}
		// the sags signed as the radii are: surface 1 convex is positive, surface 2 convex is negative
		double sag1 = Math.Clamp(SurfaceSag(r1, half) * along, -grown, grown);
		double sag2 = Math.Clamp(SurfaceSag(r2, half) * along, -grown, grown);
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
	/// the other way, so the rounding never brings on the refit of <see cref="FitRadii"/> for a lens whose surfaces meet at the rim.
	/// </summary>
	private static double RadiusOfSag(int surface, double half, double sag)
	{
		if (sag == 0.0)
		{
			return 0.0;
		}
		double hundredths = Math.CopySign((half * half + sag * sag) / (2.0 * Math.Abs(sag)), sag) * 100.0;
		return (surface == 1 ? Math.Ceiling(hundredths) : Math.Floor(hundredths)) / 100.0;
	}

	/// <summary>Where the vertex of surface <paramref name="surface"/> (1 the left, 2 the right) sits on the axis in the part frame: ∓<see cref="Thickness"/> / 2.</summary>
	public double VertexX(int surface) => (surface == 1 ? -1 : 1) * Math.Max(1, Thickness) / 2.0;

	/// <summary>Where surface <paramref name="surface"/> meets the rim, on the axis in the part frame: its vertex plus its sag at the half diameter, the radii as fitted.</summary>
	public double RimX(int surface)
	{
		(double r1, double r2) = FitRadii(Diameter, Thickness, R1, R2);
		return VertexX(surface) + SurfaceSag(surface == 1 ? r1 : r2, Math.Max(2, Diameter) / 2.0);
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
		(double r1, double r2) = FitRadii(Diameter, Thickness, R1, R2);
		R1 = r1;
		R2 = r2;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<LensPart>();
}

/// <summary>
/// A part that is a plain shape of glass or mirror: the shared material (<see cref="Mirror"/>, <see cref="RefractiveIndex"/>,
/// the coating of <see cref="CoatedPart"/>, <see cref="Absorption"/> over <see cref="AbsorptionRamp"/>) over an outline each subclass
/// generates (<see cref="Vertices"/>): a regular <see cref="PolygonPart"/>, a <see cref="RectanglePart"/>, an
/// <see cref="EllipsePart"/>, a <see cref="TrianglePart"/>, a free <see cref="OutlinePart"/> (one class each, chosen when
/// the part is added: the user's point, 2026-10-18, against a shape mode inside one class).
/// </summary>
public abstract class ShapePart : CoatedPart
{
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultIndex = 1.5;

	private bool _mirror;
	private double _refractiveIndex = DefaultIndex;
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

/// <summary>A free outline from <see cref="Points"/>, relative to the centre before rotation; fewer than three points paint nothing.</summary>
public sealed class OutlinePart : ShapePart
{
	private ObservableCollection<PartPoint> _points = [];

	/// <inheritdoc/>
	public override string NameStem => "Outline";

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

/// <summary>How the two ends of a <see cref="FiberPart"/> are cut (2026-10-22, the user's request; the ends were round from the distance test before).</summary>
public enum FiberEnds
{
	/// <summary>Cut straight across the path's direction at the endpoint, like a cleaved fibre; the coating band follows the end face.</summary>
	Flat,
	/// <summary>Rounded with the fibre's outer radius.</summary>
	Round,
}

/// <summary>
/// One node of a <see cref="FiberPart"/> path (2026-10-22, the user's request: a Bézier path with handles instead of a
/// polyline): the point the path passes through, in canvas pixels relative to the part's centre before rotation, and
/// the two handles of the cubic segments meeting there, as offsets from the node. The in-handle shapes the segment
/// arriving from the previous node, the out-handle the one leaving to the next; a handle of zero length makes its
/// segment straight at this end, two zero handles a sharp corner. While <see cref="Smooth"/> is set, the handles are
/// kept opposite each other through the node: switching it on lines them up at once (<see cref="FiberPart.AlignNode"/>),
/// and afterwards a handle dragged (<see cref="SetHandle"/>) or typed (the part listens to its nodes) turns the other
/// with it, keeping its length. A node added to a part, or loaded from a file, is taken as it is.
/// </summary>
public sealed class FiberNode : ObservableObject
{
	private int _x;
	private int _y;
	private int _inX;
	private int _inY;
	private int _outX;
	private int _outY;
	private bool _smooth = true;

	/// <summary>X relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Node")]
	[Description("X relative to the part's centre in canvas pixels, before rotation.")]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Node")]
	[Description("Y relative to the part's centre in canvas pixels, before rotation.")]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>X of the in-handle relative to the node in canvas pixels, before rotation; it shapes the segment arriving from the previous node.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("In handle")]
	[DisplayName("In X")]
	[Description("X of the in-handle relative to the node, in canvas pixels before rotation. It shapes the segment arriving from the previous node; zero makes that segment straight at this end.")]
	public int InX
	{
		get => _inX;
		set => SetProperty(ref _inX, value);
	}

	/// <summary>Y of the in-handle relative to the node in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("In handle")]
	[DisplayName("In Y")]
	[Description("Y of the in-handle relative to the node, in canvas pixels before rotation.")]
	public int InY
	{
		get => _inY;
		set => SetProperty(ref _inY, value);
	}

	/// <summary>X of the out-handle relative to the node in canvas pixels, before rotation; it shapes the segment leaving to the next node.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Out handle")]
	[DisplayName("Out X")]
	[Description("X of the out-handle relative to the node, in canvas pixels before rotation. It shapes the segment leaving to the next node; zero makes that segment straight at this end.")]
	public int OutX
	{
		get => _outX;
		set => SetProperty(ref _outX, value);
	}

	/// <summary>Y of the out-handle relative to the node in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Out handle")]
	[DisplayName("Out Y")]
	[Description("Y of the out-handle relative to the node, in canvas pixels before rotation.")]
	public int OutY
	{
		get => _outY;
		set => SetProperty(ref _outY, value);
	}

	/// <summary>Whether the editors keep the two handles opposite each other through the node, so the path runs smoothly through it; off, the node may be a corner.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Node")]
	[Description("Keep the two handles opposite each other through the node, so the path runs smoothly through it. Switching it on lines them up at once; afterwards moving or typing one handle turns the other with it, keeping its length. Off, the node may be a corner.")]
	public bool Smooth
	{
		get => _smooth;
		set => SetProperty(ref _smooth, value);
	}

	/// <summary>Set while <see cref="SetHandle"/> or <see cref="FiberPart.AlignNode"/> writes the handles, so the part's listener leaves the mirrored handle alone.</summary>
	internal bool Mirroring { get; set; }

	/// <summary>The in-handle as a point of the part frame: the node plus its offset.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public (double X, double Y) In => (X + InX, Y + InY);

	/// <summary>The out-handle as a point of the part frame: the node plus its offset.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public (double X, double Y) Out => (X + OutX, Y + OutY);

	/// <summary>
	/// Sets the out-handle (or the in-handle) to the offset (<paramref name="dx"/>, <paramref name="dy"/>); with
	/// <see cref="Smooth"/> the other handle is turned to the opposite direction, keeping <paramref name="otherLength"/>
	/// (its own length when null; an editor passes the length from before a drag, so the rounding of each step does
	/// not creep into it). A zero handle leaves the other alone.
	/// </summary>
	/// <param name="outward">True for the out-handle, false for the in-handle.</param>
	/// <param name="dx">The handle's X offset from the node in canvas pixels.</param>
	/// <param name="dy">The handle's Y offset from the node in canvas pixels.</param>
	/// <param name="otherLength">The length the other handle keeps, or null for its current length.</param>
	public void SetHandle(bool outward, int dx, int dy, double? otherLength = null)
	{
		Mirroring = true;
		try
		{
			if (outward)
			{
				OutX = dx;
				OutY = dy;
			}
			else
			{
				InX = dx;
				InY = dy;
			}
			double length = Math.Sqrt((double)dx * dx + (double)dy * dy);
			if (!Smooth || length == 0.0)
			{
				return;
			}
			(int otherX, int otherY) = outward ? (InX, InY) : (OutX, OutY);
			double keep = otherLength ?? Math.Sqrt((double)otherX * otherX + (double)otherY * otherY);
			int mirrorX = (int)Math.Round(-dx / length * keep);
			int mirrorY = (int)Math.Round(-dy / length * keep);
			if (outward)
			{
				InX = mirrorX;
				InY = mirrorY;
			}
			else
			{
				OutX = mirrorX;
				OutY = mirrorY;
			}
		}
		finally
		{
			Mirroring = false;
		}
	}

	/// <summary>The coordinates, for display.</summary>
	public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// A fibre: a core of <see cref="CoreDiameter"/> along a path of cubic Bézier segments through <see cref="Nodes"/>,
/// wrapped in a cladding of <see cref="CladdingThickness"/>; the ends cut <see cref="Ends"/>. The path is flattened
/// into chords (<see cref="Flatten"/>) for painting and drawing. The angle rotates the whole path about the centre.
/// </summary>
public sealed class FiberPart : CoatedPart
{
	/// <summary>Default <see cref="CoreIndex"/>.</summary>
	public const double DefaultCoreIndex = 1.5;
	/// <summary>Default <see cref="CoreDiameter"/> in pixels.</summary>
	public const int DefaultCoreDiameter = 20;
	/// <summary>Default <see cref="CladdingIndex"/>.</summary>
	public const double DefaultCladdingIndex = 1.45;
	/// <summary>Default <see cref="CladdingThickness"/> in pixels.</summary>
	public const int DefaultCladdingThickness = 10;
	/// <summary>Default <see cref="Ends"/>.</summary>
	public const FiberEnds DefaultEnds = FiberEnds.Flat;
	/// <summary>Default <see cref="AbsorptionRamp"/>, half the absorbing band's width in pixels.</summary>
	public const int DefaultAbsorptionBand = 10;
	/// <summary>Half the default path length in pixels: the path runs from (-100, 0) to (100, 0).</summary>
	public const int DefaultHalfLength = 100;
	/// <summary>How far a flattened chord may leave the curve, in canvas pixels: far below the painter's quarter-pixel samples.</summary>
	public const double FlattenTolerance = 0.05;

	private double _coreIndex = DefaultCoreIndex;
	private int _coreDiameter = DefaultCoreDiameter;
	private double _claddingIndex = DefaultCladdingIndex;
	private int _claddingThickness = DefaultCladdingThickness;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionBand;
	private FiberEnds _ends = DefaultEnds;
	private ObservableCollection<FiberNode> _nodes = [];
	private HashSet<FiberNode> _watched = [];

	/// <summary>A fibre with an empty path: a file's nodes or <see cref="DefaultPath"/> fill it.</summary>
	public FiberPart()
	{
		Watch(_nodes);
	}

	/// <inheritdoc/>
	public override string NameStem => "Fiber";

	/// <summary>A new fibre gets the <see cref="DefaultPath"/>.</summary>
	public override void PrepareNew() => Nodes = DefaultPath();

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

	/// <summary>
	/// Peak absorption of the cladding's absorbing band, 0 (none) to 255, painted into the blue channel (2026-10-29, the
	/// user's request, after the author's fibre scene, whose fibres run between absorbing strips): a hump in the outer
	/// part of the cladding, rising over <see cref="AbsorptionRamp"/> pixels from nothing, peaking, and falling back to
	/// nothing at the outer edge, so the guided mode's tail in the inner cladding is untouched and stray light is
	/// absorbed from either side without a damping step; a mode stripper. Nothing on a bare core.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxAbsorption)]
	[Category("Cladding")]
	[DisplayName("Cladding absorption")]
	[Description("Peak absorption of the cladding's absorbing band, 0 (not at all) to 255, as the scene image's blue channel: a hump in the outer part of the cladding, rising over the band width from nothing, peaking, and falling to nothing at the outer edge, so the guided wave's tail stays untouched and stray light is absorbed from either side. Nothing on a bare core.")]
	public int Absorption
	{
		get => _absorption;
		set => SetProperty(ref _absorption, value);
	}

	/// <summary>Half the absorbing band's width in pixels: the absorption rises over this many pixels and falls over as many; a band wider than the cladding is squeezed into it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAbsorptionBand)]
	[Range(1, MaxAbsorptionRamp)]
	[Category("Cladding")]
	[DisplayName("Absorption band")]
	[Description("Half the absorbing band's width in pixels: the absorption rises over this many pixels to its peak and falls over as many to the outer edge. A band wider than the cladding is squeezed into it.")]
	public int AbsorptionRamp
	{
		get => _absorptionRamp;
		set => SetProperty(ref _absorptionRamp, value);
	}

	/// <summary>How the two ends are cut: straight across the path, or rounded.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultEnds)]
	[Category("Shape")]
	[DisplayName("Ends")]
	[Description("How the two ends are cut: flat, straight across the path like a cleaved fibre, or rounded with the outer radius. The AR coating follows a flat end face.")]
	public FiberEnds Ends
	{
		get => _ends;
		set => SetProperty(ref _ends, value);
	}

	/// <summary>The path's nodes relative to the centre, before rotation, each with the handles of the segments meeting there; fewer than two nodes paint nothing.</summary>
	[XmlElement("Node")]
	[Category("Shape")]
	[DisplayName("Nodes")]
	[Description("The path's nodes relative to the centre, before rotation, each with the handles of the cubic segments meeting there. Fewer than two nodes paint nothing.")]
	public ObservableCollection<FiberNode> Nodes
	{
		get => _nodes;
		set
		{
			if (ReferenceEquals(_nodes, value))
			{
				return;
			}
			Unwatch(_nodes);
			SetProperty(ref _nodes, value);
			Watch(_nodes);
		}
	}

	/// <summary>Outer radius in pixels: half the core diameter plus the cladding.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterRadius => CoreDiameter / 2.0 + CladdingThickness;

	/// <summary>The farthest any node or handle lies along x' (the curve stays within its handles' hull), plus the outer radius.</summary>
	public override double AxisReach => (Nodes.Count > 0 ? Nodes.Max(n => Math.Max(Math.Abs(n.X), Math.Max(Math.Abs(n.In.X), Math.Abs(n.Out.X)))) : 0) + OuterRadius;

	/// <summary>The path flattened into chords; the core and cladding bands show through the scene outline.</summary>
	public override (double X, double Y)[] Outline() => Flatten(Nodes);

	/// <inheritdoc/>
	public override bool OutlineClosed => false;

	/// <summary>
	/// The default straight path, from (-100, 0) to (100, 0), the handles a third of the way along it so they can be
	/// taken hold of. Given to a new fibre by <see cref="PrepareNew"/>, not by the constructor:
	/// <see cref="XmlSerializer"/> appends a file's nodes to the list it finds, so the constructor's list must be empty.
	/// </summary>
	public static ObservableCollection<FiberNode> DefaultPath()
	{
		int third = (int)Math.Round(2.0 * DefaultHalfLength / 3.0);
		return
		[
			new FiberNode { X = -DefaultHalfLength, Y = 0, OutX = third },
			new FiberNode { X = DefaultHalfLength, Y = 0, InX = -third },
		];
	}

	/// <summary>
	/// The path through <paramref name="nodes"/> as chords, in the part frame: the cubic segment between two nodes
	/// (the node, its out-handle, the next node's in-handle, the next node) is cut into as many equal steps of t as
	/// keep every chord within <see cref="FlattenTolerance"/> of the curve, a straight segment staying one chord;
	/// consecutive equal points are dropped, so no chord has zero length. Fewer than two nodes give an empty path.
	/// Any thread; reads the nodes once.
	/// </summary>
	public static (double X, double Y)[] Flatten(IList<FiberNode> nodes)
	{
		if (nodes.Count < 2)
		{
			return [];
		}
		var path = new List<(double X, double Y)>(nodes.Count * 8) { (nodes[0].X, nodes[0].Y) };
		for (int i = 1; i < nodes.Count; i++)
		{
			FiberNode a = nodes[i - 1];
			FiberNode b = nodes[i];
			double x0 = a.X, y0 = a.Y, x3 = b.X, y3 = b.Y;
			(double x1, double y1) = a.Out;
			(double x2, double y2) = b.In;
			// a segment whose handles lie on the chord between its nodes is straight and stays one chord; otherwise the
			// second derivative of a cubic is bounded by 6 × the larger second difference of its control points, and n
			// equal steps leave a chord error of at most that bound / (8 n²)
			int steps = 1;
			if (DistanceToSegment(x1, y1, x0, y0, x3, y3) > FlattenTolerance || DistanceToSegment(x2, y2, x0, y0, x3, y3) > FlattenTolerance)
			{
				double second = Math.Max(Math.Sqrt(Square(x0 - 2 * x1 + x2) + Square(y0 - 2 * y1 + y2)), Math.Sqrt(Square(x1 - 2 * x2 + x3) + Square(y1 - 2 * y2 + y3)));
				steps = Math.Clamp((int)Math.Ceiling(Math.Sqrt(0.75 * second / FlattenTolerance)), 1, 512);
			}
			for (int k = 1; k <= steps; k++)
			{
				double t = (double)k / steps;
				double u = 1.0 - t;
				double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
				(double X, double Y) point = (w0 * x0 + w1 * x1 + w2 * x2 + w3 * x3, w0 * y0 + w1 * y1 + w2 * y2 + w3 * y3);
				if (point != path[^1])
				{
					path.Add(point);
				}
			}
		}
		return path.Count < 2 ? [] : [.. path];
	}

	private static double Square(double v) => v * v;

	/// <summary>
	/// Lines up the handles of <paramref name="node"/> (2026-10-22, the user's choice for what Smooth means): the in-handle
	/// opposite the out-handle, both keeping their lengths; an in-handle of zero length (with a node before) or an
	/// out-handle of zero (with a node after) takes the other's length; a node with no handles at all gets them from its
	/// neighbours, along the line between them, a third of the way to each. A node not in the list, or alone, is left as it is.
	/// </summary>
	public void AlignNode(FiberNode node)
	{
		int i = Nodes.IndexOf(node);
		bool hasPrevious = i > 0;
		bool hasNext = i >= 0 && i < Nodes.Count - 1;
		double outLength = Length(node.OutX, node.OutY);
		double inLength = Length(node.InX, node.InY);
		node.Mirroring = true;
		try
		{
			if (outLength == 0.0 && inLength == 0.0)
			{
				if (!hasPrevious && !hasNext)
				{
					return;
				}
				(double fromX, double fromY) = hasPrevious ? (Nodes[i - 1].X, Nodes[i - 1].Y) : (node.X, node.Y);
				(double toX, double toY) = hasNext ? (Nodes[i + 1].X, Nodes[i + 1].Y) : (node.X, node.Y);
				double span = Length(toX - fromX, toY - fromY);
				if (span == 0.0)
				{
					return;
				}
				double tx = (toX - fromX) / span;
				double ty = (toY - fromY) / span;
				if (hasPrevious)
				{
					double third = Length(node.X - fromX, node.Y - fromY) / 3.0;
					node.InX = (int)Math.Round(-tx * third);
					node.InY = (int)Math.Round(-ty * third);
				}
				if (hasNext)
				{
					double third = Length(toX - node.X, toY - node.Y) / 3.0;
					node.OutX = (int)Math.Round(tx * third);
					node.OutY = (int)Math.Round(ty * third);
				}
			}
			else if (outLength > 0.0)
			{
				if (inLength == 0.0 && hasPrevious)
				{
					inLength = outLength;
				}
				node.InX = (int)Math.Round(-node.OutX / outLength * inLength);
				node.InY = (int)Math.Round(-node.OutY / outLength * inLength);
			}
			else
			{
				if (hasNext)
				{
					outLength = inLength;
				}
				node.OutX = (int)Math.Round(-node.InX / inLength * outLength);
				node.OutY = (int)Math.Round(-node.InY / inLength * outLength);
			}
		}
		finally
		{
			node.Mirroring = false;
		}
	}

	private static double Length(double x, double y) => Math.Sqrt(x * x + y * y);

	/// <summary>Listens to the list and its nodes for the smooth rule (<see cref="OnNodeChanged"/>).</summary>
	private void Watch(ObservableCollection<FiberNode>? nodes)
	{
		if (nodes == null)
		{
			return;
		}
		nodes.CollectionChanged += OnNodesChanged;
		foreach (FiberNode node in nodes)
		{
			WatchNode(node);
		}
	}

	private void Unwatch(ObservableCollection<FiberNode>? nodes)
	{
		if (nodes != null)
		{
			nodes.CollectionChanged -= OnNodesChanged;
		}
		foreach (FiberNode node in _watched)
		{
			node.PropertyChanged -= OnNodeChanged;
		}
		_watched.Clear();
	}

	private void WatchNode(FiberNode node)
	{
		if (_watched.Add(node))
		{
			node.PropertyChanged += OnNodeChanged;
		}
	}

	/// <summary>A node added is listened to from then on (as it is: no alignment on arrival, which keeps a file's nodes as written); a node removed is let go.</summary>
	private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			foreach (FiberNode node in _watched)
			{
				node.PropertyChanged -= OnNodeChanged;
			}
			_watched.Clear();
			foreach (FiberNode node in _nodes)
			{
				WatchNode(node);
			}
			return;
		}
		if (e.OldItems != null)
		{
			foreach (FiberNode node in e.OldItems)
			{
				if (_watched.Remove(node))
				{
					node.PropertyChanged -= OnNodeChanged;
				}
			}
		}
		if (e.NewItems != null)
		{
			foreach (FiberNode node in e.NewItems)
			{
				WatchNode(node);
			}
		}
	}

	/// <summary>
	/// The smooth rule: a node switched to smooth gets its handles lined up at once (<see cref="AlignNode"/>), and a handle
	/// of a smooth node set by itself (a typed value) turns the other handle opposite, keeping its length, as a drag does
	/// through <see cref="FiberNode.SetHandle"/>, which flags the node while it writes so its own mirror is left alone.
	/// </summary>
	private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not FiberNode node || node.Mirroring || !node.Smooth)
		{
			return;
		}
		switch (e.PropertyName)
		{
			case nameof(FiberNode.Smooth):
				AlignNode(node);
				break;
			case nameof(FiberNode.InX) or nameof(FiberNode.InY):
				node.SetHandle(false, node.InX, node.InY);
				break;
			case nameof(FiberNode.OutX) or nameof(FiberNode.OutY):
				node.SetHandle(true, node.OutX, node.OutY);
				break;
		}
	}

	/// <summary>Distance from (x, y) to the segment a–b.</summary>
	private static double DistanceToSegment(double x, double y, double ax, double ay, double bx, double by)
	{
		double dx = bx - ax;
		double dy = by - ay;
		double length2 = dx * dx + dy * dy;
		double t = length2 > 0.0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length2, 0.0, 1.0) : 0.0;
		return Math.Sqrt(Square(ax + t * dx - x) + Square(ay + t * dy - y));
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		CoreIndex = NormalizeIndex(CoreIndex);
		CladdingIndex = NormalizeIndex(CladdingIndex);
		CoreDiameter = Math.Max(2, CoreDiameter);
		CladdingThickness = Math.Max(0, CladdingThickness);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
		if (!Enum.IsDefined(Ends))
		{
			Ends = DefaultEnds;
		}
		Nodes ??= [];
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<FiberPart>();
		// the memberwise copy shares the original's set of watched nodes and listens to nothing yet: a fresh set, then
		// the new list through the setter, which subscribes
		copy._watched = [];
		copy.Nodes = new ObservableCollection<FiberNode>(Nodes.Select(n => new FiberNode { X = n.X, Y = n.Y, InX = n.InX, InY = n.InY, OutX = n.OutX, OutY = n.OutY, Smooth = n.Smooth }));
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

/// <summary>What one output of a <see cref="LogicGatePart"/> gives, in powers relative to one input's for the inputs 00, 10, 01, 11.</summary>
public enum GateOutput
{
	/// <summary>0, ½, ½, 0: exclusive or, the waves cancel when both are on.</summary>
	Xor,
	/// <summary>0, ½, ½, 2: the sum in phase; read as AND with a threshold between ½ and 2 (4 : 1, the most a passive linear gate gives).</summary>
	And,
	/// <summary>0, ½, ½, ½: or, the waves meet at 120° and two give what one gives.</summary>
	Or,
	/// <summary>0, ½, ½, 1½: what the OR gate's coupler sends the other way, the waves meeting at 60°.</summary>
	Remainder,
}

/// <summary>
/// An optical logic gate, the interferometric logic of a Mach-Zehnder's combining half (the user's choice, 2026-10-21: a
/// 2 × 2 directional coupler): two single-mode waveguides, input A above the axis and input B below, a phase section that
/// puts wave B <see cref="PhaseShift"/> ahead of wave A, and a 3 dB coupler whose upper output carries (a + i·b) / √2 and
/// lower output (i·a + b) / √2 of two coherent inputs (equal waves of the same frequency and phase). The phase shift alone
/// makes the gate (the user's request, 2026-10-22, for AND and OR in the same dimensions as the XOR): +90° gives the upper
/// output a − b (<see cref="XorGatePart"/>), −90° a + b (<see cref="AndGatePart"/>), +30° a + e^{i120°}·b
/// (<see cref="OrGatePart"/>); the phase section sits in a stretch of the same length in all three, so they share their
/// dimensions and ports and tile together. The whole layout follows from <see cref="DesignFrequency"/> (through
/// <see cref="Wavelength"/>) and the indices (<see cref="LogicGateDesign"/>): the core width, the cladding, the gap, the
/// coupler and phase-section lengths are computed with the engines' own discrete wave equation (the 4th-order stencil;
/// the 9-point stencil's waves are slower and miss the design). Both ends are open and carry an anti-reflection band
/// (<see cref="Coating"/>) unless switched off per end (<see cref="InputCoated"/>, <see cref="OutputCoated"/>), so light
/// comes in from a fibre or a gate before and leaves into the next, and each output can
/// be switched off by an absorber in its arm (<see cref="UpperOutputActive"/>, <see cref="LowerOutputActive"/>). The gate
/// is passive; its inputs can be ordinary sources placed on <see cref="Ports"/>.
/// </summary>
public abstract class LogicGatePart : OpticalPart, IFrequencyDesigned
{
	/// <summary>Default <see cref="DesignFrequency"/>: level 50, a wavelength of 32 px at the default solver settings, the user's wavelength (2026-10-21); twelve pixels a period in the core, so the grid resolves the guide well.</summary>
	public const double DefaultFrequency = 50.0;
	/// <summary>Lowest <see cref="DesignFrequency"/>.</summary>
	public const double MinFrequency = 1.0;
	/// <summary>Shortest <see cref="Wavelength"/> the design accepts, in pixels.</summary>
	public const double MinWavelength = 6.0;
	/// <summary>Longest <see cref="Wavelength"/> the design accepts, in pixels.</summary>
	public const double MaxWavelength = 200.0;
	/// <summary>
	/// Default <see cref="Coating"/> in pixels, the least reflecting band on the default gate, measured from the standing wave
	/// in an output arm (2026-10-22): 0, 3, 5, 6, 7, 8 and 9 px reflected 24, 16, 5.5, 3.4, 3.9, 7.5 and 11 % of the power.
	/// A little above the quarter wave λ / (4 √n) = 5.0 px of the core, as the mode runs partly in the cladding.
	/// </summary>
	public const int DefaultCoating = 6;
	/// <summary>
	/// Default <see cref="CoreIndex"/>, the largest: the strongest contrast to the cladding holds the mode tightest, so the
	/// bends and the coupler are shortest. Measured at λ 32 (2026-10-21): cores of 2.0, 2.3 and 2.55 made gates of 726, 588
	/// and 539 px whose XOR output kept 1.4, 1.9 and 1.1 % with both inputs on; a core of 1.6 at λ 16 made 1100 px.
	/// </summary>
	public const double DefaultCoreIndex = 2.55;
	/// <summary>Default <see cref="CladdingIndex"/>.</summary>
	public const double DefaultCladdingIndex = 1.45;
	/// <summary>Default <see cref="PortSpacing"/> in pixels: as close as the design allows (it raises any smaller value); 60 px at the defaults, a unit 120 px wide.</summary>
	public const int DefaultPortSpacing = 2;
	/// <summary>Largest <see cref="PortSpacing"/> in pixels.</summary>
	public const int MaxPortSpacing = 2000;

	private double _designFrequency = DefaultFrequency;
	private double _frequencyScale = Simulation.DefaultSourceFrequencyScale;
	private double _coreIndex = DefaultCoreIndex;
	private double _claddingIndex = DefaultCladdingIndex;
	private int _portSpacing = DefaultPortSpacing;
	private int _coating = DefaultCoating;
	private bool _inputCoated = true;
	private bool _outputCoated = true;
	private bool _upperOutputActive = true;
	private bool _lowerOutputActive = true;

	/// <summary>How far wave B leads wave A into the coupler, in degrees; fixed per kind.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract double PhaseShift { get; }

	/// <summary>What the upper output gives; fixed per kind.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract GateOutput UpperOutput { get; }

	/// <summary>What the lower output gives; fixed per kind.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract GateOutput LowerOutput { get; }

	/// <summary>
	/// The source frequency the gate is designed for, as a source level (the scale of <see cref="Source.Frequency"/>): the
	/// frequency of the sources that drive it (the user's point, 2026-10-22: every source input is a frequency, so the
	/// gate takes one too and shows the wavelength). The layout follows from the <see cref="Wavelength"/> it gives.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(DefaultFrequency)]
	[Range(MinFrequency, Source.MaxLevel)]
	[Category("Design")]
	[DisplayName("Design frequency")]
	[Description("The source frequency the gate is designed for, on the same scale as a source's frequency: the frequency of the sources that drive it. The wavelength, and from it the core width, the coupler and the phase section, follow from it, so the gate works at this frequency only.")]
	public double DesignFrequency
	{
		get => _designFrequency;
		set
		{
			if (SetProperty(ref _designFrequency, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>
	/// The simulation's <see cref="Simulation.SourceFrequencyScale"/>, which turns <see cref="DesignFrequency"/> into an
	/// angular frequency; kept current by the window's <see cref="Parts.GatePortFollower"/>, the default scale until then.
	/// Not stored: the project's simulation settings hold it. UI thread.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public double FrequencyScale
	{
		get => _frequencyScale;
		set
		{
			if (SetProperty(ref _frequencyScale, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>
	/// The wavelength in pixels in a medium of index 1 that <see cref="DesignFrequency"/> gives: λ = 2π / ω with
	/// ω = level / 255 × 0.5 × <see cref="FrequencyScale"/> radians per step and the engines' speed of 1 pixel per step at
	/// index 1 (<see cref="Simulation.Wavelength"/> with that speed); NaN for a frequency that gives none.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Wavelength => Simulation.Wavelength(DesignFrequency, 1.0, FrequencyScale);

	/// <summary>Thickness in pixels of the anti-reflection band at the ends <see cref="InputCoated"/> and <see cref="OutputCoated"/> select, with the index √n of the core and of the cladding; 0 for none. A quarter-wave band is λ / (4 √n) thick.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoating)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band at the ends ticked below, with the index √n of the core and of the cladding; 0 for none. A quarter-wave band, the least reflecting, is λ / (4 √n) pixels thick; the form shows it for the core.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>
	/// Whether the input end carries the anti-reflection band (<see cref="Coating"/>). Off where the gate takes its light
	/// from a gate before it (the user's request, 2026-10-22: a band in the junction keeps the wave from passing from one
	/// unit into the next).
	/// </summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("AR on input side")]
	[Description("Put the anti-reflection band on the input end. Switch it off where the gate takes its light from a gate before it, so the wave passes through the junction.")]
	public bool InputCoated
	{
		get => _inputCoated;
		set => SetProperty(ref _inputCoated, value);
	}

	/// <summary>Whether the output end carries the anti-reflection band (<see cref="Coating"/>); off where the gate hands its light to a gate after it (see <see cref="InputCoated"/>).</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("AR on output side")]
	[Description("Put the anti-reflection band on the output end. Switch it off where the gate hands its light to a gate after it, so the wave passes through the junction.")]
	public bool OutputCoated
	{
		get => _outputCoated;
		set => SetProperty(ref _outputCoated, value);
	}

	/// <summary>Whether the upper output (<see cref="UpperOutput"/>) lets its light out; switched off, an absorber in the blue channel takes it in the arm's last stretch.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Outputs")]
	[DisplayName("Upper output")]
	[Description("Let the upper output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.")]
	public bool UpperOutputActive
	{
		get => _upperOutputActive;
		set => SetProperty(ref _upperOutputActive, value);
	}

	/// <summary>Whether the lower output (<see cref="LowerOutput"/>) lets its light out; switched off, an absorber in the blue channel takes it in the arm's last stretch.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Outputs")]
	[DisplayName("Lower output")]
	[Description("Let the lower output's light leave the gate. Switched off, an absorber with a soft rise takes it in the last stretch of the arm.")]
	public bool LowerOutputActive
	{
		get => _lowerOutputActive;
		set => SetProperty(ref _lowerOutputActive, value);
	}

	/// <summary>Refractive index of the waveguide cores, 1.00 to 2.55; above the cladding's for guiding.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Core index")]
	[Description("Refractive index of the waveguide cores, 1.00 to 2.55 in steps of 0.01. It must be above the cladding's; the larger the difference, the narrower the cores and the tighter the bends.")]
	public double CoreIndex
	{
		get => _coreIndex;
		set
		{
			if (SetProperty(ref _coreIndex, value))
			{
				OnPropertyChanged(nameof(PortSpacingUsed));
			}
		}
	}

	/// <summary>Refractive index of the cladding around the cores, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Material")]
	[DisplayName("Cladding index")]
	[Description("Refractive index of the cladding around the cores, 1.00 to 2.55 in steps of 0.01.")]
	public double CladdingIndex
	{
		get => _claddingIndex;
		set
		{
			if (SetProperty(ref _claddingIndex, value))
			{
				OnPropertyChanged(nameof(PortSpacingUsed));
			}
		}
	}

	/// <summary>Distance between the two inputs (and the two outputs) in pixels, centre to centre, as typed; the design raises a value below what it needs to keep the arms apart (<see cref="PortSpacingUsed"/>).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPortSpacing)]
	[Range(2, MaxPortSpacing)]
	[Category("Shape")]
	[DisplayName("Port spacing")]
	[Description("Distance between the two inputs, and between the two outputs, in pixels. A value below what the design needs to keep the arms apart is raised to it.")]
	public int PortSpacing
	{
		get => _portSpacing;
		set
		{
			if (SetProperty(ref _portSpacing, value))
			{
				OnPropertyChanged(nameof(PortSpacingUsed));
			}
		}
	}

	/// <summary>
	/// The port spacing the design uses, what the form shows and edits: <see cref="PortSpacing"/> raised to the design's
	/// minimum (60 px at the defaults), so every step of the box moves the ports (the user's report, 2026-10-21: the box
	/// stood at 2 and its steps up to the minimum changed nothing). Setting it sets <see cref="PortSpacing"/>. Not stored.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public int PortSpacingUsed
	{
		get => Design?.PortSpacing ?? PortSpacing;
		set => PortSpacing = value;
	}

	/// <summary>Tells the form that the values derived from the design follow an edit of a design input.</summary>
	private void OnDesignChanged()
	{
		OnPropertyChanged(nameof(Wavelength));
		OnPropertyChanged(nameof(PortSpacingUsed));
	}

	/// <summary>The layout for the current values (guarded as the painter guards them), or null when the cores guide nothing. Any thread; cached.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public LogicGateDesign? Design => double.IsFinite(Wavelength) && Wavelength > 0.0
		? LogicGateDesign.For(Math.Round(Math.Clamp(Wavelength, MinWavelength, MaxWavelength), 3), NormalizeIndex(CoreIndex), NormalizeIndex(CladdingIndex), Math.Clamp(PortSpacing, 2, MaxPortSpacing), PhaseShift)
		: null;

	/// <inheritdoc/>
	public override double AxisReach => (Design?.Length ?? 0) / 2.0;

	/// <summary>The outer edge of the claddings: along the upper arm's top, back along the lower arm's bottom; a closed outline of the gate's extent.</summary>
	public override (double X, double Y)[] Outline()
	{
		if (Design is not { } design)
		{
			return [];
		}
		const int steps = 64;
		double outer = design.CoreWidth / 2.0 + design.CladdingThickness + design.AbsorberWidth;
		var points = new (double X, double Y)[2 * (steps + 1)];
		for (int i = 0; i <= steps; i++)
		{
			double x = design.Left + (double)design.Length * i / steps;
			double offset = design.ArmOffset(x) + outer;
			points[i] = (x, -offset);
			points[2 * steps + 1 - i] = (x, offset);
		}
		return points;
	}

	/// <summary>The ports in canvas pixels (rounded to the pixel a source would sit on): input A, input B, the upper output and the lower output; null without a design.</summary>
	public (int X, int Y)[]? Ports()
	{
		if (Design is not { } design)
		{
			return null;
		}
		double half = design.PortSpacing / 2.0;
		(int, int) At(double x, double y)
		{
			(double cx, double cy) = ToCanvas(x, y);
			return ((int)Math.Floor(cx), (int)Math.Floor(cy));
		}
		return [At(design.InputX, -half), At(design.InputX, half), At(design.OutputX, -half), At(design.OutputX, half)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		DesignFrequency = double.IsFinite(DesignFrequency) ? Math.Round(Math.Clamp(DesignFrequency, MinFrequency, Source.MaxLevel), 2) : DefaultFrequency;
		CoreIndex = NormalizeIndex(CoreIndex);
		CladdingIndex = NormalizeIndex(CladdingIndex);
		PortSpacing = Math.Clamp(PortSpacing, 2, MaxPortSpacing);
		Coating = Math.Clamp(Coating, 0, MaxCoating);
	}
}

/// <summary>
/// An XOR gate: wave B leads by 90°, so the upper output carries (a − b) / √2, dark when both inputs are on, and the lower
/// the in-phase sum (the 2026-10-21 design, measured: with both inputs on the upper output keeps about 1 % of one input's
/// power, the lower 1.9×).
/// </summary>
public sealed class XorGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "XOR gate";

	/// <inheritdoc/>
	public override double PhaseShift => 90.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.Xor;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.And;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<XorGatePart>();
}

/// <summary>
/// An AND gate in the only sense a passive linear gate has one: wave A leads by 90°, so the upper output carries the in-phase
/// sum (a + b) / √2, half an input's power for one input and twice it for two, 4 : 1, to be read with a threshold between;
/// the lower output carries the XOR. The XOR gate with its outputs exchanged.
/// </summary>
public sealed class AndGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "AND gate";

	/// <inheritdoc/>
	public override double PhaseShift => -90.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.And;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.Xor;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<AndGatePart>();
}

/// <summary>
/// An OR gate: wave B leads by 30°, so the waves meet at 120° in the upper output, a + e^{i120°}·b over √2, and one input or
/// two give the same half an input's power (1 + 1 + 2 cos 120° = 1); the lower output, where they meet at 60°, gives ½, ½ and 1½.
/// </summary>
public sealed class OrGatePart : LogicGatePart
{
	/// <inheritdoc/>
	public override string NameStem => "OR gate";

	/// <inheritdoc/>
	public override double PhaseShift => 30.0;

	/// <inheritdoc/>
	public override GateOutput UpperOutput => GateOutput.Or;

	/// <inheritdoc/>
	public override GateOutput LowerOutput => GateOutput.Remainder;

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<OrGatePart>();
}

/// <summary>
/// A Fresnel zone plate (the user's request, 2026-10-22), a diffractive lens: a plate of <see cref="Thickness"/> along the
/// axis and <see cref="Diameter"/> across it, cut into zones at the radii rₙ = √(n·λ·f + (n·λ/2)²) from the axis, λ the
/// <see cref="Wavelength"/> of <see cref="DesignFrequency"/> and f the <see cref="FocalLength"/>; every second zone is a
/// block that absorbs (the user's choice of material), the central zone open, or a block when <see cref="Inverse"/>. The
/// waves through the open zones arrive at the focus a whole number of periods apart and add there. The blocks are painted
/// into the blue channel only, the refractive index left as it is: full at their middle, rising quadratically over
/// <see cref="Coating"/> from the faces the wave meets and leaves, so they reflect little; their sides toward the
/// neighbouring zones stay sharp, as the outer zones are narrow (about λ·f / <see cref="Diameter"/>). Light passes freely
/// outside the diameter unless the absorbing <see cref="Mount"/> is on. Positions and lengths are canvas pixels.
/// </summary>
public sealed class ZonePlatePart : OpticalPart, IFrequencyDesigned
{
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 800;
	/// <summary>Default <see cref="Thickness"/> in pixels: two wavelengths at the default frequency, room for the rise on both faces (see <see cref="BlockAbsorption"/>).</summary>
	public const int DefaultThickness = 64;
	/// <summary>Default <see cref="Coating"/> in pixels: a wavelength at the default frequency (see <see cref="BlockAbsorption"/>).</summary>
	public const int DefaultCoating = 32;
	/// <summary>Default <see cref="DesignFrequency"/>: level 50, a wavelength of 32 px at the default solver settings, as the logic gates.</summary>
	public const double DefaultFrequency = 50.0;
	/// <summary>Default <see cref="FocalLength"/> in pixels.</summary>
	public const int DefaultFocalLength = 600;
	/// <summary>
	/// The absorption (blue) in the middle of a block. Measured on a block of the whole diameter with a plane wave at λ 32
	/// (2026-10-22, transmitted / reflected power): 255 over a 16 px rise in 48 px 0.4 / 10.7 %, over 24 px 0.3 / 4.9 %;
	/// 128 over 24 px 0.3 / 2.2 %; 64 over 24 px 2.7 / 0.9 %; in 64 px over 32 px, 64 1.0 / 0.7 %, 96 0.4 / 1.0 %, 128
	/// 0.3 / 1.2 %. A block that stops all and reflects nothing does not exist: a strong absorber is a wall of its own.
	/// </summary>
	public const int BlockAbsorption = 96;

	/// <summary>Default <see cref="MountWidth"/> in pixels.</summary>
	public const int DefaultMountWidth = 200;

	private int _diameter = DefaultDiameter;
	private int _thickness = DefaultThickness;
	private int _coating = DefaultCoating;
	private double _designFrequency = DefaultFrequency;
	private double _frequencyScale = Simulation.DefaultSourceFrequencyScale;
	private int _focalLength = DefaultFocalLength;
	private bool _inverse;
	private bool _mount;
	private int _mountWidth = DefaultMountWidth;

	/// <inheritdoc/>
	public override string NameStem => "Zone plate";

	/// <summary>
	/// Whether an absorbing mount surrounds the plate (the user's request, 2026-10-22): a band of <see cref="MountWidth"/>
	/// on either side beyond the diameter, the plate's thickness and absorption, so no light passes outside the zones (which
	/// otherwise meets the focused light on the axis and pushes the focus on).
	/// </summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Shape")]
	[Description("Surround the plate with an absorbing mount, so no light passes outside its diameter. Without it, that light meets the focused light on the axis and moves the focus further out, the more so the fewer zones the plate has.")]
	public bool Mount
	{
		get => _mount;
		set => SetProperty(ref _mount, value);
	}

	/// <summary>Width in pixels of the mount on either side, beyond the diameter.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultMountWidth)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[DisplayName("Mount width")]
	[Description("Width in pixels of the absorbing mount on either side, beyond the diameter.")]
	public int MountWidth
	{
		get => _mountWidth;
		set => SetProperty(ref _mountWidth, value);
	}

	/// <summary>Half the plate's outer extent across the axis in pixels: half the diameter, plus the mount when it is on.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterHalfWidth => Math.Max(2, Diameter) / 2.0 + (Mount ? Math.Max(1, MountWidth) : 0);

	/// <summary>Extent across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels; the zones fill it.")]
	public int Diameter
	{
		get => _diameter;
		set
		{
			if (SetProperty(ref _diameter, value))
			{
				OnPropertyChanged(nameof(ZoneCount));
			}
		}
	}

	/// <summary>Extent along the axis in pixels, the blocks' depth.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultThickness)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels: the depth of the blocks. It should leave room for the anti-reflection rise on both faces.")]
	public int Thickness
	{
		get => _thickness;
		set => SetProperty(ref _thickness, value);
	}

	/// <summary>Depth in pixels over which a block's absorption rises from nothing at the faces the wave meets and leaves to its full strength; at most half the thickness takes effect.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoating)]
	[Range(0, MaxAbsorptionRamp)]
	[Category("Shape")]
	[DisplayName("Anti-reflection")]
	[Description("Depth in pixels over which a block's absorption rises from nothing at its faces to its full strength, so the blocks reflect little; at most half the thickness takes effect.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>The source frequency the plate is designed for, as a source level (the scale of <see cref="Source.Frequency"/>).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFrequency)]
	[Range(LogicGatePart.MinFrequency, Source.MaxLevel)]
	[Category("Design")]
	[DisplayName("Target frequency")]
	[Description("The source frequency the plate is designed for, on the same scale as a source's frequency. The zones follow from the wavelength it gives, so the plate focuses this frequency at the focal length; other frequencies focus elsewhere.")]
	public double DesignFrequency
	{
		get => _designFrequency;
		set
		{
			if (SetProperty(ref _designFrequency, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public double FrequencyScale
	{
		get => _frequencyScale;
		set
		{
			if (SetProperty(ref _frequencyScale, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>Distance in pixels from the plate's centre to the focus along the axis (+x'), for a plane wave arriving along it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFocalLength)]
	[Range(1, int.MaxValue)]
	[Category("Design")]
	[DisplayName("Focal length")]
	[Description("Distance in pixels from the plate to its focus, for a plane wave arriving along the axis.")]
	public int FocalLength
	{
		get => _focalLength;
		set
		{
			if (SetProperty(ref _focalLength, value))
			{
				OnPropertyChanged(nameof(ZoneCount));
			}
		}
	}

	/// <summary>Whether the zones are exchanged: the central zone a block and the first ring open, and so on.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Design")]
	[Description("Exchange the zones: the central zone becomes a block, the first ring open, and so on. The focus stays where it is.")]
	public bool Inverse
	{
		get => _inverse;
		set => SetProperty(ref _inverse, value);
	}

	/// <summary>The wavelength in pixels in a medium of index 1 that <see cref="DesignFrequency"/> gives with <see cref="FrequencyScale"/> (the engines' speed of 1 pixel per step); NaN for none.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Wavelength => Simulation.Wavelength(DesignFrequency, 1.0, FrequencyScale);

	/// <summary>How many zones, open and blocked, fit within half the diameter (the last one cut by the rim); 0 without a wavelength.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int ZoneCount
	{
		get
		{
			double half = Math.Max(2, Diameter) / 2.0;
			return ZoneAt(half) is { } zone ? zone + 1 : 0;
		}
	}

	/// <summary>
	/// The zone, counted from 0 at the axis, that the distance <paramref name="r"/> from the axis falls in: the n with
	/// rₙ ≤ r &lt; rₙ₊₁, from rₙ² = n·λ·f + (n·λ/2)², a quadratic in n; null without a wavelength or a focal length.
	/// </summary>
	public int? ZoneAt(double r)
	{
		double lambda = Wavelength;
		double f = Math.Max(1, FocalLength);
		if (!double.IsFinite(lambda) || lambda <= 0.0)
		{
			return null;
		}
		// (λ²/4)·n² + λf·n − r² = 0
		double a = lambda * lambda / 4.0;
		double b = lambda * f;
		double n = (-b + Math.Sqrt(b * b + 4.0 * a * r * r)) / (2.0 * a);
		return (int)Math.Floor(n);
	}

	/// <summary>Whether the distance <paramref name="r"/> from the axis falls in a block: the odd zones, or the even ones when inverse.</summary>
	public bool IsBlockAt(double r) => ZoneAt(Math.Abs(r)) is { } zone && (zone % 2 == 1) != Inverse;

	/// <summary>The width in pixels of the outermost whole zone within the diameter, rₙ − rₙ₋₁; what the grid must resolve.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterZoneWidth
	{
		get
		{
			double lambda = Wavelength;
			int count = ZoneCount;
			if (count < 2)
			{
				return Math.Max(2, Diameter) / 2.0;
			}
			double f = Math.Max(1, FocalLength);
			double Radius(int n) => Math.Sqrt(n * lambda * f + n * lambda / 2.0 * (n * lambda / 2.0));
			return Radius(count - 1) - Radius(count - 2);
		}
	}

	/// <summary>Tells the form that the values derived from the wavelength follow an edit.</summary>
	private void OnDesignChanged()
	{
		OnPropertyChanged(nameof(Wavelength));
		OnPropertyChanged(nameof(ZoneCount));
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Thickness) / 2.0;

	/// <summary>The plate's rectangle, the mount included when it is on.</summary>
	public override (double X, double Y)[] Outline()
	{
		double t = Math.Max(1, Thickness) / 2.0;
		double d = OuterHalfWidth;
		return [(-t, -d), (t, -d), (t, d), (-t, d)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		MountWidth = Math.Max(1, MountWidth);
		Diameter = Math.Max(2, Diameter);
		Thickness = Math.Max(1, Thickness);
		Coating = Math.Clamp(Coating, 0, MaxAbsorptionRamp);
		DesignFrequency = double.IsFinite(DesignFrequency) ? Math.Round(Math.Clamp(DesignFrequency, LogicGatePart.MinFrequency, Source.MaxLevel), 2) : DefaultFrequency;
		FocalLength = Math.Max(1, FocalLength);
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<ZonePlatePart>();
}
