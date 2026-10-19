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

using System.Globalization;
using System.Text.Json;
using NLog;

namespace WaveDudeNext.Model;

/// <summary>
/// Reads a project of the author's program (a <c>configuration.json</c> in a project folder) into a
/// <see cref="Project"/> (2026-10-14; the user promised the author an importer). The mapping follows his
/// program's readers key for key,
/// including their fallbacks for keys a file may lack, so an old project keeps its exact behaviour:
/// <list type="bullet">
/// <item><description>scene: mode, size, damping (enabled + size → <see cref="Scene.DampingEdge"/>, 0 when off), image sources, object sources, refractive index. A scene image given by a path relative to the folder is embedded when the file exists (<see cref="Scene.ImageData"/>), else the path is kept and noted.</description></item>
/// <item><description>simulation: steps, interval, solver (4th-order Laplacian, wave speed, time decay, frequency scale, intensity scale); the preview/export flags, schemes, brightness and black levels go to <see cref="Display"/>, the video settings to <see cref="Video"/>. The GPU flag is ignored (an application setting here).</description></item>
/// <item><description>object sources: one <see cref="Source"/> each; a composite one gets a <see cref="SourceArray"/>, with the random amounts only when the composite mode is "randomize" and the flag is on. The stored emitter elements are not read: they are regenerated from the project seed, so a randomised array differs from the original (noted). A source switched off is imported disabled; one whose position was never picked (<c>has_position</c> false) stays enabled at its x/y, as his program ran it (noted).</description></item>
/// <item><description>probes: one <see cref="Probe"/> each, named "Probe n"; "start after step" is 0 unless its flag is on; a scene-level <c>use_probes</c> of false deactivates them all.</description></item>
/// </list>
/// The project gets a fresh seed and the file's description. Thread-free; the caller owns the result.
/// </summary>
public static class LegacyProject
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>File name of a legacy project's configuration inside its folder.</summary>
	public const string FileName = "configuration.json";

	/// <summary>Extension of the files this importer reads.</summary>
	public const string Extension = ".json";

	/// <summary>Newest <c>format_version</c> this importer understands.</summary>
	public const int SupportedFormatVersion = 1;

	/// <summary>Whether <paramref name="path"/> names a legacy project (by its extension; the author's file is always <see cref="FileName"/>).</summary>
	public static bool IsLegacyFile(string path) => string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

	/// <summary>The <c>.wdproj</c> name proposed for an imported project: the legacy project's name (or its folder's) beside the configuration file.</summary>
	public static string SuggestedFileName(string path, string projectName)
	{
		string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
		string name = projectName.Length > 0 ? projectName : Path.GetFileName(folder);
		foreach (char invalid in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(invalid, '_');
		}
		return Path.Combine(folder, (name.Length > 0 ? name : "project") + ".wdproj");
	}

	/// <summary>
	/// Imports the legacy project at <paramref name="path"/>.
	/// </summary>
	/// <param name="path">The <c>configuration.json</c>; a scene image path is resolved against its folder.</param>
	/// <param name="notes">Receives what could not be carried over exactly (a missing image, a regenerated random array, a disabled source); may be null.</param>
	/// <param name="projectName">Receives the file's project name, "" when it has none.</param>
	/// <exception cref="InvalidDataException">The file is not a legacy project, or of a newer format.</exception>
	public static Project Import(string path, IList<string>? notes, out string projectName)
	{
		path = Path.GetFullPath(path);
		string folder = Path.GetDirectoryName(path) ?? "";
		JsonElement root;
		using JsonDocument document = Parse(path);
		root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException($"The file {Path.GetFileName(path)} is not a project configuration (a JSON object was expected).");
		}
		int version = Int(root, "format_version", SupportedFormatVersion);
		if (version > SupportedFormatVersion)
		{
			throw new InvalidDataException($"The file {Path.GetFileName(path)} was made by a newer version of the program (format {version}).");
		}
		projectName = Text(root, "project_name", "");
		var project = new Project { Description = Text(root, "description", "") };
		JsonElement? scene = Section(root, "scene");
		ImportScene(scene, project.Scene, folder, notes);
		ImportSimulation(Section(root, "simulation"), project);
		ImportSources(Section(root, "object_sources"), project, notes);
		ImportProbes(Section(root, "probes"), project, scene, notes);
		project.Normalize();
		Logger.Info("Imported legacy project '{Name}' from '{Path}': {Sources} sources, {Probes} probes", projectName, path, project.Sources.Count, project.Probes.Count);
		return project;
	}

	private static JsonDocument Parse(string path)
	{
		try
		{
			return JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
		}
		catch (JsonException ex)
		{
			throw new InvalidDataException($"The file {Path.GetFileName(path)} is not valid JSON: {ex.Message}", ex);
		}
	}

	private static void ImportScene(JsonElement? section, Scene scene, string folder, IList<string>? notes)
	{
		// the legacy fallbacks: a project saved before a key existed keeps its previous behaviour
		scene.Mode = Text(section, "mode", "user_defined") == "image" ? SceneMode.Image : SceneMode.Canvas;
		scene.Width = Int(section, "user_width", Scene.DefaultWidth);
		scene.Height = Int(section, "user_height", Scene.DefaultHeight);
		scene.DampingEdge = Bool(section, "damping_edge_enabled", false) ? Int(section, "damping_edge_size_px", Scene.DefaultDampingEdge) : 0;
		scene.ImageSources = Bool(section, "use_sources", false);
		scene.ImageSourceScale = Double(section, "image_source_scale", Scene.DefaultImageSourceScale);
		scene.ObjectSources = Bool(section, "use_object_sources", false);
		scene.RefractiveIndex = Double(section, "background_refractive_index", Scene.DefaultRefractiveIndex);
		string imageFile = Text(section, "image_file", "");
		if (imageFile.Length == 0)
		{
			return;
		}
		// the author stores the image relative to the project folder (forward slashes); here it is embedded
		string imagePath = Path.Combine(folder, imageFile.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(imagePath))
		{
			scene.ImageData = File.ReadAllBytes(imagePath);
			scene.Image = Path.GetFileName(imagePath);
			// the source stays known (absolute: the imported project has no file yet), so the file is watched
			scene.ImagePath = Path.GetFullPath(imagePath);
		}
		else
		{
			scene.Image = imageFile;
			notes?.Add($"The scene image '{imageFile}' was not found next to the project; its path is kept, but the scene has no image until one is chosen.");
		}
	}

	private static void ImportSimulation(JsonElement? section, Project project)
	{
		Simulation simulation = project.Simulation;
		simulation.Steps = Int(section, "steps", Simulation.DefaultSteps);
		simulation.VisualizeEvery = Int(section, "visualize_every", 2);
		simulation.FourthOrderLaplacian = Bool(section, "use_4th_order_laplacian", false);
		simulation.WaveSpeed = Double(section, "wave_speed_c", 0.563);
		simulation.TimeDecay = Double(section, "time_decay_factor", 10.0);
		simulation.SourceFrequencyScale = Double(section, "source_frequency_scale", 2.0);
		simulation.IntensityScale = Double(section, "intensity_scale", 1.0);
		Display display = project.Display;
		display.Field.Preview = Bool(section, "preview_field", true);
		display.Intensity.Preview = Bool(section, "preview_intensity", true);
		// the author's "export on the next run" flags have no counterpart: export is a command here
		display.Field.ColorScheme = Text(section, "field_color_scheme", Display.DefaultFieldColorScheme);
		display.Intensity.ColorScheme = Text(section, "intensity_color_scheme", Display.DefaultIntensityColorScheme);
		display.Field.Brightness = Double(section, "field_brightness", DisplayLayer.DefaultBrightness);
		display.Intensity.Brightness = Double(section, "intensity_brightness", DisplayLayer.DefaultBrightness);
		display.Field.BlackLevel = Double(section, "field_black_level", Display.DefaultFieldBlackLevel);
		display.Intensity.BlackLevel = Double(section, "intensity_black_level", 0.0);
		display.Video.Fps = Int(section, "video_fps", Video.DefaultFps);
		display.Video.Quality = Int(section, "video_quality", Video.DefaultQuality);
	}

	private static void ImportSources(JsonElement? section, Project project, IList<string>? notes)
	{
		foreach (JsonElement item in Items(section))
		{
			var source = new Source { Name = Text(item, "name", "") };
			if (source.Name.Length == 0)
			{
				source.Name = "Source " + (project.Sources.Count + 1).ToString(CultureInfo.InvariantCulture);
			}
			source.X = Int(item, "x", 0);
			source.Y = Int(item, "y", 0);
			// "has_position" only says whether the author picked the position; his program simulates a saved source
			// at x/y either way (its default is 100, 100) and only leaves its marker undrawn, so the source stays on
			// (2026-10-16, after Example 11's lattice, which has no picked position, came in disabled and the run was
			// black; an earlier rule had switched such a source off)
			source.Enabled = Bool(item, "source_on", true);
			if (!Bool(item, "has_position", true))
			{
				notes?.Add($"Source '{source.Name}' had no picked position in the original project; it ran at ({source.X}, {source.Y}) there and does here.");
			}
			source.DetachAfter = Bool(item, "detach_source", false) ? Int(item, "detach_after_steps", 1000) : Source.NeverDetach;
			source.Frequency = Double(item, "frequency", Source.DefaultLevel);
			source.Amplitude = Double(item, "amplitude", Source.DefaultAmplitude);
			source.Phase = Double(item, "phase_deg", 0.0);
			source.Mode = Text(item, "mode", "continuous") == "pulsed" ? SourceMode.Pulsed : SourceMode.Continuous;
			source.PulseShape = Text(item, "pulse_shape", "gaussian") == "smoothed_square" ? PulseShape.SmoothedSquare : PulseShape.Gaussian;
			source.PulseWidth = Int(item, "pulse_width_cycles", Source.DefaultWidth);
			source.OffWidth = Int(item, "off_state_cycles", Source.DefaultWidth);
			if (Text(item, "main_type", "single") == "composite")
			{
				// the random amounts count only when the composite mode randomises and the flag is on
				bool randomize = Text(item, "composite_mode", "inherit") == "randomize";
				source.Array = new SourceArray
				{
					SizeX = Int(item, "array_size_x", 1),
					SizeY = Int(item, "array_size_y", 1),
					SpacingX = Int(item, "spacing_x", SourceArray.DefaultSpacing),
					SpacingY = Int(item, "spacing_y", SourceArray.DefaultSpacing),
					RandomFrequency = Amount(item, randomize, "randomize_frequency"),
					RandomPhase = Amount(item, randomize, "randomize_phase"),
					RandomOffWidth = Amount(item, randomize, "randomize_off_width"),
					RandomPosition = Amount(item, randomize, "randomize_position"),
				};
				if (source.Array.IsRandomized)
				{
					notes?.Add($"Source '{source.Name}' uses random offsets; its emitters are regenerated from this project's seed and differ from the original's.");
				}
			}
			project.Sources.Add(source);
		}
	}

	/// <summary>A random amount of an array: the <c>*_amount</c> value when the array randomises and the <c>randomize_*</c> flag is on, else 0.</summary>
	private static int Amount(JsonElement item, bool randomize, string flag) =>
		randomize && Bool(item, flag, false) ? Int(item, flag + "_amount", 0) : 0;

	private static void ImportProbes(JsonElement? section, Project project, JsonElement? scene, IList<string>? notes)
	{
		// a later version of the author's program has a scene-level switch for all probes
		bool useProbes = Bool(scene, "use_probes", true);
		foreach (JsonElement item in Items(section))
		{
			var probe = new Probe
			{
				Name = "Probe " + (project.Probes.Count + 1).ToString(CultureInfo.InvariantCulture),
				Active = Bool(item, "active", true) && useProbes,
				Layer = Text(item, "place_on", "field") == "intensity" ? ProbeLayer.Intensity : ProbeLayer.Field,
				Kind = Text(item, "probe_type", "point") == "line" ? ProbeKind.Line : ProbeKind.Point,
				AX = Int(item, "ax", 100),
				AY = Int(item, "ay", 100),
				BX = Int(item, "bx", 300),
				BY = Int(item, "by", 100),
				BoxSize = Text(item, "box_size", "medium") switch
				{
					"tiny" => ProbeBoxSize.Tiny,
					"small" => ProbeBoxSize.Small,
					"large" => ProbeBoxSize.Large,
					"extra_large" => ProbeBoxSize.ExtraLarge,
					_ => ProbeBoxSize.Medium,
				},
				ScaleMode = Text(item, "scale_mode", "running") == "full" ? ProbeScaleMode.Full : ProbeScaleMode.Running,
				AutoScaleX = Bool(item, "auto_scale_x", true),
				AutoScaleY = Bool(item, "auto_scale_y", true),
				YMin = Double(item, "y_min", -1.0),
				YMax = Double(item, "y_max", 1.0),
				Window = Int(item, "running_window", Probe.DefaultWindow),
				StartAfter = Bool(item, "use_start_after_step", false) ? Int(item, "start_after_step", 0) : 0,
			};
			// the measurement after the layer, which coerces it
			probe.Measurement = Text(item, "measurement", "amplitude") switch
			{
				"intensity" => ProbeMeasurement.Value,
				"integrated_intensity" => ProbeMeasurement.IntegratedValue,
				_ => ProbeMeasurement.Amplitude,
			};
			// the box position last: setting A first would drag a box that happens to share its position along
			probe.BoxX = Int(item, "box_x", 450);
			probe.BoxY = Int(item, "box_y", 20);
			project.Probes.Add(probe);
		}
		if (!useProbes && project.Probes.Count > 0)
		{
			notes?.Add("The original project had its probes switched off; they are imported inactive.");
		}
	}

	// ---------------------------------------------------------------- tolerant readers (the legacy readers' rules)

	/// <summary>The named object below <paramref name="parent"/>, or null when missing or not an object.</summary>
	private static JsonElement? Section(JsonElement parent, string name) =>
		parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : null;

	/// <summary>The objects of a section's <c>items</c> array (a "count" entry is ignored, the array rules); none when absent.</summary>
	private static IEnumerable<JsonElement> Items(JsonElement? section)
	{
		if (section is { } s && s.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in items.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					yield return item;
				}
			}
		}
	}

	private static bool TryGet(JsonElement? parent, string key, out JsonElement value)
	{
		if (parent is { } p && p.TryGetProperty(key, out value) && value.ValueKind != JsonValueKind.Null)
		{
			return true;
		}
		value = default;
		return false;
	}

	/// <summary>A string value, or any scalar as text (invariant culture); the fallback when missing or null.</summary>
	private static string Text(JsonElement? parent, string key, string fallback)
	{
		if (!TryGet(parent, key, out JsonElement value))
		{
			return fallback;
		}
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString() ?? fallback,
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Number => value.GetRawText(),
			_ => fallback,
		};
	}

	/// <summary>A boolean; the fallback unless the value is an actual JSON boolean (the legacy readers did not parse strings here either).</summary>
	private static bool Bool(JsonElement? parent, string key, bool fallback) =>
		TryGet(parent, key, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

	/// <summary>An integer (a whole number, or numeric text); the fallback when missing or not convertible. The model clamps the range.</summary>
	private static int Int(JsonElement? parent, string key, int fallback)
	{
		if (!TryGet(parent, key, out JsonElement value))
		{
			return fallback;
		}
		if (value.ValueKind == JsonValueKind.Number)
		{
			return value.TryGetInt32(out int whole) ? whole : value.TryGetDouble(out double number) && Math.Abs(number) < int.MaxValue ? (int)Math.Round(number) : fallback;
		}
		return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
	}

	/// <summary>A double (a number, or numeric text); the fallback when missing or not convertible. The model clamps the range.</summary>
	private static double Double(JsonElement? parent, string key, double fallback)
	{
		if (!TryGet(parent, key, out JsonElement value))
		{
			return fallback;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
		{
			return number;
		}
		return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
	}
}
