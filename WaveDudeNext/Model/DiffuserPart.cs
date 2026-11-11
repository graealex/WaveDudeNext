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
/// A diffuser (the user's request, 2026-11-07): a rectangular plate of <see cref="Length"/> along the axis and
/// <see cref="Width"/> across whose front face (−x', toward a source on the left) is rough and whose back is flat; the
/// material, the coating and the absorption of a <see cref="ShapePart"/>. The roughness is a sum of sinusoids across the
/// face: one at the <see cref="CenterWavelength"/> with the <see cref="Amplitude"/>, and about it wavelengths spread by
/// <see cref="Sigma"/>, each weighted by a Gaussian of its distance from the centre wavelength (<see cref="Components"/>);
/// the phases are random, drawn from the project's seed and the part's name (<see cref="ISeeded"/>, as a source's jitter
/// is), so a file reproduces its surface. The profile is computed once and kept until a value it depends on changes
/// (<see cref="Profile"/>). Coatings on the front follow the rough face; the sides are the two short ends.
/// </summary>
public sealed class DiffuserPart : ShapePart, ISeeded, IProfiledPlate
{
	/// <summary>Default <see cref="Length"/> in pixels.</summary>
	public const int DefaultLength = 50;
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 500;
	/// <summary>Default <see cref="CenterWavelength"/> in pixels.</summary>
	public const double DefaultWavelength = 16.0;
	/// <summary>Shortest <see cref="CenterWavelength"/> in pixels: two pixels, what the profile's one-pixel sampling can hold.</summary>
	public const double MinWavelength = 2.0;
	/// <summary>Longest <see cref="CenterWavelength"/>, <see cref="Amplitude"/> and <see cref="Sigma"/> in pixels.</summary>
	public const double MaxPixels = 10000.0;
	/// <summary>Default <see cref="Amplitude"/> in pixels.</summary>
	public const double DefaultAmplitude = 1.0;
	/// <summary>Default <see cref="Sigma"/> in pixels.</summary>
	public const double DefaultSigma = 6.0;
	/// <summary>
	/// How far the spread reaches on either side of the centre wavelength, in sigmas, and how many wavelengths lie in each
	/// sigma: components at every half sigma out to three sigmas, 13 in all, the outermost at e^(−4.5) of the centre's amplitude.
	/// </summary>
	public const int SpreadSigmas = 3;
	/// <summary>Components per sigma of the spread (see <see cref="SpreadSigmas"/>).</summary>
	public const int StepsPerSigma = 2;
	/// <summary>Stream selector XOR-ed onto the name hash, so the phases come from another <see cref="Pcg32"/> stream than a source's jitter would.</summary>
	private const ulong RoughnessStream = 0x44494646UL;

	private int _length = DefaultLength;
	private int _width = DefaultWidth;
	private double _centerWavelength = DefaultWavelength;
	private double _amplitude = DefaultAmplitude;
	private double _sigma = DefaultSigma;
	private int _projectSeed;
	private double[]? _profile;
	private (int Width, double Wavelength, double Amplitude, double Sigma, string Name, int Seed) _profileKey;

	/// <summary>A diffuser at the defaults; the readouts follow a rename too, as the phases come from the name (the setter is the base class's).</summary>
	public DiffuserPart()
	{
		PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(Name))
			{
				OnRoughnessChanged();
			}
		};
	}

	/// <inheritdoc/>
	public override string NameStem => "Diffuser";

	/// <summary>
	/// A new diffuser coats its flat back alone (2026-11-07, the user's defaults): the faces' defaults are the
	/// <see cref="CoatedPart"/>'s, every face, so a loaded diffuser keeps what its file says and a new one gets these.
	/// </summary>
	public override void PrepareNew()
	{
		FrontCoated = false;
		BackCoated = true;
		SidesCoated = false;
	}

	/// <summary>Extent along the axis in pixels: the plate's thickness at the mean of its rough face.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLength)]
	[Range(1, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent along the axis in pixels: the plate's thickness at the mean of its rough face.")]
	public int Length
	{
		get => _length;
		set => SetProperty(ref _length, value);
	}

	/// <summary>Extent across the axis in pixels: the rough face's width.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(2, int.MaxValue)]
	[Category("Shape")]
	[Description("Extent across the axis in pixels: the width of the rough face.")]
	public int Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>The spatial wavelength in pixels at the middle of the roughness's spectrum, the one with the full <see cref="Amplitude"/>.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWavelength)]
	[Range(MinWavelength, MaxPixels)]
	[Category("Roughness")]
	[DisplayName("Centre wavelength")]
	[Description("The spatial wavelength in pixels at the middle of the roughness's spectrum: the ripple along the face that has the full amplitude. Two pixels at least.")]
	public double CenterWavelength
	{
		get => _centerWavelength;
		set
		{
			if (SetProperty(ref _centerWavelength, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>The amplitude in pixels of the ripple at the centre wavelength; the other wavelengths get less, by their distance from it, and the face carries their sum.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAmplitude)]
	[Range(0.0, MaxPixels)]
	[Category("Roughness")]
	[Description("The amplitude in pixels of the ripple at the centre wavelength. The other wavelengths of the spread get less, by their distance from the centre, and the face carries the sum of them all; 0 makes the face flat.")]
	public double Amplitude
	{
		get => _amplitude;
		set
		{
			if (SetProperty(ref _amplitude, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>
	/// The spread of the roughness's wavelengths about the centre wavelength, in pixels: a wavelength λ gets the amplitude
	/// times e^(−(λ − centre)² / 2σ²), the bell. 0 leaves the centre wavelength alone, a single ripple; the larger, the more
	/// wavelengths join in and the less regular the face.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(DefaultSigma)]
	[Range(0.0, MaxPixels)]
	[Category("Roughness")]
	[DisplayName("Sigma")]
	[Description("The spread of the roughness's wavelengths about the centre wavelength, in pixels: a wavelength gets the amplitude times the bell e^(−(λ − centre)² / 2σ²). 0 leaves the centre wavelength alone, a single regular ripple; the larger, the more wavelengths join in and the less regular the face.")]
	public double Sigma
	{
		get => _sigma;
		set
		{
			if (SetProperty(ref _sigma, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public int ProjectSeed
	{
		get => _projectSeed;
		set
		{
			if (SetProperty(ref _projectSeed, value))
			{
				OnRoughnessChanged();
			}
		}
	}

	/// <summary>
	/// The wavelengths of the roughness and their amplitudes, in pixels: the centre wavelength at the full amplitude and,
	/// when <see cref="Sigma"/> is above 0, <see cref="SpreadSigmas"/> × <see cref="StepsPerSigma"/> more on either side
	/// of it, a half sigma apart, each at the amplitude times e^(−(λ − centre)² / 2σ²); a wavelength below
	/// <see cref="MinWavelength"/> is left out. Out-of-range values are guarded as the painter guards them. The phases are
	/// drawn per component in this order, so a change of the width or the sigma keeps the phases of the components that stay.
	/// </summary>
	public (double Wavelength, double Amplitude, int Index)[] Components()
	{
		double centre = double.IsFinite(CenterWavelength) ? Math.Clamp(CenterWavelength, MinWavelength, MaxPixels) : DefaultWavelength;
		double amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, MaxPixels) : 0.0;
		double sigma = double.IsFinite(Sigma) ? Math.Clamp(Sigma, 0.0, MaxPixels) : 0.0;
		if (sigma <= 0.0)
		{
			return [(centre, amplitude, 0)];
		}
		var components = new List<(double, double, int)>();
		int reach = SpreadSigmas * StepsPerSigma;
		for (int k = -reach; k <= reach; k++)
		{
			double wavelength = centre + k * sigma / StepsPerSigma;
			if (wavelength < MinWavelength)
			{
				continue;
			}
			double distance = wavelength - centre;
			components.Add((wavelength, amplitude * Math.Exp(-distance * distance / (2.0 * sigma * sigma)), k));
		}
		return [.. components];
	}

	/// <summary>
	/// The rough face's displacement from the plate's mean front plane in pixels, at every whole pixel across the face from
	/// y' = −width / 2 to +width / 2 (<see cref="Width"/> + 1 values, positive into the plate): the sum of the
	/// <see cref="Components"/>, each a sine of 2π y' / λ with a random phase from the project's seed and the part's name
	/// (one <see cref="Pcg32"/> stream per part, the phases drawn in component order). Computed once and kept until the
	/// width, a roughness value, the name or the seed changes (the user's rule, 2026-11-07); the array is never modified,
	/// so a clone may share it. Any thread for a part no other thread edits.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public double[] Profile
	{
		get
		{
			int width = Math.Max(2, Width);
			var key = (width, CenterWavelength, Amplitude, Sigma, Name ?? "", ProjectSeed);
			if (_profile == null || !_profileKey.Equals(key))
			{
				_profile = BuildProfile(width, key.Item5, ProjectSeed, Components());
				_profileKey = key;
			}
			return _profile;
		}
	}

	/// <summary>The displacement of each whole pixel across a face of <paramref name="width"/> pixels from the sum of <paramref name="components"/>, the phases from <paramref name="seed"/> and <paramref name="name"/>.</summary>
	private static double[] BuildProfile(int width, string name, int seed, (double Wavelength, double Amplitude, int Index)[] components)
	{
		// the phases: one draw per possible component in order, so a component keeps its phase whether its neighbours are there or not
		var rng = new Pcg32((ulong)(uint)seed, Pcg32.StableHash(name) ^ RoughnessStream);
		int reach = SpreadSigmas * StepsPerSigma;
		var phases = new double[2 * reach + 1];
		for (int k = 0; k < phases.Length; k++)
		{
			phases[k] = 2.0 * Math.PI * rng.NextDouble();
		}
		var profile = new double[width + 1];
		double half = width / 2.0;
		foreach ((double wavelength, double amplitude, int index) in components)
		{
			double phase = phases[index + reach];
			double k = 2.0 * Math.PI / wavelength;
			for (int i = 0; i <= width; i++)
			{
				profile[i] += amplitude * Math.Sin(k * (i - half) + phase);
			}
		}
		return profile;
	}

	/// <summary>The rough face from its highest point to its lowest, in pixels; what the form shows for the roughness as a whole.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double ProfileHeight
	{
		get
		{
			double[] profile = Profile;
			return profile.Max() - profile.Min();
		}
	}

	/// <summary>How many wavelengths make up the roughness (<see cref="Components"/>).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int ComponentCount => Components().Length;

	/// <summary>Tells the form that the values derived from the roughness follow an edit; the profile itself is rebuilt when next asked for.</summary>
	private void OnRoughnessChanged()
	{
		OnPropertyChanged(nameof(ProfileHeight));
		OnPropertyChanged(nameof(ComponentCount));
	}

	/// <summary>
	/// The front face's x' at every whole pixel across the plate, from y' = −width / 2 to +width / 2: −length / 2 plus the
	/// <see cref="Profile"/>, held at least a pixel before the back face, so the plate is never cut through.
	/// </summary>
	public double[] FrontFace()
	{
		double[] profile = Profile;
		double half = Math.Max(1, Length) / 2.0;
		var front = new double[profile.Length];
		for (int i = 0; i < front.Length; i++)
		{
			front[i] = Math.Min(-half + profile[i], half - 1.0);
		}
		return front;
	}

	/// <inheritdoc/>
	public override double AxisReach => Math.Max(1, Length) / 2.0 + Math.Max(0.0, -Profile.Min());

	/// <summary>The outline: down the rough front face, pixel by pixel, then the two back corners.</summary>
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
		CenterWavelength = double.IsFinite(CenterWavelength) ? Math.Clamp(CenterWavelength, MinWavelength, MaxPixels) : DefaultWavelength;
		Amplitude = double.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0.0, MaxPixels) : DefaultAmplitude;
		Sigma = double.IsFinite(Sigma) ? Math.Clamp(Sigma, 0.0, MaxPixels) : DefaultSigma;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<DiffuserPart>();
}
