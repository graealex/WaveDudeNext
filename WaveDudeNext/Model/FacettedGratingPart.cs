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

/// <summary>The facet pattern of a <see cref="FacettedGratingPart"/> (2026-11-08, the user's request for these three, sawtooth the default).</summary>
public enum FacetPattern
{
	/// <summary>A ramp over each pitch from the crest to the trough, then a step back: a blazed grating.</summary>
	Sawtooth,
	/// <summary>A crest over half of each pitch and a trough over the other half: a binary grating.</summary>
	SquareWave,
	/// <summary>A sine over each pitch: a holographic grating.</summary>
	Sinusoidal,
}

/// <summary>
/// A facetted grating (2026-11-08, the user's request, in the Diffraction gratings submenu): a plate of
/// <see cref="Length"/> along the axis and <see cref="Width"/> across, the material of a <see cref="ShapePart"/>, whose
/// front face (−x') carries a periodic relief, <see cref="Pattern"/> repeated every <see cref="Pitch"/> pixels across
/// the face with <see cref="Amplitude"/> pixels either side of the mean; the back is flat. A diffuser with one
/// wavelength, as the user put it, and painted by the same shape: the face is a sample per pixel, so a square wave's
/// and a sawtooth's steps stand between two pixels. The AR coating lies on the back alone (the user's rule; only
/// <see cref="CoatedPart.BackCoated"/> counts, the form hides the other two boxes).
/// </summary>
public sealed class FacettedGratingPart : ShapePart, IProfiledPlate
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>Default <see cref="Pitch"/> in pixels (the user's choice, 2026-11-08, after a first look at 16).</summary>
	public const double DefaultPitch = 32.0;
	/// <summary>Smallest <see cref="Pitch"/> in pixels: the face is sampled a pixel apart.</summary>
	public const double MinPitch = 2.0;
	/// <summary>Default <see cref="Amplitude"/> in pixels (the user's choice, 2026-11-08, after a first look at 2).</summary>
	public const double DefaultAmplitude = 4.0;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private FacetPattern _pattern = FacetPattern.Sawtooth;
	private double _pitch = DefaultPitch;
	private double _amplitude = DefaultAmplitude;

	/// <inheritdoc/>
	public override string NameStem => "Facetted grating";

	/// <summary>Extent along the axis in pixels: the plate's thickness at the mean of its facetted face.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels: the plate's thickness at the mean of its facetted face.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent across the axis in pixels: the width of the facetted face.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels: the width of the facetted face.")]
	public int Width
	{
		get => _width;
		set => SetProperty(ref _width, value);
	}

	/// <summary>The facets' shape over each pitch.</summary>
	[XmlAttribute]
	[DefaultValue(FacetPattern.Sawtooth)]
	[Category("Facets")]
	[Description("The facets' shape over each pitch: a sawtooth, a ramp then a step; a square wave, a crest and a trough of half a pitch each; or a sinusoid.")]
	public FacetPattern Pattern
	{
		get => _pattern;
		set => SetProperty(ref _pattern, value);
	}

	/// <summary>The period of the facets across the face in pixels, 2 to <see cref="DiffuserPart.MaxPixels"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultPitch)]
	[Range(MinPitch, DiffuserPart.MaxPixels)]
	[Category("Facets")]
	[Description("The period of the facets across the face in pixels: the grating's pitch, which with the wavelength sets the diffraction angles. Two pixels at least.")]
	public double Pitch
	{
		get => _pitch;
		set => SetProperty(ref _pitch, value);
	}

	/// <summary>The relief's height either side of the mean face in pixels, 0 to <see cref="DiffuserPart.MaxPixels"/>; the facets span twice this.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAmplitude)]
	[Range(0.0, DiffuserPart.MaxPixels)]
	[Category("Facets")]
	[Description("The relief's height either side of the mean face in pixels: the facets run from this far out to this far in, twice it from crest to trough. 0 makes the face flat.")]
	public double Amplitude
	{
		get => _amplitude;
		set => SetProperty(ref _amplitude, value);
	}

	/// <summary>The relief at every whole pixel across the face, positive into the plate: the pattern over the position in its pitch, from the face's centre.</summary>
	public double[] Profile()
	{
		int width = Math.Max(2, Width);
		double pitch = double.IsFinite(Pitch) ? Math.Clamp(Pitch, MinPitch, DiffuserPart.MaxPixels) : DefaultPitch;
		double amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, DiffuserPart.MaxPixels) : 0.0;
		FacetPattern pattern = Enum.IsDefined(Pattern) ? Pattern : FacetPattern.Sawtooth;
		var profile = new double[width + 1];
		double half = width / 2.0;
		for (int i = 0; i <= width; i++)
		{
			double t = (i - half) / pitch;
			t -= Math.Floor(t);
			profile[i] = amplitude * pattern switch
			{
				FacetPattern.SquareWave => t < 0.5 ? -1.0 : 1.0,
				FacetPattern.Sinusoidal => Math.Sin(2.0 * Math.PI * t),
				_ => 2.0 * t - 1.0,
			};
		}
		return profile;
	}

	/// <inheritdoc/>
	public double[] FrontFace()
	{
		double[] profile = Profile();
		double half = Math.Max(1, Length) / 2.0;
		var front = new double[profile.Length];
		for (int i = 0; i < front.Length; i++)
		{
			front[i] = Math.Min(-half + profile[i], half - 1.0);
		}
		return front;
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + Math.Max(0.0, -Profile().Min());

	/// <summary>The polygon of the face, a vertex per pixel, then the back's two corners.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double[] front = FrontFace();
		double halfWidth = Math.Max(2, Width) / 2.0;
		double halfLength = Math.Max(1, Length) / 2.0;
		var vertices = new (double X, double Y)[front.Length + 2];
		for (int i = 0; i < front.Length; i++)
		{
			vertices[i] = (front[i], -halfWidth + i);
		}
		vertices[front.Length] = (halfLength, halfWidth);
		vertices[front.Length + 1] = (halfLength, -halfWidth);
		return vertices;
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Length = Math.Max(1, Length);
		Width = Math.Max(2, Width);
		Pattern = Enum.IsDefined(Pattern) ? Pattern : FacetPattern.Sawtooth;
		Pitch = double.IsFinite(Pitch) ? Math.Clamp(Pitch, MinPitch, DiffuserPart.MaxPixels) : DefaultPitch;
		Amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, DiffuserPart.MaxPixels) : DefaultAmplitude;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<FacettedGratingPart>();
}
