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
