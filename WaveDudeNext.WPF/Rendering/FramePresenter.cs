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

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NLog;
using WaveDudeNext.Engine;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.WPF.Rendering;

/// <summary>
/// Turns frames of an <see cref="IFrameSource"/> (a run's store, or a single scratch frame) into the preview's
/// two <see cref="WriteableBitmap"/>s. A UI-thread object: <see cref="Source"/>, <see cref="SetLayer"/> and
/// <see cref="Show"/> are called there; the colouring
/// runs on the thread pool, one job at a time, and only the newest request is honoured (a scrub that outruns
/// the renderer skips frames rather than queueing them). The bitmaps are written on the UI thread.
/// </summary>
public sealed class FramePresenter
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>Every this many presented frames the average cost is logged at Debug level.</summary>
	private const int RenderLogInterval = 100;

	private readonly Dispatcher _dispatcher;
	private readonly LayerSettings[] _settings = [new(), new()];
	private IFrameSource? _source;
	private Job? _job;
	private int _wanted;
	private int _version;
	private int _busy;
	private int _renderedFrame = -1;
	private Job? _renderedJob;
	private readonly Stopwatch _renderClock = new();
	private readonly Stopwatch _copyClock = new(), _colourClock = new(), _writeClock = new();
	private int _renderCount;

	/// <param name="dispatcher">The UI thread's dispatcher, where the bitmaps live.</param>
	public FramePresenter(Dispatcher dispatcher) => _dispatcher = dispatcher;

	/// <summary>Colour and visibility of one layer, as last set through <see cref="SetLayer"/>.</summary>
	private sealed record LayerSettings
	{
		public int[]? Lut { get; init; }
		public float Brightness { get; init; } = 1f;
		public bool Enabled { get; init; } = true;
	}

	/// <summary>An immutable snapshot of everything one render needs, replaced wholesale on every change.</summary>
	private sealed class Job(IFrameSource source, WriteableBitmap? field, WriteableBitmap? intensity, LayerSettings[] settings)
	{
		public IFrameSource Source { get; } = source;
		public WriteableBitmap?[] Bitmaps { get; } = [field, intensity];
		public LayerSettings[] Settings { get; } = settings;
		/// <summary>The plane read from the source as halves (a <see cref="FrameFormat.Half"/> source), else null.</summary>
		public Half[]? Halves { get; } = source.Format == FrameFormat.Half ? new Half[source.Width * source.Height] : null;
		/// <summary>The plane read from the source as palette indices (a <see cref="FrameFormat.Index"/> source), else null.</summary>
		public byte[]? Indices { get; } = source.Format == FrameFormat.Index ? new byte[source.Width * source.Height] : null;
		public int[] Pixels { get; } = new int[source.Width * source.Height];
	}

	/// <summary>
	/// The frames to show; null shows nothing. The bitmaps (<see cref="FieldBitmap"/>, <see cref="IntensityBitmap"/>)
	/// are kept across sources of the same size and planes, so switching between a run's store and a scratch
	/// frame does not swap images; a new size creates new ones.
	/// </summary>
	public IFrameSource? Source
	{
		get => _source;
		set
		{
			if (ReferenceEquals(_source, value))
			{
				return;
			}
			bool sameShape = _source != null && value != null && _source.Width == value.Width && _source.Height == value.Height
				&& _source.Format == value.Format && _source.Has(FrameLayer.Intensity) == value.Has(FrameLayer.Intensity);
			Logger.Trace("Presenter source: {Source} (same shape {Same})", value?.GetType().Name ?? "none", sameShape);
			_source = value;
			if (!sameShape)
			{
				// the old pictures go first, so their memory can be reclaimed for the new ones
				FieldBitmap = null;
				IntensityBitmap = null;
				FieldBitmap = value != null && value.Has(FrameLayer.Field) ? NewBitmap(value) : null;
				IntensityBitmap = value != null && value.Has(FrameLayer.Intensity) ? NewBitmap(value) : null;
			}
			Rebuild();
		}
	}

	/// <summary>The field picture of the current store, or null.</summary>
	public WriteableBitmap? FieldBitmap { get; private set; }

	/// <summary>The intensity picture of the current store, or null (no store, or no intensity plane).</summary>
	public WriteableBitmap? IntensityBitmap { get; private set; }

	/// <summary>Sets how a layer is coloured and whether it is rendered at all; the shown frame is redrawn.</summary>
	/// <param name="layer">The plane.</param>
	/// <param name="lut">Palette, or null for greyscale.</param>
	/// <param name="brightness">Gain before the palette lookup.</param>
	/// <param name="enabled">False leaves the bitmap untouched (the picture is hidden anyway).</param>
	public void SetLayer(FrameLayer layer, int[]? lut, float brightness, bool enabled)
	{
		_settings[(int)layer] = new LayerSettings { Lut = lut, Brightness = brightness, Enabled = enabled };
		Rebuild();
	}

	/// <summary>
	/// Whether a frame is being rendered right now (2026-10-16): the pane advances the shown position only when this is
	/// false, so the overlays and the timeline are redrawn once per picture that can be shown, not sixty times a
	/// second for pictures that never appear; on a machine that renders at the display rate nothing changes.
	/// </summary>
	public bool IsBusy => Volatile.Read(ref _busy) == 1;

	/// <summary>Requests frame <paramref name="frame"/> (clamped to the frames available); coalesced with pending requests.</summary>
	public void Show(int frame)
	{
		Volatile.Write(ref _wanted, frame);
		Kick();
	}

	/// <summary>
	/// A picture of the source's size, or null when Windows has no memory for it (the bitmap is a shared section, which
	/// fails at the commit limit as MILERR_WIN32ERROR; it took the application down on 2026-10-19 after a run had
	/// filled the memory): logged as an error, the picture then stays empty until a source with a new shape comes.
	/// </summary>
	private static WriteableBitmap? NewBitmap(IFrameSource source)
	{
		try
		{
			return new WriteableBitmap(source.Width, source.Height, 96, 96, PixelFormats.Bgr32, null);
		}
		catch (Exception ex) when (ex is COMException or OutOfMemoryException)
		{
			Logger.Error(ex, "No memory for a preview picture of {Width}×{Height}: the picture stays empty", source.Width, source.Height);
			return null;
		}
	}

	private void Rebuild()
	{
		_job = _source == null ? null : new Job(_source, FieldBitmap, IntensityBitmap, [.. _settings]);
		Kick();
	}

	/// <summary>Marks a change and starts the render loop unless one is running (it re-checks the version before it ends).</summary>
	private void Kick()
	{
		Interlocked.Increment(ref _version);
		if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
		{
			Task.Run(RenderLoop);
		}
	}

	private void RenderLoop()
	{
		try
		{
			while (true)
			{
				int version = Volatile.Read(ref _version);
				Job? job = Volatile.Read(ref _job);
				int frame = Volatile.Read(ref _wanted);
				if (job != null)
				{
					RenderOne(job, frame);
				}
				Volatile.Write(ref _busy, 0);
				if (Volatile.Read(ref _version) == version || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
				{
					return;
				}
			}
		}
		catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
		{
			// the dispatcher is shutting down; nothing left to show
			Volatile.Write(ref _busy, 0);
		}
		catch (Exception ex)
		{
			Volatile.Write(ref _busy, 0);
			Logger.Error(ex, "Preview rendering failed");
		}
	}

	private void RenderOne(Job job, int frame)
	{
		int count = job.Source.Count;
		if (count == 0)
		{
			return;
		}
		frame = Math.Clamp(frame, 0, count - 1);
		if (frame == _renderedFrame && ReferenceEquals(job, _renderedJob))
		{
			return;
		}
		int width = job.Source.Width;
		int height = job.Source.Height;
		Logger.Trace("Presenter render: frame {Frame} of {Source} (job {Job})", frame, job.Source.GetType().Name, job.GetHashCode());
		_renderClock.Start();
		var locked = new List<(WriteableBitmap Bitmap, Int32Rect Rect)>(2);
		try
		{
			foreach (FrameLayer layer in new[] { FrameLayer.Field, FrameLayer.Intensity })
			{
				LayerSettings settings = job.Settings[(int)layer];
				WriteableBitmap? bitmap = job.Bitmaps[(int)layer];
				if (!settings.Enabled || bitmap == null || !job.Source.Has(layer))
				{
					continue;
				}
				_copyClock.Start();
				bool copied = job.Indices != null ? job.Source.CopyLayer(frame, layer, job.Indices.AsSpan()) : job.Source.CopyLayer(frame, layer, job.Halves.AsSpan());
				_copyClock.Stop();
				if (!copied)
				{
					continue;
				}
				_colourClock.Start();
				if (job.Indices != null)
				{
					// palette indices: the brightness was applied when the frame was captured, only the palette is looked up
					FrameRenderer.RenderIndexed(job.Indices, width, height, settings.Lut, job.Pixels);
				}
				else if (layer == FrameLayer.Field)
				{
					FrameRenderer.RenderField(job.Halves, width, height, settings.Lut, settings.Brightness, job.Pixels);
				}
				else
				{
					FrameRenderer.RenderIntensity(job.Halves, width, height, settings.Lut, settings.Brightness, job.Pixels);
				}
				_colourClock.Stop();
				// the copy into the bitmap runs here, on the pool thread, between a lock and an unlock on the UI thread
				// (2026-10-16): WritePixels did the 8 to 12 MB copy on the UI thread, over 100 ms of every second at eleven
				// frames. The back buffer's pointer and stride are read inside the lock call, on the UI thread: they are
				// dependency properties with thread affinity (read from here they threw on every frame, 2026-10-16). Both
				// layers are unlocked in one call below, one render pass per presented frame instead of two. Render
				// priority, on purpose: at background priority the calls waited behind every mouse move of a scrub
				_writeClock.Start();
				(IntPtr buffer, int stride) = _dispatcher.Invoke(() =>
				{
					bitmap.Lock();
					return (bitmap.BackBuffer, bitmap.BackBufferStride);
				}, DispatcherPriority.Render);
				locked.Add((bitmap, new Int32Rect(0, 0, width, height)));
				if (stride == width * 4)
				{
					Marshal.Copy(job.Pixels, 0, buffer, width * height);
				}
				else
				{
					for (int y = 0; y < height; y++)
					{
						Marshal.Copy(job.Pixels, y * width, buffer + y * stride, width);
					}
				}
				_writeClock.Stop();
			}
		}
		finally
		{
			if (locked.Count > 0)
			{
				_dispatcher.Invoke(() =>
				{
					foreach ((WriteableBitmap bitmap, Int32Rect rect) in locked)
					{
						bitmap.AddDirtyRect(rect);
						bitmap.Unlock();
					}
				}, DispatcherPriority.Render);
			}
		}
		_renderClock.Stop();
		_renderedFrame = frame;
		_renderedJob = job;
		if (++_renderCount % RenderLogInterval == 0)
		{
			// the cost of one presented frame (copy, colouring, bitmap write), averaged; for judging the pipeline at full resolution
			Logger.Debug("Preview: {Width}x{Height}, {Average:0.0} ms per frame over the last {Count} (copy out of the store {Copy:0.0}, colouring {Colour:0.0}, copy into the bitmap {Write:0.0}, all on the pool)",
				width, height, _renderClock.Elapsed.TotalMilliseconds / RenderLogInterval, RenderLogInterval,
				_copyClock.Elapsed.TotalMilliseconds / RenderLogInterval, _colourClock.Elapsed.TotalMilliseconds / RenderLogInterval, _writeClock.Elapsed.TotalMilliseconds / RenderLogInterval);
			_renderClock.Reset();
			_copyClock.Reset();
			_colourClock.Reset();
			_writeClock.Reset();
		}
	}
}
