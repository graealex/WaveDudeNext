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
using System.Runtime.CompilerServices;

namespace WaveDudeNext.Model;

/// <summary>
/// Base of the model classes: raises <see cref="PropertyChanged"/> when a property value
/// changes, so bound views (tree headers, editors) follow edits without manual refreshes.
/// Not thread-safe; the model is edited on the UI thread only.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Stores <paramref name="value"/> in <paramref name="field"/> and raises
	/// <see cref="PropertyChanged"/> when it differs, with the value before and after
	/// (<see cref="PropertyValueChangedEventArgs"/>); returns whether it did.
	/// </summary>
	protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}
		T old = field;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyValueChangedEventArgs(name, old, value));
		return true;
	}

	/// <summary>Raises <see cref="PropertyChanged"/> for <paramref name="name"/>; use for derived properties.</summary>
	protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

	/// <summary>
	/// Memberwise copy that does not carry over the <see cref="PropertyChanged"/> subscribers
	/// of the original. Derived classes deep-copy their reference-typed members on top.
	/// </summary>
	protected T ShallowClone<T>() where T : ObservableObject
	{
		var copy = (T)MemberwiseClone();
		copy.PropertyChanged = null;
		return copy;
	}
}

/// <summary>
/// The arguments <see cref="ObservableObject.SetProperty{T}"/> raises: the value before and after the edit, so a
/// watcher can log "old → new" without a shadow copy of the model (2026-10-27, the user's wish after a report whose
/// log named the property edited but not the value). A derived property's notification
/// (<see cref="ObservableObject.OnPropertyChanged"/>) carries the plain arguments.
/// </summary>
/// <param name="propertyName">The property that changed.</param>
/// <param name="oldValue">Its value before the edit.</param>
/// <param name="newValue">The value set.</param>
public sealed class PropertyValueChangedEventArgs(string? propertyName, object? oldValue, object? newValue) : PropertyChangedEventArgs(propertyName)
{
	/// <summary>The value before the edit.</summary>
	public object? OldValue { get; } = oldValue;

	/// <summary>The value set.</summary>
	public object? NewValue { get; } = newValue;
}
