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

/// <summary>
/// A simple 2D diffraction grating (the user's request, 2026-11-07): a lattice of round dots, <see cref="Columns"/> along
/// the axis by <see cref="Rows"/> across, <see cref="ColumnSpacing"/> and <see cref="RowSpacing"/> apart, each dot
/// <see cref="Diameter"/> pixels across, at most half the smaller spacing; nothing between the dots and no carrier (the
/// user's choices). The lattice is the composite source's: an odd count keeps the middle dot on the part's position, an
/// even one straddles it. The dots are of one material, that of a <see cref="ShapePart"/> (a mirror, or glass with an
/// index and absorption; a new grating is a mirror, <see cref="PrepareNew"/>); the coating of <see cref="CoatedPart"/>
/// is not used (too small). A new grating is one column of 31 rows, 16 px apart, 2 px dots: a line grating across the
/// axis, which a wave from the left diffracts at.
/// </summary>
public sealed class SimpleGratingPart : ShapePart
{
	/// <summary>Default <see cref="Columns"/>.</summary>
	public const int DefaultColumns = 1;
	/// <summary>Default <see cref="Rows"/>.</summary>
	public const int DefaultRows = 31;
	/// <summary>Default <see cref="ColumnSpacing"/> and <see cref="RowSpacing"/> in pixels.</summary>
	public const int DefaultSpacing = 16;
	/// <summary>Smallest spacing in pixels: room for a dot of a pixel.</summary>
	public const int MinSpacing = 2;
	/// <summary>Default <see cref="Diameter"/> in pixels.</summary>
	public const int DefaultDiameter = 2;

	private int _columns = DefaultColumns;
	private int _rows = DefaultRows;
	private int _columnSpacing = DefaultSpacing;
	private int _rowSpacing = DefaultSpacing;
	private int _diameter = DefaultDiameter;

	/// <inheritdoc/>
	public override string NameStem => "Grating";

	/// <summary>Number of dots along the axis; odd counts keep the middle dot on the part's position.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultColumns)]
	[Range(1, int.MaxValue)]
	[Category("Lattice")]
	[Description("Number of dots along the axis. Odd counts keep the middle dot on the part's position.")]
	public int Columns
	{
		get => _columns;
		set => SetProperty(ref _columns, value);
	}

	/// <summary>Number of dots across the axis.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultRows)]
	[Range(1, int.MaxValue)]
	[Category("Lattice")]
	[Description("Number of dots across the axis. Odd counts keep the middle dot on the part's position.")]
	public int Rows
	{
		get => _rows;
		set => SetProperty(ref _rows, value);
	}

	/// <summary>Distance between neighbouring columns along the axis in pixels; the largest dot is half of the smaller spacing.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSpacing)]
	[Range(MinSpacing, int.MaxValue)]
	[Category("Lattice")]
	[DisplayName("Column spacing")]
	[Description("Distance between neighbouring columns along the axis in pixels: the grating's period in that direction. The dots can be at most half the smaller spacing across.")]
	public int ColumnSpacing
	{
		get => _columnSpacing;
		set
		{
			if (SetProperty(ref _columnSpacing, value))
			{
				OnPropertyChanged(nameof(MaxDiameter));
			}
		}
	}

	/// <summary>Distance between neighbouring rows across the axis in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultSpacing)]
	[Range(MinSpacing, int.MaxValue)]
	[Category("Lattice")]
	[DisplayName("Row spacing")]
	[Description("Distance between neighbouring rows across the axis in pixels: the grating's period in that direction. The dots can be at most half the smaller spacing across.")]
	public int RowSpacing
	{
		get => _rowSpacing;
		set
		{
			if (SetProperty(ref _rowSpacing, value))
			{
				OnPropertyChanged(nameof(MaxDiameter));
			}
		}
	}

	/// <summary>Diameter of each dot in pixels, 1 to <see cref="MaxDiameter"/> (half the smaller spacing; a bound that depends on the spacings, so no <c>[Range]</c>).</summary>
	[XmlAttribute]
	[DefaultValue(DefaultDiameter)]
	[Category("Lattice")]
	[Description("Diameter of each dot in pixels, at most half the smaller spacing. A dot of 2 pixels on a whole-pixel position covers a square of 2 × 2 pixels; a mirror dot under 2 pixels covers no pixel centre and paints nothing.")]
	public int Diameter
	{
		get => _diameter;
		set => SetProperty(ref _diameter, value);
	}

	/// <summary>The largest <see cref="Diameter"/>: half the smaller spacing, 1 at least (the user's rule, 2026-11-07).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int MaxDiameter => Math.Max(1, Math.Min(Spacing(ColumnSpacing), Spacing(RowSpacing)) / 2);

	/// <summary>The diameter as the painter takes it: 1 to <see cref="MaxDiameter"/>.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public int GuardedDiameter => Math.Clamp(Diameter, 1, MaxDiameter);

	/// <summary>A spacing guarded to <see cref="MinSpacing"/> or more.</summary>
	public static int Spacing(int spacing) => Math.Max(MinSpacing, spacing);

	/// <summary>The dot centres in the part frame, column by column: (i − (columns − 1) / 2) × column spacing along the axis, the rows likewise across.</summary>
	public (double X, double Y)[] Points()
	{
		int columns = Math.Max(1, Columns), rows = Math.Max(1, Rows);
		double sx = Spacing(ColumnSpacing), sy = Spacing(RowSpacing);
		var points = new (double X, double Y)[columns * rows];
		for (int i = 0; i < columns; i++)
		{
			for (int j = 0; j < rows; j++)
			{
				points[i * rows + j] = ((i - (columns - 1) / 2.0) * sx, (j - (rows - 1) / 2.0) * sy);
			}
		}
		return points;
	}

	/// <summary>A new grating is a mirror: dots that reflect everything, the plainest grating (the user's choice, 2026-11-07).</summary>
	public override void PrepareNew() => Mirror = true;

	/// <summary>The box around the lattice, the dots' radius included: the outline the overlay draws and the reach of the rotation handle.</summary>
	public override (double X, double Y)[] Vertices()
	{
		double r = GuardedDiameter / 2.0;
		double l = (Math.Max(1, Columns) - 1) / 2.0 * Spacing(ColumnSpacing) + r;
		double w = (Math.Max(1, Rows) - 1) / 2.0 * Spacing(RowSpacing) + r;
		return [(-l, -w), (l, -w), (l, w), (-l, w)];
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Columns = Math.Max(1, Columns);
		Rows = Math.Max(1, Rows);
		ColumnSpacing = Spacing(ColumnSpacing);
		RowSpacing = Spacing(RowSpacing);
		Diameter = GuardedDiameter;
	}

	/// <inheritdoc/>
	public override OpticalPart Clone() => ShallowClone<SimpleGratingPart>();
}
