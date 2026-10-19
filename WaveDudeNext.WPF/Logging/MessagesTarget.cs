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

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using NLog;
using NLog.Targets;

namespace WaveDudeNext.WPF.Logging;

/// <summary>
/// NLog target that keeps the last <see cref="MaxCount"/> log events in memory for the
/// Messages pane. Events may arrive on any thread; they are queued and moved into
/// <see cref="Entries"/> on the UI thread in batches, so a burst of logging does not stall the
/// UI. Create it on the UI thread (it captures that dispatcher); the pane finds it by name.
/// </summary>
[Target("Messages")]
public sealed class MessagesTarget : Target
{
	private readonly ObservableCollection<LogEntry> _entries = new();
	private readonly ConcurrentQueue<LogEntry> _pending = new();
	private readonly DispatcherTimer _flush;

	public MessagesTarget()
	{
		Entries = new ReadOnlyObservableCollection<LogEntry>(_entries);
		_flush = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
		{
			Interval = TimeSpan.FromMilliseconds(100),
		};
		_flush.Tick += (_, _) => Flush();
	}

	/// <summary>Upper bound of kept entries; the oldest are dropped beyond it.</summary>
	public int MaxCount { get; set; } = 5000;

	/// <summary>The kept entries, oldest first. UI thread only.</summary>
	public ReadOnlyObservableCollection<LogEntry> Entries { get; }

	/// <summary>Drops every kept entry. UI thread only.</summary>
	public void Clear()
	{
		while (_pending.TryDequeue(out _))
		{
		}
		_entries.Clear();
	}

	/// <inheritdoc/>
	protected override void Write(LogEventInfo logEvent)
	{
		_pending.Enqueue(new LogEntry(logEvent.TimeStamp, logEvent.Level, logEvent.LoggerName ?? "", logEvent.FormattedMessage, logEvent.Exception?.ToString()));
		if (!_flush.IsEnabled)
		{
			_flush.Start();
		}
	}

	/// <inheritdoc/>
	protected override void CloseTarget()
	{
		_flush.Stop();
		base.CloseTarget();
	}

	private void Flush()
	{
		_flush.Stop();
		while (_pending.TryDequeue(out LogEntry? entry))
		{
			_entries.Add(entry);
		}
		while (_entries.Count > MaxCount)
		{
			_entries.RemoveAt(0);
		}
	}
}

/// <summary>One line of the Messages pane.</summary>
/// <param name="Time">When the event was logged.</param>
/// <param name="Level">Severity.</param>
/// <param name="Logger">Full logger name, usually the class.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception with stack trace, or null.</param>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Logger, string Message, string? Exception)
{
	/// <summary>Level name as shown in the pane ("Info", "Warn", ...).</summary>
	public string LevelName => Level.Name;

	/// <summary>Logger name without its namespace.</summary>
	public string LoggerShort => Logger[(Logger.LastIndexOf('.') + 1)..];

	/// <summary>Message plus the exception text, for display and copy.</summary>
	public string FullMessage => Exception == null ? Message : $"{Message}\n{Exception}";

	/// <summary>Tab-separated line for the clipboard.</summary>
	public string ToLine() => $"{Time:HH:mm:ss.fff}\t{LevelName}\t{Logger}\t{FullMessage}";
}
