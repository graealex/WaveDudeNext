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

/// <summary>
/// An optical part placed on the canvas: a lens or mirror, a shape (polygon, rectangle, ellipse, triangle, outline), a
/// diffuser, an interference filter, a simple grating, a facetted grating, a meta lens, a Fresnel lens, a fibre, a Bragg reflector, a GRIN lens, a logic gate or a zone plate (Jeroen Vleggaar's proposal, decided 2026-10-18;
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
