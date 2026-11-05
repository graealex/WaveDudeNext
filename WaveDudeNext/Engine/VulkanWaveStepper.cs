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
using WaveDudeNext.Model;
using WaveDudeNext.Probes;

namespace WaveDudeNext.Engine;

/// <summary>Vulkan field and intensity stepping for one fixed scene, driven by one thread.</summary>
/// <remarks>Image and object sources are applied here; probe values are read from mapped memory. Each dispatch waits.</remarks>
internal sealed class VulkanWaveStepper : IDisposable
{
	private readonly VulkanComputeContext _context;
	private readonly List<VulkanBuffer> _buffers = [];
	private readonly List<VulkanPipeline> _pipelines = [];
	private readonly VulkanBuffer[] _field = new VulkanBuffer[2];
	private readonly VulkanBuffer _stageOne;
	private readonly VulkanBuffer _stageTwo;
	private readonly VulkanBuffer _c2;
	private readonly VulkanBuffer _damping;
	private readonly VulkanBuffer? _sourceIndices;
	private readonly VulkanBuffer? _sourceLevels;
	private readonly VulkanPipeline[] _step = new VulkanPipeline[2];
	private readonly VulkanPipeline[]? _edges;
	private readonly VulkanPipeline[]? _imageSources;
	private VulkanPipeline[]? _objectSources;
	private VulkanBuffer? _objectBuffer;
	private byte[]? _initialObjectState;
	private int _objectCount;
	private readonly double _sourceFrequencyScale;
	private readonly int _sourceCount;
	private readonly float _sourceAmplitude;
	private double _time;
	private readonly int _width;
	private readonly int _height;
	private readonly int _stride;
	private readonly int _rim;
	private readonly int _canvasWidth;
	private readonly int _canvasHeight;
	private int _subSteps;
	private float _speedScale;
	private readonly bool _fourthOrder;
	private readonly bool _openEdges;
	private readonly bool _trackIntensity;
	private float _alpha;
	private readonly float _intensityScale;
	private int _current;
	private bool _disposed;
	private ProbeSampler? _sampler;
	private readonly List<List<float>> _probeValues = [];
	private double[]?[]? _profileTotals;

	/// <summary>Creates the scene buffers and pipelines. <paramref name="alpha"/> is the intensity retention per step.</summary>
	public VulkanWaveStepper(SceneData scene, bool fourthOrder, bool trackIntensity, float alpha, double sourceFrequencyScale = 1, double sourceAmplitude = 1, IReadOnlyList<Emitter>? emitters = null, double intensityScale = 1)
	{
		_width = scene.Width;
		_height = scene.Height;
		_rim = scene.Rim;
		_canvasWidth = scene.CanvasWidth;
		_canvasHeight = scene.CanvasHeight;
		_stride = checked(_width + 2);
		int count = checked(_stride * checked(_height + 2));
		int bytes = checked(count * sizeof(float));
		_fourthOrder = fourthOrder;
		_openEdges = scene.OpenEdges;
		_trackIntensity = trackIntensity;
		_alpha = alpha;
		_intensityScale = (float)intensityScale;
		_sourceCount = scene.SourceCount;
		_sourceAmplitude = (float)sourceAmplitude;
		_sourceFrequencyScale = sourceFrequencyScale;
		_context = new VulkanComputeContext();
		try
		{
			_field[0] = NewBuffer(bytes);
			_field[1] = NewBuffer(bytes);
			_c2 = NewBuffer(bytes);
			_damping = NewBuffer(bytes);
			_stageOne = NewBuffer(bytes);
			_stageTwo = NewBuffer(bytes);
			foreach (VulkanBuffer buffer in _buffers)
			{
				buffer.Bytes.Clear();
			}
			if (scene.OpenEdges)
			{
				_edges = new VulkanPipeline[2];
			}
			VulkanBuffer? frequencies = null;
			if (_sourceCount > 0)
			{
				_imageSources = new VulkanPipeline[2];
				_sourceIndices = NewBuffer(checked(_sourceCount * sizeof(int)));
				_sourceLevels = NewBuffer(checked(_sourceCount * sizeof(int)));
				frequencies = NewBuffer(256 * sizeof(float));
				Span<float> table = MemoryMarshal.Cast<byte, float>(frequencies.Bytes);
				for (int level = 0; level < 256; level++)
				{
					table[level] = (float)(level / 255.0 * 0.5 * sourceFrequencyScale);
				}
			}
			for (int i = 0; i < 2; i++)
			{
				_step[i] = NewPipeline("WaveStep", [_field[i], _field[1 - i], _c2, _damping, _stageOne, _stageTwo], fourthOrder);
				if (_edges != null)
				{
					_edges[i] = NewPipeline("OpenEdges", [_field[i], _field[1 - i], _c2], null);
				}
				if (_imageSources != null)
				{
					_imageSources[i] = NewPipeline("ImageSources", [_field[i], _sourceIndices!, _sourceLevels!, frequencies!], null, (uint)Marshal.SizeOf<ImageSourceParameters>());
				}

			}
			LoadScene(scene);
			SetEmitters(emitters, alpha);
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	private VulkanBuffer NewBuffer(int bytes)
	{
		var buffer = new VulkanBuffer(_context, bytes);
		_buffers.Add(buffer);
		return buffer;
	}

	private VulkanPipeline NewPipeline(string name, IReadOnlyList<VulkanBuffer> buffers, bool? fourthOrder, uint pushSize = 0)
	{
		var pipeline = new VulkanPipeline(_context, name, buffers, pushSize == 0 ? (uint)Marshal.SizeOf<Parameters>() : pushSize, fourthOrder);
		_pipelines.Add(pipeline);
		return pipeline;
	}

	/// <summary>Current padded field, for source updates after preceding GPU work has finished.</summary>
	public Span<float> Field => MemoryMarshal.Cast<byte, float>(_field[_current].Bytes);
	/// <summary>First intensity stage in the padded grid.</summary>
	public Span<float> StageOne => MemoryMarshal.Cast<byte, float>(_stageOne.Bytes);
	/// <summary>Second intensity stage in the padded grid.</summary>
	public Span<float> StageTwo => MemoryMarshal.Cast<byte, float>(_stageTwo.Bytes);
	/// <summary>Name of the selected device.</summary>
	public string DeviceName => _context.Name;


	/// <summary>Replaces a compatible scene and resets the simulation without rebuilding its pipelines.</summary>
	public bool TryReplaceScene(SceneData scene, float intensityAlpha)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (scene.Width != _width || scene.Height != _height || scene.Rim != _rim || scene.OpenEdges != _openEdges || scene.SourceCount != _sourceCount)
		{
			return false;
		}
		LoadScene(scene);
		_alpha = intensityAlpha;
		Reset();
		return true;
	}

	private void LoadScene(SceneData scene)
	{
		_subSteps = _fourthOrder ? WaveSimulator.ComputeSubSteps(WaveSimulator.MaxOf(scene.C2)) : 1;
		_speedScale = 1f / (_subSteps * _subSteps);
		Span<float> speed = MemoryMarshal.Cast<byte, float>(_c2.Bytes);
		Span<float> loss = MemoryMarshal.Cast<byte, float>(_damping.Bytes);
		for (int y = 0; y < _height; y++)
		{
			for (int x = 0; x < _width; x++)
			{
				int source = y * _width + x;
				int target = (y + 1) * _stride + x + 1;
				speed[target] = scene.C2[source];
				loss[target] = _subSteps == 1 ? scene.D[source] : (float)Math.Pow(scene.D[source], 1.0 / _subSteps);
			}
		}
		if (_sourceIndices != null && _sourceLevels != null)
		{
			Span<int> indices = MemoryMarshal.Cast<byte, int>(_sourceIndices.Bytes);
			Span<int> levels = MemoryMarshal.Cast<byte, int>(_sourceLevels.Bytes);
			for (int i = 0; i < _sourceCount; i++)
			{
				indices[i] = (scene.SourceY[i] + 1) * _stride + scene.SourceX[i] + 1;
				levels[i] = scene.SourceLevel[i];
			}
		}
	}

	/// <summary>Installs probe definitions and clears earlier samples and totals.</summary>
	public void SetProbes(ProbeSampler? sampler)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_probeValues.Clear();
		_profileTotals = null;
		if (sampler == null || sampler.Count == 0)
		{
			_sampler = null;
			return;
		}
		if (sampler.NeedsIntensity && !_trackIntensity)
		{
			throw new InvalidOperationException("the intensity is not tracked");
		}
		_sampler = sampler;
		_profileTotals = new double[sampler.Count][];
		for (int i = 0; i < sampler.Count; i++)
		{
			_probeValues.Add([]);
			if (sampler.Integrate[i])
			{
				_profileTotals[i] = new double[sampler.Pixels[i].Length];
			}
		}
	}

	/// <summary>Samples point probes and accumulates integrated line profiles at the current step.</summary>
	public void SampleProbes()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_sampler == null)
		{
			return;
		}
		Span<float> field = Field;
		Span<float> first = StageOne;
		Span<float> second = StageTwo;
		int step = (int)Math.Round(_time);
		for (int p = 0; p < _sampler.Count; p++)
		{
			int[] pixels = _sampler.Pixels[p];
			if (!_sampler.IsProfile[p])
			{
				int i = pixels[0];
				_probeValues[p].Add(_sampler.UseIntensity[p] ? (first[i] * 0.5f + second[i] * 0.5f) * _intensityScale : field[i]);
			}
			else if (_sampler.Integrate[p] && _profileTotals?[p] is { } totals)
			{
				if (_sampler.UseStartAfterStep[p] && step < _sampler.StartAfterStep[p])
				{
					continue;
				}
				for (int k = 0; k < pixels.Length; k++)
				{
					int i = pixels[k];
					totals[k] += (first[i] * 0.5f + second[i] * 0.5f) * _intensityScale;
				}
			}
		}
	}

	/// <summary>Returns and clears point-probe samples.</summary>
	public float[][] TakeProbeSamples()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_sampler == null)
		{
			return [];
		}
		float[][] result = new float[_sampler.Count][];
		for (int i = 0; i < result.Length; i++)
		{
			result[i] = _probeValues[i].ToArray();
			_probeValues[i].Clear();
		}
		return result;
	}

	/// <summary>Returns the current or integrated values along each line probe.</summary>
	public float[]?[] ReadProbeProfiles()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_sampler == null)
		{
			return [];
		}
		var result = new float[]?[_sampler.Count];
		Span<float> field = Field;
		Span<float> first = StageOne;
		Span<float> second = StageTwo;
		for (int p = 0; p < result.Length; p++)
		{
			if (!_sampler.IsProfile[p])
			{
				continue;
			}
			int[] pixels = _sampler.Pixels[p];
			var values = new float[pixels.Length];
			for (int k = 0; k < pixels.Length; k++)
			{
				int i = pixels[k];
				values[k] = _sampler.Integrate[p] && _profileTotals?[p] is { } totals ? (float)totals[k]
					: _sampler.UseIntensity[p] ? (first[i] * 0.5f + second[i] * 0.5f) * _intensityScale : field[i];
			}
			result[p] = values;
		}
		return result;
	}


	/// <summary>Replaces object emitters without altering the current field or intensity.</summary>
	public void SetEmitters(IReadOnlyList<Emitter>? emitters, float intensityAlpha)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Emitter[] active = emitters?.Where(source => source.X >= 0 && source.Y >= 0 && source.X < _canvasWidth && source.Y < _canvasHeight).ToArray() ?? [];
		VulkanBuffer? newBuffer = null;
		VulkanPipeline[]? newPipelines = null;
		int bufferStart = _buffers.Count;
		int pipelineStart = _pipelines.Count;
		byte[]? initial = null;
		try
		{
			if (active.Length > 0)
			{
				newBuffer = NewBuffer(checked(active.Length * Marshal.SizeOf<ObjectSource>()));
				Span<ObjectSource> sources = MemoryMarshal.Cast<byte, ObjectSource>(newBuffer.Bytes);
				for (int i = 0; i < active.Length; i++)
				{
					Emitter source = active[i];
					sources[i] = new ObjectSource
					{
						Index = (source.Y + 1 + _rim) * _stride + source.X + 1 + _rim,
						Omega = (float)(source.Level / 255.0 * 0.5 * _sourceFrequencyScale),
						Amplitude = (float)source.Amplitude,
						Phase = (float)source.PhaseRadians,
						Mode = source.Mode == SourceMode.Pulsed ? 1 : 0,
						PulseShape = source.PulseShape == PulseShape.SmoothedSquare ? 1 : 0,
						OnWidth = source.OnWidthCycles,
						OffWidth = source.OffWidthCycles,
						DetachStep = source.DetachAfterStep,
						OffWidthBase = source.OffWidthBase,
						RandomizeAmount = source.RandomizeOffWidthAmount,
						RandomState = source.RngSeed,
					};
				}
				initial = newBuffer.Bytes.ToArray();
				newPipelines = new VulkanPipeline[2];
				for (int i = 0; i < 2; i++)
				{
					newPipelines[i] = NewPipeline("ObjectSources", [_field[i], newBuffer], null, (uint)Marshal.SizeOf<ObjectSourceParameters>());
				}
			}
		}
		catch
		{
			while (_pipelines.Count > pipelineStart)
			{
				int last = _pipelines.Count - 1;
				_pipelines[last].Dispose();
				_pipelines.RemoveAt(last);
			}
			while (_buffers.Count > bufferStart)
			{
				int last = _buffers.Count - 1;
				_buffers[last].Dispose();
				_buffers.RemoveAt(last);
			}
			throw;
		}
		if (_objectSources != null)
		{
			foreach (VulkanPipeline pipeline in _objectSources)
			{
				pipeline.Dispose();
				_pipelines.Remove(pipeline);
			}
		}
		if (_objectBuffer != null)
		{
			_objectBuffer.Dispose();
			_buffers.Remove(_objectBuffer);
		}
		_objectSources = newPipelines;
		_objectBuffer = newBuffer;
		_initialObjectState = initial;
		_objectCount = active.Length;
		_alpha = intensityAlpha;
	}

	/// <summary>Clears the field and intensity, restores emitter cycle state and removes probes.</summary>
	public void Reset()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_field[0].Bytes.Clear();
		_field[1].Bytes.Clear();
		_stageOne.Bytes.Clear();
		_stageTwo.Bytes.Clear();
		if (_objectBuffer != null && _initialObjectState != null)
		{
			_initialObjectState.CopyTo(_objectBuffer.Bytes);
		}
		_current = 0;
		_time = 0;
		SetProbes(null);
	}

	/// <summary>Copies the current canvas into a snapshot's staging planes; awaiting performs its format conversion.</summary>
	public void CaptureFrame(FrameSnapshot snapshot, bool needIntensity)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (snapshot.Shape.CanvasWidth != _canvasWidth || snapshot.Shape.CanvasHeight != _canvasHeight)
		{
			throw new ArgumentException("The snapshot's canvas does not match the engine's size.", nameof(snapshot));
		}
		int cells = checked(_canvasWidth * _canvasHeight);
		snapshot.FieldFloats ??= new float[cells];
		Span<float> source = Field;
		for (int y = 0; y < _canvasHeight; y++)
		{
			source.Slice((y + 1 + _rim) * _stride + 1 + _rim, _canvasWidth).CopyTo(snapshot.FieldFloats.AsSpan(y * _canvasWidth, _canvasWidth));
		}
		snapshot.HasIntensity = needIntensity && _trackIntensity && snapshot.HasIntensityPlane;
		if (snapshot.HasIntensity)
		{
			snapshot.IntensityFloats ??= new float[cells];
			Span<float> first = StageOne;
			Span<float> second = StageTwo;
			for (int y = 0; y < _canvasHeight; y++)
			{
				int start = (y + 1 + _rim) * _stride + 1 + _rim;
				for (int x = 0; x < _canvasWidth; x++)
				{
					int i = start + x;
					snapshot.IntensityFloats[y * _canvasWidth + x] = (first[i] * 0.5f + second[i] * 0.5f) * _intensityScale;
				}
			}
		}
		snapshot.PendingConversion = true;
	}

	/// <summary>Converts a captured snapshot's staging planes to their requested sample format.</summary>
	public void AwaitFrame(FrameSnapshot snapshot)
	{
		if (!snapshot.PendingConversion)
		{
			return;
		}
		snapshot.PendingConversion = false;
		FrameConversion.Convert(snapshot.FieldFloats!, 0, _canvasWidth, snapshot.Shape, false, snapshot.FieldMemory);
		if (snapshot.HasIntensity)
		{
			FrameConversion.Convert(snapshot.IntensityFloats!, 0, _canvasWidth, snapshot.Shape, true, snapshot.IntensityMemory);
		}
	}

	/// <summary>Applies image and object sources and advances the field by one simulation step.</summary>
	public void AdvanceOneStep()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_imageSources != null)
		{
			var sources = new ImageSourceParameters { Count = _sourceCount, Time = (float)_time, Amplitude = _sourceAmplitude };
			_imageSources[_current].Dispatch(in sources, (uint)((_sourceCount + 63) / 64));
		}
		if (_objectSources != null)
		{
			var sources = new ObjectSourceParameters { Count = _objectCount, Time = (float)_time };
			_objectSources[_current].Dispatch(in sources, (uint)((_objectCount + 63) / 64));
		}
		for (int sub = 0; sub < _subSteps; sub++)
		{
			var parameters = new Parameters
			{
				Width = _width, Height = _height, Stride = _stride,
				TrackIntensity = _trackIntensity && sub == _subSteps - 1 ? 1 : 0,
				SpeedScale = _speedScale, IntensityAlpha = _alpha,
			};
			_step[_current].Dispatch(in parameters, (uint)((_width + 31) / 32), (uint)((_height + 7) / 8));
			_edges?[_current].Dispatch(in parameters, (uint)((_width + 2 + _height + 63) / 64));
			_current = 1 - _current;
		}
		_time += 1;
	}

	/// <summary>Releases pipelines, buffers and the device.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		foreach (VulkanPipeline pipeline in _pipelines)
		{
			pipeline.Dispose();
		}
		foreach (VulkanBuffer buffer in _buffers)
		{
			buffer.Dispose();
		}
		_context.Dispose();
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Parameters
	{
		public int Width;
		public int Height;
		public int Stride;
		public int TrackIntensity;
		public float SpeedScale;
		public float IntensityAlpha;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ImageSourceParameters
	{
		public int Count;
		public float Time;
		public float Amplitude;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ObjectSourceParameters
	{
		public int Count;
		public float Time;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ObjectSource
	{
		public int Index;
		public float Omega;
		public float Amplitude;
		public float Phase;
		public int Mode;
		public int PulseShape;
		public int OnWidth;
		public int OffWidth;
		public int DetachStep;
		public int OffWidthBase;
		public int RandomizeAmount;
		public float CycleAnchor;
		public uint RandomState;
	}
}
