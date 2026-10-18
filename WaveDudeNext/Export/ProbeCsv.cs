// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Globalization;
using WaveDudeNext.Model;
using WaveDudeNext.Probes;

namespace WaveDudeNext.Export;

/// <summary>
/// Writes a probe's recording as a CSV file (2026-10-18, the user's format: a semicolon between columns, a
/// decimal point, a header row, nothing else). A point probe gives one row per simulation step,
/// <c>step;&lt;measurement&gt;</c>, the step numbered like the readout (0-based); a line probe one row per
/// recorded frame, <c>frame;step</c> and one column per sample along the line, headed by the sample's
/// distance from A in canvas pixels. Values are written in round-trip precision, invariant culture. Any
/// thread, as long as the trace is not being filled meanwhile (a completed run's is not).
/// </summary>
public static class ProbeCsv
{
	/// <summary>The column separator.</summary>
	public const string Separator = ";";

	/// <summary>The file extension.</summary>
	public const string Extension = ".csv";

	/// <summary>
	/// The file name proposed for a probe: the project's name, an underscore and the probe's name, characters a
	/// file name cannot hold replaced by underscores, with the extension.
	/// </summary>
	/// <param name="projectName">The project's name for a file.</param>
	/// <param name="probe">The probe.</param>
	public static string FileName(string projectName, Probe probe)
	{
		char[] name = probe.Name.Trim().ToCharArray();
		char[] invalid = Path.GetInvalidFileNameChars();
		for (int i = 0; i < name.Length; i++)
		{
			if (Array.IndexOf(invalid, name[i]) >= 0)
			{
				name[i] = '_';
			}
		}
		string probeName = new string(name);
		return projectName + "_" + (probeName.Length == 0 ? "probe" : probeName) + Extension;
	}

	/// <summary>Writes the recording of <paramref name="probe"/> to the file at <paramref name="path"/> (replaced when it exists); returns the number of data rows.</summary>
	/// <param name="path">The file to write.</param>
	/// <param name="probe">The probe, for its kind and measurement.</param>
	/// <param name="trace">The probe's recording from the run.</param>
	/// <param name="visualizeEvery">Steps per timeline frame of the run.</param>
	/// <param name="frameStride">Timeline frames per recorded frame of the run (1 for a full run).</param>
	public static int Write(string path, Probe probe, ProbeTrace trace, int visualizeEvery, int frameStride)
	{
		using var writer = new StreamWriter(path);
		return Write(writer, probe, trace, visualizeEvery, frameStride);
	}

	/// <summary>Writes the recording of <paramref name="probe"/> to <paramref name="writer"/>; returns the number of data rows.</summary>
	/// <param name="writer">Destination; left open.</param>
	/// <param name="probe">The probe, for its kind and measurement.</param>
	/// <param name="trace">The probe's recording from the run.</param>
	/// <param name="visualizeEvery">Steps per timeline frame of the run.</param>
	/// <param name="frameStride">Timeline frames per recorded frame of the run (1 for a full run).</param>
	public static int Write(TextWriter writer, Probe probe, ProbeTrace trace, int visualizeEvery, int frameStride)
	{
		CultureInfo culture = CultureInfo.InvariantCulture;
		if (probe.Kind == ProbeKind.Point)
		{
			writer.Write("step");
			writer.Write(Separator);
			writer.WriteLine(ColumnName(probe.Measurement));
			ReadOnlySpan<float> values = trace.Series.Values.AsSpan(0, trace.Series.Count);
			for (int i = 0; i < values.Length; i++)
			{
				writer.Write(i.ToString(culture));
				writer.Write(Separator);
				writer.WriteLine(values[i].ToString(culture));
			}
			return values.Length;
		}
		IReadOnlyList<float[]> profiles = trace.Profiles;
		int samples = profiles.Count == 0 ? 0 : profiles[0].Length;
		writer.Write("frame");
		writer.Write(Separator);
		writer.Write("step");
		for (int k = 0; k < samples; k++)
		{
			writer.Write(Separator);
			double distance = samples > 1 ? k * trace.ProfileLength / (samples - 1) : 0;
			writer.Write(distance.ToString("0.###", culture));
		}
		writer.WriteLine();
		for (int p = 0; p < profiles.Count; p++)
		{
			int frame = p * Math.Max(1, frameStride);
			writer.Write(frame.ToString(culture));
			writer.Write(Separator);
			writer.Write((frame * Math.Max(1, visualizeEvery)).ToString(culture));
			foreach (float value in profiles[p])
			{
				writer.Write(Separator);
				writer.Write(value.ToString(culture));
			}
			writer.WriteLine();
		}
		return profiles.Count;
	}

	/// <summary>The value column's head for a measurement, in plain English (the file is data, not UI).</summary>
	private static string ColumnName(ProbeMeasurement measurement) => measurement switch
	{
		ProbeMeasurement.Amplitude => "amplitude",
		ProbeMeasurement.Value => "value",
		ProbeMeasurement.IntegratedValue => "integrated value",
		_ => measurement.ToString().ToLowerInvariant(),
	};
}
