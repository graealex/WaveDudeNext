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
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>Output a probe samples and is drawn on.</summary>
public enum ProbeLayer
{
	/// <summary>The wave field.</summary>
	Field,
	/// <summary>The accumulated intensity.</summary>
	Intensity,
}

/// <summary>Quantity a probe records.</summary>
public enum ProbeMeasurement
{
	/// <summary>Field value at the probe; field layer only.</summary>
	Amplitude,
	/// <summary>Averaged intensity at the probe; intensity layer only (the layer already says intensity, so the member is just "Value"; renamed from Intensity on the author's point 8, 2026-10-14, before the format shipped).</summary>
	Value,
	/// <summary>Intensity summed over the run so far; intensity layer only. Shown as "Integrated value".</summary>
	[Description("Integrated value")]
	IntegratedValue,
}

/// <summary>Point probe (position A) or line probe (A to B).</summary>
public enum ProbeKind
{
	/// <summary>Samples one pixel and plots it over time.</summary>
	Point,
	/// <summary>Samples along a line and plots the current profile.</summary>
	Line,
}

/// <summary>Nominal size of the graph box drawn over the output.</summary>
public enum ProbeBoxSize
{
	/// <summary>Smallest box.</summary>
	Tiny,
	/// <summary>Small box.</summary>
	Small,
	/// <summary>Default box.</summary>
	Medium,
	/// <summary>Large box.</summary>
	Large,
	/// <summary>Largest box.</summary>
	ExtraLarge,
	/// <summary>A box of <see cref="Probe.BoxWidth"/> × <see cref="Probe.BoxHeight"/> canvas pixels (set by dragging the box's corner).</summary>
	Custom,
}

/// <summary>How the step axis of a point probe's time series is chosen.</summary>
public enum ProbeScaleMode
{
	/// <summary>A sliding window of <see cref="Probe.Window"/> steps.</summary>
	Running,
	/// <summary>The whole run.</summary>
	Full,
}

/// <summary>
/// A measurement point or line on the canvas and the graph that shows its values. Positions are
/// canvas pixels with the origin at the top left.
/// </summary>
public sealed class Probe : ObservableObject, IMovable
{
	/// <summary>Default <see cref="Window"/> in steps.</summary>
	public const int DefaultWindow = 500;
	/// <summary>Smallest accepted <see cref="Window"/>.</summary>
	public const int MinWindow = 10;
	/// <summary>Largest accepted <see cref="Window"/>.</summary>
	public const int MaxWindow = 1_000_000;
	/// <summary>Default <see cref="BoxWidth"/> in canvas pixels (the Medium box).</summary>
	public const int DefaultBoxWidth = 400;
	/// <summary>Default <see cref="BoxHeight"/> in canvas pixels (the Medium box).</summary>
	public const int DefaultBoxHeight = 200;
	/// <summary>Smallest accepted <see cref="BoxWidth"/>.</summary>
	public const int MinBoxWidth = 60;
	/// <summary>Smallest accepted <see cref="BoxHeight"/>.</summary>
	public const int MinBoxHeight = 30;
	/// <summary>Largest accepted <see cref="BoxWidth"/> and <see cref="BoxHeight"/>.</summary>
	public const int MaxBoxSide = 4096;

	private string _name = "";
	private bool _active = true;
	private ProbeLayer _layer = ProbeLayer.Field;
	private ProbeMeasurement _measurement = ProbeMeasurement.Amplitude;
	private ProbeKind _kind = ProbeKind.Point;
	private int _aX;
	private int _aY;
	private int _bX;
	private int _bY;
	private ProbeBoxSize _boxSize = ProbeBoxSize.Medium;
	private int _boxX;
	private int _boxY;
	private int _boxWidth = DefaultBoxWidth;
	private int _boxHeight = DefaultBoxHeight;
	private ProbeScaleMode _scaleMode = ProbeScaleMode.Running;
	private bool _autoScaleX = true;
	private bool _autoScaleY = true;
	private double _yMin = -1.0;
	private double _yMax = 1.0;
	private int _window = DefaultWindow;
	private int _startAfter;

	/// <summary>Name shown in the project tree and in the graph title.</summary>
	[XmlAttribute]
	[Category("General")]
	[Description("Name shown in the project tree and in the graph title.")]
	public string Name
	{
		get => _name;
		set => SetProperty(ref _name, value);
	}

	/// <summary>Whether the probe is sampled and drawn.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("General")]
	[Description("Sample and draw this probe.")]
	public bool Active
	{
		get => _active;
		set => SetProperty(ref _active, value);
	}

	/// <summary>Output the probe samples and is drawn on.</summary>
	[XmlAttribute]
	[DefaultValue(ProbeLayer.Field)]
	[Category("Measurement")]
	[Description("Output the probe samples and is drawn on.")]
	public ProbeLayer Layer
	{
		get => _layer;
		set
		{
			if (SetProperty(ref _layer, value))
			{
				// the measurement follows the layer at once, so the editor never shows an impossible pair
				Measurement = MeasurementsFor(value).Contains(Measurement) ? Measurement : MeasurementsFor(value)[0];
			}
		}
	}

	/// <summary>Recorded quantity; must fit <see cref="Layer"/>: setting the layer coerces it, <see cref="Normalize"/> re-checks it.</summary>
	[XmlAttribute]
	[DefaultValue(ProbeMeasurement.Amplitude)]
	[Category("Measurement")]
	[Description("Recorded quantity. The field offers its amplitude only; the intensity layer its value, or the value integrated over the run.")]
	public ProbeMeasurement Measurement
	{
		get => _measurement;
		set => SetProperty(ref _measurement, value);
	}

	/// <summary>A single point, or a line from A to B.</summary>
	[XmlAttribute]
	[DefaultValue(ProbeKind.Point)]
	[Category("Measurement")]
	[Description("A single point, or a line from A to B.")]
	public ProbeKind Kind
	{
		get => _kind;
		set => SetProperty(ref _kind, value);
	}

	/// <summary>
	/// X of the probe pixel, or of the start of the line, in canvas pixels. While the graph box is attached
	/// (<see cref="IsBoxAttached"/>: its anchor on this point) the box moves along; a box dragged away stays
	/// put (the user's rule, 2026-10-14: one attachment, not one per axis).
	/// </summary>
	[XmlAttribute]
	[Category("Position")]
	[DisplayName("A x")]
	[Description("X of the probe pixel, or of the start of the line, in canvas pixels.")]
	public int AX
	{
		get => _aX;
		set
		{
			bool attached = IsBoxAttached;
			if (SetProperty(ref _aX, value) && attached)
			{
				BoxX = value;
			}
		}
	}

	/// <summary>Y of the probe pixel, or of the start of the line, in canvas pixels. An attached graph box follows it like <see cref="AX"/>.</summary>
	[XmlAttribute]
	[Category("Position")]
	[DisplayName("A y")]
	[Description("Y of the probe pixel, or of the start of the line, in canvas pixels.")]
	public int AY
	{
		get => _aY;
		set
		{
			bool attached = IsBoxAttached;
			if (SetProperty(ref _aY, value) && attached)
			{
				BoxY = value;
			}
		}
	}

	/// <summary>
	/// Whether the graph box is attached to the probe: its anchor (<see cref="BoxX"/>, <see cref="BoxY"/>) sits on
	/// point A, so the box moves with the probe. The preview snaps a box dragged close to the probe onto it and
	/// detaches it when it is dragged away (2026-10-14).
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool IsBoxAttached => _boxX == _aX && _boxY == _aY;

	/// <summary>X of the end of the line in canvas pixels; line probes only, omitted from the file otherwise.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Position")]
	[DisplayName("B x")]
	[Description("X of the end of the line in canvas pixels; line probes only.")]
	public int BX
	{
		get => _bX;
		set => SetProperty(ref _bX, value);
	}

	/// <summary>Y of the end of the line in canvas pixels; line probes only, omitted from the file otherwise.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Position")]
	[DisplayName("B y")]
	[Description("Y of the end of the line in canvas pixels; line probes only.")]
	public int BY
	{
		get => _bY;
		set => SetProperty(ref _bY, value);
	}

	/// <summary>Nominal size of the graph box drawn over the output.</summary>
	[XmlAttribute]
	[DefaultValue(ProbeBoxSize.Medium)]
	[Category("Graph")]
	[DisplayName("Box size")]
	[Description("Nominal size of the graph box drawn over the output; a box larger than the canvas is shrunk to fit it, aspect kept.")]
	public ProbeBoxSize BoxSize
	{
		get => _boxSize;
		set => SetProperty(ref _boxSize, value);
	}

	/// <summary>Left edge of the graph box in canvas pixels; with the box on point A (<see cref="IsBoxAttached"/>) it moves with the probe.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Graph")]
	[DisplayName("Box x")]
	[Description("Left edge of the graph box in canvas pixels. A box placed on the probe's point A moves with the probe; dragging it close snaps it on.")]
	public int BoxX
	{
		get => _boxX;
		set => SetProperty(ref _boxX, value);
	}

	/// <summary>Top edge of the graph box in canvas pixels; with the box on point A (<see cref="IsBoxAttached"/>) it moves with the probe.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Graph")]
	[DisplayName("Box y")]
	[Description("Top edge of the graph box in canvas pixels. A box placed on the probe's point A moves with the probe; dragging it close snaps it on.")]
	public int BoxY
	{
		get => _boxY;
		set => SetProperty(ref _boxY, value);
	}

	/// <summary>Width of the graph box in canvas pixels; used when <see cref="BoxSize"/> is <see cref="ProbeBoxSize.Custom"/>, omitted from the file otherwise.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultBoxWidth)]
	[Category("Graph")]
	[DisplayName("Box width")]
	[Description("Width of the graph box in canvas pixels; custom box size only. A box larger than the canvas is shrunk to fit it, aspect kept.")]
	public int BoxWidth
	{
		get => _boxWidth;
		set => SetProperty(ref _boxWidth, value);
	}

	/// <summary>Height of the graph box in canvas pixels; used when <see cref="BoxSize"/> is <see cref="ProbeBoxSize.Custom"/>, omitted from the file otherwise.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultBoxHeight)]
	[Category("Graph")]
	[DisplayName("Box height")]
	[Description("Height of the graph box in canvas pixels; custom box size only. A box larger than the canvas is shrunk to fit it, aspect kept.")]
	public int BoxHeight
	{
		get => _boxHeight;
		set => SetProperty(ref _boxHeight, value);
	}

	/// <summary>Sliding window of the last steps, or the whole run; point probes only.</summary>
	[XmlAttribute]
	[DefaultValue(ProbeScaleMode.Running)]
	[Category("Axes")]
	[DisplayName("Step axis")]
	[Description("Sliding window of the last steps, or the whole run; point probes only.")]
	public ProbeScaleMode ScaleMode
	{
		get => _scaleMode;
		set => SetProperty(ref _scaleMode, value);
	}

	/// <summary>Whether the step axis follows the samples recorded so far; point probes only.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Axes")]
	[DisplayName("Auto-scale steps")]
	[Description("Let the step axis follow the samples recorded so far; point probes only.")]
	public bool AutoScaleX
	{
		get => _autoScaleX;
		set => SetProperty(ref _autoScaleX, value);
	}

	/// <summary>Whether the value axis is fitted to the data instead of <see cref="YMin"/>..<see cref="YMax"/>.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Axes")]
	[DisplayName("Auto-scale values")]
	[Description("Fit the value axis to the data instead of the fixed minimum and maximum.")]
	public bool AutoScaleY
	{
		get => _autoScaleY;
		set => SetProperty(ref _autoScaleY, value);
	}

	/// <summary>Lower bound of the value axis when it is not auto-scaled.</summary>
	[XmlAttribute]
	[DefaultValue(-1.0)]
	[Category("Axes")]
	[DisplayName("Value minimum")]
	[Description("Lower bound of the value axis when it is not auto-scaled.")]
	public double YMin
	{
		get => _yMin;
		set => SetProperty(ref _yMin, value);
	}

	/// <summary>Upper bound of the value axis when it is not auto-scaled; must exceed <see cref="YMin"/>.</summary>
	[XmlAttribute]
	[DefaultValue(1.0)]
	[Category("Axes")]
	[DisplayName("Value maximum")]
	[Description("Upper bound of the value axis when it is not auto-scaled.")]
	public double YMax
	{
		get => _yMax;
		set => SetProperty(ref _yMax, value);
	}

	/// <summary>Width of the sliding step window for <see cref="ProbeScaleMode.Running"/>, in steps.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWindow)]
	[Category("Axes")]
	[DisplayName("Window")]
	[Description("Width of the sliding step window, in steps.")]
	public int Window
	{
		get => _window;
		set => SetProperty(ref _window, value);
	}

	/// <summary>Step at which recording starts; 0 records from the first step.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("General")]
	[DisplayName("Start after step")]
	[Description("Step at which recording starts; 0 records from the first step.")]
	public int StartAfter
	{
		get => _startAfter;
		set => SetProperty(ref _startAfter, value);
	}

	/// <summary>Tells <see cref="XmlSerializer"/> to write B only for line probes.</summary>
	public bool ShouldSerializeBX() => Kind == ProbeKind.Line;

	/// <summary>Tells <see cref="XmlSerializer"/> to write B only for line probes.</summary>
	public bool ShouldSerializeBY() => Kind == ProbeKind.Line;

	/// <summary>Tells <see cref="XmlSerializer"/> to write the box width only for a custom box size.</summary>
	public bool ShouldSerializeBoxWidth() => BoxSize == ProbeBoxSize.Custom;

	/// <summary>Tells <see cref="XmlSerializer"/> to write the box height only for a custom box size.</summary>
	public bool ShouldSerializeBoxHeight() => BoxSize == ProbeBoxSize.Custom;

	/// <summary>
	/// Whether a change of <paramref name="property"/> changes what a run measures (position, kind, layer,
	/// measurement, start step, name, active), as opposed to how the graph is drawn (box and axes).
	/// </summary>
	public static bool AffectsMeasurement(string? property) =>
		property is nameof(Name) or nameof(Active) or nameof(Layer) or nameof(Measurement) or nameof(Kind)
			or nameof(AX) or nameof(AY) or nameof(BX) or nameof(BY) or nameof(StartAfter);

	/// <summary>The measurements a probe on <paramref name="layer"/> can record (the editor offers only these).</summary>
	public static ProbeMeasurement[] MeasurementsFor(ProbeLayer layer) =>
		layer == ProbeLayer.Field ? [ProbeMeasurement.Amplitude] : [ProbeMeasurement.Value, ProbeMeasurement.IntegratedValue];


	/// <inheritdoc/>
	[XmlIgnore]
	[Browsable(false)]
	public IEnumerable<(int X, int Y)> Anchors => Kind == ProbeKind.Line ? [(AX, AY), (BX, BY)] : [(AX, AY)];

	/// <summary>Moves point A, and point B of a line probe, by the delta; the graph box follows while it sits on point A.</summary>
	public void MoveBy(int dx, int dy)
	{
		AX += dx;
		AY += dy;
		if (Kind == ProbeKind.Line)
		{
			BX += dx;
			BY += dy;
		}
	}

	/// <summary>
	/// Forces a consistent state: the field only allows <see cref="ProbeMeasurement.Amplitude"/>,
	/// the intensity output never does; an empty or inverted Y range becomes -1..1; the window,
	/// the custom box size and the start step are clamped.
	/// </summary>
	public void Normalize()
	{
		Name ??= "";
		if (Layer == ProbeLayer.Field)
		{
			Measurement = ProbeMeasurement.Amplitude;
		}
		else if (Measurement == ProbeMeasurement.Amplitude)
		{
			Measurement = ProbeMeasurement.Value;
		}
		if (YMax <= YMin)
		{
			YMin = -1.0;
			YMax = 1.0;
		}
		Window = Math.Clamp(Window, MinWindow, MaxWindow);
		BoxWidth = Math.Clamp(BoxWidth, MinBoxWidth, MaxBoxSide);
		BoxHeight = Math.Clamp(BoxHeight, MinBoxHeight, MaxBoxSide);
		StartAfter = Math.Max(0, StartAfter);
	}

	/// <summary>A copy with the same settings and no change subscribers.</summary>
	public Probe Clone() => ShallowClone<Probe>();

	/// <summary>The name, for display in the project tree and property grids.</summary>
	public override string ToString() => Name;
}
