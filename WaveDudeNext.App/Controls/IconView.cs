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
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace WaveDudeNext.App.Controls;

/// <summary>
/// A 16 px icon: one of the application's own drawings named by <see cref="Drawing"/> (Themes/Icons.axaml, the WPF head's
/// Themes/Icons.xaml converted at build, Build/SharedXaml.targets, 2026-11-10: one set of geometries for both heads),
/// stroked at 1 px in the inherited text colour so it greys out with the text beside it, with the filled part the
/// <c>Icon{name}Fill</c> geometry gives it, and, for an add button, the <see cref="PlusBadge"/> in the bottom-right
/// corner. The project tree's rows, folders and toolbar hold one each, and a menu entry takes one as its icon
/// (<see cref="MenuIcon"/>). The WPF head's IconView had also shown a glyph of Windows' icon font, which this head never
/// had (the port's ADR 0002); since 2026-11-10 every one of those is a drawing in the shared file. Not focusable. UI thread.
/// </summary>
public sealed class IconView : Control
{
	/// <summary>Identifies the <see cref="Drawing"/> property.</summary>
	public static readonly StyledProperty<string?> DrawingProperty =
		AvaloniaProperty.Register<IconView, string?>(nameof(Drawing));

	/// <summary>Identifies the <see cref="PlusBadge"/> property.</summary>
	public static readonly StyledProperty<bool> PlusBadgeProperty =
		AvaloniaProperty.Register<IconView, bool>(nameof(PlusBadge));

	/// <summary>Identifies the <see cref="Foreground"/> property: the text colour, inherited from the row or button around the icon.</summary>
	public static readonly StyledProperty<IBrush?> ForegroundProperty =
		TextElement.ForegroundProperty.AddOwner<IconView>();

	/// <summary>Identifies the <see cref="BadgeBrush"/> property.</summary>
	public static readonly StyledProperty<IBrush?> BadgeBrushProperty =
		AvaloniaProperty.Register<IconView, IBrush?>(nameof(BadgeBrush));

	private const string BadgeKey = "IconPlusBadge";
	private const string AccentKey = "Accent";

	private Geometry? _stroke;
	private Geometry? _fill;
	private Geometry? _badge;
	private bool _resolved;

	static IconView()
	{
		AffectsRender<IconView>(DrawingProperty, PlusBadgeProperty, ForegroundProperty, BadgeBrushProperty, IsEffectivelyEnabledProperty);
	}

	/// <summary>An empty 16 px icon, out of the Tab cycle.</summary>
	public IconView()
	{
		Focusable = false;
		IsTabStop = false;
		Width = 16;
		Height = 16;
	}

	/// <summary>
	/// The name of one of the application's drawings: the stroked geometry <c>Icon{name}</c> and, where part of it is
	/// solid, the filled <c>Icon{name}Fill</c>, both on the 16 px grid (Themes/Icons.axaml); or null for an empty icon.
	/// </summary>
	public string? Drawing
	{
		get => GetValue(DrawingProperty);
		set => SetValue(DrawingProperty, value);
	}

	/// <summary>
	/// Whether the drawing carries the plus of an add command (the tree's toolbar and folders): <c>IconPlusBadge</c> in the
	/// bottom-right corner, which the drawings leave free, in <see cref="BadgeBrush"/>, else the accent colour, else the
	/// text colour, which it also takes on a disabled control.
	/// </summary>
	public bool PlusBadge
	{
		get => GetValue(PlusBadgeProperty);
		set => SetValue(PlusBadgeProperty, value);
	}

	/// <summary>The colour of the lines and the solid parts: the inherited text colour unless set.</summary>
	public IBrush? Foreground
	{
		get => GetValue(ForegroundProperty);
		set => SetValue(ForegroundProperty, value);
	}

	/// <summary>
	/// The colour of the plus badge; null takes the theme's Accent. A style sets it to the text colour on a pressed
	/// button, where the accent is the background (upstream's IconPlusBadgeStyle).
	/// </summary>
	public IBrush? BadgeBrush
	{
		get => GetValue(BadgeBrushProperty);
		set => SetValue(BadgeBrushProperty, value);
	}

	/// <inheritdoc />
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == DrawingProperty)
		{
			_resolved = false;
		}
	}

	/// <inheritdoc />
	protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
	{
		base.OnAttachedToLogicalTree(e);
		// the application's resources are reachable only from here on; a lookup made earlier would have missed them
		_resolved = false;
	}

	/// <inheritdoc />
	public override void Render(DrawingContext context)
	{
		if (!_resolved)
		{
			Resolve();
		}
		if (Foreground is not { } brush)
		{
			return;
		}
		if (_stroke != null)
		{
			context.DrawGeometry(null, new Pen(brush, 1), _stroke);
		}
		if (_fill != null)
		{
			context.DrawGeometry(brush, null, _fill);
		}
		if (PlusBadge && _badge != null)
		{
			// the accent where it shows: a disabled control greys its text, and the badge follows it
			IBrush badge = BadgeBrush ?? (IsEffectivelyEnabled ? FindBrush(AccentKey) : null) ?? brush;
			context.DrawGeometry(null, new Pen(badge, 1), _badge);
		}
	}

	private void Resolve()
	{
		_resolved = true;
		_stroke = null;
		_fill = null;
		_badge = FindGeometry(BadgeKey);
		if (Drawing is { Length: > 0 } name)
		{
			_stroke = FindGeometry("Icon" + name);
			_fill = FindGeometry("Icon" + name + "Fill");
		}
	}

	/// <summary>
	/// The geometry under <paramref name="key"/> in the shared Themes/Icons.axaml, which the application merges
	/// (App.axaml, 2026-11-11; the icon had loaded a copy of its own before): found from the icon's place in the tree,
	/// whose last stop is the application.
	/// </summary>
	private Geometry? FindGeometry(string key) =>
		this.TryFindResource(key, ActualThemeVariant, out object? value) && value is Geometry geometry ? geometry : null;

	private IBrush? FindBrush(string key) =>
		this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush : null;
}
