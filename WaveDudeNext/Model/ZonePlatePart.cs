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
	/// <summary>Default <see cref="Thickness"/> in pixels: a wavelength at the default frequency (Jeroen's choice, 2026-11-08; <see cref="Version3Thickness"/> before).</summary>
	public const int DefaultThickness = 32;
	/// <summary>Default <see cref="Coating"/> in pixels: no rise (Jeroen's choice, 2026-11-08; <see cref="Version3Coating"/> before).</summary>
	public const int DefaultCoating = 0;
	/// <summary>The <see cref="Thickness"/> a project file up to version 3 means when it leaves the attribute out, the default until 2026-11-08: two wavelengths at the default frequency, room for the rise on both faces (see <see cref="BlockAbsorption"/>).</summary>
	public const int Version3Thickness = 64;
	/// <summary>The <see cref="Coating"/> a project file up to version 3 means when it leaves the attribute out, the default until 2026-11-08: a wavelength at the default frequency (see <see cref="BlockAbsorption"/>).</summary>
	public const int Version3Coating = 32;
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
	private bool _thicknessRead;
	private bool _coatingRead;
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
		set
		{
			_thicknessRead = true;
			SetProperty(ref _thickness, value);
		}
	}

	/// <summary>
	/// Whether <see cref="Thickness"/> and <see cref="Coating"/> have been set since construction: right after a file is
	/// read, whether the file carried the attribute. A file up to version 3 left them out for 64 and 32, their defaults
	/// then, which <see cref="Project.Load(System.IO.Stream)"/> restores (2026-11-09, when the defaults became 32 and 0).
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	internal bool ThicknessRead => _thicknessRead;

	/// <inheritdoc cref="ThicknessRead"/>
	[XmlIgnore]
	[Browsable(false)]
	internal bool CoatingRead => _coatingRead;

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
		set
		{
			_coatingRead = true;
			SetProperty(ref _coating, value);
		}
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
