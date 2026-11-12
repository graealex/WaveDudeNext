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

using Avalonia;
using Avalonia.Media;

namespace WaveDudeNext.App.Theming;

/// <summary>
/// How the text of every window is rendered (2026-11-09, the user's report that the text looked worse than the WPF
/// head's): subpixel antialiasing, ClearType's way (the backend's own default for an unspecified mode, see
/// <see cref="Apply"/>), strong hinting and the baseline on the pixel grid, which is what WPF's Display text formatting
/// gives the WPF head at 100 %. Avalonia 12 exposes these as methods on <see cref="TextOptions"/>, not as properties a
/// style's setter can name, so <see cref="WindowZoom.Attach"/> applies them to each window as it attaches; the inherited
/// value reaches the window's popups, which are roots of their own. What stays apart from WPF (2026-11-11, measured
/// against its captures): Skia blends the raw 3×1 ClearType coverage from DirectWrite with its own gamma and contrast,
/// so the strokes are heavier and the fringes saturated where WPF's are faint, which nothing in Avalonia reaches; the
/// glyphs are placed on whole pixels by <see cref="WholePixelShaper"/>, where Skia alone places them on quarter pixels.
/// Greyscale for the whole window was built and measured that night (thinner, no colour, more stems whole) and the user
/// refused it: subpixel antialiasing stays, the turned header strip alone is grey (<c>PictureFrame</c>).
/// </summary>
public static class TextRendering
{
	/// <summary>
	/// Sets the hinting and the baseline alignment on <paramref name="root"/>, for it and everything under it. The rendering
	/// mode is left unspecified on purpose (2026-11-11): the Skia backend's draw context merges a visual's options into the
	/// current ones by filling what is unspecified (<c>TextOptions.MergeWith</c> in <c>DrawingContextImpl.PushTextOptions</c>,
	/// read at 12.1.5), so a mode set on the window stands over every descendant's, and an unspecified mode is drawn as
	/// subpixel antialiasing anyway (<c>GlyphRunImpl.GetTextBlob</c>); unspecified, it lets the turned header strip ask for
	/// greyscale (<c>PictureFrame</c>), as WPF renders turned text.
	/// </summary>
	public static void Apply(Visual root)
	{
		TextOptions.SetTextHintingMode(root, TextHintingMode.Strong);
		TextOptions.SetBaselinePixelAlignment(root, BaselinePixelAlignment.Aligned);
	}
}
