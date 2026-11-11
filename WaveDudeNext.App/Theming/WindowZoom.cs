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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using NLog;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Theming;

/// <summary>
/// The UI zoom (View > UI zoom, <see cref="Settings.UiZoom"/>) on every window that is attached, applied live. Upstream
/// scaled each window's content with a WPF LayoutTransform and told the visual tree a zoomed DPI so that layout rounded
/// to device pixels; Avalonia has no per-tree DPI, so the content is wrapped in a <see cref="LayoutTransformControl"/>
/// whose scale takes part in layout (the window measures its content at the zoomed size, unlike a render transform),
/// the window's explicit sizes (Width, Height and the finite minimum and maximum) are multiplied by the change of
/// zoom so the same content stays in view, and the window is kept inside its screen's working area
/// (<see cref="KeepOnScreen"/>, the user's rule: a zoomed window had wandered off the screen). At zoom 1 the wrapper
/// carries no transform and everything is as the theme has it. Lines under a fractional zoom are anti-aliased by the
/// renderer, which is the one part of upstream's whole-pixel rule Avalonia offers no hook for.
/// <para>
/// Popups (menus, context menus, tooltips, drop-downs) are separate native windows on X11 and on Windows alike (the
/// platforms' OverlayPopups option is off by default), and Avalonia gives a popup the scale of its placement target only
/// when <c>Popup.InheritsTransform</c> is set: the Popup style in Themes/Controls.axaml sets it for every popup (the
/// property's default cannot be changed on Popup itself), so each popup root scales its content as the window does
/// (Avalonia's PopupRoot template holds a LayoutTransformControl for exactly this). Verified on the headless platform,
/// where popups render inside the window; to be checked on Windows and X11 proper, where the popup is its own
/// top-level window.
/// </para>
/// UI thread only.
/// </summary>
public static class WindowZoom
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>The attached windows with the zoom their sizes were last multiplied by; weak, so a closed window is forgotten.</summary>
	private static readonly ConditionalWeakTable<Window, State> Windows = new();

	private static bool _installed;

	// the bounds before the sizes: a shrinking minimum or a growing maximum must be in place when the size moves
	private static readonly StyledProperty<double>[] Sizes =
	[
		Layoutable.MinWidthProperty, Layoutable.MinHeightProperty, Layoutable.MaxWidthProperty, Layoutable.MaxHeightProperty,
		Layoutable.WidthProperty, Layoutable.HeightProperty,
	];

	/// <summary>The zoom in force: the setting. A size saved in the settings is the size at zoom 1, so a window divides by this when it saves its own.</summary>
	public static double Scale => Settings.Default.UiZoom;

	/// <summary>
	/// Applies the zoom to <paramref name="window"/> and follows later changes of the setting: call once, after the
	/// window's XAML is loaded and its sizes set, before it shows. The window's Content is wrapped in a
	/// <see cref="LayoutTransformControl"/> (an existing one is reused), so code must not expect Content to be its own
	/// root afterwards; named children found by the XAML loader are unaffected. A second call for the same window does nothing.
	/// </summary>
	public static void Attach(Window window)
	{
		Install();
		if (Windows.TryGetValue(window, out _))
		{
			return;
		}
		LayoutTransformControl host;
		if (window.Content is LayoutTransformControl existing)
		{
			host = existing;
		}
		else
		{
			// the content leaves the window before it joins the wrapper: a control has one logical parent at a time
			object? content = window.Content;
			window.Content = null;
			host = Wrap(content);
			window.Content = host;
		}
		var state = new State(host);
		Windows.Add(window, state);
		Apply(window, state);
		TextRendering.Apply(window);
		TitleBar.Attach(window);
		window.Opened += (sender, _) => KeepOnScreen((Window)sender!);
		Logger.Debug("UI zoom {Zoom} on {Window}", state.Applied, window.GetType().Name);
	}

	/// <summary>Once per process: the setting's changes reach every attached window.</summary>
	private static void Install()
	{
		if (_installed)
		{
			return;
		}
		_installed = true;
		Settings.Default.PropertyChanged += OnSettingChanged;
	}

	private static void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(Settings.UiZoom))
		{
			return;
		}
		foreach ((Window window, State state) in Windows)
		{
			Apply(window, state);
		}
	}

	private static LayoutTransformControl Wrap(object? content) => new()
	{
		// a window's content is a control in this application; anything else is shown as a content control would show it
		Child = content as Control ?? (content is null ? null : new ContentControl { Content = content }),
	};

	private static void Apply(Window window, State state)
	{
		double zoom = Scale;
		double applied = state.Applied;
		state.Host.LayoutTransform = zoom == 1 ? null : new ScaleTransform(zoom, zoom);
		if (zoom != applied)
		{
			foreach (StyledProperty<double> size in Sizes)
			{
				double value = window.GetValue(size);
				if (value > 0 && double.IsFinite(value))
				{
					window.SetValue(size, value * zoom / applied);
				}
			}
			state.Applied = zoom;
			Logger.Debug("UI zoom {Zoom} on {Window} (was {Applied})", zoom, window.GetType().Name, applied);
		}
		if (window.IsVisible)
		{
			KeepOnScreen(window);
		}
	}

	/// <summary>
	/// Keeps a shown window inside the working area of the screen it is on: shrunk to the area when larger, then moved
	/// so that it lies within. The frame size (client plus decorations) is compared in the screen's pixels; the client
	/// size is set through Width and Height so a window that saves its size sees the change. A maximized, minimized or
	/// full-screen window is left alone, as is one without a screen (headless, or off every screen).
	/// </summary>
	private static void KeepOnScreen(Window window)
	{
		if (!window.IsVisible || window.WindowState != WindowState.Normal)
		{
			return;
		}
		Screen? screen = window.Screens.ScreenFromWindow(window);
		if (screen is null)
		{
			return;
		}
		PixelRect work = screen.WorkingArea;
		double scaling = window.DesktopScaling;
		Size client = window.ClientSize;
		Size frame = window.FrameSize ?? client;
		PixelSize size = PixelSize.FromSize(frame, scaling);
		PixelPoint position = window.Position;
		int width = Math.Min(size.Width, work.Width);
		int height = Math.Min(size.Height, work.Height);
		if (width < size.Width)
		{
			window.Width = Math.Max(1, width / scaling - (frame.Width - client.Width));
		}
		if (height < size.Height)
		{
			window.Height = Math.Max(1, height / scaling - (frame.Height - client.Height));
		}
		int left = Math.Clamp(position.X, work.X, Math.Max(work.X, work.Right - width));
		int top = Math.Clamp(position.Y, work.Y, Math.Max(work.Y, work.Bottom - height));
		if (left != position.X || top != position.Y)
		{
			window.Position = new PixelPoint(left, top);
		}
		if (width != size.Width || height != size.Height || left != position.X || top != position.Y)
		{
			Logger.Debug("UI zoom: {Window} kept on the screen, {Width}×{Height} at {Left},{Top} (work area {WorkWidth}×{WorkHeight})",
				window.GetType().Name, width, height, left, top, work.Width, work.Height);
		}
	}

	/// <summary>What is remembered per attached window.</summary>
	private sealed class State(LayoutTransformControl host)
	{
		/// <summary>The wrapper around the window's content that carries the scale.</summary>
		public LayoutTransformControl Host { get; } = host;

		/// <summary>The zoom the window's sizes were last multiplied by: 1 before the first application, since the XAML sizes are at zoom 1.</summary>
		public double Applied { get; set; } = 1;
	}
}
