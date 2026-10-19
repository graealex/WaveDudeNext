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

using WaveDudeNext.Model;

namespace WaveDudeNext.Export;

/// <summary>
/// The probe markers of an exported frame, in the author's style (his <c>ProbeOverlay.DrawMarkerPixels</c>):
/// a cross at a probe point, two crosses and the connecting line for a line probe, each stroke drawn twice,
/// black with a radius of 2 under white with a radius of 1, arms of max(6, height / 120) pixels. The
/// labels ("1", or "1A" and "1B") need a font and are drawn by the application (<c>ProbeGraphPainter</c>)
/// at the position <see cref="LabelOrigin"/> gives, bold Segoe UI at <see cref="FontSize"/> pixels with a
/// drop shadow. Any thread.
/// </summary>
public static class ProbeMarkers
{
	private const int Black = unchecked((int)0xFF000000);
	private const int White = unchecked((int)0xFFFFFFFF);

	/// <summary>Arm length of a cross for a frame height, in pixels.</summary>
	public static int ArmLength(int height) => Math.Max(6, height / 120);

	/// <summary>Label height for a frame height, in pixels.</summary>
	public static float FontSize(int height) => Math.Max(12f, height / 60f);

	/// <summary>Top-left corner of the label of the cross at (<paramref name="x"/>, <paramref name="y"/>): above and to the right, as the author placed it.</summary>
	public static (int X, int Y) LabelOrigin(int x, int y, int height)
	{
		int arm = ArmLength(height);
		return (x + arm + 2, y - arm - (int)Math.Round(FontSize(height) * 1.4f));
	}

	/// <summary>Draws the strokes of one probe's marker into a packed BGRA32 frame.</summary>
	/// <param name="pixels">The frame, width × height, modified in place.</param>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="probe">The probe; its positions are canvas pixels, the frame's.</param>
	public static void DrawStrokes(Span<int> pixels, int width, int height, Probe probe)
	{
		int arm = ArmLength(height);
		bool line = probe.Kind == ProbeKind.Line;
		for (int pass = 0; pass <= 1; pass++)
		{
			int color = pass == 0 ? Black : White;
			int radius = pass == 0 ? 2 : 1;
			if (line)
			{
				ThickLine(pixels, width, height, probe.AX, probe.AY, probe.BX, probe.BY, color, radius);
			}
			Cross(pixels, width, height, probe.AX, probe.AY, arm, color, radius);
			if (line)
			{
				Cross(pixels, width, height, probe.BX, probe.BY, arm, color, radius);
			}
		}
	}

	/// <summary>The label text of a probe end: the 1-based number, with "A"/"B" for a line probe's ends.</summary>
	public static string Label(int number, bool line, bool endB) => line ? number + (endB ? "B" : "A") : number.ToString(System.Globalization.CultureInfo.InvariantCulture);

	private static void Cross(Span<int> pixels, int width, int height, int x, int y, int arm, int color, int radius)
	{
		ThickLine(pixels, width, height, x - arm, y, x + arm, y, color, radius);
		ThickLine(pixels, width, height, x, y - arm, x, y + arm, color, radius);
	}

	/// <summary>A line of (2 × <paramref name="radius"/> + 1) pixels' thickness, stamped point by point, clipped to the frame.</summary>
	public static void ThickLine(Span<int> pixels, int width, int height, int x0, int y0, int x1, int y1, int color, int radius)
	{
		int dx = Math.Abs(x1 - x0);
		int dy = Math.Abs(y1 - y0);
		int steps = Math.Max(dx, dy);
		for (int i = 0; i <= steps; i++)
		{
			int x = steps == 0 ? x0 : x0 + (int)Math.Round((x1 - x0) * (double)i / steps);
			int y = steps == 0 ? y0 : y0 + (int)Math.Round((y1 - y0) * (double)i / steps);
			for (int yy = Math.Max(0, y - radius); yy <= Math.Min(height - 1, y + radius); yy++)
			{
				for (int xx = Math.Max(0, x - radius); xx <= Math.Min(width - 1, x + radius); xx++)
				{
					pixels[yy * width + xx] = color;
				}
			}
		}
	}
}
