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

using System.Diagnostics;
using NLog;

namespace WaveDudeNext.WPF.Logging;

/// <summary>
/// Forwards a <see cref="TraceSource"/> into NLog, under the source's name as logger. Used for
/// WPF's data-binding trace, which reports binding errors only this way.
/// </summary>
public sealed class TraceLogListener : TraceListener
{
	/// <inheritdoc/>
	public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
		LogManager.GetLogger(source).Log(ToLevel(eventType), message ?? "");

	/// <inheritdoc/>
	public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
		LogManager.GetLogger(source).Log(ToLevel(eventType), format ?? "", args ?? []);

	/// <inheritdoc/>
	public override void Write(string? message) => LogManager.GetLogger(Name).Debug(message ?? "");

	/// <inheritdoc/>
	public override void WriteLine(string? message) => LogManager.GetLogger(Name).Debug(message ?? "");

	private static LogLevel ToLevel(TraceEventType type) => type switch
	{
		TraceEventType.Critical => LogLevel.Fatal,
		TraceEventType.Error => LogLevel.Error,
		TraceEventType.Warning => LogLevel.Warn,
		TraceEventType.Information => LogLevel.Info,
		TraceEventType.Verbose => LogLevel.Debug,
		_ => LogLevel.Trace,
	};
}
