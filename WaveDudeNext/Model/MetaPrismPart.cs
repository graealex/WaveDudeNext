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
/// A meta prism (2026-11-08, the user's request, the second entry of the Meta surfaces submenu): a <see cref="MetaSurfacePart"/>
/// whose cells delay in a line across the face, (2π/λ)·y'·sin θ wrapped to a turn, so a plane wave arriving along the
/// axis leaves turned by <see cref="Deflection"/> θ, counter-clockwise on the screen for a positive angle, as a blazed
/// grating's first order does; each zone is a turn, λ / sin θ wide (<see cref="ZoneWidth"/>).
/// </summary>
public sealed class MetaPrismPart : MetaSurfacePart
{
	/// <summary>Default <see cref="Deflection"/> in degrees.</summary>
	public const double DefaultDeflection = 20.0;
	/// <summary>Largest magnitude of <see cref="Deflection"/> in degrees: short of grazing.</summary>
	public const double MaxDeflection = 89.0;

	private double _deflection = DefaultDeflection;

	/// <inheritdoc/>
	public override string NameStem => "Meta prism";

	/// <summary>The angle in degrees by which a plane wave arriving along the axis is turned, −<see cref="MaxDeflection"/> to <see cref="MaxDeflection"/>, counter-clockwise on the screen for a positive angle.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDeflection)]
	[Range(-MaxDeflection, MaxDeflection)]
	[Category("Prism")]
	[Description("The angle in degrees by which a wave arriving along the axis is turned, counter-clockwise on the screen for a positive angle; 0 turns nothing. The steeper, the narrower the zones, which must stay a few cells wide.")]
	public double Deflection
	{
		get => _deflection;
		set
		{
			if (SetProperty(ref _deflection, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The sine of the deflection, guarded.</summary>
	private double Sine => Math.Sin((double.IsFinite(Deflection) ? Math.Clamp(Deflection, -MaxDeflection, MaxDeflection) : DefaultDeflection) * Math.PI / 180.0);

	/// <summary>The width of a zone, a full turn of phase, across the face in pixels: λ / sin θ; infinite at no deflection.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ZoneWidth => Wavelength / Math.Max(1e-12, Math.Abs(Sine));

	/// <inheritdoc/>
	public override int ZoneCount => HasWavelength ? (int)Math.Floor(Math.Max(2, Width) / ZoneWidth) + 1 : 0;

	/// <summary>A line across the face: the delay grows toward −y' (up the screen at 0°) for a positive angle, so the wavefront lags there and the wave turns that way.</summary>
	protected override double Delay(double y) => -2.0 * Math.PI / Wavelength * y * Sine;

	/// <inheritdoc/>
	protected override void OnDesignChanged()
	{
		base.OnDesignChanged();
		OnPropertyChanged(nameof(ZoneWidth));
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Deflection = double.IsFinite(Deflection) ? Math.Round(Math.Clamp(Deflection, -MaxDeflection, MaxDeflection), 2) : DefaultDeflection;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<MetaPrismPart>();
}
