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

/// <summary>How the two outputs, field and intensity, are rendered, previewed and exported.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class Display : ObservableObject
{
	/// <summary>Colour scheme name used for the field when none is given (an old file's "Default_Field" resolves to it through <see cref="Rendering.ColorSchemeStore.Find"/>).</summary>
	public const string DefaultFieldColorScheme = "Default field";
	/// <summary>Colour scheme name used for the intensity when none is given (an old file's "Default_intensity" resolves to it).</summary>
	public const string DefaultIntensityColorScheme = "Default intensity";
	/// <summary>Black level of the field layer in a new project.</summary>
	public const double DefaultFieldBlackLevel = -0.05;

	private DisplayLayer _field = new() { ColorScheme = DefaultFieldColorScheme, BlackLevel = DefaultFieldBlackLevel };
	private DisplayLayer _intensity = new() { ColorScheme = DefaultIntensityColorScheme };
	private Video _video = new();
	private int _exportFrom;
	private int _exportTo;
	private bool _probeGraphsInExport = true;
	private bool _probeMarkersInExport = true;
	private bool _sourceMarkersInExport;

	/// <summary>Rendering of the wave field.</summary>
	[XmlElement]
	[Category("Outputs")]
	[Description("Rendering of the wave field.")]
	public DisplayLayer Field
	{
		get => _field;
		set => SetProperty(ref _field, value);
	}

	/// <summary>Rendering of the accumulated intensity.</summary>
	[XmlElement]
	[Category("Outputs")]
	[Description("Rendering of the accumulated intensity.")]
	public DisplayLayer Intensity
	{
		get => _intensity;
		set => SetProperty(ref _intensity, value);
	}

	/// <summary>Encoder settings of exported videos.</summary>
	[XmlElement]
	[Category("Export")]
	[Description("Encoder settings of exported videos.")]
	public Video Video
	{
		get => _video;
		set => SetProperty(ref _video, value);
	}

	/// <summary>First simulation step of the exported range (and of loop playback); 0 is the start of the run.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, int.MaxValue)]
	[Category("Export")]
	[DisplayName("From step")]
	[Description("First simulation step of the exported range; 0 is the start of the run.")]
	public int ExportFrom
	{
		get => _exportFrom;
		set => SetProperty(ref _exportFrom, value);
	}

	/// <summary>Last simulation step of the exported range (and of loop playback); 0 means the end of the run.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, int.MaxValue)]
	[Category("Export")]
	[DisplayName("To step")]
	[Description("Last simulation step of the exported range; 0 means the end of the run.")]
	public int ExportTo
	{
		get => _exportTo;
		set => SetProperty(ref _exportTo, value);
	}

	/// <summary>
	/// Whether an export draws the probes' graph boxes over the frames, as the author's program always did
	/// (2026-10-14, his videos carried them; here a choice, on by default). The boxes are drawn at canvas
	/// scale with the recording as it stood at each frame, like the preview shows them.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Export")]
	[DisplayName("Probe graphs in the export")]
	[Description("Draw the probes' graph boxes over exported frames and videos.")]
	public bool ProbeGraphsInExport
	{
		get => _probeGraphsInExport;
		set => SetProperty(ref _probeGraphsInExport, value);
	}

	/// <summary>
	/// Whether an export draws the probe markers over the frames in the author's style (2026-10-15): a cross at a
	/// probe point, two crosses and the line for a line probe, black under white, and the probe's number
	/// ("1", "1A"/"1B") beside it. His program always did; here a choice, on by default.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Export")]
	[DisplayName("Probe markers in the export")]
	[Description("Draw the probes' positions (crosses, the line of a line probe, the number) over exported frames and videos.")]
	public bool ProbeMarkersInExport
	{
		get => _probeMarkersInExport;
		set => SetProperty(ref _probeMarkersInExport, value);
	}

	/// <summary>
	/// Whether an export draws the enabled sources over the frames of both layers (2026-10-21, the user's request; off
	/// by default, so existing projects export as before): the preview's source marker (a ring with a crosshair over a
	/// dark halo), a composite source's emitter dots, and the source's name, scaled to the canvas like the probe
	/// labels, under the probe markers and graph boxes. Jeroen's program drew sources only on its Sources tab.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Export")]
	[DisplayName("Source markers in the export")]
	[Description("Draw the enabled sources (the marker, a composite source's emitter dots, the name) over exported frames and videos.")]
	public bool SourceMarkersInExport
	{
		get => _sourceMarkersInExport;
		set => SetProperty(ref _sourceMarkersInExport, value);
	}

	/// <summary>Tells <see cref="XmlSerializer"/> to leave all-default video settings out of the file.</summary>
	public bool ShouldSerializeVideo() => Video.Fps != Video.DefaultFps || Video.Quality != Video.DefaultQuality;

	/// <summary>Clamps every value into its valid range.</summary>
	public void Normalize()
	{
		Field.Normalize(DefaultFieldColorScheme);
		Intensity.Normalize(DefaultIntensityColorScheme);
		Video.Normalize();
		ExportFrom = Math.Max(0, ExportFrom);
		ExportTo = Math.Max(0, ExportTo);
		if (ExportTo != 0 && ExportTo < ExportFrom)
		{
			ExportTo = 0;
		}
	}

	/// <summary>The two colour scheme names, for display in a property grid.</summary>
	public override string ToString() => $"{Field.ColorScheme} / {Intensity.ColorScheme}";
}

/// <summary>Rendering of one output (field or intensity).</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class DisplayLayer : ObservableObject
{
	/// <summary>Default <see cref="Brightness"/>.</summary>
	public const double DefaultBrightness = 30.0;

	/// <summary>Default <see cref="OverlayGain"/>: the edge picture as computed.</summary>
	public const double DefaultOverlayGain = 1.0;

	/// <summary>Largest <see cref="OverlayGain"/>, the author's control's maximum.</summary>
	public const double MaxOverlayGain = 5.0;

	private string _colorScheme = "";
	private double _brightness = DefaultBrightness;
	private double _blackLevel;
	private bool _preview = true;
	private bool _overlay = true;
	private double _overlayGain = DefaultOverlayGain;
	private bool _overlayInExport;

	/// <summary>Name of the colour scheme mapped onto the values.</summary>
	[XmlAttribute]
	[Category("Rendering")]
	[DisplayName("Colour scheme")]
	[Description("Name of the colour scheme mapped onto the values.")]
	public string ColorScheme
	{
		get => _colorScheme;
		set => SetProperty(ref _colorScheme, value);
	}

	/// <summary>Gain applied to the values before the colour lookup.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultBrightness)]
	[Range(1e-4, 1e5)]
	[Category("Rendering")]
	[Description("Gain applied to the values before the colour lookup.")]
	public double Brightness
	{
		get => _brightness;
		set => SetProperty(ref _brightness, value);
	}

	/// <summary>Offset applied before the colour lookup; values at or below it map to the first colour.</summary>
	[XmlAttribute]
	[DefaultValue(0.0)]
	[Range(-10.0, 1.0)]
	[Category("Rendering")]
	[DisplayName("Black level")]
	[Description("Offset applied before the colour lookup; values at or below it map to the first colour.")]
	public double BlackLevel
	{
		get => _blackLevel;
		set => SetProperty(ref _blackLevel, value);
	}

	/// <summary>Whether the output is shown in the live preview.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Output")]
	[Description("Show this output in the live preview.")]
	public bool Preview
	{
		get => _preview;
		set => SetProperty(ref _preview, value);
	}

	/// <summary>
	/// Whether the preview lays the scene image's "glowing edges" outline over this output (the author's
	/// "Display overlay"; on by default here, 2026-10-13), so the scene layout shows under the field or
	/// intensity. Only an image scene has one.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Scene outline")]
	[DisplayName("Outline in the preview")]
	[Description("Lay the scene image's glowing-edges outline over this output in the preview.")]
	public bool Overlay
	{
		get => _overlay;
		set => SetProperty(ref _overlay, value);
	}

	/// <summary>Multiplier of the outline's brightness, 0 to <see cref="MaxOverlayGain"/>; 1 shows the edge picture as computed, 0 hides it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultOverlayGain)]
	[Range(0.0, MaxOverlayGain)]
	[Category("Scene outline")]
	[DisplayName("Outline brightness")]
	[Description("Multiplier of the outline: 1 as computed, higher lifts faint edges, 0 hides it.")]
	public double OverlayGain
	{
		get => _overlayGain;
		set => SetProperty(ref _overlayGain, value);
	}

	/// <summary>Whether an export bakes the outline into this output's frames (additively, as the author's program does); off by default.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Scene outline")]
	[DisplayName("Outline in the export")]
	[Description("Bake the outline into exported frames and videos of this output.")]
	public bool OverlayInExport
	{
		get => _overlayInExport;
		set => SetProperty(ref _overlayInExport, value);
	}

	/// <summary>Clamps every value into its valid range; an empty scheme name falls back to <paramref name="defaultScheme"/>.</summary>
	public void Normalize(string defaultScheme)
	{
		if (string.IsNullOrWhiteSpace(ColorScheme))
		{
			ColorScheme = defaultScheme;
		}
		Brightness = Math.Clamp(Brightness, 1e-4, 1e5);
		BlackLevel = Math.Clamp(BlackLevel, -10.0, 1.0);
		OverlayGain = Math.Clamp(OverlayGain, 0.0, MaxOverlayGain);
	}

	/// <summary>The colour scheme name, for display in a property grid.</summary>
	public override string ToString() => ColorScheme;
}

/// <summary>Encoder settings of exported videos.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class Video : ObservableObject
{
	/// <summary>Default <see cref="Fps"/>.</summary>
	public const int DefaultFps = 60;
	/// <summary>Largest accepted <see cref="Fps"/>.</summary>
	public const int MaxFps = 240;
	/// <summary>Default <see cref="Quality"/>.</summary>
	public const int DefaultQuality = 85;

	private int _fps = DefaultFps;
	private int _quality = DefaultQuality;

	/// <summary>Frames per second of the exported video.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultFps)]
	[Range(1, MaxFps)]
	[Category("Video")]
	[DisplayName("Frame rate")]
	[Description("Frames per second of the exported video.")]
	public int Fps
	{
		get => _fps;
		set => SetProperty(ref _fps, value);
	}

	/// <summary>Encoder quality, 1 to 100.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultQuality)]
	[Range(1, 100)]
	[Category("Video")]
	[Description("Encoder quality, 1 to 100.")]
	public int Quality
	{
		get => _quality;
		set => SetProperty(ref _quality, value);
	}

	/// <summary>Clamps every value into its valid range.</summary>
	public void Normalize()
	{
		Fps = Math.Clamp(Fps, 1, MaxFps);
		Quality = Math.Clamp(Quality, 1, 100);
	}

	/// <summary>Frame rate and quality, for display in a property grid.</summary>
	public override string ToString() => $"{Fps} fps, quality {Quality}";
}
