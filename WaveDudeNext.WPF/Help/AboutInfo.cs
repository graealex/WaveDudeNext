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


using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Documents;
using System.Windows.Markup;
using WaveDudeNext.Engine;
using WaveDudeNext.Export;
using WaveDudeNext.Runs;

namespace WaveDudeNext.WPF.Help;

/// <summary>
/// What the About dialog shows and what a bug report needs: the product name, version and copyright from the assembly
/// attributes (set in <c>Directory.Build.props</c> and the project file), the license text embedded from
/// <c>LICENSE.md</c>, and the system facts (runtime, OS, memory, cores, the OpenCL devices, the H.264 encoders).
/// Thread-free; <see cref="SystemReport"/> enumerates devices and encoders, a few milliseconds, call it once.
/// </summary>
public static class AboutInfo
{
	private static readonly Assembly Assembly = typeof(AboutInfo).Assembly;

	/// <summary>The product name of the assembly ("WaveDude Next").</summary>
	public static string Product { get; } = Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "WaveDude Next";

	/// <summary>The copyright line of the assembly.</summary>
	public static string Copyright { get; } = Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

	/// <summary>
	/// The version for people: "0.1.0 (05e5865)", the informational version with the SDK's commit hash shortened
	/// to seven characters in brackets; without a hash, the plain version.
	/// </summary>
	public static string Version { get; } = FormatVersion(
		Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? Assembly.GetName().Version?.ToString(3) ?? "");

	/// <summary>Splits "0.1.0+05e58652c8..." into "0.1.0 (05e5865)"; a hash of seven characters or fewer stays as it is.</summary>
	public static string FormatVersion(string informational)
	{
		int plus = informational.IndexOf('+');
		if (plus < 0)
		{
			return informational;
		}
		string hash = informational[(plus + 1)..];
		if (hash.Length > 7)
		{
			hash = hash[..7];
		}
		return hash.Length == 0 ? informational[..plus] : $"{informational[..plus]} ({hash})";
	}

	/// <summary>
	/// LICENSE.md as a <see cref="FlowDocument"/>: the build turns the Markdown into the embedded resource
	/// <c>License.xaml</c> (<c>Build/LicenseDocument.targets</c>), read here with <see cref="XamlReader"/>. A build without
	/// the resource gets a one-paragraph document pointing at gnu.org. UI thread (the document is a WPF object).
	/// </summary>
	public static FlowDocument LicenseDocument()
	{
		using Stream? stream = Assembly.GetManifestResourceStream("License.xaml");
		if (stream != null)
		{
			return (FlowDocument)XamlReader.Load(stream);
		}
		var document = new FlowDocument();
		document.Blocks.Add(new Paragraph(new Run("LICENSE.md is not embedded in this build. See https://www.gnu.org/licenses/gpl-3.0.html")));
		return document;
	}

	/// <summary>
	/// The system facts as "label: value" lines, the labels looked up through <paramref name="label"/> (a string table
	/// key to its text). <paramref name="gpuSetting"/> is the application's GPU switch. Devices and encoders are
	/// enumerated here; a missing OpenCL or Media Foundation is reported as a line, never thrown.
	/// </summary>
	public static string SystemReport(bool gpuSetting, Func<string, string> label)
	{
		var sb = new StringBuilder();
		void Line(string key, string value) => sb.Append(label(key)).Append(": ").AppendLine(value);
		Line("AboutProductLine", $"{Product} {Version}");
		Line("AboutRuntime", $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
		Line("AboutOperatingSystem", $"{RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}");
		Line("AboutProcessor", $"{Environment.ProcessorCount} {label("AboutCores")}");
		Line("AboutMemory", $"{FrameMemory.Gigabytes(FrameMemory.AvailableBytes())} {label("AboutMemoryFree")} {FrameMemory.Gigabytes(FrameMemory.PhysicalBytes())}");
		Line("AboutGpuSetting", label(gpuSetting ? "AboutOn" : "AboutOff"));
		Line("AboutOpenClDevices", Devices(label));
		Line("AboutH264Encoders", Encoders(label));
		return sb.ToString().TrimEnd();
	}

	private static string Devices(Func<string, string> label)
	{
		try
		{
			List<GpuDeviceInfo> devices = GpuDeviceInfo.FindAll();
			return devices.Count == 0
				? label("AboutNone")
				: string.Join("; ", devices.Select(d => $"{d.Name} ({d.Vendor}, {FrameMemory.Gigabytes(d.GlobalMemory)})"));
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			return label("AboutOpenClMissing");
		}
	}

	private static string Encoders(Func<string, string> label)
	{
		if (!Mp4Writer.IsAvailable())
		{
			return label("AboutMediaFoundationMissing");
		}
		IReadOnlyList<string> encoders = Mp4Writer.AvailableEncoders(hardwareOnly: false);
		return encoders.Count == 0 ? label("AboutNone") : string.Join("; ", encoders);
	}
}
