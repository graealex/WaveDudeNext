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

using System.Runtime.InteropServices;
using System.Windows;
using WaveDudeNext.Model;

namespace WaveDudeNext.WPF;

/// <summary>
/// Project objects carried by a drag or the clipboard (2026-10-23): the text <see cref="ProjectClipboard.Write"/> makes,
/// in the app's format or as plain text that may hold it (copied back from a text editor, which keeps text only;
/// <see cref="ProjectClipboard.Read"/> decides, and text not starting with an element is not even parsed). An instance
/// parses each text once, for the many drag-over events of one drag. The text is all a drag carries, from this window
/// or another: the target decides what a drop does (the tree reorders its own drag, anything else pastes a copy). UI thread.
/// </summary>
internal sealed class ObjectsData
{
	private (string Text, ProjectClipboard.Clip? Clip)? _cache;

	/// <summary>The objects <paramref name="data"/> carries, parsed once per text; null without any.</summary>
	public ProjectClipboard.Clip? Clip(IDataObject data)
	{
		if (Text(data) is not { } text)
		{
			return null;
		}
		if (_cache is not { } cache || cache.Text != text)
		{
			_cache = cache = (text, ProjectClipboard.Read(text));
		}
		return cache.Clip;
	}

	/// <summary>The objects as <see cref="Clip"/> gives them, then forgotten: a paste makes them the project's, so the same text is parsed afresh next time.</summary>
	public ProjectClipboard.Clip? Take(IDataObject data)
	{
		ProjectClipboard.Clip? clip = Clip(data);
		_cache = null;
		return clip;
	}

	/// <summary>Forgets the parsed text.</summary>
	public void Clear() => _cache = null;

	/// <summary>The objects' text in <paramref name="data"/>: the app's format, else plain text that starts with an element; null for neither (and when the data cannot be read).</summary>
	public static string? Text(IDataObject data)
	{
		try
		{
			if (data.GetDataPresent(ProjectClipboard.Format))
			{
				return data.GetData(ProjectClipboard.Format) as string;
			}
			return data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.TrimStart().StartsWith('<')
				? text
				: null;
		}
		catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
		{
			return null;
		}
	}
}
