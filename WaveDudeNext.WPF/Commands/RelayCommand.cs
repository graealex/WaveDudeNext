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

using System.Windows.Input;

namespace WaveDudeNext.WPF.Commands;

/// <summary>
/// <see cref="ICommand"/> over a delegate with a typed parameter.
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
	private readonly Action<T> _execute;
	private readonly Predicate<T>? _canExecute;

	public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	/// <inheritdoc/>
	public event EventHandler? CanExecuteChanged
	{
		add => CommandManager.RequerySuggested += value;
		remove => CommandManager.RequerySuggested -= value;
	}

	/// <inheritdoc/>
	public bool CanExecute(object? parameter) =>
		parameter is T value && (_canExecute == null || _canExecute(value));

	/// <inheritdoc/>
	public void Execute(object? parameter)
	{
		if (parameter is T value)
		{
			_execute(value);
		}
	}
}
