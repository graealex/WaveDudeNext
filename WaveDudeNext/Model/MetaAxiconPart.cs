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
/// A meta axicon (2026-11-08, the user's request, the third entry of the Meta surfaces submenu): a <see cref="MetaSurfacePart"/>
/// whose cells delay in proportion to the distance from the axis, a full turn less (2π/λ)·|y'|·sin β wrapped to a
/// turn, so the two halves of a plane wave arriving along the axis are turned toward it by <see cref="ConeAngle"/> β and
/// cross along a line behind the surface (a Bessel-like beam) of about half the width over tan β
/// (<see cref="LineLength"/>) rather than at a point; each zone is λ / sin β wide (<see cref="ZoneWidth"/>).
/// </summary>
public sealed class MetaAxiconPart : MetaSurfacePart
{
	/// <summary>Default <see cref="ConeAngle"/> in degrees.</summary>
	public const double DefaultConeAngle = 10.0;
	/// <summary>Smallest <see cref="ConeAngle"/> in degrees: short of no cone.</summary>
	public const double MinConeAngle = 0.1;
	/// <summary>Largest <see cref="ConeAngle"/> in degrees: short of grazing.</summary>
	public const double MaxConeAngle = 89.0;

	private double _coneAngle = DefaultConeAngle;

	/// <inheritdoc/>
	public override string NameStem => "Meta axicon";

	/// <summary>The angle in degrees by which each half of a wave arriving along the axis is turned toward it, <see cref="MinConeAngle"/> to <see cref="MaxConeAngle"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultConeAngle)]
	[Range(MinConeAngle, MaxConeAngle)]
	[Category("Axicon")]
	[DisplayName("Cone angle")]
	[Description("The angle in degrees by which each half of a wave arriving along the axis is turned toward it. The steeper, the shorter the line of focus behind the surface and the narrower the zones, which must stay a few cells wide.")]
	public double ConeAngle
	{
		get => _coneAngle;
		set
		{
			if (SetProperty(ref _coneAngle, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The cone angle guarded, in radians.</summary>
	private double Radians => (double.IsFinite(ConeAngle) ? Math.Clamp(ConeAngle, MinConeAngle, MaxConeAngle) : DefaultConeAngle) * Math.PI / 180.0;

	/// <summary>The width of a zone, a full turn of phase, across the face in pixels: λ / sin β.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ZoneWidth => Wavelength / Math.Sin(Radians);

	/// <summary>How far behind the surface the line of focus reaches, in pixels: the half width over tan β, where the rim's rays cross the axis.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double LineLength => Math.Max(2, Width) / 2.0 / Math.Tan(Radians);

	/// <inheritdoc/>
	public override int ZoneCount => HasWavelength ? (int)Math.Floor(Math.Max(2, Width) / 2.0 / ZoneWidth) + 1 : 0;

	/// <summary>A full turn less the phase a cone needs, (2π/λ)·|y'|·sin β, so the delay is greatest on the axis and falls outward within each zone, as a converging lens's.</summary>
	protected override double Delay(double y) => 2.0 * Math.PI - Wrap(2.0 * Math.PI / Wavelength * Math.Abs(y) * Math.Sin(Radians));

	/// <inheritdoc/>
	protected override void OnDesignChanged()
	{
		base.OnDesignChanged();
		OnPropertyChanged(nameof(ZoneWidth));
		OnPropertyChanged(nameof(LineLength));
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		ConeAngle = double.IsFinite(ConeAngle) ? Math.Round(Math.Clamp(ConeAngle, MinConeAngle, MaxConeAngle), 2) : DefaultConeAngle;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<MetaAxiconPart>();
}
