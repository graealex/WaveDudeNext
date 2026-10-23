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

using System.Collections.Specialized;
using System.ComponentModel;
using NLog;
using WaveDudeNext.Model;

namespace WaveDudeNext.Parts;

/// <summary>
/// Keeps the sources that drive a <see cref="LogicGatePart"/> on its inputs (the user's request, 2026-10-21): a source on an
/// input port (within <see cref="Reach"/> pixels) is moved onto the port's new position whenever an edit of the gate moves
/// the port: its position, angle, port spacing, or a design value that changes the layout. Remembers each gate's input
/// ports after every edit, so it knows where they were. A group move that takes the sources along moves them before the
/// gate (<see cref="Movable.MoveAll"/>), so they have left the old ports by then and are not moved twice. It also hands every
/// gate, and every other part laid out for a frequency (<see cref="IFrequencyDesigned"/>, a zone plate), the simulation's
/// <see cref="Simulation.SourceFrequencyScale"/>, which turns its design frequency into its wavelength. UI thread, like the model.
/// </summary>
public sealed class GatePortFollower : IDisposable
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>How far, in canvas pixels on either axis, a source may stand from an input port and still count as on it.</summary>
	public const int Reach = 1;

	private readonly Project _project;
	private readonly Dictionary<LogicGatePart, (int X, int Y)[]?> _inputs = [];
	private INotifyCollectionChanged? _parts;
	private Simulation? _simulation;

	/// <param name="project">The project whose gates to follow for its lifetime (or until <see cref="Dispose"/>).</param>
	public GatePortFollower(Project project)
	{
		_project = project;
		project.PropertyChanged += OnProjectPropertyChanged;
		Resubscribe();
	}

	/// <summary>Stops following.</summary>
	public void Dispose()
	{
		_project.PropertyChanged -= OnProjectPropertyChanged;
		Unsubscribe();
	}

	private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(Project.Parts) or nameof(Project.Simulation))
		{
			Resubscribe();
		}
	}

	private void OnPartsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Resubscribe();

	/// <summary>A new frequency scale: every part laid out for a frequency gets it (and a gate's sources follow the ports it moves).</summary>
	private void OnSimulationChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(Simulation.SourceFrequencyScale))
		{
			foreach (IFrequencyDesigned part in _project.Parts.OfType<IFrequencyDesigned>())
			{
				part.FrequencyScale = _project.Simulation.SourceFrequencyScale;
			}
		}
	}

	/// <summary>Subscribes to every gate in the parts list, keeping what is known of gates still there and noting the ports of new ones.</summary>
	private void Resubscribe()
	{
		Dictionary<LogicGatePart, (int X, int Y)[]?> known = new(_inputs);
		Unsubscribe();
		_parts = _project.Parts;
		_parts.CollectionChanged += OnPartsChanged;
		_simulation = _project.Simulation;
		_simulation.PropertyChanged += OnSimulationChanged;
		double scale = _simulation.SourceFrequencyScale;
		foreach (LogicGatePart gate in _project.Parts.OfType<LogicGatePart>())
		{
			gate.PropertyChanged += OnGateChanged;
			if (known.TryGetValue(gate, out (int X, int Y)[]? ports))
			{
				// a gate seen before keeps its remembered ports, so its sources follow the scale handed over now
				_inputs[gate] = ports;
				gate.FrequencyScale = scale;
			}
			else
			{
				// a new one starts at the ports it has at that scale (the change handler does nothing without remembered ports)
				gate.FrequencyScale = scale;
				_inputs[gate] = Inputs(gate);
			}
		}
		// the other parts laid out for a frequency (a zone plate) have no ports to follow, only the scale
		foreach (IFrequencyDesigned part in _project.Parts.OfType<IFrequencyDesigned>().Where(part => part is not LogicGatePart))
		{
			part.FrequencyScale = scale;
		}
	}

	private void Unsubscribe()
	{
		if (_parts != null)
		{
			_parts.CollectionChanged -= OnPartsChanged;
			_parts = null;
		}
		if (_simulation != null)
		{
			_simulation.PropertyChanged -= OnSimulationChanged;
			_simulation = null;
		}
		foreach (LogicGatePart gate in _inputs.Keys)
		{
			gate.PropertyChanged -= OnGateChanged;
		}
		_inputs.Clear();
	}

	/// <summary>Moves the sources on the gate's old input ports onto the new ones, then remembers the new ones.</summary>
	private void OnGateChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not LogicGatePart gate || !_inputs.TryGetValue(gate, out (int X, int Y)[]? before))
		{
			return;
		}
		(int X, int Y)[]? after = Inputs(gate);
		_inputs[gate] = after;
		if (before == null || after == null)
		{
			return;
		}
		for (int i = 0; i < before.Length; i++)
		{
			if (before[i] == after[i])
			{
				continue;
			}
			foreach (Source source in _project.Sources)
			{
				if (Math.Abs(source.X - before[i].X) <= Reach && Math.Abs(source.Y - before[i].Y) <= Reach)
				{
					Logger.Debug("Source '{Source}' follows input {Port} of '{Gate}' ({Property}): ({X0}, {Y0}) to ({X1}, {Y1})",
						source.Name, i == 0 ? "A" : "B", gate.Name, e.PropertyName, source.X, source.Y, after[i].X, after[i].Y);
					source.MoveBy(after[i].X - source.X, after[i].Y - source.Y);
				}
			}
		}
	}

	/// <summary>The gate's two input ports in canvas pixels, or null without a design.</summary>
	private static (int X, int Y)[]? Inputs(LogicGatePart gate) => gate.Ports() is { } ports ? [ports[0], ports[1]] : null;
}
