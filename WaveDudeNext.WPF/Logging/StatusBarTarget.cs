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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NLog;
using NLog.Common;
using NLog.Conditions;
using NLog.Config;
using NLog.Targets;

namespace WaveDudeNext.WPF.Logging;

/// <summary>
/// NLog target that shows the latest log line in a status bar text block, so the user sees
/// what the application last did ("Opened ...", "Saved ...", errors) without a log window.
/// The text block is found by name in a window of the given type, as in the reference apps,
/// so the target can also come from an NLog configuration file. Colours are theme resource
/// keys chosen per <see cref="ColoringRules"/>; the background goes to the enclosing
/// <see cref="StatusBar"/> so the whole bar changes, Visual Studio style. Safe to call from
/// any thread: updates are posted to the dispatcher of the thread that created the target.
/// The target may be created before any window exists; it attaches to the text block on the
/// first update that finds it, so the last line logged during start-up is what the bar shows.
/// </summary>
[Target("StatusBar")]
public sealed class StatusBarTarget : TargetWithLayout
{
	private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
	private TextBlock? _text;
	private StatusBar? _bar;

	/// <summary>Type name of the window holding the text block, for example <c>MainWindow</c>.</summary>
	public string WindowName { get; set; } = "";

	/// <summary>The <c>x:Name</c> of the <see cref="TextBlock"/> that shows the message.</summary>
	public string ControlName { get; set; } = "";

	/// <summary>Resource key of the brush restored for text when no rule matches.</summary>
	[DefaultValue("StatusBarForeground")]
	public string DefaultForeground { get; set; } = "StatusBarForeground";

	/// <summary>Resource key of the brush restored for the bar when no rule matches.</summary>
	[DefaultValue("StatusBarBackground")]
	public string DefaultBackground { get; set; } = "StatusBarBackground";

	/// <summary>Colour rules, first match wins; without a match the defaults apply.</summary>
	[ArrayParameter(typeof(StatusBarColoringRule), "coloring")]
	public IList<StatusBarColoringRule> ColoringRules { get; } = new List<StatusBarColoringRule>();

	/// <inheritdoc/>
	protected override void InitializeTarget()
	{
		base.InitializeTarget();
		if (string.IsNullOrEmpty(WindowName) || string.IsNullOrEmpty(ControlName))
		{
			throw new NLogConfigurationException("StatusBarTarget needs WindowName and ControlName.");
		}
	}

	/// <inheritdoc/>
	protected override void Write(LogEventInfo logEvent)
	{
		string message = RenderLogEvent(Layout, logEvent);
		string? tooltip = logEvent.Exception?.ToString();
		StatusBarColoringRule? rule = ColoringRules.FirstOrDefault(r => r.Matches(logEvent));
		// Loaded priority: during start-up these run after the main window has been created
		_dispatcher.InvokeAsync(() => Show(message, tooltip, rule), DispatcherPriority.Loaded);
	}

	private void Show(string message, string? tooltip, StatusBarColoringRule? rule)
	{
		if (_text == null && !TryAttach())
		{
			return; // no window yet; a later message will try again
		}
		_text!.Text = message;
		_text.ToolTip = tooltip;
		_text.SetResourceReference(TextBlock.ForegroundProperty, rule?.Foreground ?? DefaultForeground);
		_bar ??= FindAncestor<StatusBar>(_text);
		if (_bar != null)
		{
			_bar.SetResourceReference(Control.BackgroundProperty, rule?.Background ?? DefaultBackground);
		}
		else
		{
			_text.SetResourceReference(TextBlock.BackgroundProperty, rule?.Background ?? DefaultBackground);
		}
	}

	/// <summary>Looks for the text block in the named window; false while there is no such window yet.</summary>
	private bool TryAttach()
	{
		if (Application.Current == null)
		{
			return false;
		}
		foreach (Window window in Application.Current.Windows)
		{
			if (window.GetType().Name.Equals(WindowName, StringComparison.OrdinalIgnoreCase)
				&& window.FindName(ControlName) is TextBlock text)
			{
				_text = text;
				return true;
			}
		}
		return false;
	}

	private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
	{
		for (DependencyObject? d = VisualTreeHelper.GetParent(start); d != null; d = VisualTreeHelper.GetParent(d))
		{
			if (d is T match)
			{
				return match;
			}
		}
		return null;
	}
}

/// <summary>
/// One colour rule of <see cref="StatusBarTarget"/>: an NLog condition such as
/// <c>level >= LogLevel.Error</c> and the theme brush keys to use when it matches.
/// </summary>
[NLogConfigurationItem]
public sealed class StatusBarColoringRule
{
	/// <summary>Creates an empty rule for configuration files.</summary>
	public StatusBarColoringRule()
	{
	}

	/// <param name="condition">NLog condition, for example <c>level == LogLevel.Warn</c>.</param>
	/// <param name="foreground">Theme brush key for the text, or null to keep the default.</param>
	/// <param name="background">Theme brush key for the bar, or null to keep the default.</param>
	public StatusBarColoringRule(string condition, string? foreground, string? background)
	{
		Condition = condition;
		Foreground = foreground;
		Background = background;
	}

	/// <summary>The condition evaluated against each log event; a string converts implicitly.</summary>
	public ConditionExpression? Condition { get; set; }

	/// <summary>Theme brush key for the text; null keeps the target's default.</summary>
	public string? Foreground { get; set; }

	/// <summary>Theme brush key for the bar; null keeps the target's default.</summary>
	public string? Background { get; set; }

	/// <summary>True when the condition holds for the event.</summary>
	public bool Matches(LogEventInfo logEvent) => Condition != null && true.Equals(Condition.Evaluate(logEvent));
}
