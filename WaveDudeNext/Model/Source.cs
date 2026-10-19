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

/// <summary>Continuous or pulsed emission.</summary>
public enum SourceMode
{
	/// <summary>Emits without interruption.</summary>
	Continuous,
	/// <summary>Emits bursts of <see cref="Source.PulseWidth"/> cycles separated by gaps of <see cref="Source.OffWidth"/> cycles.</summary>
	Pulsed,
}

/// <summary>Envelope of each burst in pulsed mode.</summary>
public enum PulseShape
{
	/// <summary>Gaussian bell.</summary>
	Gaussian,
	/// <summary>Flat top with smoothed flanks.</summary>
	SmoothedSquare,
}

/// <summary>
/// One wave source placed on the canvas: a single emitter, or a composite array of emitters
/// when <see cref="Array"/> is set. Units are those of the editor: positions in canvas pixels,
/// frequency as a level up to 255 with decimals (2026-10-19, Jeroen's point 10: the whole-number levels were a
/// remnant of the 8-bit image channel; above 255 the grid cannot sample the wave), amplitude in percent, phase in
/// degrees with decimals, widths in cycles of the source frequency, detach time in simulation steps.
/// </summary>
public sealed class Source : ObservableObject
{
	/// <summary>Smallest frequency level: just above 0, a wave of any length.</summary>
	public const double MinLevel = 0.01;
	/// <summary>Largest frequency level: the green channel's 255, above which the grid cannot sample the wave.</summary>
	public const double MaxLevel = 255.0;
	/// <summary>Default <see cref="Frequency"/>.</summary>
	public const double DefaultLevel = 100.0;
	/// <summary>Default <see cref="Amplitude"/> in percent.</summary>
	public const int DefaultAmplitude = 100;
	/// <summary>Default <see cref="PulseWidth"/> and <see cref="OffWidth"/> in cycles.</summary>
	public const int DefaultWidth = 10;
	/// <summary>Value of <see cref="DetachAfter"/> meaning the source stays on for the whole run.</summary>
	public const int NeverDetach = -1;

	private string _name = "";
	private bool _enabled = true;
	private int _x;
	private int _y;
	private double _frequency = DefaultLevel;
	private int _amplitude = DefaultAmplitude;
	private double _phase;
	private SourceMode _mode = SourceMode.Continuous;
	private PulseShape _pulseShape = PulseShape.Gaussian;
	private int _pulseWidth = DefaultWidth;
	private int _offWidth = DefaultWidth;
	private int _detachAfter = NeverDetach;
	private SourceArray? _array;

	/// <summary>Name shown in the project tree and on the canvas; must be non-blank.</summary>
	[XmlAttribute]
	[Category("General")]
	[Description("Name shown in the project tree and on the canvas.")]
	public string Name
	{
		get => _name;
		set => SetProperty(ref _name, value);
	}

	/// <summary>Whether the source takes part in simulation runs.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("General")]
	[Description("Include the source in simulation runs.")]
	public bool Enabled
	{
		get => _enabled;
		set => SetProperty(ref _enabled, value);
	}

	/// <summary>X of the emitter, or of the array centre, in canvas pixels.</summary>
	[XmlAttribute]
	[Category("Position")]
	[Description("X of the emitter, or of the array centre, in canvas pixels.")]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y of the emitter, or of the array centre, in canvas pixels.</summary>
	[XmlAttribute]
	[Category("Position")]
	[Description("Y of the emitter, or of the array centre, in canvas pixels.")]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>Frequency as a level up to 255, decimals allowed, the same scale as the green channel of image sources (ω = level / 255 × 0.5 × <see cref="Simulation.SourceFrequencyScale"/>).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultLevel)]
	[Category("Wave")]
	[Description("Frequency as a level up to 255, decimals allowed, the same scale as the green channel of image sources. Above 255 the grid cannot sample the wave.")]
	public double Frequency
	{
		get => _frequency;
		set => SetProperty(ref _frequency, value);
	}

	/// <summary>Amplitude in percent, 0 to 100.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAmplitude)]
	[Category("Wave")]
	[Description("Amplitude in percent, 0 to 100.")]
	public int Amplitude
	{
		get => _amplitude;
		set => SetProperty(ref _amplitude, value);
	}

	/// <summary>Phase offset in degrees, -180 to 180, decimals allowed.</summary>
	[XmlAttribute]
	[DefaultValue(0.0)]
	[Category("Wave")]
	[Description("Phase offset in degrees, -180 to 180.")]
	public double Phase
	{
		get => _phase;
		set => SetProperty(ref _phase, value);
	}

	/// <summary>Emit continuously or in bursts.</summary>
	[XmlAttribute]
	[DefaultValue(SourceMode.Continuous)]
	[Category("Pulsing")]
	[Description("Emit continuously or in bursts.")]
	public SourceMode Mode
	{
		get => _mode;
		set => SetProperty(ref _mode, value);
	}

	/// <summary>Envelope of each burst; pulsed mode only.</summary>
	[XmlAttribute]
	[DefaultValue(PulseShape.Gaussian)]
	[Category("Pulsing")]
	[DisplayName("Pulse shape")]
	[Description("Envelope of each burst; pulsed mode only.")]
	public PulseShape PulseShape
	{
		get => _pulseShape;
		set => SetProperty(ref _pulseShape, value);
	}

	/// <summary>Length of each burst in cycles of the source frequency; pulsed mode only.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Category("Pulsing")]
	[DisplayName("Pulse width")]
	[Description("Length of each burst in cycles of the source frequency; pulsed mode only.")]
	public int PulseWidth
	{
		get => _pulseWidth;
		set => SetProperty(ref _pulseWidth, value);
	}

	/// <summary>Length of the gap between bursts in cycles; pulsed mode only.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Category("Pulsing")]
	[DisplayName("Off width")]
	[Description("Length of the gap between bursts in cycles; pulsed mode only.")]
	public int OffWidth
	{
		get => _offWidth;
		set => SetProperty(ref _offWidth, value);
	}

	/// <summary>Simulation step at which the source fades out; <see cref="NeverDetach"/> keeps it on for the whole run.</summary>
	[XmlAttribute]
	[DefaultValue(NeverDetach)]
	[Category("General")]
	[DisplayName("Detach after step")]
	[Description("Simulation step at which the source fades out; -1 keeps it on for the whole run.")]
	public int DetachAfter
	{
		get => _detachAfter;
		set => SetProperty(ref _detachAfter, value);
	}

	/// <summary>Grid of emitters around the position; null for a single emitter.</summary>
	[XmlElement]
	[Category("Array")]
	[Description("Grid of emitters around the position; empty for a single emitter.")]
	public SourceArray? Array
	{
		get => _array;
		set
		{
			if (SetProperty(ref _array, value))
			{
				OnPropertyChanged(nameof(IsComposite));
			}
		}
	}

	/// <summary>
	/// The emitters generated from the settings by <see cref="GenerateElements"/>. Not stored:
	/// they are a pure function of the settings and the project seed.
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	public List<SourceElement> Elements { get; private set; } = new();

	/// <summary>
	/// True for a composite source. Setting it creates a default <see cref="Array"/> or removes
	/// the existing one; the file stores the array itself, not this flag.
	/// </summary>
	[XmlIgnore]
	[Category("Array")]
	[DisplayName("Composite source")]
	[Description("Emit from an array of emitters instead of a single point.")]
	public bool IsComposite
	{
		get => Array != null;
		set
		{
			if (value != IsComposite)
			{
				Array = value ? new SourceArray() : null;
			}
		}
	}

	/// <summary>True when the array applies random per-emitter offsets.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool IsRandomized => Array?.IsRandomized == true;

	/// <summary>
	/// Random stream for this source: derived from the project seed and the source name, so
	/// the same name in the same project always jitters the same way regardless of list order.
	/// </summary>
	public Pcg32 CreateRandom(int projectSeed) => new((ulong)(uint)projectSeed, Pcg32.StableHash(Name));

	/// <summary>Clamps every value into its valid range.</summary>
	public void Normalize()
	{
		Name ??= "";
		Frequency = double.IsFinite(Frequency) ? Math.Clamp(Frequency, MinLevel, MaxLevel) : DefaultLevel;
		Amplitude = Math.Clamp(Amplitude, 0, 100);
		Phase = double.IsFinite(Phase) ? Math.Clamp(Phase, -180.0, 180.0) : 0.0;
		PulseWidth = Math.Max(1, PulseWidth);
		OffWidth = Math.Max(1, OffWidth);
		if (DetachAfter < 0)
		{
			DetachAfter = NeverDetach;
		}
		Array?.Normalize();
	}

	/// <summary>
	/// Rebuilds <see cref="Elements"/>: one emitter for a single source, or the array grid
	/// centred on (<see cref="X"/>, <see cref="Y"/>), rotated by the array's <see cref="SourceArray.Angle"/>
	/// (counter-clockwise on the screen, whose y axis points down), with the random offsets applied.
	/// Deterministic for a given project seed, name and settings.
	/// </summary>
	public void GenerateElements(int projectSeed)
	{
		Normalize();
		Pcg32 rng = CreateRandom(projectSeed);
		var elements = new List<SourceElement>();
		if (Array is not { } array)
		{
			elements.Add(new SourceElement { X = X, Y = Y, Level = Frequency, Phase = Phase, OffWidth = OffWidth });
		}
		else
		{
			double radians = array.Angle * Math.PI / 180.0;
			double cos = Math.Cos(radians);
			double sin = Math.Sin(radians);
			for (int row = 0; row < array.SizeY; row++)
			{
				for (int column = 0; column < array.SizeX; column++)
				{
					// grid centred on (X, Y); with an even size the centre falls between two emitters. Rotated about the
					// centre: with y pointing down, a counter-clockwise turn on the screen is x' = x cos + y sin, y' = y cos - x sin
					double dx = (column - (array.SizeX - 1) / 2.0) * array.SpacingX;
					double dy = (row - (array.SizeY - 1) / 2.0) * array.SpacingY;
					int x = X + (int)Math.Round(dx * cos + dy * sin);
					int y = Y + (int)Math.Round(dy * cos - dx * sin);
					elements.Add(new SourceElement
					{
						X = x + Jitter(rng, array.RandomPosition),
						Y = y + Jitter(rng, array.RandomPosition),
						Level = Math.Clamp(Frequency + Jitter(rng, array.RandomFrequency), MinLevel, MaxLevel),
						Phase = Math.Clamp(Phase + Jitter(rng, array.RandomPhase), -180.0, 180.0),
						OffWidth = Math.Max(1, OffWidth + Jitter(rng, array.RandomOffWidth)),
					});
				}
			}
		}
		Elements = elements;
	}

	/// <summary>Uniform random integer in [-amount, +amount]; 0 when amount is not positive.</summary>
	public static int Jitter(Pcg32 rng, int amount) => amount <= 0 ? 0 : rng.Next(-amount, amount);

	/// <summary>A deep copy with the same settings, its own emitter array and no change subscribers.</summary>
	public Source Clone()
	{
		Source copy = ShallowClone<Source>();
		copy.Array = Array?.Clone();
		copy.Elements = new List<SourceElement>(Elements);
		return copy;
	}

	/// <summary>The name, for display in the project tree and property grids.</summary>
	public override string ToString() => Name;
}

/// <summary>Grid layout of a composite source and its per-emitter randomisation. Amounts of 0 mean off.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class SourceArray : ObservableObject
{
	/// <summary>Default <see cref="SpacingX"/> and <see cref="SpacingY"/> in pixels.</summary>
	public const int DefaultSpacing = 10;

	private int _sizeX = 1;
	private int _sizeY = 1;
	private int _spacingX = DefaultSpacing;
	private int _spacingY = DefaultSpacing;
	private double _angle;
	private int _randomFrequency;
	private int _randomPhase;
	private int _randomOffWidth;
	private int _randomPosition;

	/// <summary>Number of emitters across; odd counts keep the centre emitter on the source position.</summary>
	[XmlAttribute]
	[DefaultValue(1)]
	[Category("Grid")]
	[DisplayName("Columns")]
	[Description("Number of emitters across. Odd counts keep the centre emitter on the source position.")]
	public int SizeX
	{
		get => _sizeX;
		set => SetProperty(ref _sizeX, value);
	}

	/// <summary>Number of emitters down.</summary>
	[XmlAttribute]
	[DefaultValue(1)]
	[Category("Grid")]
	[DisplayName("Rows")]
	[Description("Number of emitters down.")]
	public int SizeY
	{
		get => _sizeY;
		set => SetProperty(ref _sizeY, value);
	}

	/// <summary>Distance between neighbouring columns in canvas pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSpacing)]
	[Category("Grid")]
	[DisplayName("Horizontal spacing")]
	[Description("Distance between neighbouring columns in canvas pixels.")]
	public int SpacingX
	{
		get => _spacingX;
		set => SetProperty(ref _spacingX, value);
	}

	/// <summary>Distance between neighbouring rows in canvas pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSpacing)]
	[Category("Grid")]
	[DisplayName("Vertical spacing")]
	[Description("Distance between neighbouring rows in canvas pixels.")]
	public int SpacingY
	{
		get => _spacingY;
		set => SetProperty(ref _spacingY, value);
	}

	/// <summary>Rotation of the grid about the source position in degrees, counter-clockwise as seen on the screen; 0 keeps the rows horizontal (2026-10-14, the user's request).</summary>
	[XmlAttribute]
	[DefaultValue(0.0)]
	[Category("Grid")]
	[DisplayName("Rotation")]
	[Description("Rotation of the grid about the source position in degrees, counter-clockwise on the screen. 0 keeps the rows horizontal.")]
	public double Angle
	{
		get => _angle;
		set => SetProperty(ref _angle, value);
	}

	/// <summary>Half-range of the per-emitter frequency offset in levels; 0 turns it off.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Randomisation")]
	[DisplayName("Frequency")]
	[Description("Each emitter's level is offset by up to ± this amount. 0 turns it off.")]
	public int RandomFrequency
	{
		get => _randomFrequency;
		set => SetProperty(ref _randomFrequency, value);
	}

	/// <summary>Half-range of the per-emitter phase offset in degrees; 0 turns it off.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Randomisation")]
	[DisplayName("Phase")]
	[Description("Each emitter's phase is offset by up to ± this many degrees. 0 turns it off.")]
	public int RandomPhase
	{
		get => _randomPhase;
		set => SetProperty(ref _randomPhase, value);
	}

	/// <summary>Half-range of the gap offset in cycles, re-drawn by the engine every period; 0 turns it off.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Randomisation")]
	[DisplayName("Off width")]
	[Description("Each gap between bursts is offset by up to ± this many cycles, re-drawn every period. 0 turns it off.")]
	public int RandomOffWidth
	{
		get => _randomOffWidth;
		set => SetProperty(ref _randomOffWidth, value);
	}

	/// <summary>Half-range of the per-emitter position jitter in canvas pixels, independent for X and Y; 0 turns it off.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Randomisation")]
	[DisplayName("Position")]
	[Description("Each emitter is moved by up to ± this many pixels in X and in Y. 0 turns it off.")]
	public int RandomPosition
	{
		get => _randomPosition;
		set => SetProperty(ref _randomPosition, value);
	}

	/// <summary>True when any random amount is positive.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool IsRandomized => RandomFrequency > 0 || RandomPhase > 0 || RandomOffWidth > 0 || RandomPosition > 0;

	/// <summary>Clamps every value into its valid range; the angle is brought into (-180, 180].</summary>
	public void Normalize()
	{
		SizeX = Math.Max(1, SizeX);
		SizeY = Math.Max(1, SizeY);
		if (!double.IsFinite(Angle))
		{
			Angle = 0.0;
		}
		else if (Angle <= -180.0 || Angle > 180.0)
		{
			Angle = Angle - 360.0 * Math.Ceiling((Angle - 180.0) / 360.0);
		}
		RandomFrequency = Math.Max(0, RandomFrequency);
		RandomPhase = Math.Max(0, RandomPhase);
		RandomOffWidth = Math.Max(0, RandomOffWidth);
		RandomPosition = Math.Max(0, RandomPosition);
	}

	/// <summary>A copy with the same settings and no change subscribers.</summary>
	public SourceArray Clone() => ShallowClone<SourceArray>();

	/// <summary>The grid size, for display in a property grid.</summary>
	public override string ToString() => $"{SizeX} × {SizeY}";
}

/// <summary>
/// One emitter of a source after generation. Holds only what can differ between the emitters
/// of one source; amplitude, mode, shape, pulse width and detach time come from the source.
/// </summary>
public sealed class SourceElement
{
	/// <summary>X in canvas pixels.</summary>
	public int X { get; init; }

	/// <summary>Y in canvas pixels.</summary>
	public int Y { get; init; }

	/// <summary>Frequency level, up to 255, decimals allowed.</summary>
	public double Level { get; init; } = Source.DefaultLevel;

	/// <summary>Phase offset in degrees, decimals allowed.</summary>
	public double Phase { get; init; }

	/// <summary>Gap between bursts in cycles (pulsed mode).</summary>
	public int OffWidth { get; init; } = Source.DefaultWidth;
}
