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

using WaveDudeNext.Probes;
using NLog;

namespace WaveDudeNext.Engine;

/// <summary>A Vulkan implementation of the wave-engine contract, driven by one thread at a time.</summary>
/// <remarks>Captures and probes use host-visible memory. Each Vulkan dispatch currently waits for completion.</remarks>
internal sealed class VulkanWaveSimulator : IWaveEngine, IDisposable
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
	private readonly EngineOptions _options;
	private readonly VulkanWaveStepper _stepper;
	private SceneData _scene;
	private Emitter[] _emitters;
	private readonly float[] _field;
	private readonly float[]? _intensity;
	private bool _disposed;

	/// <summary>Creates the engine for a scene, options and object emitters.</summary>
	public VulkanWaveSimulator(SceneData scene, EngineOptions options, IReadOnlyList<Emitter>? emitters = null)
	{
		_scene = scene;
		_options = options;
		_emitters = FilterEmitters(scene, emitters);
		_field = new float[checked((scene.Width + 2) * (scene.Height + 2))];
		_intensity = options.TrackIntensity ? new float[_field.Length] : null;
		_stepper = new VulkanWaveStepper(scene, options.FourthOrderLaplacian, options.TrackIntensity, Retention(scene, _emitters), options.SourceFrequencyScale, options.SourceAmplitude, _emitters, options.IntensityScale);
		Logger.Debug("Vulkan engine built on {Device}: grid {Width} × {Height} (canvas {CanvasWidth} × {CanvasHeight}, rim {Rim}, {Edges}), stride {Stride}, {Stencil}, work group 32 × 8",
			_stepper.DeviceName, scene.Width, scene.Height, scene.CanvasWidth, scene.CanvasHeight, scene.Rim, scene.OpenEdges ? "open" : "reflective", scene.Width + 2, WaveSimulator.StencilText(options.FourthOrderLaplacian, _stepper.SubSteps));
	}

	/// <inheritdoc/>
	public string Description => "GPU: " + _stepper.DeviceName + " (Vulkan)";
	/// <inheritdoc/>
	public int Width => _scene.CanvasWidth;
	/// <inheritdoc/>
	public int Height => _scene.CanvasHeight;
	/// <inheritdoc/>
	public int Stride => _scene.Width + 2;
	/// <inheritdoc/>
	public int Rim => _scene.Rim;
	/// <inheritdoc/>
	public float[] FieldData => _field;
	/// <inheritdoc/>
	public float[]? IntensityData => _intensity;

	/// <inheritdoc/>
	public void AdvanceOneStep() => _stepper.AdvanceOneStep();

	/// <inheritdoc/>
	public void PrepareFrame(bool needField, bool needIntensity)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (needField)
		{
			_stepper.Field.CopyTo(_field);
		}
		if (needIntensity && _intensity != null)
		{
			Span<float> first = _stepper.StageOne;
			Span<float> second = _stepper.StageTwo;
			float scale = (float)_options.IntensityScale;
			for (int i = 0; i < _intensity.Length; i++)
			{
				_intensity[i] = (first[i] * 0.5f + second[i] * 0.5f) * scale;
			}
		}
	}

	/// <inheritdoc/>
	public FrameSnapshot CreateSnapshot(FrameShape shape, bool withIntensity) => new(shape, withIntensity);
	/// <inheritdoc/>
	public void CaptureFrame(FrameSnapshot snapshot, bool needIntensity) => _stepper.CaptureFrame(snapshot, needIntensity);
	/// <inheritdoc/>
	public void AwaitFrame(FrameSnapshot snapshot) => _stepper.AwaitFrame(snapshot);
	/// <inheritdoc/>
	public void Reset() => _stepper.Reset();

	/// <inheritdoc/>
	public bool TryReplaceScene(SceneData scene)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!_stepper.TryReplaceScene(scene, Retention(scene, _emitters)))
		{
			return false;
		}
		_scene = scene;
		return true;
	}

	/// <inheritdoc/>
	public void SetEmitters(IReadOnlyList<Emitter>? emitters)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Emitter[] filtered = FilterEmitters(_scene, emitters);
		_stepper.SetEmitters(filtered, Retention(_scene, filtered));
		_emitters = filtered;
	}

	/// <inheritdoc/>
	public void SetProbes(ProbeSampler? sampler) => _stepper.SetProbes(sampler);
	/// <inheritdoc/>
	public void SampleProbes() => _stepper.SampleProbes();
	/// <inheritdoc/>
	public float[][] TakeProbeSamples() => _stepper.TakeProbeSamples();
	/// <inheritdoc/>
	public float[]?[] ReadProbeProfiles() => _stepper.ReadProbeProfiles();

	private float Retention(SceneData scene, IEnumerable<Emitter> emitters)
	{
		var levels = new bool[256];
		foreach (byte level in scene.SourceLevel)
		{
			levels[level] = true;
		}
		return WaveSimulator.IntensityRetention(_options.TimeDecay, WaveSimulator.LowestSourceLevel(levels, emitters));
	}

	private static Emitter[] FilterEmitters(SceneData scene, IReadOnlyList<Emitter>? emitters)
	{
		Emitter[] filtered = emitters?.Where(source => source.X >= 0 && source.Y >= 0 && source.X < scene.CanvasWidth && source.Y < scene.CanvasHeight).ToArray() ?? [];
		return filtered;
	}

	/// <summary>Releases the Vulkan engine resources after all captures have been awaited.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_stepper.Dispose();
	}
}
