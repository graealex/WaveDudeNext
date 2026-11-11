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
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>How the two ends of a <see cref="FiberPart"/> are cut (2026-10-22, the user's request; the ends were round from the distance test before).</summary>
public enum FiberEnds
{
	/// <summary>Cut straight across the path's direction at the endpoint, like a cleaved fibre; the coating band follows the end face.</summary>
	Flat,
	/// <summary>Rounded with the fibre's outer radius.</summary>
	Round,
}

/// <summary>
/// One node of a <see cref="FiberPart"/> path (2026-10-22, the user's request: a Bézier path with handles instead of a
/// polyline): the point the path passes through, in canvas pixels relative to the part's centre before rotation, and
/// the two handles of the cubic segments meeting there, as offsets from the node. The in-handle shapes the segment
/// arriving from the previous node, the out-handle the one leaving to the next; a handle of zero length makes its
/// segment straight at this end, two zero handles a sharp corner. While <see cref="Smooth"/> is set, the handles are
/// kept opposite each other through the node: switching it on lines them up at once (<see cref="FiberPart.AlignNode"/>),
/// and afterwards a handle dragged (<see cref="SetHandle"/>) or typed (the part listens to its nodes) turns the other
/// with it, keeping its length. A node added to a part, or loaded from a file, is taken as it is.
/// </summary>
public sealed class FiberNode : ObservableObject
{
	private int _x;
	private int _y;
	private int _inX;
	private int _inY;
	private int _outX;
	private int _outY;
	private bool _smooth = true;

	/// <summary>X relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Node")]
	[Description("X relative to the part's centre in canvas pixels, before rotation.")]
	public int X
	{
		get => _x;
		set => SetProperty(ref _x, value);
	}

	/// <summary>Y relative to the part's centre in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[Category("Node")]
	[Description("Y relative to the part's centre in canvas pixels, before rotation.")]
	public int Y
	{
		get => _y;
		set => SetProperty(ref _y, value);
	}

	/// <summary>X of the in-handle relative to the node in canvas pixels, before rotation; it shapes the segment arriving from the previous node.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("In handle")]
	[DisplayName("In X")]
	[Description("X of the in-handle relative to the node, in canvas pixels before rotation. It shapes the segment arriving from the previous node; zero makes that segment straight at this end.")]
	public int InX
	{
		get => _inX;
		set => SetProperty(ref _inX, value);
	}

	/// <summary>Y of the in-handle relative to the node in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("In handle")]
	[DisplayName("In Y")]
	[Description("Y of the in-handle relative to the node, in canvas pixels before rotation.")]
	public int InY
	{
		get => _inY;
		set => SetProperty(ref _inY, value);
	}

	/// <summary>X of the out-handle relative to the node in canvas pixels, before rotation; it shapes the segment leaving to the next node.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Out handle")]
	[DisplayName("Out X")]
	[Description("X of the out-handle relative to the node, in canvas pixels before rotation. It shapes the segment leaving to the next node; zero makes that segment straight at this end.")]
	public int OutX
	{
		get => _outX;
		set => SetProperty(ref _outX, value);
	}

	/// <summary>Y of the out-handle relative to the node in canvas pixels, before rotation.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Category("Out handle")]
	[DisplayName("Out Y")]
	[Description("Y of the out-handle relative to the node, in canvas pixels before rotation.")]
	public int OutY
	{
		get => _outY;
		set => SetProperty(ref _outY, value);
	}

	/// <summary>Whether the editors keep the two handles opposite each other through the node, so the path runs smoothly through it; off, the node may be a corner.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Node")]
	[Description("Keep the two handles opposite each other through the node, so the path runs smoothly through it. Switching it on lines them up at once; afterwards moving or typing one handle turns the other with it, keeping its length. Off, the node may be a corner.")]
	public bool Smooth
	{
		get => _smooth;
		set => SetProperty(ref _smooth, value);
	}

	/// <summary>Set while <see cref="SetHandle"/> or <see cref="FiberPart.AlignNode"/> writes the handles, so the part's listener leaves the mirrored handle alone.</summary>
	internal bool Mirroring { get; set; }

	/// <summary>The in-handle as a point of the part frame: the node plus its offset.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public (double X, double Y) In => (X + InX, Y + InY);

	/// <summary>The out-handle as a point of the part frame: the node plus its offset.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public (double X, double Y) Out => (X + OutX, Y + OutY);

	/// <summary>
	/// Sets the out-handle (or the in-handle) to the offset (<paramref name="dx"/>, <paramref name="dy"/>); with
	/// <see cref="Smooth"/> the other handle is turned to the opposite direction, keeping <paramref name="otherLength"/>
	/// (its own length when null; an editor passes the length from before a drag, so the rounding of each step does
	/// not creep into it). A zero handle leaves the other alone.
	/// </summary>
	/// <param name="outward">True for the out-handle, false for the in-handle.</param>
	/// <param name="dx">The handle's X offset from the node in canvas pixels.</param>
	/// <param name="dy">The handle's Y offset from the node in canvas pixels.</param>
	/// <param name="otherLength">The length the other handle keeps, or null for its current length.</param>
	public void SetHandle(bool outward, int dx, int dy, double? otherLength = null)
	{
		Mirroring = true;
		try
		{
			if (outward)
			{
				OutX = dx;
				OutY = dy;
			}
			else
			{
				InX = dx;
				InY = dy;
			}
			double length = Math.Sqrt((double)dx * dx + (double)dy * dy);
			if (!Smooth || length == 0.0)
			{
				return;
			}
			(int otherX, int otherY) = outward ? (InX, InY) : (OutX, OutY);
			double keep = otherLength ?? Math.Sqrt((double)otherX * otherX + (double)otherY * otherY);
			int mirrorX = (int)Math.Round(-dx / length * keep);
			int mirrorY = (int)Math.Round(-dy / length * keep);
			if (outward)
			{
				InX = mirrorX;
				InY = mirrorY;
			}
			else
			{
				OutX = mirrorX;
				OutY = mirrorY;
			}
		}
		finally
		{
			Mirroring = false;
		}
	}

	/// <summary>The coordinates, for display.</summary>
	public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// A fibre: a core of <see cref="CoreDiameter"/> along a path of cubic Bézier segments through <see cref="Nodes"/>,
/// wrapped in a cladding of <see cref="CladdingThickness"/>; the ends cut <see cref="Ends"/>. The path is flattened
/// into chords (<see cref="Flatten"/>) for painting and drawing. The angle rotates the whole path about the centre.
/// </summary>
public sealed class FiberPart : CoatedPart
{
	/// <summary>Default <see cref="CoreIndex"/>.</summary>
	public const double DefaultCoreIndex = 1.5;
	/// <summary>Default <see cref="CoreDiameter"/> in pixels.</summary>
	public const int DefaultCoreDiameter = 20;
	/// <summary>Default <see cref="CladdingIndex"/>.</summary>
	public const double DefaultCladdingIndex = 1.45;
	/// <summary>Default <see cref="CladdingThickness"/> in pixels.</summary>
	public const int DefaultCladdingThickness = 10;
	/// <summary>Default <see cref="Ends"/>.</summary>
	public const FiberEnds DefaultEnds = FiberEnds.Flat;
	/// <summary>Default <see cref="AbsorptionRamp"/>, half the absorbing band's width in pixels.</summary>
	public const int DefaultAbsorptionBand = 10;
	/// <summary>Half the default path length in pixels: the path runs from (-100, 0) to (100, 0).</summary>
	public const int DefaultHalfLength = 100;
	/// <summary>How far a flattened chord may leave the curve, in canvas pixels: far below the painter's quarter-pixel samples.</summary>
	public const double FlattenTolerance = 0.05;

	private double _coreIndex = DefaultCoreIndex;
	private int _coreDiameter = DefaultCoreDiameter;
	private double _claddingIndex = DefaultCladdingIndex;
	private int _claddingThickness = DefaultCladdingThickness;
	private int _absorption;
	private int _absorptionRamp = DefaultAbsorptionBand;
	private FiberEnds _ends = DefaultEnds;
	private ObservableCollection<FiberNode> _nodes = [];
	private HashSet<FiberNode> _watched = [];

	/// <summary>A fibre with an empty path: a file's nodes or <see cref="DefaultPath"/> fill it.</summary>
	public FiberPart()
	{
		Watch(_nodes);
	}

	/// <inheritdoc/>
	public override string NameStem => "Fiber";

	/// <summary>A new fibre gets the <see cref="DefaultPath"/>.</summary>
	public override void PrepareNew() => Nodes = DefaultPath();

	/// <summary>Refractive index of the core, 1.00 to 2.55.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Core")]
	[DisplayName("Core index")]
	[Description("Refractive index of the core, 1.00 to 2.55 in steps of 0.01.")]
	public double CoreIndex
	{
		get => _coreIndex;
		set => SetProperty(ref _coreIndex, value);
	}

	/// <summary>Diameter of the core in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoreDiameter)]
	[Range(2, int.MaxValue)]
	[Category("Core")]
	[DisplayName("Core diameter")]
	[Description("Diameter of the core in pixels.")]
	public int CoreDiameter
	{
		get => _coreDiameter;
		set => SetProperty(ref _coreDiameter, value);
	}

	/// <summary>Refractive index of the cladding, 1.00 to 2.55; below the core's for guiding.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingIndex)]
	[Range(MinIndex, MaxIndex)]
	[Category("Cladding")]
	[DisplayName("Cladding index")]
	[Description("Refractive index of the cladding, 1.00 to 2.55 in steps of 0.01. Below the core's index, the fibre guides.")]
	public double CladdingIndex
	{
		get => _claddingIndex;
		set => SetProperty(ref _claddingIndex, value);
	}

	/// <summary>Thickness of the cladding around the core in pixels; 0 for a bare core.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCladdingThickness)]
	[Range(0, int.MaxValue)]
	[Category("Cladding")]
	[DisplayName("Cladding thickness")]
	[Description("Thickness of the cladding around the core in pixels; 0 for a bare core.")]
	public int CladdingThickness
	{
		get => _claddingThickness;
		set => SetProperty(ref _claddingThickness, value);
	}

	/// <summary>
	/// Peak absorption of the cladding's absorbing band, 0 (none) to 255, painted into the blue channel (2026-10-29, the
	/// user's request, after the author's fibre scene, whose fibres run between absorbing strips): a hump in the outer
	/// part of the cladding, rising over <see cref="AbsorptionRamp"/> pixels from nothing, peaking, and falling back to
	/// nothing at the outer edge, so the guided mode's tail in the inner cladding is untouched and stray light is
	/// absorbed from either side without a damping step; a mode stripper. Nothing on a bare core.
	/// </summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxAbsorption)]
	[Category("Cladding")]
	[DisplayName("Cladding absorption")]
	[Description("Peak absorption of the cladding's absorbing band, 0 (not at all) to 255, as the scene image's blue channel: a hump in the outer part of the cladding, rising over the band width from nothing, peaking, and falling to nothing at the outer edge, so the guided wave's tail stays untouched and stray light is absorbed from either side. Nothing on a bare core.")]
	public int Absorption
	{
		get => _absorption;
		set => SetProperty(ref _absorption, value);
	}

	/// <summary>Half the absorbing band's width in pixels: the absorption rises over this many pixels and falls over as many; a band wider than the cladding is squeezed into it.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultAbsorptionBand)]
	[Range(1, MaxAbsorptionRamp)]
	[Category("Cladding")]
	[DisplayName("Absorption band")]
	[Description("Half the absorbing band's width in pixels: the absorption rises over this many pixels to its peak and falls over as many to the outer edge. A band wider than the cladding is squeezed into it.")]
	public int AbsorptionRamp
	{
		get => _absorptionRamp;
		set => SetProperty(ref _absorptionRamp, value);
	}

	/// <summary>How the two ends are cut: straight across the path, or rounded.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultEnds)]
	[Category("Shape")]
	[DisplayName("Ends")]
	[Description("How the two ends are cut: flat, straight across the path like a cleaved fibre, or rounded with the outer radius. The AR coating follows a flat end face.")]
	public FiberEnds Ends
	{
		get => _ends;
		set => SetProperty(ref _ends, value);
	}

	/// <summary>The path's nodes relative to the centre, before rotation, each with the handles of the segments meeting there; fewer than two nodes paint nothing.</summary>
	[XmlElement("Node")]
	[Category("Shape")]
	[DisplayName("Nodes")]
	[Description("The path's nodes relative to the centre, before rotation, each with the handles of the cubic segments meeting there. Fewer than two nodes paint nothing.")]
	public ObservableCollection<FiberNode> Nodes
	{
		get => _nodes;
		set
		{
			if (ReferenceEquals(_nodes, value))
			{
				return;
			}
			Unwatch(_nodes);
			SetProperty(ref _nodes, value);
			Watch(_nodes);
		}
	}

	/// <summary>Outer radius in pixels: half the core diameter plus the cladding.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public double OuterRadius => CoreDiameter / 2.0 + CladdingThickness;

	/// <summary>The farthest any node or handle lies along x' (the curve stays within its handles' hull), plus the outer radius.</summary>
	public override double AxisReach => (Nodes.Count > 0 ? Nodes.Max(n => Math.Max(Math.Abs(n.X), Math.Max(Math.Abs(n.In.X), Math.Abs(n.Out.X)))) : 0) + OuterRadius;

	/// <summary>The path flattened into chords; the core and cladding bands show through the scene outline.</summary>
	public override (double X, double Y)[] Outline() => Flatten(Nodes);

	/// <inheritdoc/>
	public override bool OutlineClosed => false;

	/// <summary>
	/// The default straight path, from (-100, 0) to (100, 0), the handles a third of the way along it so they can be
	/// taken hold of. Given to a new fibre by <see cref="PrepareNew"/>, not by the constructor:
	/// <see cref="XmlSerializer"/> appends a file's nodes to the list it finds, so the constructor's list must be empty.
	/// </summary>
	public static ObservableCollection<FiberNode> DefaultPath()
	{
		int third = (int)Math.Round(2.0 * DefaultHalfLength / 3.0);
		return
		[
			new FiberNode { X = -DefaultHalfLength, Y = 0, OutX = third },
			new FiberNode { X = DefaultHalfLength, Y = 0, InX = -third },
		];
	}

	/// <summary>
	/// The path through <paramref name="nodes"/> as chords, in the part frame: the cubic segment between two nodes
	/// (the node, its out-handle, the next node's in-handle, the next node) is cut into as many equal steps of t as
	/// keep every chord within <see cref="FlattenTolerance"/> of the curve, a straight segment staying one chord;
	/// consecutive equal points are dropped, so no chord has zero length. Fewer than two nodes give an empty path.
	/// Any thread; reads the nodes once.
	/// </summary>
	public static (double X, double Y)[] Flatten(IList<FiberNode> nodes)
	{
		if (nodes.Count < 2)
		{
			return [];
		}
		var path = new List<(double X, double Y)>(nodes.Count * 8) { (nodes[0].X, nodes[0].Y) };
		for (int i = 1; i < nodes.Count; i++)
		{
			FiberNode a = nodes[i - 1];
			FiberNode b = nodes[i];
			double x0 = a.X, y0 = a.Y, x3 = b.X, y3 = b.Y;
			(double x1, double y1) = a.Out;
			(double x2, double y2) = b.In;
			// a segment whose handles lie on the chord between its nodes is straight and stays one chord; otherwise the
			// second derivative of a cubic is bounded by 6 × the larger second difference of its control points, and n
			// equal steps leave a chord error of at most that bound / (8 n²)
			int steps = 1;
			if (DistanceToSegment(x1, y1, x0, y0, x3, y3) > FlattenTolerance || DistanceToSegment(x2, y2, x0, y0, x3, y3) > FlattenTolerance)
			{
				double second = Math.Max(Math.Sqrt(Square(x0 - 2 * x1 + x2) + Square(y0 - 2 * y1 + y2)), Math.Sqrt(Square(x1 - 2 * x2 + x3) + Square(y1 - 2 * y2 + y3)));
				steps = Math.Clamp((int)Math.Ceiling(Math.Sqrt(0.75 * second / FlattenTolerance)), 1, 512);
			}
			for (int k = 1; k <= steps; k++)
			{
				double t = (double)k / steps;
				double u = 1.0 - t;
				double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
				(double X, double Y) point = (w0 * x0 + w1 * x1 + w2 * x2 + w3 * x3, w0 * y0 + w1 * y1 + w2 * y2 + w3 * y3);
				if (point != path[^1])
				{
					path.Add(point);
				}
			}
		}
		return path.Count < 2 ? [] : [.. path];
	}

	private static double Square(double v) => v * v;

	/// <summary>
	/// Lines up the handles of <paramref name="node"/> (2026-10-22, the user's choice for what Smooth means): the in-handle
	/// opposite the out-handle, both keeping their lengths; an in-handle of zero length (with a node before) or an
	/// out-handle of zero (with a node after) takes the other's length; a node with no handles at all gets them from its
	/// neighbours, along the line between them, a third of the way to each. A node not in the list, or alone, is left as it is.
	/// </summary>
	public void AlignNode(FiberNode node)
	{
		int i = Nodes.IndexOf(node);
		bool hasPrevious = i > 0;
		bool hasNext = i >= 0 && i < Nodes.Count - 1;
		double outLength = Length(node.OutX, node.OutY);
		double inLength = Length(node.InX, node.InY);
		node.Mirroring = true;
		try
		{
			if (outLength == 0.0 && inLength == 0.0)
			{
				if (!hasPrevious && !hasNext)
				{
					return;
				}
				(double fromX, double fromY) = hasPrevious ? (Nodes[i - 1].X, Nodes[i - 1].Y) : (node.X, node.Y);
				(double toX, double toY) = hasNext ? (Nodes[i + 1].X, Nodes[i + 1].Y) : (node.X, node.Y);
				double span = Length(toX - fromX, toY - fromY);
				if (span == 0.0)
				{
					return;
				}
				double tx = (toX - fromX) / span;
				double ty = (toY - fromY) / span;
				if (hasPrevious)
				{
					double third = Length(node.X - fromX, node.Y - fromY) / 3.0;
					node.InX = (int)Math.Round(-tx * third);
					node.InY = (int)Math.Round(-ty * third);
				}
				if (hasNext)
				{
					double third = Length(toX - node.X, toY - node.Y) / 3.0;
					node.OutX = (int)Math.Round(tx * third);
					node.OutY = (int)Math.Round(ty * third);
				}
			}
			else if (outLength > 0.0)
			{
				if (inLength == 0.0 && hasPrevious)
				{
					inLength = outLength;
				}
				node.InX = (int)Math.Round(-node.OutX / outLength * inLength);
				node.InY = (int)Math.Round(-node.OutY / outLength * inLength);
			}
			else
			{
				if (hasNext)
				{
					outLength = inLength;
				}
				node.OutX = (int)Math.Round(-node.InX / inLength * outLength);
				node.OutY = (int)Math.Round(-node.InY / inLength * outLength);
			}
		}
		finally
		{
			node.Mirroring = false;
		}
	}

	private static double Length(double x, double y) => Math.Sqrt(x * x + y * y);

	/// <summary>Listens to the list and its nodes for the smooth rule (<see cref="OnNodeChanged"/>).</summary>
	private void Watch(ObservableCollection<FiberNode>? nodes)
	{
		if (nodes == null)
		{
			return;
		}
		nodes.CollectionChanged += OnNodesChanged;
		foreach (FiberNode node in nodes)
		{
			WatchNode(node);
		}
	}

	private void Unwatch(ObservableCollection<FiberNode>? nodes)
	{
		if (nodes != null)
		{
			nodes.CollectionChanged -= OnNodesChanged;
		}
		foreach (FiberNode node in _watched)
		{
			node.PropertyChanged -= OnNodeChanged;
		}
		_watched.Clear();
	}

	private void WatchNode(FiberNode node)
	{
		if (_watched.Add(node))
		{
			node.PropertyChanged += OnNodeChanged;
		}
	}

	/// <summary>A node added is listened to from then on (as it is: no alignment on arrival, which keeps a file's nodes as written); a node removed is let go.</summary>
	private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			foreach (FiberNode node in _watched)
			{
				node.PropertyChanged -= OnNodeChanged;
			}
			_watched.Clear();
			foreach (FiberNode node in _nodes)
			{
				WatchNode(node);
			}
			return;
		}
		if (e.OldItems != null)
		{
			foreach (FiberNode node in e.OldItems)
			{
				if (_watched.Remove(node))
				{
					node.PropertyChanged -= OnNodeChanged;
				}
			}
		}
		if (e.NewItems != null)
		{
			foreach (FiberNode node in e.NewItems)
			{
				WatchNode(node);
			}
		}
	}

	/// <summary>
	/// The smooth rule: a node switched to smooth gets its handles lined up at once (<see cref="AlignNode"/>), and a handle
	/// of a smooth node set by itself (a typed value) turns the other handle opposite, keeping its length, as a drag does
	/// through <see cref="FiberNode.SetHandle"/>, which flags the node while it writes so its own mirror is left alone.
	/// </summary>
	private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not FiberNode node || node.Mirroring || !node.Smooth)
		{
			return;
		}
		switch (e.PropertyName)
		{
			case nameof(FiberNode.Smooth):
				AlignNode(node);
				break;
			case nameof(FiberNode.InX) or nameof(FiberNode.InY):
				node.SetHandle(false, node.InX, node.InY);
				break;
			case nameof(FiberNode.OutX) or nameof(FiberNode.OutY):
				node.SetHandle(true, node.OutX, node.OutY);
				break;
		}
	}

	/// <summary>Distance from (x, y) to the segment a–b.</summary>
	private static double DistanceToSegment(double x, double y, double ax, double ay, double bx, double by)
	{
		double dx = bx - ax;
		double dy = by - ay;
		double length2 = dx * dx + dy * dy;
		double t = length2 > 0.0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length2, 0.0, 1.0) : 0.0;
		return Math.Sqrt(Square(ax + t * dx - x) + Square(ay + t * dy - y));
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		CoreIndex = NormalizeIndex(CoreIndex);
		CladdingIndex = NormalizeIndex(CladdingIndex);
		CoreDiameter = Math.Max(2, CoreDiameter);
		CladdingThickness = Math.Max(0, CladdingThickness);
		Absorption = Math.Clamp(Absorption, 0, MaxAbsorption);
		AbsorptionRamp = Math.Clamp(AbsorptionRamp, 1, MaxAbsorptionRamp);
		if (!Enum.IsDefined(Ends))
		{
			Ends = DefaultEnds;
		}
		Nodes ??= [];
	}

	/// <inheritdoc/>
	public override OpticalPart Clone()
	{
		var copy = ShallowClone<FiberPart>();
		// the memberwise copy shares the original's set of watched nodes and listens to nothing yet: a fresh set, then
		// the new list through the setter, which subscribes
		copy._watched = [];
		copy.Nodes = new ObservableCollection<FiberNode>(Nodes.Select(n => new FiberNode { X = n.X, Y = n.Y, InX = n.InX, InY = n.InY, OutX = n.OutX, OutY = n.OutY, Smooth = n.Smooth }));
		return copy;
	}
}
