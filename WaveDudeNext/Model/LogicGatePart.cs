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
using WaveDudeNext.Parts;

namespace WaveDudeNext.Model;

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
