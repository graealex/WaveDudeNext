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

/// <summary>What a <see cref="MetaLensPart"/> does to a plane wave: focus it, or spread it as from a focus behind it.</summary>
public enum MetaLensType
{
	/// <summary>Converging: the cells' delay is greatest at the centre and falls outward within each zone, as a convex lens's glass thins.</summary>
	Converging,
	/// <summary>Diverging: the delay grows outward within each zone, as a concave lens's glass thickens.</summary>
	Diverging,
}

/// <summary>
/// A meta lens (2026-11-08, the user's request, the first entry of the Meta surfaces submenu): a <see cref="MetaSurfacePart"/>
/// whose cells delay as a lens would (the user's choice: computed from the wavelength and the <see cref="FocalLength"/>):
/// a cell at y' needs the phase of a lens, (2π/λ)(√(F² + y'²) − F), wrapped to a turn, taken from a full turn down for
/// a converging lens and up from zero for a diverging one (<see cref="LensType"/>).
/// </summary>
public sealed class MetaLensPart : MetaSurfacePart
{
	/// <summary>Default <see cref="FocalLength"/> in pixels (the user's choice, 2026-11-08).</summary>
	public const int DefaultFocalLength = 500;

	private int _focalLength = DefaultFocalLength;
	private MetaLensType _lensType = MetaLensType.Converging;

	/// <inheritdoc/>
	public override string NameStem => "Meta lens";

	/// <summary>Distance in pixels from the lens to its focus along the axis (+x'), for a plane wave arriving along it; a diverging lens spreads as from a focus that far behind it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFocalLength)]
	[Range(1, int.MaxValue)]
	[Category("Lens")]
	[DisplayName("Focal length")]
	[Description("Distance in pixels from the lens to its focus, for a plane wave arriving along the axis; a diverging lens spreads the wave as if from a focus that far behind it.")]
	public int FocalLength
	{
		get => _focalLength;
		set
		{
			if (SetProperty(ref _focalLength, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>Converging or diverging: which way the fill runs within each zone.</summary>
	[XmlAttribute]
	[DefaultValue(MetaLensType.Converging)]
	[Category("Lens")]
	[DisplayName("Lens type")]
	[Description("Converging, the fill greatest at the centre and falling outward within each zone, as a convex lens's glass thins; or diverging, the fill growing outward.")]
	public MetaLensType LensType
	{
		get => _lensType;
		set
		{
			if (SetProperty(ref _lensType, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <inheritdoc/>
	public override int ZoneCount => HasWavelength ? (int)Math.Floor(LensPhase(Math.Max(2, Width) / 2.0) / (2.0 * Math.PI)) + 1 : 0;

	/// <summary>The lens's phase lead at <paramref name="y"/> from the axis relative to the centre, in radians, (2π/λ)(√(F² + y²) − F): what a converging lens must take away there, a diverging one add; 0 without a wavelength.</summary>
	public double LensPhase(double y)
	{
		if (!HasWavelength)
		{
			return 0.0;
		}
		double f = Math.Max(1, FocalLength);
		return 2.0 * Math.PI / Wavelength * (Math.Sqrt(f * f + y * y) - f);
	}

	/// <summary>A full turn less the wrapped lens phase for a converging lens, the wrapped phase itself for a diverging one.</summary>
	protected override double Delay(double y)
	{
		double wrapped = Wrap(LensPhase(y));
		return LensType == MetaLensType.Diverging ? wrapped : 2.0 * Math.PI - wrapped;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		FocalLength = Math.Max(1, FocalLength);
		LensType = Enum.IsDefined(LensType) ? LensType : MetaLensType.Converging;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<MetaLensPart>();
}
