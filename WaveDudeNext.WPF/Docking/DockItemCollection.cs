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

using System.Collections.ObjectModel;

namespace WaveDudeNext.WPF.Docking;

/// <summary>
/// The registered items of a <see cref="DockHost"/>. Adding registers the item with the host;
/// items cannot be replaced or removed once registered, because saved layouts refer to them.
/// </summary>
public sealed class DockItemCollection : ObservableCollection<DockItem>
{
	private readonly Action<DockItem> _register;

	internal DockItemCollection(Action<DockItem> register)
	{
		_register = register;
	}

	/// <inheritdoc/>
	protected override void InsertItem(int index, DockItem item)
	{
		_register(item);
		base.InsertItem(index, item);
	}

	/// <inheritdoc/>
	protected override void SetItem(int index, DockItem item) =>
		throw new NotSupportedException("Dock items cannot be replaced.");

	/// <inheritdoc/>
	protected override void RemoveItem(int index) =>
		throw new NotSupportedException("Dock items cannot be unregistered.");

	/// <inheritdoc/>
	protected override void ClearItems() =>
		throw new NotSupportedException("Dock items cannot be unregistered.");
}
