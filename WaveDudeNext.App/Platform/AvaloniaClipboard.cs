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

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using NLog;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// The Avalonia head's clipboard for the models of <c>WaveDudeNext.UI</c> (<see cref="IClipboard"/>). Avalonia's clipboard
/// answers later, and the interface answers at once, as it foresees: a read starts an asynchronous read and reports
/// "nothing known" until that has landed, after which the text read is answered for as long as the clipboard's change
/// count stays (Windows' clipboard sequence number; 0 elsewhere, where every question reads again). Writing is started
/// and not awaited; a refusal is logged.
/// </summary>
/// <param name="owner">The window whose clipboard is used.</param>
public sealed class AvaloniaClipboard(Window owner) : IClipboard
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	private (uint ChangeCount, string? Text)? _read;
	private uint _reading;
	private bool _readInFlight;

	/// <inheritdoc/>
	public uint ChangeCount => OperatingSystem.IsWindows() ? GetClipboardSequenceNumber() : 0;

	/// <inheritdoc/>
	public bool TrySetText(string format, string text)
	{
		Avalonia.Input.Platform.IClipboard? clipboard = owner.Clipboard;
		if (clipboard == null)
		{
			Logger.Warn("No clipboard on this platform; nothing was copied");
			return false;
		}
		var transfer = new DataTransfer();
		var item = new DataTransferItem();
		item.Set(DataFormat.CreateStringApplicationFormat(format), text);
		item.SetText(text);
		transfer.Add(item);
		_read = null;
		_ = WriteAsync(clipboard, transfer);
		return true;
	}

	/// <inheritdoc/>
	public bool TryGetText(string format, out string? text)
	{
		uint count = ChangeCount;
		if (_read is { } read && (count == 0 || read.ChangeCount == count) && !_readInFlight)
		{
			text = read.Text;
			return true;
		}
		text = null;
		if (!_readInFlight)
		{
			_reading = count;
			_readInFlight = true;
			_ = ReadAsync(format);
		}
		return false;
	}

	private async Task WriteAsync(Avalonia.Input.Platform.IClipboard clipboard, DataTransfer transfer)
	{
		try
		{
			await clipboard.SetDataAsync(transfer);
		}
		catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
		{
			Logger.Warn(ex, "The clipboard is busy; nothing was copied");
		}
	}

	private async Task ReadAsync(string format)
	{
		string? text = null;
		bool refused = false;
		try
		{
			if (owner.Clipboard is { } clipboard && await clipboard.TryGetDataAsync() is { } data)
			{
				text = await data.TryGetValueAsync(DataFormat.CreateStringApplicationFormat(format)) ?? await data.TryGetTextAsync();
			}
		}
		catch (ExternalException ex)
		{
			// another program holds the clipboard open (CLIPBRD_E_CANT_OPEN): nothing is known, and the next question reads
			// again, as the interface says and the WPF head does (2026-11-11: the refusal had been kept as "holds nothing"
			// until the clipboard's next change, and logged as a warning with its stack trace)
			refused = true;
			Logger.Debug("The clipboard is held by another program; the next question reads again ({0})", ex.Message);
		}
		catch (InvalidOperationException)
		{
			// what the clipboard holds cannot be had as text after all: as good as none
			text = null;
		}
		finally
		{
			_read = refused ? null : (_reading, text);
			_readInFlight = false;
		}
	}

	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();
}
