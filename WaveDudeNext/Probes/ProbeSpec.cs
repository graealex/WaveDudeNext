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

using WaveDudeNext.Model;

namespace WaveDudeNext.Probes;

/// <summary>
/// What a run needs to know about one probe: an immutable copy of the measurement part of a
/// <see cref="Probe"/> (the graph box and scaling stay in the model). Built on the UI thread by
/// <see cref="From"/>, consumed by <see cref="ProbeSampler.Build"/> on the run's worker. Positions are in
/// canvas pixels, <see cref="StartAfter"/> in steps.
/// </summary>
/// <param name="Name">The probe's name, for labelling its trace.</param>
/// <param name="Kind">Point (A only) or line (A to B).</param>
/// <param name="Measurement">Field amplitude, intensity or intensity integrated over the run.</param>
/// <param name="AX">X of point A in canvas pixels.</param>
/// <param name="AY">Y of point A in canvas pixels.</param>
/// <param name="BX">X of point B in canvas pixels (line probes).</param>
/// <param name="BY">Y of point B in canvas pixels (line probes).</param>
/// <param name="StartAfter">Step from which an integrated probe records; 0 records from the first step.</param>
/// <param name="Origin">The model probe the spec was taken from, as an identity only: the preview finds a probe's trace through it and never reads its values, which may have changed since. Null for a spec not taken from the model.</param>
public sealed record ProbeSpec(
	string Name,
	ProbeKind Kind,
	ProbeMeasurement Measurement,
	int AX,
	int AY,
	int BX,
	int BY,
	int StartAfter,
	Probe? Origin = null)
{
	/// <summary>The spec of a model probe, remembering the probe as its <see cref="Origin"/>.</summary>
	public static ProbeSpec From(Probe probe) =>
		new(probe.Name, probe.Kind, probe.Measurement, probe.AX, probe.AY, probe.BX, probe.BY, probe.StartAfter, probe);
}
