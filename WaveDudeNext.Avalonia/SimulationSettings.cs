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

// Contributors: Vinicius Miguel

using System.Globalization;
using System.Xml.Linq;
using NLog;
using WaveDudeNext.Engine;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>Portable simulation preferences, preserving other settings in the shared XML file. UI thread only.</summary>
internal sealed class SimulationSettings(string path)
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	/// <summary>Whether simulation may use a GPU.</summary>
	public bool UseGpu { get; set; } = true;
	/// <summary>Whether edits automatically restart a stale run.</summary>
	public bool AutoRerun { get; set; } = true;
	/// <summary>Whether runs that spill from RAM show a warning.</summary>
	public bool WarnWhenRamInsufficient { get; set; } = true;
	/// <summary>The UI language code, or an empty string to follow the system language.</summary>
	public string Language { get; set; } = "";
	/// <summary>RAM cache limit as a percentage of physical memory; zero is automatic.</summary>
	public int FrameMemoryPercent { get; set; }
	/// <summary>The stored preview quality.</summary>
	public PreviewQuality Quality { get; set; } = new(1, 1, FrameFormat.Index);

	/// <summary>Reads settings, or chooses the first-start quality from the machine.</summary>
	public static SimulationSettings Load(string path)
	{
		var settings = new SimulationSettings(path);
		if (!File.Exists(path))
		{
			string gpu = "";
			try { gpu = VulkanDeviceInfo.FindAll().FirstOrDefault(device => !device.Software)?.Name ?? ""; }
			catch (Exception ex) { Logger.Debug(ex, "Vulkan device discovery failed while choosing preview quality"); }
			var facts = new MachineFacts(FrameMemory.PhysicalBytes(), Environment.ProcessorCount, gpu, Path.GetPathRoot(FrameCache.Directory) ?? "", StorageDevice.IncursSeekPenalty(FrameCache.Directory));
			PreviewTier tier = PreviewDefaults.TierOf(facts);
			settings.Quality = PreviewDefaults.QualityOf(tier);
			Logger.Info("First-start preview: resolution divisor {Resolution}, frame divisor {Frames}, sample format {Format}; memory {Memory}, threads {Threads}, GPU {Gpu}, cache seek penalty {Seek}", settings.Quality.ResolutionDivisor, settings.Quality.FrameDivisor, settings.Quality.Format, facts.PhysicalBytes, facts.Threads, facts.Gpu, facts.CacheDriveSeekPenalty);
			settings.Save();
			return settings;
		}
		try
		{
			XElement root = XDocument.Load(path).Root ?? throw new InvalidDataException("The settings document has no root.");
			if (root.Name != "Settings") throw new InvalidDataException("The settings document has the wrong root.");
			bool Bool(string name, bool fallback) => bool.TryParse((string?)root.Attribute(name), out bool value) ? value : fallback;
			int Number(string name, int fallback) => int.TryParse((string?)root.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
			int Divisor(string name) { int value = Number(name, 1); return value is 1 or 2 or 4 ? value : 1; }
			settings.UseGpu = Bool("UseGpu", true);
			settings.AutoRerun = Bool("AutoRerun", true);
			settings.WarnWhenRamInsufficient = Bool("WarnWhenRamInsufficient", true);
			string language = ((string?)root.Attribute("Language") ?? "").ToLowerInvariant();
			settings.Language = language is "en" or "de" or "fr" or "nl" ? language : "";
			settings.FrameMemoryPercent = Math.Clamp(Number("FrameMemoryPercent", 0), 0, 100);
			settings.Quality = new PreviewQuality(Divisor("PreviewResolution"), Divisor("PreviewFrames"), Number("PreviewSampleBytes", 1) == 2 ? FrameFormat.Half : FrameFormat.Index);
		}
		catch (Exception ex) { Logger.Error(new UnreadableFileException(path, ex), "The settings file '{Path}' is unreadable, using defaults", path); }
		return settings;
	}

	/// <summary>Writes these preferences while retaining settings owned by other parts of the application.</summary>
	public void Save()
	{
		try
		{
			XDocument document;
			try
			{
				document = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("Settings"));
				if (document.Root?.Name != "Settings") throw new InvalidDataException("The settings document has the wrong root.");
			}
			catch (Exception ex) { Logger.Error(new UnreadableFileException(path, ex), "The settings file '{Path}' is unreadable, writing defaults", path); document = new XDocument(new XElement("Settings")); }
			XElement root = document.Root ?? throw new InvalidDataException("The settings document has no root.");
			root.SetAttributeValue("UseGpu", UseGpu ? "true" : "false");
			root.SetAttributeValue("AutoRerun", AutoRerun ? "true" : "false");
			root.SetAttributeValue("WarnWhenRamInsufficient", WarnWhenRamInsufficient ? "true" : "false");
			root.SetAttributeValue("Language", Language.Length == 0 ? null : Language);
			root.SetAttributeValue("FrameMemoryPercent", FrameMemoryPercent);
			root.SetAttributeValue("PreviewResolution", Quality.ResolutionDivisor);
			root.SetAttributeValue("PreviewFrames", Quality.FrameDivisor);
			root.SetAttributeValue("PreviewSampleBytes", Quality.Format == FrameFormat.Half ? 2 : 1);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			document.Save(path);
		}
		catch (Exception ex) { Logger.Error(ex, "Could not save settings to '{Path}'", path); }
	}
}
