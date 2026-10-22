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

using NLog;

namespace WaveDudeNext.Parts;

/// <summary>
/// The layout of a logic gate (<see cref="Model.LogicGatePart"/>) designed for one wavelength and one phase shift: two
/// single-mode waveguides, input A above (−y') and input B below (+y'), <see cref="PortSpacing"/> apart; a phase section
/// whose core is <see cref="PhaseIndexStep"/> higher or lower over <see cref="PhaseLength"/>, within a stretch of a quarter
/// period's length (<see cref="PhaseRegionLength"/>) whatever the shift, so every gate kind has the same dimensions;
/// smooth S-bends (<see cref="BendShare"/>) that bring the arms <see cref="CoupledSpacing"/> apart; a 3 dB directional
/// coupler; S-bends back out; straight outputs, where a switched-off output's absorber sits. Both ends are open, so gates
/// cascade; the anti-reflection bands on them belong to the part, not the design (they do not change the layout).
/// A coupler of total coupling ∫κ dz = π/4 sends (a + i·b) / √2 to the upper output and (i·a + b) / √2 to the lower; with
/// b a quarter period ahead (b → i·b) the upper output carries (a − b) / √2, the XOR of two equal coherent inputs, and
/// the lower i·(a + b) / √2, their sum; with b a quarter period behind the two change places (AND on top), and with b 30°
/// ahead the waves meet at 120° on top (OR). κ = (β_even − β_odd) / 2 comes from <see cref="WaveguideModes"/> at every
/// spacing, so the bends' share of the coupling is counted and the straight coupler takes the rest.
/// All lengths are pixels along the axis x', the part frame's origin at the gate's centre. Immutable; any thread.
/// </summary>
public sealed class LogicGateDesign
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>How much higher the phase section's core index is than the core's.</summary>
	public const double PhaseIndexStep = 0.1;

	/// <summary>
	/// The bend's radiation exponent: the smallest radius R of the S-bends makes (2/3)·γ³R / β² this large, the exponent of
	/// a bent guide's radiation loss (Marcuse), γ being the mode's decay constant in the cladding and β its propagation
	/// constant. So a guide that holds its mode tighter bends tighter. Measured on a core of 1.6 in 1.45 at λ 16
	/// (2026-10-21): an exponent of 5.6 (R = 30 λ) let the bends radiate about 7 % into the other arm, 11 (60 λ) left a floor
	/// of 1.3 % at the XOR output.
	/// </summary>
	public const double BendLossExponent = 11.0;

	/// <summary>
	/// The narrowest absorbing band outside the clear cladding, in wavelengths. Measured on the default gate (2026-10-22, both
	/// inputs on): bands of 64, 32, 16 and 8 px left 2.1, 3.4, 1.5 and 3.1 % at the XOR output, none 7.6 % (the cladding
	/// then guides the stray light to the outputs); half a wavelength keeps a margin over the quarter that still worked.
	/// </summary>
	public const double MinAbsorber = 0.5;

	/// <summary>The absorption (blue) at the rim of the cladding's absorbing band; a strength that reflected little in the measurements of OpticalParts.md 6a.</summary>
	public const int Absorption = 64;

	/// <summary>
	/// The largest share of the coupler's π/4 the two S-bends may give; the gap is widened until the straight coupler carries
	/// the rest. Measured on the default gate (2026-10-21, bends of 60 λ): at 0.6 (gap 12 px, 1161 px long) the XOR output
	/// kept 2.3 % of one input's power with both inputs on, at 0.25 (gap 16 px, 1621 px long) 1.3 %. The light the bends
	/// radiate into the other arm, not the coupling model, sets that floor: the solver's κ matched the straight coupler to 1 %.
	/// 0.6, the shorter gate, is the user's choice (2026-10-21).
	/// </summary>
	public const double BendShareLimit = 0.6;

	/// <summary>
	/// What the simulated couplers coupled beyond the model's ∫κ dz, in radians, taken off the straight coupler: 0.06 to 0.08
	/// on gates with cores of 1.6 to 2.2 in 1.45 at λ 16 (2026-10-21, one input split 43/57 instead of 50/50); with it
	/// taken off, the split came out 50/51 and the XOR output's floor fell from 4.2 % to 2.9 % (core 2.0). It held with the
	/// output taper and at λ 32 (cores of 2.0 and 2.55: without it 42/58 and 2.3 to 2.5 %, with it 48/52 and 1.1 to 1.4 %).
	/// </summary>
	public const double CouplingExcess = 0.07;

	/// <summary>Points per S-bend at which the coupling is evaluated.</summary>
	private const int BendSamples = 24;

	private static readonly Dictionary<(double, double, double, int, double), LogicGateDesign?> Cache = [];
	private static readonly Lock CacheGate = new();

	private LogicGateDesign()
	{
	}

	/// <summary>The design wavelength in pixels (in a medium of index 1).</summary>
	public double Wavelength { get; private init; }
	/// <summary>Refractive index of the cores.</summary>
	public double CoreIndex { get; private init; }
	/// <summary>Refractive index of the cladding around the cores.</summary>
	public double CladdingIndex { get; private init; }
	/// <summary>Width of each core in pixels: nine tenths of the single-mode cut-off λ / (2 NA), at least 2.</summary>
	public int CoreWidth { get; private init; }
	/// <summary>Thickness of the clear cladding around each core in pixels: three decay lengths of the mode.</summary>
	public int CladdingThickness { get; private init; }
	/// <summary>
	/// Thickness in pixels of the absorbing band outside the clear cladding (cladding index, absorption rising to the rim):
	/// whatever is left out to half the port spacing past the ports, at least <see cref="MinAbsorber"/> wavelengths.
	/// </summary>
	public double AbsorberWidth => PortSpacing / 2.0 - CoreWidth / 2.0 - CladdingThickness;
	/// <summary>Distance between the cores' centres at the ports in pixels, even.</summary>
	public int PortSpacing { get; private init; }
	/// <summary>Distance between the cores' centres in the coupler in pixels, even.</summary>
	public int CoupledSpacing { get; private init; }
	/// <summary>Gap between the cores in the coupler in pixels.</summary>
	public int Gap => CoupledSpacing - CoreWidth;
	/// <summary>
	/// Length of the straight input stretch before the port in pixels, half a wavelength. The input end is open, coated like
	/// the output end, so light from a fibre or a gate before it comes in (the user's choice, 2026-10-22, which removed an
	/// absorbing termination that took the light arriving from a gate before); the sources are to be coupled in from outside.
	/// </summary>
	public int InputLength { get; private init; }
	/// <summary>Length of the straight stretches from the port to the phase section and from it to the bend in pixels, a wavelength each.</summary>
	public int LeadLength { get; private init; }
	/// <summary>Length of the stretch that holds the phase section in pixels: what a quarter-period shift needs, for every gate kind.</summary>
	public int PhaseRegionLength { get; private init; }
	/// <summary>Length of the phase section in pixels, from the start of its stretch: the shift's share of a quarter period.</summary>
	public int PhaseLength { get; private init; }
	/// <summary>How far wave B leads wave A into the coupler, in degrees.</summary>
	public double PhaseShift { get; private init; }
	/// <summary>Length of each S-bend in pixels.</summary>
	public int BendLength { get; private init; }
	/// <summary>Length of the straight coupler in pixels.</summary>
	public int CouplerLength { get; private init; }
	/// <summary>
	/// Length of the straight output stretch in pixels, three wavelengths: the output ports a half wavelength in, then the
	/// <see cref="OutputRampLength"/> where a switched-off output's absorber rises, up to the coated end.
	/// </summary>
	public int OutputLength { get; private init; }
	/// <summary>Length in pixels over which a switched-off output's absorber rises to its full strength at the right end, two wavelengths (a soft rise reflects little, OpticalParts.md 6a; measured on the default gate, 2026-10-22: 0.03 % of the power came back, nothing left past the end).</summary>
	public int OutputRampLength { get; private init; }
	/// <summary>
	/// Whether the phase section sits on arm A rather than arm B: on the arm to be put ahead, raised by
	/// <see cref="PhaseIndexStep"/>, or, for a core within the step of the largest index, on the other arm, lowered.
	/// </summary>
	public bool PhaseOnArmA { get; private init; }
	/// <summary>Refractive index of the phase section's core.</summary>
	public double PhaseIndex { get; private init; }
	/// <summary>Propagation constant of a single guide in radians per pixel.</summary>
	public double Beta { get; private init; }
	/// <summary>Coupling constant κ in the straight coupler in radians per pixel.</summary>
	public double Kappa { get; private init; }
	/// <summary>The coupling both S-bends add, ∫κ dz in radians.</summary>
	public double BendCoupling { get; private init; }

	/// <summary>Total length along the axis in pixels.</summary>
	public int Length => InputLength + LeadLength + PhaseRegionLength + LeadLength + 2 * BendLength + CouplerLength + OutputLength;

	/// <summary>Where the input arms start, x' of the left end.</summary>
	public double Left => -Length / 2.0;

	/// <summary>x' of the input ports, half a wavelength in from the open input end: where a source placed in the unit drives an arm.</summary>
	public double InputX => Left + InputLength;

	/// <summary>x' where a switched-off output's absorber starts to rise.</summary>
	public double OutputRampStart => Length / 2.0 - OutputRampLength;

	/// <summary>x' of the output ports, a half wavelength into the straight output stretch.</summary>
	public double OutputX => Length / 2.0 - OutputLength + Math.Round(Wavelength / 2.0);

	/// <summary>The thickness in pixels of a quarter-wave anti-reflection band on the core, λ / (4 √n).</summary>
	public double QuarterWaveCoating => Wavelength / (4.0 * Math.Sqrt(CoreIndex));

	/// <summary>Where the phase section starts and ends along x'.</summary>
	public (double Start, double End) PhaseSection => (InputX + LeadLength, InputX + LeadLength + PhaseLength);

	/// <summary>
	/// The S-bend's offset profile from the far end (t = 0, share 0) to the near end (t = 1, share 1): t − sin(2πt) / 2π,
	/// whose slope and curvature are both 0 at the ends, so the bend joins the straight stretches without a kink in
	/// direction or in curvature (the user's point, 2026-10-21: bends must be smooth). Its curvature peaks at
	/// 2π·Δ / L² in the middle third, Δ being the offset and L the length.
	/// </summary>
	public static double BendShare(double t) => t - Math.Sin(2.0 * Math.PI * t) / (2.0 * Math.PI);

	/// <summary>
	/// Half the outer width of the gate across the axis, at the ports: the port spacing itself, so the unit is twice the
	/// port spacing wide with its ports at a quarter and three quarters of its width (the user's request, 2026-10-22). Gates
	/// then tile in staggered columns: a gate's outputs meet one input each of two gates set a port spacing above and below
	/// it, which touch without overlapping.
	/// </summary>
	public double HalfWidth => PortSpacing;

	/// <summary>
	/// The design for the given inputs, from a cache shared by every gate with the same values; null when the core
	/// guides nothing at this wavelength (a core index not above the cladding's).
	/// </summary>
	public static LogicGateDesign? For(double wavelength, double coreIndex, double claddingIndex, int portSpacing, double phaseShift)
	{
		var key = (wavelength, coreIndex, claddingIndex, portSpacing, phaseShift);
		lock (CacheGate)
		{
			if (Cache.TryGetValue(key, out LogicGateDesign? cached))
			{
				return cached;
			}
		}
		LogicGateDesign? design = Create(wavelength, coreIndex, claddingIndex, portSpacing, phaseShift);
		lock (CacheGate)
		{
			if (Cache.Count > 64)
			{
				Cache.Clear();
			}
			Cache[key] = design;
		}
		return design;
	}

	/// <summary>The distance of each arm's centre from the axis at <paramref name="x"/>: half the port spacing at the ports, half the coupled spacing in the coupler, the smooth S-bend (<see cref="BendShare"/>) between.</summary>
	public double ArmOffset(double x)
	{
		(double offset, _) = ArmOffsetAndSlope(x);
		return offset;
	}

	/// <summary>The arm's offset from the axis at <paramref name="x"/> and its slope d(offset)/dx.</summary>
	public (double Offset, double Slope) ArmOffsetAndSlope(double x)
	{
		double far = PortSpacing / 2.0;
		double near = CoupledSpacing / 2.0;
		double bendIn = InputX + 2 * LeadLength + PhaseRegionLength;
		double couplerStart = bendIn + BendLength;
		double bendOut = couplerStart + CouplerLength;
		if (x <= bendIn || x >= bendOut + BendLength)
		{
			return (far, 0.0);
		}
		if (x >= couplerStart && x <= bendOut)
		{
			return (near, 0.0);
		}
		// along the bend from the far end (t = 0) to the near end (t = 1)
		double t = x < couplerStart ? (x - bendIn) / BendLength : 1.0 - (x - bendOut) / BendLength;
		double sign = x < couplerStart ? 1.0 : -1.0;
		double offset = far - (far - near) * BendShare(t);
		double slope = -sign * (far - near) * (1.0 - Math.Cos(2.0 * Math.PI * t)) / BendLength;
		return (offset, slope);
	}

	private static LogicGateDesign? Create(double wavelength, double coreIndex, double claddingIndex, int portSpacing, double phaseShift)
	{
		if (!(wavelength > 0.0) || !(coreIndex > claddingIndex))
		{
			Logger.Debug("Logic gate: no guided mode for λ {Wavelength} px, core {Core}, cladding {Cladding}", wavelength, coreIndex, claddingIndex);
			return null;
		}
		double omega = WaveguideModes.Omega(wavelength);
		// single mode: the slab's cut-off of the second mode is at w = λ / (2 NA); stay at nine tenths of it, in whole pixels
		double aperture = Math.Sqrt(coreIndex * coreIndex - claddingIndex * claddingIndex);
		int width = Math.Max(2, (int)Math.Floor(0.9 * wavelength / (2.0 * aperture)));
		double? single = SingleBeta(wavelength, omega, coreIndex, claddingIndex, width, 0);
		if (single is not { } beta)
		{
			Logger.Debug("Logic gate: a {Width} px core guides nothing at λ {Wavelength} px", width, wavelength);
			return null;
		}
		// the cladding reaches three decay lengths past the core: the field decays as e^(−γ|y|), γ² ≈ μ − Ω n_cl² for slow decay
		double mu = Mu(beta);
		double gamma = Math.Sqrt(Math.Max(1e-9, mu - omega * claddingIndex * claddingIndex));
		int cladding = Math.Max(2, (int)Math.Ceiling(3.0 / gamma));
		int absorber = Math.Max(2, (int)Math.Ceiling(MinAbsorber * wavelength));
		// the modes see the cladding index out to the narrowest absorbing band's rim (its damping is left out: the field there is
		// e^-3 of the core's); a wider band, from a wider port spacing, changes them by nothing that counts
		int outer = cladding + absorber;
		// the phase section: the leading arm's core raised by the step, or, where that would pass the largest index, the other
		// arm's lowered by it; either way the leading wave gains the shift on the other
		bool lowered = coreIndex + PhaseIndexStep > Model.OpticalPart.MaxIndex + 1e-9;
		bool bLeads = phaseShift >= 0.0;
		bool phaseOnA = bLeads == lowered;
		double phaseIndex = Math.Round(lowered ? coreIndex - PhaseIndexStep : coreIndex + PhaseIndexStep, 2);
		double singleBeta = SingleBeta(wavelength, omega, coreIndex, claddingIndex, width, outer) ?? beta;
		double phaseBeta = phaseIndex > claddingIndex ? SingleBeta(wavelength, omega, phaseIndex, claddingIndex, width, outer) ?? singleBeta : singleBeta;
		double phaseStep = Math.Abs(phaseBeta - singleBeta);
		if (phaseStep < 1e-6)
		{
			Logger.Debug("Logic gate: a phase section of index {Index} shifts nothing at λ {Wavelength} px", phaseIndex, wavelength);
			return null;
		}
		int regionLength = Math.Max(1, (int)Math.Round(Math.PI / 2.0 / phaseStep));
		int phaseLength = Math.Min(regionLength, (int)Math.Round(Math.Abs(phaseShift) * Math.PI / 180.0 / phaseStep));
		double radius = BendLossExponent * 1.5 * singleBeta * singleBeta / (gamma * gamma * gamma);
		// the gap: from one decay length up, in steps of two pixels (the coupled spacing stays even), until the bends' share of
		// the coupling is at most BendShareLimit; the straight coupler, whose κ the solver gives exactly, takes the rest
		int gap = Math.Max(2, (int)Math.Round(1.0 / gamma));
		gap += (width + gap) % 2;
		int coupled, spacing, bendLength;
		double kappa, bend;
		while (true)
		{
			coupled = width + gap;
			// the arms' claddings apart at the ports, and room for the narrowest absorbing band out to half the spacing past them
			spacing = Math.Max(portSpacing, Math.Max(coupled + width + 2 * cladding + 2, (int)Math.Ceiling(width + 2.0 * (cladding + absorber))));
			spacing += spacing % 2;
			// the S-bend's smallest radius L² / (2π Δ) is the radius above
			bendLength = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(2.0 * Math.PI * radius * (spacing - coupled) / 2.0)));
			kappa = KappaAt(coupled, omega, coreIndex, claddingIndex, width, outer);
			bend = BendKappa(coupled, spacing, bendLength, omega, coreIndex, claddingIndex, width, outer);
			if (2.0 * bend <= BendShareLimit * Math.PI / 4.0 || gap >= 2 * cladding)
			{
				break;
			}
			gap += 2;
		}
		int couplerLength = kappa > 0.0 ? Math.Max(0, (int)Math.Round((Math.PI / 4.0 - CouplingExcess - 2.0 * bend) / kappa)) : 0;
		Logger.Debug("Logic gate for λ {Wavelength} px, shift {Shift}°: core {Width} px, cladding {Cladding} px, gap {Gap} px, β {Beta:0.00000}, κ {Kappa:0.00000}/px, bends add {Bend:0.000} rad, coupler {Coupler} px, phase section {Phase} of {Region} px on arm {Arm}, bends {BendLength} px of radius {Radius:0} px",
			wavelength, phaseShift, width, cladding, gap, singleBeta, kappa, 2.0 * bend, couplerLength, phaseLength, regionLength, phaseOnA ? "A" : "B", bendLength, radius);
		return new LogicGateDesign
		{
			Wavelength = wavelength,
			CoreIndex = coreIndex,
			CladdingIndex = claddingIndex,
			CoreWidth = width,
			CladdingThickness = cladding,
			PortSpacing = spacing,
			CoupledSpacing = coupled,
			InputLength = (int)Math.Round(wavelength / 2.0),
			LeadLength = (int)Math.Round(wavelength),
			PhaseRegionLength = regionLength,
			PhaseLength = phaseLength,
			PhaseShift = phaseShift,
			BendLength = bendLength,
			CouplerLength = couplerLength,
			OutputLength = (int)Math.Round(3.0 * wavelength),
			OutputRampLength = (int)Math.Round(2.0 * wavelength),
			PhaseOnArmA = phaseOnA,
			PhaseIndex = phaseIndex,
			Beta = singleBeta,
			Kappa = kappa,
			BendCoupling = 2.0 * bend,
		};
	}

	/// <summary>The coupling one S-bend adds, ∫κ dz over its spacing profile from <paramref name="spacing"/> to <paramref name="coupled"/> (Simpson).</summary>
	private static double BendKappa(int coupled, int spacing, int bendLength, double omega, double coreIndex, double claddingIndex, int width, int outer)
	{
		double sum = 0.0;
		for (int i = 0; i <= BendSamples; i++)
		{
			double t = (double)i / BendSamples;
			double distance = spacing - (spacing - coupled) * BendShare(t);
			double weight = i == 0 || i == BendSamples ? 1.0 : i % 2 == 1 ? 4.0 : 2.0;
			sum += weight * KappaAt(distance, omega, coreIndex, claddingIndex, width, outer);
		}
		return sum * bendLength / (3.0 * BendSamples);
	}

	/// <summary>The longitudinal stencil term μ(β) = cos(2β) / 6 − 8 cos(β) / 3 + 5 / 2.</summary>
	private static double Mu(double beta) => Math.Cos(2.0 * beta) / 6.0 - 8.0 * Math.Cos(beta) / 3.0 + 2.5;

	/// <summary>β of one core of <paramref name="width"/> and <paramref name="coreIndex"/> in its cladding (a band of the cladding index wide enough when <paramref name="cladding"/> is 0), index 1 beyond.</summary>
	private static double? SingleBeta(double wavelength, double omega, double coreIndex, double claddingIndex, int width, int cladding)
	{
		double reach = cladding > 0 ? cladding : 2.0 * wavelength;
		int half = (int)Math.Ceiling(width / 2.0 + reach + 8);
		double[] rows = WaveguideModes.Rows(-half, 2 * half, y =>
			Math.Abs(y) <= width / 2.0 ? coreIndex : Math.Abs(y) <= width / 2.0 + reach ? claddingIndex : 1.0);
		return WaveguideModes.Beta(rows, omega, false);
	}

	/// <summary>κ = (β_even − β_odd) / 2 of the two cores <paramref name="distance"/> apart (centre to centre), each in its cladding, index 1 beyond.</summary>
	private static double KappaAt(double distance, double omega, double coreIndex, double claddingIndex, int width, int cladding)
	{
		double core = width / 2.0;
		double outer = core + cladding;
		int half = (int)Math.Ceiling(distance / 2.0 + outer + 8);
		double[] rows = WaveguideModes.Rows(-half, 2 * half, y =>
		{
			double nearest = Math.Min(Math.Abs(y - distance / 2.0), Math.Abs(y + distance / 2.0));
			return nearest <= core ? coreIndex : nearest <= outer ? claddingIndex : 1.0;
		});
		double? even = WaveguideModes.Beta(rows, omega, false);
		double? odd = WaveguideModes.Beta(rows, omega, true);
		return even is { } e && odd is { } o ? Math.Max(0.0, (e - o) / 2.0) : 0.0;
	}
}
