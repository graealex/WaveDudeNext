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
/// A meta surface (2026-11-08, the user's request): a carrier plate of <see cref="Length"/> along the axis and
/// <see cref="Width"/> across, the material of a <see cref="ShapePart"/>, with a sub-wavelength pattern on its front
/// (−x'): a ridge of <see cref="PatternIndex"/> in every cell of <see cref="Pitch"/> across the face, <see cref="Height"/>
/// deep in front of the plate (the user's choice: before the plate, as the filter's layers), its width the cell's fill
/// factor times the pitch. What the surface does is the phase delay a subclass asks of each cell (<see cref="Delay"/>,
/// wrapped to a turn): a ridge of effective index n_eff delays by (2π/λ)·H·(n_eff − 1), where a cell filled to f has
/// n_eff² = f·n_p² + (1 − f) (the lamellar effective medium), so f = (n_eff² − 1) / (n_p² − 1), clamped to 0..1: a full
/// turn needs a height of λ / (n_p − 1) (<see cref="FullTurnHeight"/>), and a lower pattern saturates. λ comes from
/// <see cref="DesignFrequency"/> through the simulation's frequency scale, as a zone plate's. The AR band lies on the
/// ridges' tops (front) and the plate's back (the user's rule); the plate's sides are not offered.
/// </summary>
public abstract class MetaSurfacePart : ShapePart, IFrequencyDesigned
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>Default <see cref="DesignFrequency"/>: level 50, a wavelength of 32 px at the default solver settings, as the zone plate's.</summary>
	public const double DefaultFrequency = 50.0;
	/// <summary>Default <see cref="Pitch"/> in pixels: an eighth of the default wavelength, sub-wavelength.</summary>
	public const double DefaultPitch = 4.0;
	/// <summary>Default <see cref="Height"/> in pixels: a full turn at the default wavelength and pattern index, λ / (n − 1) = 32.</summary>
	public const double DefaultHeight = 32.0;
	/// <summary>Default <see cref="PatternIndex"/> (the user's choice, 2026-11-08).</summary>
	public const double DefaultPatternIndex = 2.0;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private double _designFrequency = DefaultFrequency;
	private double _frequencyScale = Simulation.DefaultSourceFrequencyScale;
	private double _pitch = DefaultPitch;
	private double _height = DefaultHeight;
	private double _patternIndex = DefaultPatternIndex;

	/// <summary>A new meta surface coats nothing on its sides: the band goes on the ridges' tops and the plate's back when a thickness is set.</summary>
	public override void PrepareNew() => SidesCoated = false;

	/// <summary>Extent of the carrier plate along the axis in pixels, without the pattern.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Thickness of the carrier plate along the axis in pixels; the pattern stands on its front, outside it.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent of the plate and the pattern across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Width of the plate and its pattern across the axis in pixels.")]
	public int Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The source frequency the surface is designed for, on a source's scale; the wavelength it gives sets every cell's fill.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFrequency)]
	[Range(LogicGatePart.MinFrequency, Source.MaxLevel)]
	[Category("Design")]
	[DisplayName("Target frequency")]
	[Description("The source frequency the surface is designed for, on the same scale as a source's frequency. The cells' fill follows from the wavelength it gives; other frequencies see another surface.")]
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

	/// <summary>The cell's width across the face in pixels, 1 to <see cref="DiffuserPart.MaxPixels"/>: one ridge per cell; well below the wavelength, so the wave sees a mean index.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPitch)]
	[Range(1.0, DiffuserPart.MaxPixels)]
	[Category("Pattern")]
	[Description("The width of a cell of the pattern across the face in pixels, one ridge per cell. Well below the wavelength, so the wave sees each cell as a mean index rather than diffracting at it.")]
	public double Pitch
	{
		get => _pitch;
		set
		{
			if (SetProperty(ref _pitch, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The ridges' height in front of the plate in pixels, 1 to <see cref="DiffuserPart.MaxPixels"/>; a full turn of phase needs λ / (n − 1).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultHeight)]
	[Range(1.0, DiffuserPart.MaxPixels)]
	[Category("Pattern")]
	[Description("The height of the ridges in front of the plate in pixels. A full turn of phase takes a height of the wavelength over the pattern index less one; a lower pattern cannot delay enough and its fill saturates.")]
	public double Height
	{
		get => _height;
		set
		{
			if (SetProperty(ref _height, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>Refractive index of the ridges, 1.00 to 2.55 in steps of 0.01; the plate's is <see cref="ShapePart.RefractiveIndex"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPatternIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Pattern")]
	[DisplayName("Pattern index")]
	[Description("Refractive index of the ridges, 1.00 to 2.55 in steps of 0.01, usually above the plate's. The higher, the lower the pattern can be.")]
	public double PatternIndex
	{
		get => _patternIndex;
		set
		{
			if (SetProperty(ref _patternIndex, value))
			{
				OnDesignChanged();
			}
		}
	}

	/// <summary>The wavelength in pixels in a medium of index 1 that <see cref="DesignFrequency"/> gives with <see cref="FrequencyScale"/>; NaN for none.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double Wavelength => Simulation.Wavelength(DesignFrequency, 1.0, FrequencyScale);

	/// <summary>The height in pixels at which a fully filled cell delays by a whole turn, λ / (n − 1).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double FullTurnHeight => Wavelength / Math.Max(1e-9, GuardedPatternIndex - 1.0);

	/// <summary>How many zones, each a turn of phase, the surface has across its face (a lens's from the centre to the rim), the last one cut by the rim; 0 without a wavelength.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public abstract int ZoneCount { get; }

	/// <summary>Number of cells across the face: the width over the pitch, rounded down, 1 at least.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int CellCount => Math.Max(1, (int)Math.Floor(Math.Max(2, Width) / GuardedPitch));

	/// <summary>The pitch as the pattern uses it: finite, 1 to the width.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedPitch => double.IsFinite(Pitch) ? Math.Clamp(Pitch, 1.0, Math.Max(2, Width)) : DefaultPitch;

	/// <summary>The height as the pattern uses it: finite, 1 to <see cref="DiffuserPart.MaxPixels"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedHeight => double.IsFinite(Height) ? Math.Clamp(Height, 1.0, DiffuserPart.MaxPixels) : DefaultHeight;

	/// <summary>The pattern index as the pattern uses it: finite, within the index range.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double GuardedPatternIndex => double.IsFinite(PatternIndex) ? Math.Clamp(PatternIndex, MinIndex, MaxIndex) : DefaultPatternIndex;

	/// <summary>Whether the design has a wavelength to work with.</summary>
	protected bool HasWavelength => double.IsFinite(Wavelength) && Wavelength > 0.0;

	/// <summary>The y' of the centre of cell <paramref name="cell"/> (0 to <see cref="CellCount"/> − 1), the cells laid from the face's one end, centred together on the axis.</summary>
	public double CellCentre(int cell)
	{
		double pitch = GuardedPitch;
		return (cell - (CellCount - 1) / 2.0) * pitch;
	}

	/// <summary>The phase delay the surface asks of the cell at <paramref name="y"/> from the axis, in radians, any value: it is wrapped to a turn for the fill. Only asked with a wavelength.</summary>
	protected abstract double Delay(double y);

	/// <summary><paramref name="phase"/> wrapped into 0 to 2π.</summary>
	protected static double Wrap(double phase) => phase - 2.0 * Math.PI * Math.Floor(phase / (2.0 * Math.PI));

	/// <summary>
	/// The fill factor of every cell, 0 (no ridge) to 1 (the cell solid): the delay the cell needs (<see cref="Delay"/>,
	/// wrapped to a turn, a full turn kept as one, so a converging centre cell is solid rather than empty), turned into the effective index the ridge must give, (delay / 2π)·λ / H + 1, and that into the
	/// fill through the lamellar mean of n², clamped where the height cannot reach it. Empty without a wavelength.
	/// </summary>
	public double[] FillFactors()
	{
		int cells = CellCount;
		var fills = new double[cells];
		if (!HasWavelength)
		{
			return fills;
		}
		double lambda = Wavelength;
		double height = GuardedHeight;
		double n2 = GuardedPatternIndex * GuardedPatternIndex;
		for (int j = 0; j < cells; j++)
		{
			double asked = Delay(CellCentre(j));
			double delay = asked >= 0.0 && asked <= 2.0 * Math.PI ? asked : Wrap(asked);
			double effective = 1.0 + delay / (2.0 * Math.PI) * lambda / height;
			fills[j] = n2 > 1.0 ? Math.Clamp((effective * effective - 1.0) / (n2 - 1.0), 0.0, 1.0) : 0.0;
		}
		return fills;
	}

	/// <summary>Raises the readouts that follow the design: the wavelength, the full-turn height, the zones, the cells; a subclass adds its own.</summary>
	protected virtual void OnDesignChanged()
	{
		OnPropertyChanged(nameof(Wavelength));
		OnPropertyChanged(nameof(FullTurnHeight));
		OnPropertyChanged(nameof(ZoneCount));
		OnPropertyChanged(nameof(CellCount));
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + GuardedHeight;

	/// <summary>The box around the plate and its pattern: the outline the overlay draws.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double back = Math.Max(1, Length) / 2.0;
		double front = -back - GuardedHeight;
		double w = Math.Max(2, Width) / 2.0;
		return [(front, -w), (back, -w), (back, w), (front, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(2, Width);
		DesignFrequency = double.IsFinite(DesignFrequency) ? Math.Round(Math.Clamp(DesignFrequency, LogicGatePart.MinFrequency, Source.MaxLevel), 2) : DefaultFrequency;
		Pitch = double.IsFinite(Pitch) ? Math.Clamp(Pitch, 1.0, DiffuserPart.MaxPixels) : DefaultPitch;
		Height = GuardedHeight;
		PatternIndex = NormalizeIndex(PatternIndex);
	}
}
