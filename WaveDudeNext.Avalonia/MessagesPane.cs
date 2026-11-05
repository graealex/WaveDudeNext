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

// Contributors: Vinicius Miguel

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;
using NLog;
using NLog.Targets;

namespace WaveDudeNext.CrossPlatform;

/// <summary>A bounded in-memory NLog target. Writes may arrive on any thread; entries and notifications belong to the UI thread.</summary>
internal sealed class MessagesTarget : Target
{
	private readonly ConcurrentQueue<MessageEntry> _pending = new();
	private readonly List<MessageEntry> _entries = [];
	private int _scheduled;
	/// <summary>Entries in chronological order, up to 5000. UI thread only.</summary>
	internal IReadOnlyList<MessageEntry> Entries => _entries;
	/// <summary>Raised on the UI thread after a batch is added or cleared.</summary>
	internal event EventHandler? Changed;
	/// <summary>Raised on the UI thread for the latest informational or higher message in a batch.</summary>
	internal event Action<MessageEntry>? StatusMessage;
	/// <inheritdoc/>
	protected override void Write(LogEventInfo logEvent)
	{
		_pending.Enqueue(new MessageEntry(logEvent.TimeStamp, logEvent.Level, logEvent.LoggerName ?? "", logEvent.FormattedMessage, logEvent.Exception?.ToString()));
		if (Interlocked.Exchange(ref _scheduled, 1) == 0) Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
	}
	private void Flush()
	{
		MessageEntry? status = null;
		while (_pending.TryDequeue(out MessageEntry? entry))
		{
			_entries.Add(entry);
			if (entry.Level >= LogLevel.Info) status = entry;
		}
		if (_entries.Count > 5000) _entries.RemoveRange(0, _entries.Count - 5000);
		Interlocked.Exchange(ref _scheduled, 0);
		if (!_pending.IsEmpty && Interlocked.Exchange(ref _scheduled, 1) == 0) Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
		Changed?.Invoke(this, EventArgs.Empty);
		if (status != null) StatusMessage?.Invoke(status);
	}
	/// <summary>Clears kept and queued entries on the UI thread.</summary>
	internal void Clear()
	{
		while (_pending.TryDequeue(out _)) { }
		_entries.Clear();
		Changed?.Invoke(this, EventArgs.Empty);
	}
}

/// <summary>A log event captured on its producer thread.</summary>
internal sealed record MessageEntry(DateTime Time, LogLevel Level, string Logger, string Message, string? Exception)
{
	/// <summary>Message and full exception for search and copying.</summary>
	internal string FullMessage => Exception == null ? Message : $"{Message}\n{Exception}";
	/// <summary>Tab-separated line for the clipboard.</summary>
	internal string ToLine() => $"{Time:HH:mm:ss.fff}\t{Level.Name}\t{Logger}\t{FullMessage}";
	/// <inheritdoc/>
	public override string ToString() => $"{Time:HH:mm:ss.fff}  {Level.Name,-5}  {Logger[(Logger.LastIndexOf('.') + 1)..]}  {Message}";
}

/// <summary>The filterable in-memory log. Construct and use on the UI thread.</summary>
internal sealed class MessagesPane : DockPanel
{
	private readonly MessagesTarget _target;
	private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
	private readonly TextBox _search = new() { Width = 180 };
	private readonly CheckBox _regex = new();
	private readonly ToggleButton _autoScroll = new() { IsChecked = true };
	private readonly Dictionary<LogLevel, ToggleButton> _levels = [];
	private MessageEntry[] _visible = [];

	/// <summary>Builds the controls and attaches them to the target.</summary>
	internal MessagesPane(MessagesTarget target)
	{
		_target = target;
		var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
#if DEBUG
		LogLevel[] levels = [LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal];
#else
		LogLevel[] levels = [LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal];
#endif
		foreach (LogLevel level in levels)
		{
			var button = new ToggleButton { IsChecked = level >= LogLevel.Info };
#if DEBUG
			button.IsChecked = true;
#endif
			AutomationProperties.SetAutomationId(button, "MessagesLevel:" + level.Name);
			button.IsCheckedChanged += (_, _) => Refresh();
			_levels.Add(level, button);
			toolbar.Children.Add(button);
		}
		AutomationProperties.SetAutomationId(_search, "MessagesSearch");
		_search.Bind(TextBox.PlaceholderTextProperty, _search.GetResourceObservable("MessagesSearchHint"));
		_search.TextChanged += (_, _) => Refresh();
		_regex.Bind(ContentControl.ContentProperty, _regex.GetResourceObservable("MessagesRegex"));
		AutomationProperties.SetAutomationId(_regex, "MessagesRegex");
		_regex.IsCheckedChanged += (_, _) => Refresh();
		_autoScroll.Bind(ContentControl.ContentProperty, _autoScroll.GetResourceObservable("MessagesAutoScroll"));
		AutomationProperties.SetAutomationId(_autoScroll, "MessagesAutoScroll");
		_autoScroll.IsCheckedChanged += (_, _) => { if (_autoScroll.IsChecked == true) ScrollToEnd(); };
		var clear = new Button { Command = new ControlCommand(target.Clear, () => true) };
		clear.Bind(ContentControl.ContentProperty, clear.GetResourceObservable("MessagesClear"));
		AutomationProperties.SetAutomationId(clear, "MessagesClear");
		toolbar.Children.Add(_search); toolbar.Children.Add(_regex); toolbar.Children.Add(_autoScroll); toolbar.Children.Add(clear);
		SetDock(toolbar, Dock.Top); Children.Add(toolbar);
		AutomationProperties.SetAutomationId(_list, "MessagesList");
		_list.ItemTemplate = new FuncDataTemplate<MessageEntry>((entry, _) =>
		{
			var text = new TextBlock { Text = entry?.ToString() };
			if (entry?.Exception != null) ToolTip.SetTip(text, entry.Exception);
			return text;
		});
		var copy = new MenuItem { Command = new OpenCommand(() => Copy(false)) };
		KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
		_list.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.C, modifier), Command = copy.Command });
		copy.Bind(HeaderedItemsControl.HeaderProperty, copy.GetResourceObservable("MessagesCopy"));
		var copyAll = new MenuItem { Command = new OpenCommand(() => Copy(true)) };
		copyAll.Bind(HeaderedItemsControl.HeaderProperty, copyAll.GetResourceObservable("MessagesCopyAll"));
		_list.ContextMenu = new ContextMenu { Items = { copy, copyAll } };
		Children.Add(_list);
		target.Changed += (_, _) => Refresh();
		Refresh();
	}
	/// <summary>Focuses the message list when the pane is shown.</summary>
	internal void FocusList() => _list.Focus();
	private async Task Copy(bool all)
	{
		var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard == null) return;
		IEnumerable<MessageEntry> entries = all ? _visible : _list.SelectedItems?.OfType<MessageEntry>() ?? [];
		try { await clipboard.SetTextAsync(string.Join(Environment.NewLine, entries.Select(entry => entry.ToLine()))); }
		catch (Exception ex) { LogManager.GetCurrentClassLogger().Warn(ex, "Could not copy messages to clipboard"); }
	}
	private void Refresh()
	{
		MessageEntry[] selected = _list.SelectedItems?.OfType<MessageEntry>().ToArray() ?? [];
		string search = _search.Text ?? "";
		Regex? regex = null;
		if (_regex.IsChecked == true && search.Length > 0)
		{
			try { regex = new Regex(search, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
			catch (ArgumentException) { search = ""; }
		}
		bool Match(MessageEntry entry)
		{
			if (!_levels.TryGetValue(entry.Level, out ToggleButton? button) || button.IsChecked != true) return false;
			return search.Length == 0 || (regex != null ? regex.IsMatch(entry.FullMessage) || regex.IsMatch(entry.Logger) : entry.FullMessage.Contains(search, StringComparison.OrdinalIgnoreCase) || entry.Logger.Contains(search, StringComparison.OrdinalIgnoreCase));
		}
		foreach ((LogLevel level, ToggleButton button) in _levels) button.Content = $"{level.Name} ({_target.Entries.Count(entry => entry.Level == level)})";
		try { _visible = _target.Entries.Where(Match).ToArray(); }
		catch (RegexMatchTimeoutException) { _visible = []; }
		_list.ItemsSource = _visible;
		if (_list.SelectedItems is { } selection)
			foreach (MessageEntry entry in selected.Where(_visible.Contains)) selection.Add(entry);
		if (_autoScroll.IsChecked == true) ScrollToEnd();
	}
	private void ScrollToEnd() { if (_visible.LastOrDefault() is { } last) _list.ScrollIntoView(last); }
}
