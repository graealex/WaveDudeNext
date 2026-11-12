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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using NLog;

namespace WaveDudeNext.App.Theming;

/// <summary>
/// The platform's text shaper with every glyph advance rounded to a whole pixel, the rule of WPF's Display text
/// formatting, which the WPF head renders with (2026-11-11, the user's choice among the options for the text that looked
/// washed beside the WPF head's). Why: Skia places glyphs at quarter pixels for every antialiased mode
/// (<c>GlyphRunImpl.CreateFont</c> at 12.1.5: <c>Subpixel = edging != Alias</c>, which nothing in Avalonia reaches),
/// and the shaper's advances are the font's linear ones, so most glyphs of a run land between pixels and a one-pixel
/// stem is shared by two columns at half intensity; with whole advances every glyph of a run that starts on a whole
/// pixel sits on one, and the stem is one column, as measured in isolation (59/241/84 against WPF's 71/228/85).
/// A run starting at a half pixel, centred text can, still splits; and the rule holds at a device scale of 1: under a
/// UI zoom or a DPI scale the run is drawn through a matrix and lands on fractions again. <see cref="Install"/> takes
/// the platform's <see cref="ITextShaperImpl"/> from the locator and binds a proxy of it in its place through
/// <c>AppBuilder.With</c>, before the first text is shaped; a <see cref="GlyphTypeface"/> asks the locator for the
/// implementation itself (its <c>TextShaperTypeface</c>), which is why the binding replaces the implementation and
/// not the <see cref="TextShaper"/> in front of it. Two things Avalonia keeps from client code (read at 12.1.5),
/// reached at run time: <see cref="ITextShaperImpl"/> is marked not client implementable and the reference assembly
/// enforces it with a member no other assembly can implement, so the proxy is a <see cref="DispatchProxy"/> made against
/// the run-time interface; and the locator's <c>Current</c> and <c>CurrentMutable</c>, public properties the reference
/// assembly hides as a private API, are read by reflection. The typeface the shaper hands out (<c>CreateTypeface</c>) is
/// passed through untouched: its interface is a marker without members, which the HarfBuzz shaper casts back to its own
/// class, so a proxy of it broke the shaping. Every shaped buffer that comes out of the proxy is rounded. An Avalonia
/// update may break this; then <see cref="Install"/> logs a warning and the application runs without it. UI thread, as
/// the shaper is.
/// </summary>
public class WholePixelShaper : DispatchProxy
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private object? _inner;

	/// <summary>
	/// Binds the proxy in front of the platform's shaper, once it is registered; nothing happens before that, or twice.
	/// The binding goes through the locator itself (<c>AvaloniaLocator.CurrentMutable.Bind&lt;ITextShaperImpl&gt;().ToConstant</c>,
	/// by reflection): <c>AppBuilder.With</c> only queues a binding for the setup, which has passed by the time the
	/// platform's shaper can be taken. Fails soft (2026-11-11, the user's choice): when an Avalonia update has moved
	/// what this reaches for, a warning names it and the application runs with the platform's shaper, its text placed
	/// on quarter pixels again.
	/// </summary>
	public static void Install()
	{
		try
		{
			object? platform = PlatformService(typeof(ITextShaperImpl));
			if (platform is null)
			{
				Logger.Warn("Whole-pixel shaper not installed: no platform text shaper is registered yet");
				return;
			}
			if (platform is WholePixelShaper)
			{
				return;
			}
			ITextShaperImpl proxy = Wrap<ITextShaperImpl>(platform);
			object mutable = typeof(AvaloniaLocator).GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
				?? throw new InvalidOperationException("AvaloniaLocator.CurrentMutable not found");
			object helper = mutable.GetType().GetMethod("Bind")?.MakeGenericMethod(typeof(ITextShaperImpl)).Invoke(mutable, null)
				?? throw new InvalidOperationException("the locator has no Bind<T>()");
			MethodInfo toConstant = helper.GetType().GetMethod("ToConstant") ?? throw new InvalidOperationException("the registration has no ToConstant");
			if (toConstant.IsGenericMethodDefinition)
			{
				toConstant = toConstant.MakeGenericMethod(typeof(ITextShaperImpl));
			}
			toConstant.Invoke(helper, [proxy]);
			Logger.Debug("Whole-pixel shaper installed in front of {0}", platform.GetType().FullName);
		}
		catch (Exception e) when (e is InvalidOperationException or TargetInvocationException or ArgumentException or MemberAccessException or TypeLoadException)
		{
			Logger.Warn("Whole-pixel shaper not installed; the text is placed on quarter pixels: {0}", e.InnerException?.Message ?? e.Message);
		}
	}

	/// <inheritdoc/>
	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		object? result;
		try
		{
			result = targetMethod!.Invoke(_inner, args);
		}
		catch (TargetInvocationException e) when (e.InnerException is { } inner)
		{
			ExceptionDispatchInfo.Capture(inner).Throw();
			throw;
		}
		if (result is ShapedBuffer buffer)
		{
			Round(buffer);
		}
		return result;
	}

	/// <summary>A proxy of the interface whose calls go to <paramref name="inner"/> through <see cref="Invoke"/>.</summary>
	private static T Wrap<T>(object inner) where T : class
	{
		T proxy = Create<T, WholePixelShaper>();
		((WholePixelShaper)(object)proxy)._inner = inner;
		return proxy;
	}

	/// <summary>A service of the locator: <c>AvaloniaLocator.Current.GetService(type)</c> by reflection, null when none is registered.</summary>
	private static object? PlatformService(Type type)
	{
		object current = typeof(AvaloniaLocator).GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
			?? throw new InvalidOperationException("AvaloniaLocator.Current not found");
		MethodInfo getService = current.GetType().GetMethod("GetService", [typeof(Type)])
			?? current.GetType().GetInterfaces().Select(i => i.GetMethod("GetService", [typeof(Type)])).FirstOrDefault(m => m != null)
			?? throw new InvalidOperationException("the locator has no GetService(Type)");
		return getService.Invoke(current, [type]);
	}

	/// <summary>Rounds every advance of the buffer to a whole pixel, half pixels away from zero.</summary>
	private static void Round(ShapedBuffer buffer)
	{
		for (int i = 0; i < buffer.Length; i++)
		{
			GlyphInfo glyph = buffer[i];
			double whole = Math.Round(glyph.GlyphAdvance, MidpointRounding.AwayFromZero);
			if (whole != glyph.GlyphAdvance)
			{
				buffer[i] = new GlyphInfo(glyph.GlyphIndex, glyph.GlyphCluster, whole, glyph.GlyphOffset);
			}
		}
	}
}
