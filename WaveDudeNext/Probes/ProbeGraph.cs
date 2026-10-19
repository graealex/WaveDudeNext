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
using System.Numerics;
using WaveDudeNext.Model;

namespace WaveDudeNext.Probes;

/// <summary>A size in canvas pixels.</summary>
/// <param name="Width">Width in canvas pixels.</param>
/// <param name="Height">Height in canvas pixels.</param>
public readonly record struct BoxSize(int Width, int Height);

/// <summary>A rectangle in canvas pixels, origin at the top left.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width, at least 1.</param>
/// <param name="Height">Height, at least 1.</param>
public readonly record struct BoxRect(int X, int Y, int Width, int Height);

/// <summary>One tick of a graph axis.</summary>
/// <param name="Position">Pixel position along the axis, relative to the box's top-left corner (x for the step/position axis, y for the value axis).</param>
/// <param name="Label">The tick's text.</param>
/// <param name="IsZero">Whether this is the zero line, drawn brighter than the grid.</param>
public readonly record struct GraphTick(float Position, string Label, bool IsZero);

/// <summary>
/// What a probe graph shows at one instant: the first <see cref="Count"/> samples of a point probe's series
/// (value i belongs to step i + 1), or the profile of a line probe at that instant. Taken from a
/// <see cref="ProbeTrace"/> with <see cref="At"/> for the frame the preview shows; <c>default</c> is an
/// empty graph. The arrays are the trace's own and are only read.
/// </summary>
/// <param name="Values">The series' backing array, or null; only the first <see cref="Count"/> entries are shown.</param>
/// <param name="Count">Samples shown, 0 for none.</param>
/// <param name="Profile">The line profile of the instant, or null for none.</param>
/// <param name="ProfileLength">Length of the probe line in canvas pixels, the x span of a profile graph.</param>
/// <param name="StepsSampled">Steps completed at the instant shown; gates a line probe's "start after step".</param>
public readonly record struct ProbeGraphData(float[]? Values, int Count, float[]? Profile, double ProfileLength, int StepsSampled)
{
	/// <summary>
	/// The trace as it stood when frame <paramref name="frame"/> was recorded, after 0-based step
	/// <paramref name="step"/>: the series cut off after that step (fewer when the run has not got there), and
	/// the profile recorded with that frame. When that frame's profile has not arrived yet (a run delivers its
	/// probe batches a little behind its frames, and a scrub can run ahead of it) the newest profile stands in,
	/// so the graph never drops to an empty axis for a moment (the user's flicker, 2026-10-14).
	/// </summary>
	/// <param name="trace">The probe's trace.</param>
	/// <param name="step">The 0-based index of the step shown (frame × visualise-every).</param>
	/// <param name="frame">The frame shown.</param>
	public static ProbeGraphData At(ProbeTrace trace, int step, int frame)
	{
		int count = Math.Clamp(step + 1, 0, trace.Series.Count);
		int profiles = trace.Profiles.Count;
		float[]? profile = profiles == 0 ? null : trace.Profiles[Math.Clamp(frame, 0, profiles - 1)];
		return new ProbeGraphData(trace.Series.Values, count, profile, trace.ProfileLength, Math.Min(trace.StepsSampled, step + 1));
	}
}

/// <summary>
/// How a renderer sets the text of a graph box: the font size and line height in the box's pixels, and how
/// wide a given label comes out. The text does not scale with the box (the author's rule, box height over
/// 14, gave 9 px labels on small boxes and 20 px on large ones, 2026-10-14): the preview passes its fixed
/// UI size, an export the size it wants in canvas pixels.
/// </summary>
/// <param name="fontSize">Font height in pixels.</param>
/// <param name="lineHeight">Height of one text line in pixels (what the renderer reserves for a label).</param>
/// <param name="measure">Width of a label in pixels, as the renderer will draw it.</param>
public sealed class ProbeGraphStyle(float fontSize, float lineHeight, Func<string, float> measure)
{
	/// <summary>Font height in pixels.</summary>
	public float FontSize { get; } = fontSize;

	/// <summary>Height of one text line in pixels.</summary>
	public float LineHeight { get; } = lineHeight;

	/// <summary>Width of a label in pixels.</summary>
	public Func<string, float> Measure { get; } = measure;

	/// <summary>A style without a renderer: 0.6 font sizes per character and 1.3 font sizes per line, for harnesses.</summary>
	public static ProbeGraphStyle Estimate(float fontSize) => new(fontSize, fontSize * 1.3f, text => text.Length * fontSize * 0.6f);
}

/// <summary>
/// The geometry of one probe graph box, in pixels relative to the box's top-left corner, ready to be drawn by
/// any renderer: the plot area, the ticks of both axes with their labels, the curve as a polyline (already
/// reduced to a min/max pair per pixel column when the data is denser than the plot), the title and the
/// readout. Produced by <see cref="ProbeGraph.Layout"/>. Immutable.
/// </summary>
/// <param name="Width">Box width in pixels.</param>
/// <param name="Height">Box height in pixels.</param>
/// <param name="IsVisible">False when the box is too small to draw at all (under 40 × 30).</param>
/// <param name="HasPlot">False when only the box, its border and title fit (plot under 20 × 15 even without labels); the axes and curve are then empty.</param>
/// <param name="FontSize">Label and title font height in pixels (the style's).</param>
/// <param name="LineWidth">Curve stroke width in pixels.</param>
/// <param name="PlotX">Left edge of the plot area.</param>
/// <param name="PlotY">Top edge of the plot area.</param>
/// <param name="PlotWidth">Width of the plot area.</param>
/// <param name="PlotHeight">Height of the plot area.</param>
/// <param name="YTicks">Value-axis ticks: horizontal grid lines at y = Position, labelled right-aligned in the left margin. Labels are empty when the box is too small for them (<see cref="ShowLabels"/>).</param>
/// <param name="XTicks">Step or position axis ticks: vertical grid lines at x = Position, labelled centred below the plot (kept inside the box).</param>
/// <param name="ShowLabels">Whether the tick labels fit; without them the plot takes the margins.</param>
/// <param name="Curve">The curve's points in order; one point is a dot, none is nothing. Clip to the plot area when stroking.</param>
/// <param name="Title">The box title (the probe's name).</param>
/// <param name="Readout">Live readout for the title strip's right end ("= value" or "max value"), empty for none.</param>
public sealed record ProbeGraphLayout(
	float Width,
	float Height,
	bool IsVisible,
	bool HasPlot,
	float FontSize,
	float LineWidth,
	float PlotX,
	float PlotY,
	float PlotWidth,
	float PlotHeight,
	IReadOnlyList<GraphTick> YTicks,
	IReadOnlyList<GraphTick> XTicks,
	bool ShowLabels,
	Vector2[] Curve,
	string Title,
	string Readout)
{
	/// <summary>Left edge of the y labels' column (right-aligned up to <see cref="PlotX"/> − <see cref="ProbeGraph.LabelGap"/>).</summary>
	public float LabelLeft => ProbeGraph.EdgePad;

	/// <summary>Top of the title strip's text.</summary>
	public float TitleTop => ProbeGraph.EdgePad;
}

/// <summary>
/// The numbers behind a probe graph box (a port of the author's GDI+ painter without the drawing): the box
/// rectangle on the canvas, the axis ranges, "nice" ticks, the curve reduced to the pixel grid, and the readout.
/// Everything is pure arithmetic over a <see cref="Probe"/>'s settings and a <see cref="ProbeGraphData"/>, so
/// the WPF preview and a later export draw the same graph. Thread-free: no state.
/// <list type="bullet">
/// <item><description>Point probe: value (y) against the simulation step (x), either the whole run or a running window.</description></item>
/// <item><description>Line probe: value (y) against the position on the line A–B in canvas pixels (x), at the instant shown.</description></item>
/// </list>
/// </summary>
public static class ProbeGraph
{
	/// <summary>Smallest box that is drawn at all, in pixels.</summary>
	public const int MinVisibleWidth = 40;

	/// <summary>Smallest box that is drawn at all, in pixels.</summary>
	public const int MinVisibleHeight = 30;

	/// <summary>Space between the box edge and its text, in pixels.</summary>
	public const float EdgePad = 3f;

	/// <summary>Space between a label and the plot frame, in pixels.</summary>
	public const float LabelGap = 4f;

	/// <summary>Right margin of the plot, in pixels (room for the last x label to overhang).</summary>
	private const float RightMargin = 8f;

	/// <summary>Margin on a side whose labels were dropped, in pixels.</summary>
	private const float BareMargin = 6f;

	/// <summary>Least space between two x labels, in pixels.</summary>
	private const float XLabelSpacing = 8f;

	/// <summary>Smallest plot that is drawn, in pixels.</summary>
	private const float MinPlotWidth = 20f;
	private const float MinPlotHeight = 15f;

	/// <summary>The fixed box sizes, in the order of the enum, for snapping a dragged size.</summary>
	private static readonly ProbeBoxSize[] Presets = [ProbeBoxSize.Tiny, ProbeBoxSize.Small, ProbeBoxSize.Medium, ProbeBoxSize.Large, ProbeBoxSize.ExtraLarge];

	/// <summary>Unscaled box size in canvas pixels for a fixed box size; Medium (and any unknown value, Custom included) is 400 × 200.</summary>
	public static BoxSize NominalBoxSize(ProbeBoxSize boxSize) => boxSize switch
	{
		ProbeBoxSize.Tiny => new BoxSize(200, 100),
		ProbeBoxSize.Small => new BoxSize(300, 150),
		ProbeBoxSize.Large => new BoxSize(500, 250),
		ProbeBoxSize.ExtraLarge => new BoxSize(700, 350),
		_ => new BoxSize(Probe.DefaultBoxWidth, Probe.DefaultBoxHeight),
	};

	/// <summary>The probe's box size in canvas pixels: its own width and height for a custom size, else the fixed size.</summary>
	public static BoxSize BoxSizeOf(Probe probe) => probe.BoxSize == ProbeBoxSize.Custom
		? new BoxSize(Math.Clamp(probe.BoxWidth, Probe.MinBoxWidth, Probe.MaxBoxSide), Math.Clamp(probe.BoxHeight, Probe.MinBoxHeight, Probe.MaxBoxSide))
		: NominalBoxSize(probe.BoxSize);

	/// <summary>
	/// The graph box of a probe on a canvas: its size, shrunk (keeping the aspect ratio) only when it is wider
	/// or taller than the canvas, placed at the probe's box position and clamped to stay inside the canvas. Until
	/// 2026-10-18 a box was held to half the canvas width (Wave Dude 2D's rule for its fixed sizes), which cut a
	/// custom size below the typed values without a word.
	/// </summary>
	/// <param name="probe">Provides the box size and the wanted position.</param>
	/// <param name="w">Canvas width in pixels.</param>
	/// <param name="h">Canvas height in pixels.</param>
	/// <returns>The box in canvas pixels, never smaller than 1 × 1.</returns>
	public static BoxRect BoxInCanvas(Probe probe, int w, int h)
	{
		BoxSize nominal = BoxSizeOf(probe);
		double scale = 1.0;
		if (nominal.Width > w)
		{
			scale = Math.Min(scale, (double)w / nominal.Width);
		}
		if (nominal.Height > h)
		{
			scale = Math.Min(scale, (double)h / nominal.Height);
		}
		int boxWidth = Math.Max(1, (int)Math.Round(nominal.Width * scale));
		int boxHeight = Math.Max(1, (int)Math.Round(nominal.Height * scale));
		int boxX = Math.Max(0, Math.Min(w - boxWidth, probe.BoxX));
		int boxY = Math.Max(0, Math.Min(h - boxHeight, probe.BoxY));
		return new BoxRect(boxX, boxY, boxWidth, boxHeight);
	}

	/// <summary>
	/// The box size setting for a size the user dragged: a fixed size when both sides are within 8 % (at
	/// least 8 pixels) of it, else a custom size clamped to the accepted range. Applied by the preview when the
	/// box's corner is dragged, so the fixed sizes act as snap points.
	/// </summary>
	/// <param name="width">Wanted width in canvas pixels.</param>
	/// <param name="height">Wanted height in canvas pixels.</param>
	/// <returns>The size setting and the size it stands for.</returns>
	public static (ProbeBoxSize Size, BoxSize Box) Snap(int width, int height)
	{
		foreach (ProbeBoxSize preset in Presets)
		{
			BoxSize nominal = NominalBoxSize(preset);
			if (Math.Abs(width - nominal.Width) <= Math.Max(8, nominal.Width * 0.08) && Math.Abs(height - nominal.Height) <= Math.Max(8, nominal.Height * 0.08))
			{
				return (preset, nominal);
			}
		}
		return (ProbeBoxSize.Custom, new BoxSize(Math.Clamp(width, Probe.MinBoxWidth, Probe.MaxBoxSide), Math.Clamp(height, Probe.MinBoxHeight, Probe.MaxBoxSide)));
	}

	/// <summary>
	/// The axis ranges of a probe graph. For series graphs x is the step number (sample i sits at x = i + 1),
	/// spanning either the whole run or a running window; for profiles x is the distance along the line in
	/// canvas pixels. y comes from the probe's fixed range (falling back to -1..1 when it is empty or inverted)
	/// or, with auto-scaling, from the visible data plus a small margin.
	/// </summary>
	/// <param name="probe">Selects kind, scale mode, fixed y range and auto-scaling.</param>
	/// <param name="data">The data shown; <c>default</c> yields the ranges of an empty graph.</param>
	/// <param name="totalSteps">Total steps of the run; the upper x limit of full-scale series graphs without auto-scaling.</param>
	/// <param name="xMin">Receives the lower x limit.</param>
	/// <param name="xMax">Receives the upper x limit, always greater than <paramref name="xMin"/>.</param>
	/// <param name="yMin">Receives the lower y limit.</param>
	/// <param name="yMax">Receives the upper y limit, always greater than <paramref name="yMin"/>.</param>
	public static void ComputeRanges(Probe probe, in ProbeGraphData data, int totalSteps, out double xMin, out double xMax, out double yMin, out double yMax)
	{
		if (probe.Kind == ProbeKind.Line)
		{
			ProfileRanges(probe, data, out xMin, out xMax, out yMin, out yMax);
		}
		else
		{
			SeriesRanges(probe, data, totalSteps, Math.Max(0, probe.StartAfter), out xMin, out xMax, out yMin, out yMax);
		}
	}

	/// <summary>
	/// Ranges of a time-series graph. Full mode spans from <paramref name="startStep"/> to the samples shown
	/// (auto-scaled x) or to the run length. Running mode shows a window of the probe's <see cref="Probe.Window"/>
	/// (at least 10) steps that slides with the samples shown; auto-scaled it starts narrow and grows to full
	/// width, otherwise it is full width from the start. Auto-scaled y is fitted to the samples inside the x
	/// range. With "start after step" the samples before that step are trimmed off: x never goes below it, so
	/// the visible range starts there (with more room for the data that follows, especially at full scale,
	/// which would otherwise show mostly empty space before the data starts).
	/// </summary>
	private static void SeriesRanges(Probe probe, in ProbeGraphData data, int totalSteps, double startStep, out double xMin, out double xMax, out double yMin, out double yMax)
	{
		int count = data.Values == null ? 0 : data.Count;
		double sampleCount = count;
		if (probe.ScaleMode == ProbeScaleMode.Full)
		{
			xMin = startStep;
			xMax = probe.AutoScaleX ? Math.Max(sampleCount, startStep + 1.0) : Math.Max(totalSteps, startStep + 1.0);
		}
		else
		{
			double window = Math.Max(Probe.MinWindow, probe.Window);
			if (probe.AutoScaleX)
			{
				xMax = Math.Max(sampleCount, startStep + 1.0);
				xMin = Math.Max(startStep, xMax - window);
			}
			else
			{
				xMax = Math.Max(sampleCount, startStep + window);
				xMin = Math.Max(startStep, xMax - window);
			}
		}
		if (xMax <= xMin)
		{
			xMax = xMin + 1.0;
		}
		FixedRange(probe, out yMin, out yMax);
		if (probe.AutoScaleY && count > 0)
		{
			int firstIndex = Math.Max(0, (int)Math.Floor(xMin) - 1);
			int lastIndex = Math.Min(count - 1, (int)Math.Ceiling(xMax));
			if (Extent(data.Values!, firstIndex, lastIndex, out float lo, out float hi))
			{
				AutoRange(probe, lo, hi, ref yMin, ref yMax);
			}
		}
	}

	/// <summary>
	/// Ranges of a line-profile graph: x spans 0..line length in canvas pixels (0..1 without a profile); y is
	/// the fixed range or, auto-scaled, fitted to the profile values. Intensity profiles that are entirely
	/// non-negative are fitted from 0 upwards.
	/// </summary>
	private static void ProfileRanges(Probe probe, in ProbeGraphData data, out double xMin, out double xMax, out double yMin, out double yMax)
	{
		xMin = 0.0;
		xMax = data.Profile != null && data.ProfileLength > 0.0 ? data.ProfileLength : 1.0;
		FixedRange(probe, out yMin, out yMax);
		if (probe.AutoScaleY && data.Profile is { } profile && Extent(profile, 0, profile.Length - 1, out float lo, out float hi))
		{
			AutoRange(probe, lo, hi, ref yMin, ref yMax);
		}
	}

	/// <summary>The probe's fixed y range, or -1..1 when it is empty, inverted or NaN (written so a NaN limit also falls back).</summary>
	private static void FixedRange(Probe probe, out double yMin, out double yMax)
	{
		yMin = probe.YMin;
		yMax = probe.YMax;
		if (!(yMax > yMin))
		{
			yMin = -1.0;
			yMax = 1.0;
		}
	}

	/// <summary>Smallest and largest finite value of <paramref name="values"/>[first..last]; false when there is none.</summary>
	private static bool Extent(float[] values, int first, int last, out float lo, out float hi)
	{
		lo = float.MaxValue;
		hi = float.MinValue;
		for (int i = Math.Max(0, first); i <= last && i < values.Length; i++)
		{
			float value = values[i];
			if (float.IsFinite(value))
			{
				lo = Math.Min(lo, value);
				hi = Math.Max(hi, value);
			}
		}
		return lo <= hi;
	}

	/// <summary>
	/// The auto-scaled value range for data spanning [lo, hi], written so the axis stays put as far as the data
	/// allows (the user's rule, 2026-10-14: fitting both ends independently made the zero line wander while the
	/// first oscillations arrived): a field amplitude gets a range symmetric about zero, ±(largest magnitude
	/// plus 8 %); an intensity (never negative) runs from 0 to the largest value plus 8 %; data that is still
	/// all zeros leaves the fixed range in place (the author's ±1e-6 fit flickered its labels until the first
	/// real value came). A range that goes negative on an intensity probe is treated like an amplitude.
	/// </summary>
	/// <param name="probe">Selects the measurement.</param>
	/// <param name="lo">Smallest data value.</param>
	/// <param name="hi">Largest data value, not below <paramref name="lo"/>.</param>
	/// <param name="yMin">The fixed lower limit; replaced unless the data is all zeros.</param>
	/// <param name="yMax">The fixed upper limit; replaced unless the data is all zeros.</param>
	private static void AutoRange(Probe probe, double lo, double hi, ref double yMin, ref double yMax)
	{
		double magnitude = Math.Max(Math.Abs(hi), Math.Abs(lo));
		if (magnitude <= 0.0)
		{
			return;
		}
		if (probe.Measurement == ProbeMeasurement.Amplitude || lo < 0.0)
		{
			yMax = magnitude * (1.0 + Margin);
			yMin = -yMax;
		}
		else
		{
			yMin = 0.0;
			yMax = hi * (1.0 + Margin);
		}
	}

	/// <summary>Room above the largest value (and below the smallest of a symmetric axis), as a fraction, so the curve does not touch the plot border.</summary>
	private const double Margin = 0.08;

	/// <summary>
	/// "Nice" (1/2/5 × 10ⁿ) tick positions covering [lo, hi], at most 40 of them. A tick within rounding
	/// distance of 0 is snapped to exactly 0 so the zero line can be recognised.
	/// </summary>
	/// <param name="lo">Lower end of the axis range.</param>
	/// <param name="hi">Upper end of the axis range.</param>
	/// <param name="target">Approximate number of ticks wanted.</param>
	/// <returns>Ascending tick values; empty when the range is empty, inverted or infinite.</returns>
	public static List<double> NiceTicks(double lo, double hi, int target)
	{
		var ticks = new List<double>();
		double span = hi - lo;
		// written this way round so a NaN span also yields no ticks
		if (!(span > 0.0) || double.IsInfinity(span))
		{
			return ticks;
		}
		double rawStep = span / Math.Max(1, target);
		double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(rawStep)));
		double mantissa = rawStep / magnitude;
		double step = mantissa switch
		{
			< 1.5 => 1.0,
			< 3.0 => 2.0,
			< 7.0 => 5.0,
			_ => 10.0,
		} * magnitude;
		double tick = Math.Ceiling(lo / step) * step;
		int guard = 0;
		while (tick <= hi + step * 1e-9 && guard < 40)
		{
			ticks.Add(Math.Abs(tick) < step * 1e-9 ? 0.0 : tick);
			tick += step;
			guard++;
		}
		return ticks;
	}

	/// <summary>Formats an axis value with three significant digits; 0 is just "0".</summary>
	public static string FormatTick(double v, IFormatProvider culture) => v == 0.0 ? "0" : v.ToString("G3", culture);

	/// <summary>Formats an x-axis position: as an integer when the visible <paramref name="span"/> is at least 10 units, otherwise with one decimal.</summary>
	public static string FormatPosition(double v, double span, IFormatProvider culture) =>
		span >= 10.0 ? Math.Round(v).ToString("0", culture) : v.ToString("0.#", culture);

	/// <summary>
	/// Lays out one graph box of <paramref name="width"/> × <paramref name="height"/> pixels. The text keeps
	/// the style's size; the margins come from the labels as measured (the widest value label on the left, a
	/// line below), the tick count is reduced when the labels would collide, and when the labels leave no room
	/// for a plot they are dropped before the plot is. A line probe with "start after step" shows neither
	/// profile nor readout until <see cref="ProbeGraphData.StepsSampled"/> has reached that step; a point
	/// probe's series is trimmed to the start step instead (see <see cref="ComputeRanges"/>) and drawn up to
	/// the current value even before it is reached.
	/// </summary>
	/// <param name="probe">Settings that select kind, scaling and the start-after gate; its name is the title.</param>
	/// <param name="data">The data shown; <c>default</c> for an empty graph.</param>
	/// <param name="totalSteps">Total steps of the run, see <see cref="ComputeRanges"/>.</param>
	/// <param name="width">Box width in pixels (view pixels for the preview, canvas pixels for an export).</param>
	/// <param name="height">Box height in pixels.</param>
	/// <param name="style">The renderer's text metrics in the same pixels.</param>
	/// <param name="culture">Number formatting of the labels; the current culture when null.</param>
	public static ProbeGraphLayout Layout(Probe probe, in ProbeGraphData data, int totalSteps, float width, float height, ProbeGraphStyle style, IFormatProvider? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		bool visible = width >= MinVisibleWidth && height >= MinVisibleHeight;
		float lineWidth = Math.Max(1.2f, height / 110f);
		float titleHeight = style.LineHeight + 2 * EdgePad;
		// a line probe (a profile of an instant) has nothing meaningful to show before the start step is reached,
		// so it stays blank until then; a point probe (a series) is trimmed instead, see SeriesRanges
		bool isLine = probe.Kind == ProbeKind.Line;
		bool started = probe.StartAfter <= 0 || data.StepsSampled >= probe.StartAfter;
		ProbeGraphData shown = isLine && !started ? default : data;
		// a point probe's own range/autoscale sees the real data (it is trimmed by "start after step" inside
		// SeriesRanges, not hidden); only a line probe uses the gated data, to stay blank until it starts
		ComputeRanges(probe, isLine ? shown : data, totalSteps, out double xMin, out double xMax, out double yMin, out double yMax);
		// the value labels decide the left margin; fewer of them when the plot is low
		List<double> yValues = NiceTicks(yMin, yMax, 4);
		float widestY = 0f;
		foreach (double tick in yValues)
		{
			widestY = Math.Max(widestY, style.Measure(FormatTick(tick, culture)));
		}
		float plotX = EdgePad + widestY + LabelGap;
		float plotY = titleHeight;
		float plotWidth = width - plotX - RightMargin;
		float plotHeight = height - plotY - (style.LineHeight + LabelGap);
		bool showLabels = visible && plotWidth >= MinPlotWidth && plotHeight >= MinPlotHeight;
		if (!showLabels)
		{
			// no room for labels: the plot takes their margins (the title strip stays)
			plotX = BareMargin;
			plotWidth = width - plotX - BareMargin;
			plotHeight = height - plotY - BareMargin;
		}
		bool hasPlot = visible && plotWidth >= MinPlotWidth && plotHeight >= MinPlotHeight;
		if (!hasPlot)
		{
			return new ProbeGraphLayout(width, height, visible, false, style.FontSize, lineWidth, plotX, plotY, Math.Max(0, plotWidth), Math.Max(0, plotHeight), [], [], false, [], probe.Name, "");
		}
		if (showLabels)
		{
			// the labels must not overlap: adjacent ticks at least 1.1 lines apart, else fewer ticks (asking for two
			// outright gave a single tick at zero where three fitted, 2026-10-14)
			foreach (int target in new[] { 3, 2 })
			{
				if ((yValues.Count - 1) * style.LineHeight * 1.1f <= plotHeight)
				{
					break;
				}
				yValues = NiceTicks(yMin, yMax, target);
			}
		}
		var yTicks = new List<GraphTick>();
		foreach (double tick in yValues)
		{
			yTicks.Add(new GraphTick(ToPixelY(tick, plotY, plotHeight, yMin, yMax), showLabels ? FormatTick(tick, culture) : "", tick == 0.0));
		}
		// the step/position labels: as many as fit side by side, 5 wanted
		var xTicks = new List<GraphTick>();
		foreach (int target in new[] { 5, 3, 2 })
		{
			xTicks.Clear();
			float labelsWidth = 0f;
			foreach (double tick in NiceTicks(xMin, xMax, target))
			{
				string label = showLabels ? FormatPosition(tick, xMax - xMin, culture) : "";
				labelsWidth += style.Measure(label) + XLabelSpacing;
				xTicks.Add(new GraphTick((float)(plotX + (tick - xMin) / (xMax - xMin) * plotWidth), label, tick == 0.0));
			}
			if (labelsWidth - XLabelSpacing <= plotWidth)
			{
				break;
			}
		}
		Vector2[] curve = isLine
			? ProfilePoints(shown.Profile, plotX, plotY, plotWidth, plotHeight, yMin, yMax)
			: SeriesPoints(data.Values, data.Count, plotX, plotY, plotWidth, plotHeight, xMin, xMax, yMin, yMax);
		return new ProbeGraphLayout(width, height, true, true, style.FontSize, lineWidth, plotX, plotY, plotWidth, plotHeight, yTicks, xTicks, showLabels, curve, probe.Name, ReadoutText(isLine, shown, culture));
	}

	/// <summary>Short readout for the title strip: "max" of the profile or "=" the latest series value; empty when there is no valid value.</summary>
	private static string ReadoutText(bool isLine, in ProbeGraphData data, IFormatProvider culture)
	{
		if (isLine)
		{
			if (data.Profile is not { Length: > 0 } profile || !Extent(profile, 0, profile.Length - 1, out _, out float max))
			{
				return "";
			}
			return "max " + FormatTick(max, culture);
		}
		if (data.Values == null || data.Count <= 0)
		{
			return "";
		}
		float lastValue = data.Values[data.Count - 1];
		return float.IsFinite(lastValue) ? "= " + FormatTick(lastValue, culture) : "";
	}

	/// <summary>
	/// Maps a data value to a pixel y inside the plot (bottom edge = yMin, top edge = yMax). The result is
	/// clamped to ±30000 so a renderer never sees coordinates far outside its surface.
	/// </summary>
	private static float ToPixelY(double v, float plotY, float plotHeight, double yMin, double yMax)
	{
		double py = plotY + plotHeight - (v - yMin) / (yMax - yMin) * plotHeight;
		return (float)Math.Clamp(py, -30000.0, 30000.0);
	}

	/// <summary>
	/// The points of a time series: sample i is placed at x = i + 1 (its step number); NaN and infinite samples
	/// are skipped. With more than two samples per pixel column each column gets a vertical min/max segment
	/// instead of individual points.
	/// </summary>
	private static Vector2[] SeriesPoints(float[]? values, int count, float plotX, float plotY, float plotWidth, float plotHeight, double xMin, double xMax, double yMin, double yMax)
	{
		if (values == null || count < 1)
		{
			return [];
		}
		int firstIndex = Math.Max(0, (int)Math.Floor(xMin) - 1);
		int lastIndex = Math.Min(count - 1, (int)Math.Ceiling(xMax));
		if (lastIndex < firstIndex)
		{
			return [];
		}
		var points = new List<Vector2>();
		int visibleCount = lastIndex - firstIndex + 1;
		int columns = (int)plotWidth;
		if (visibleCount <= columns * 2)
		{
			// every value gets its own point (value i belongs to step i + 1)
			double xScale = plotWidth / (xMax - xMin);
			for (int i = firstIndex; i <= lastIndex; i++)
			{
				float value = values[i];
				if (float.IsFinite(value))
				{
					points.Add(new Vector2((float)(plotX + (i + 1 - xMin) * xScale), ToPixelY(value, plotY, plotHeight, yMin, yMax)));
				}
			}
		}
		else
		{
			BinColumns(values, firstIndex, visibleCount, lastIndex, columns, plotX, plotY, plotHeight, yMin, yMax, points);
		}
		return [.. points];
	}

	/// <summary>
	/// The points of a line profile stretched over the full plot width (first value at the left edge, last at
	/// the right edge), min/max binned per pixel column when dense. NaN and infinite values are skipped.
	/// </summary>
	private static Vector2[] ProfilePoints(float[]? profile, float plotX, float plotY, float plotWidth, float plotHeight, double yMin, double yMax)
	{
		if (profile == null || profile.Length < 1)
		{
			return [];
		}
		int count = profile.Length;
		var points = new List<Vector2>();
		int columns = (int)plotWidth;
		if (count <= columns * 2)
		{
			for (int i = 0; i < count; i++)
			{
				float value = profile[i];
				if (float.IsFinite(value))
				{
					double fraction = count > 1 ? i / (count - 1.0) : 0.0;
					points.Add(new Vector2((float)(plotX + fraction * plotWidth), ToPixelY(value, plotY, plotHeight, yMin, yMax)));
				}
			}
		}
		else
		{
			BinColumns(profile, 0, count, count - 1, columns, plotX, plotY, plotHeight, yMin, yMax, points);
		}
		return [.. points];
	}

	/// <summary>
	/// Reduces values[first..] to one vertical min/max segment per pixel column: column c covers the values
	/// from first + c × visible / columns on; the long arithmetic keeps c × visible inside the range for
	/// long runs. Columns without a finite value are skipped.
	/// </summary>
	private static void BinColumns(float[] values, int first, int visible, int last, int columns, float plotX, float plotY, float plotHeight, double yMin, double yMax, List<Vector2> points)
	{
		for (int column = 0; column < columns; column++)
		{
			int binStart = first + (int)((long)column * visible / columns);
			int binEnd = first + (int)((long)(column + 1) * visible / columns) - 1;
			if (binEnd < binStart)
			{
				binEnd = binStart;
			}
			if (Extent(values, binStart, Math.Min(binEnd, last), out float binMin, out float binMax))
			{
				float px = plotX + column + 0.5f;
				points.Add(new Vector2(px, ToPixelY(binMin, plotY, plotHeight, yMin, yMax)));
				points.Add(new Vector2(px, ToPixelY(binMax, plotY, plotHeight, yMin, yMax)));
			}
		}
	}
}
