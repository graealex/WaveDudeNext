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

using WaveDudeNext.Model;

namespace WaveDudeNext.Parts;

/// <summary>
/// The geometry of one <see cref="OpticalPart"/> as the painter sees it: a point of the canvas is mapped into the
/// part's frame (the centre at the canvas point (X, Y), the top-left corner of pixel (X, Y), so a part of even size
/// covers whole pixels; x' along the axis turned by the angle, counter-clockwise on the screen, y' across) and classified by <see cref="Depth"/>, how far inside the outline it lies, and
/// <see cref="BodyIndex"/>, the refractive index there. <see cref="IndexAt"/> combines both with the coating band.
/// Immutable once built from a part; any thread.
/// </summary>
public abstract class PartShape
{
	private readonly double _centreX;
	private readonly double _centreY;
	private readonly double _cos;
	private readonly double _sin;

	/// <param name="part">The part; its position and angle are read once.</param>
	protected PartShape(OpticalPart part)
	{
		_centreX = part.X;
		_centreY = part.Y;
		double radians = part.Angle * Math.PI / 180.0;
		_cos = Math.Cos(radians);
		_sin = Math.Sin(radians);
	}

	/// <summary>
	/// The shape of <paramref name="part"/>, or null when it has nothing to paint (an outline of fewer than three
	/// points, a path of fewer than two). The part is read as it is, out-of-range values guarded here rather than
	/// written back: a form edit reaches the painter before the model's <see cref="OpticalPart.Normalize"/> runs,
	/// and normalising from inside a change handler would re-enter it.
	/// </summary>
	public static PartShape? For(OpticalPart part)
	{
		return part switch
		{
			LensPart lens => new LensShape(lens),
			ShapePart shape => shape.Vertices().Length < 3 ? null : new VertexShape(shape),
			FiberPart fiber => fiber.Points.Count < 2 ? null : new FiberShape(fiber),
			BraggReflectorPart bragg => new BraggShape(bragg),
			GrinLensPart grin => new GrinShape(grin),
			_ => null,
		};
	}

	/// <summary>Whether the part is a mirror: painted as red 0 by the pixel-centre test, no index, no coating.</summary>
	public abstract bool IsMirror { get; }

	/// <summary>Thickness of the anti-reflection band inside the outline in pixels; 0 for none.</summary>
	protected abstract int Coating { get; }

	/// <summary>Farthest the outline reaches from the centre, in pixels (a bound, not necessarily tight).</summary>
	protected abstract double Extent { get; }

	/// <summary>How far inside the outline the point (x', y') of the part frame lies, in pixels; negative outside. Exact for straight and circular edges.</summary>
	protected abstract double Depth(double x, double y);

	/// <summary>The refractive index at (x', y') of the part frame, the point being inside; a mirror returns 0.</summary>
	protected abstract double BodyIndex(double x, double y);

	/// <summary>The pixel rectangle (inclusive) the shape can touch, clipped to the canvas; empty (x0 &gt; x1) when it lies outside.</summary>
	public (int X0, int Y0, int X1, int Y1) Bounds(int width, int height)
	{
		double extent = Extent + 1.0;
		int x0 = Math.Max(0, (int)Math.Floor(_centreX - extent));
		int y0 = Math.Max(0, (int)Math.Floor(_centreY - extent));
		int x1 = Math.Min(width - 1, (int)Math.Ceiling(_centreX + extent));
		int y1 = Math.Min(height - 1, (int)Math.Ceiling(_centreY + extent));
		return (x0, y0, x1, y1);
	}

	/// <summary>
	/// The refractive index the part gives the canvas point (<paramref name="canvasX"/>, <paramref name="canvasY"/>)
	/// (continuous coordinates, pixel (x, y) spanning [x, x + 1)): null outside the outline, √n rounded to 0.01 in the
	/// coating band, else the body index; 0 inside a mirror.
	/// </summary>
	public double? IndexAt(double canvasX, double canvasY)
	{
		// the inverse of the composite source's turn (x = dx cos + dy sin, y = dy cos - dx sin): the transpose
		double ox = canvasX - _centreX;
		double oy = canvasY - _centreY;
		double x = ox * _cos - oy * _sin;
		double y = ox * _sin + oy * _cos;
		double depth = Depth(x, y);
		if (depth < 0.0)
		{
			return null;
		}
		if (IsMirror)
		{
			return 0.0;
		}
		double index = BodyIndex(x, y);
		return Coating > 0 && depth < Coating ? Math.Round(Math.Sqrt(index), 2) : index;
	}

	/// <summary>A refractive index within the part range.</summary>
	protected static double GuardIndex(double index) => double.IsFinite(index) ? Math.Clamp(index, OpticalPart.MinIndex, OpticalPart.MaxIndex) : OpticalPart.MinIndex;

	/// <summary>A coating thickness within range.</summary>
	protected static int GuardCoating(int coating) => Math.Clamp(coating, 0, OpticalPart.MaxCoating);

	/// <summary>Signed distance of (x, y) inside a simple polygon: the distance to the nearest edge, negative outside (even-odd rule).</summary>
	protected static double PolygonDepth(ReadOnlySpan<(double X, double Y)> vertices, double x, double y)
	{
		bool inside = false;
		double nearest = double.PositiveInfinity;
		for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
		{
			(double ax, double ay) = vertices[j];
			(double bx, double by) = vertices[i];
			if (ay > y != by > y)
			{
				double crossX = ax + (y - ay) / (by - ay) * (bx - ax);
				if (x < crossX)
				{
					inside = !inside;
				}
			}
			nearest = Math.Min(nearest, SegmentDistance(ax, ay, bx, by, x, y));
		}
		return inside ? nearest : -nearest;
	}

	/// <summary>Distance from (x, y) to the segment a–b.</summary>
	protected static double SegmentDistance(double ax, double ay, double bx, double by, double x, double y)
	{
		double dx = bx - ax;
		double dy = by - ay;
		double length2 = dx * dx + dy * dy;
		double t = length2 > 0.0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length2, 0.0, 1.0) : 0.0;
		double px = ax + t * dx - x;
		double py = ay + t * dy - y;
		return Math.Sqrt(px * px + py * py);
	}

	private sealed class LensShape : PartShape
	{
		private readonly double _half;
		private readonly double _vertex1;
		private readonly double _vertex2;
		private readonly double _r1;
		private readonly double _r2;
		private readonly double _index;
		private readonly double _extent;

		public LensShape(LensPart lens) : base(lens)
		{
			_half = Math.Max(1, lens.Diameter) / 2.0;
			_vertex1 = -Math.Max(1, lens.Thickness) / 2.0;
			_vertex2 = -_vertex1;
			_r1 = GuardRadius(lens.R1, _half);
			_r2 = GuardRadius(lens.R2, _half);
			_index = GuardIndex(lens.RefractiveIndex);
			IsMirror = lens.Mirror;
			Coating = lens.Mirror ? 0 : GuardCoating(lens.Coating);
			// the surfaces stay within the vertex and its sag at the lens edge, convex or concave
			double reach = _vertex2 + Math.Max(Sag(_r1), Sag(_r2));
			_extent = Math.Sqrt(reach * reach + _half * _half);
		}

		public override bool IsMirror { get; }
		protected override int Coating { get; }
		protected override double Extent => _extent;
		protected override double BodyIndex(double x, double y) => _index;

		/// <summary>How far a surface of radius <paramref name="radius"/> bows out beyond its vertex at the lens edge.</summary>
		private double Sag(double radius) => radius == 0.0 ? 0.0 : Math.Abs(radius) - Math.Sqrt(radius * radius - _half * _half);

		/// <summary>A radius of at least <paramref name="half"/> in magnitude, its sign kept; 0 stays flat (the model's rule, applied without writing back).</summary>
		private static double GuardRadius(double radius, double half) =>
			!double.IsFinite(radius) || radius == 0.0 ? 0.0 : Math.Abs(radius) < half ? Math.CopySign(half, radius) : radius;

		/// <summary>The least of: inside the first surface, inside the second, within the half-diameter.</summary>
		protected override double Depth(double x, double y) =>
			Math.Min(Math.Min(SurfaceDepth(x, y, _vertex1, _r1, -1), SurfaceDepth(x, y, _vertex2, _r2, +1)), _half - Math.Abs(y));

		/// <summary>
		/// How far inside one surface (x', y') lies: the surface has its vertex at <paramref name="vertex"/> on the axis,
		/// the lens interior on the side opposite to <paramref name="side"/> (-1 for the first surface, whose interior is
		/// toward +x'). A radius of 0 is flat; the circle's centre is at vertex + radius, so the surface is convex when
		/// radius and side have opposite signs. Distances are to the circle, so the coating band keeps its thickness
		/// along the normal.
		/// </summary>
		private static double SurfaceDepth(double x, double y, double vertex, double radius, int side)
		{
			if (radius == 0.0)
			{
				return (x - vertex) * -side;
			}
			double centre = vertex + radius;
			double along = (x - centre) * -side;        // positive past the centre in the interior direction
			double distance = Math.Sqrt((x - centre) * (x - centre) + y * y);
			double magnitude = Math.Abs(radius);
			if (radius * side < 0.0)
			{
				// convex: inside the circle, or beyond its centre (the arc is only the outer half)
				return along > 0.0 ? magnitude : magnitude - distance;
			}
			// concave: outside the circle and on the interior side of its centre
			return along < 0.0 ? double.NegativeInfinity : distance - magnitude;
		}
	}

	/// <summary>A polygon, rectangle, triangle or outline: the part's vertices as a simple polygon.</summary>
	private sealed class VertexShape : PartShape
	{
		private readonly (double X, double Y)[] _vertices;
		private readonly double _index;
		private readonly double _extent;

		public VertexShape(ShapePart shape) : base(shape)
		{
			_vertices = shape.Vertices();
			_index = GuardIndex(shape.RefractiveIndex);
			IsMirror = shape.Mirror;
			Coating = shape.Mirror ? 0 : GuardCoating(shape.Coating);
			_extent = _vertices.Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y));
		}

		public override bool IsMirror { get; }
		protected override int Coating { get; }
		protected override double Extent => _extent;
		protected override double Depth(double x, double y) => PolygonDepth(_vertices, x, y);
		protected override double BodyIndex(double x, double y) => _index;
	}

	private sealed class FiberShape : PartShape
	{
		private readonly (double X, double Y)[] _path;
		private readonly double _coreRadius;
		private readonly double _outerRadius;
		private readonly double _coreIndex;
		private readonly double _claddingIndex;
		private readonly double _extent;

		public FiberShape(FiberPart fiber) : base(fiber)
		{
			_path = fiber.Points.Select(p => ((double)p.X, (double)p.Y)).ToArray();
			_coreRadius = Math.Max(1, fiber.CoreDiameter) / 2.0;
			_outerRadius = _coreRadius + Math.Max(0, fiber.CladdingThickness);
			_coreIndex = GuardIndex(fiber.CoreIndex);
			_claddingIndex = fiber.CladdingThickness > 0 ? GuardIndex(fiber.CladdingIndex) : _coreIndex;
			Coating = GuardCoating(fiber.Coating);
			_extent = _path.Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y)) + _outerRadius;
		}

		public override bool IsMirror => false;
		protected override int Coating { get; }
		protected override double Extent => _extent;
		protected override double Depth(double x, double y) => _outerRadius - PathDistance(x, y);
		protected override double BodyIndex(double x, double y) => PathDistance(x, y) <= _coreRadius ? _coreIndex : _claddingIndex;

		/// <summary>Distance to the nearest segment of the path (round ends and joints follow).</summary>
		private double PathDistance(double x, double y)
		{
			double nearest = double.PositiveInfinity;
			for (int i = 1; i < _path.Length; i++)
			{
				nearest = Math.Min(nearest, SegmentDistance(_path[i - 1].X, _path[i - 1].Y, _path[i].X, _path[i].Y, x, y));
			}
			return nearest;
		}
	}

	private sealed class BraggShape : PartShape
	{
		private readonly double _halfLength;
		private readonly double _halfWidth;
		private readonly double _period;
		private readonly double _thickness1;
		private readonly double _index1;
		private readonly double _index2;

		public BraggShape(BraggReflectorPart bragg) : base(bragg)
		{
			_thickness1 = Math.Max(1, bragg.Thickness1);
			_period = _thickness1 + Math.Max(1, bragg.Thickness2);
			_halfLength = Math.Clamp(bragg.Pairs, 1, BraggReflectorPart.MaxPairs) * _period / 2.0;
			_halfWidth = Math.Max(1, bragg.Width) / 2.0;
			_index1 = GuardIndex(bragg.Index1);
			_index2 = GuardIndex(bragg.Index2);
		}

		public override bool IsMirror => false;
		protected override int Coating => 0;
		protected override double Extent => Math.Sqrt(_halfLength * _halfLength + _halfWidth * _halfWidth);
		protected override double Depth(double x, double y) => Math.Min(_halfLength - Math.Abs(x), _halfWidth - Math.Abs(y));

		/// <summary>The first layer of a pair starts at -L/2 and the pairs repeat along +x'.</summary>
		protected override double BodyIndex(double x, double y)
		{
			double along = (x + _halfLength) % _period;
			return along < _thickness1 ? _index1 : _index2;
		}
	}

	private sealed class GrinShape : PartShape
	{
		private readonly double _halfLength;
		private readonly double _halfDiameter;
		private readonly double _centreIndex;
		private readonly double _edgeIndex;

		public GrinShape(GrinLensPart grin) : base(grin)
		{
			_halfLength = Math.Max(1, grin.Length) / 2.0;
			_halfDiameter = Math.Max(1, grin.Diameter) / 2.0;
			_centreIndex = GuardIndex(grin.CentreIndex);
			_edgeIndex = GuardIndex(grin.EdgeIndex);
			Coating = GuardCoating(grin.Coating);
		}

		public override bool IsMirror => false;
		protected override int Coating { get; }
		protected override double Extent => Math.Sqrt(_halfLength * _halfLength + _halfDiameter * _halfDiameter);
		protected override double Depth(double x, double y) => Math.Min(_halfLength - Math.Abs(x), _halfDiameter - Math.Abs(y));

		/// <summary>n(y') = n_c − (n_c − n_e)·(y' / (D/2))².</summary>
		protected override double BodyIndex(double x, double y)
		{
			double t = y / _halfDiameter;
			return _centreIndex - (_centreIndex - _edgeIndex) * t * t;
		}
	}
}
