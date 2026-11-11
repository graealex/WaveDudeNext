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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>One vertex of an <see cref="OutlinePart"/>, in canvas pixels relative to the part's centre, before rotation.</summary>
public sealed class PartPoint : ObservableObject
{
	private int _x;
	private int _y;

	/// <summary>X relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Point")]
	[Description("X relative to the part's centre in canvas pixels, before rotation.")]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Point")]
	[Description("Y relative to the part's centre in canvas pixels, before rotation.")]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>The coordinates, for display.</summary>
	public override string ToString() => $"({X}, {Y})";
}

/// <summary>A free outline from <see cref="Points"/>, relative to the centre before rotation; fewer than three points paint nothing.</summary>
public sealed class OutlinePart : ShapePart
{
	private ObservableCollection<PartPoint> _points = [];

	/// <inheritdoc/>
	public override string NameStem => "Outline";

	/// <summary>The outline's vertices relative to the centre, before rotation; fewer than three points paint nothing.</summary>
	[XmlElement("Point")]
	[Category("Shape")]
	[DisplayName("Points")]
	[Description("The outline's vertices relative to the centre, before rotation. Fewer than three points paint nothing.")]
	public ObservableCollection<PartPoint> Points
	{
		get => _points;
		set => SetProperty(ref _points, value);
	}

	/// <inheritdoc/>
	public override (double X, double Y)[] Vertices() => Points.Select(p => ((double)p.X, (double)p.Y)).ToArray();

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Points ??= [];
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<OutlinePart>();
		copy.Points = ClonePoints(Points);
		return copy;
	}
}
