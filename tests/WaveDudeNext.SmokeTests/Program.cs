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


using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using WaveDudeNext.Engine;
using WaveDudeNext.Export;
using WaveDudeNext.Model;
using WaveDudeNext.Parts;
using WaveDudeNext.Runs;

internal static class Program
{
	private static readonly string Temp = Path.Combine(Path.GetTempPath(), "WaveDude smoke ü " + Guid.NewGuid().ToString("N"));

	private static async Task<int> Main()
	{
		Directory.CreateDirectory(Temp);
		// A broken worker must not hang CI forever. Individual async checks have tighter deadlines.
		using var watchdog = new Timer(_ => { Console.Error.WriteLine("FAIL: 90-second suite deadline exceeded"); Environment.Exit(2); }, null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan);
		try
		{
			Check("CPU field/intensity, reset and deterministic seed", TestEngine);
			Check("GPU-preferred factory and portable CPU fallback", TestPreferredGpu);
			Check("native examples load/save/load", TestProjects);
			Check("part painting and image scene channels", TestPainting);
			Check("portable paths and PNG pixel roundtrip", TestPngAndPaths);
			TestCacheSweep();
			await TestRunLifecycle();
			await TestCancelledProbeTail();
			Console.WriteLine("PASS: run pause/resume/completion/cancel/disposal, RAM and disk frame retrieval");
			Console.WriteLine("All headless smoke tests passed on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
			return 0;
		}
		catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
		finally { Directory.Delete(Temp, recursive: true); }
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void Check(string name, Action test) { test(); Console.WriteLine("PASS: " + name); }

	private static Project SmallProject()
	{
		var p = new Project { Seed = 123456 };
		p.Scene.Mode = SceneMode.Canvas;
		p.Scene.Width = 48; p.Scene.Height = 32; p.Scene.DampingEdge = -4;
		p.Simulation.Steps = 80; p.Simulation.VisualizeEvery = 4;
		var s = p.AddSource(24, 16);
		s.Amplitude = 1; s.Frequency = 50;
		s.Array = new SourceArray { SizeX = 3, SizeY = 2, SpacingX = 3, SpacingY = 3, RandomPhase = 90, RandomPosition = 1 };
		p.GenerateElements();
		return p;
	}

	private static void TestEngine()
	{
		var p = SmallProject();
		var emitters = Emitter.FromProject(p);
		p.GenerateElements();
		Require(emitters.SequenceEqual(Emitter.FromProject(p)), "Same seed did not reproduce emitters");
		p.Seed++;
		p.GenerateElements();
		Require(!emitters.SequenceEqual(Emitter.FromProject(p)), "Changing randomized seed did not change emitters");
		foreach (bool fourthOrder in new[] { false, true })
		{
			p.Simulation.FourthOrderLaplacian = fourthOrder;
			using var engine = new WaveSimulator(SceneData.Empty(48, 32, -4), EngineOptions.From(p.Simulation, p.Scene, true), emitters);
			Half[] Run()
			{
				for (int i = 0; i < 80; i++) engine.AdvanceOneStep();
				engine.PrepareFrame(true, true);
				Require(engine.FieldData.All(float.IsFinite) && engine.FieldData.Any(v => Math.Abs(v) > 1e-8), "CPU field is zero or non-finite");
				Require(engine.IntensityData is { } intensity && intensity.All(v => float.IsFinite(v) && v >= 0) && intensity.Any(v => v > 0), "CPU intensity is absent, zero, negative or non-finite");
				var snapshot = engine.CreateSnapshot(FrameShape.Full(48, 32), true);
				engine.CaptureFrame(snapshot, true); engine.AwaitFrame(snapshot);
				Require(snapshot.HasIntensity && snapshot.Field.Length == 48 * 32, "Snapshot geometry/intensity mismatch");
				return snapshot.Field.ToArray();
			}
			var first = Run(); engine.Reset(); var second = Run();
			Require(first.SequenceEqual(second), "CPU reset is not deterministic");
		}
	}

	private static void TestPreferredGpu()
	{
		var p = SmallProject();
		IWaveEngine engine = WaveEngineFactory.Create(SceneData.Empty(48, 32, -4),
			EngineOptions.From(p.Simulation, p.Scene, true), preferGpu: true, Emitter.FromProject(p), out string note);
		try
		{
			Console.WriteLine("  GPU-preferred selection: " + engine.Description);
			Console.WriteLine("  Factory note: " + (string.IsNullOrEmpty(note) ? "(none)" : note));
			Require(engine is GpuWaveSimulator or WaveSimulator, "Factory returned an unexpected engine");
			if (engine is WaveSimulator) Require(!string.IsNullOrWhiteSpace(note), "CPU fallback did not explain unavailable GPU");
			for (int i = 0; i < 80; i++) engine.AdvanceOneStep();
			var snapshot = engine.CreateSnapshot(FrameShape.Full(48, 32), true);
			engine.CaptureFrame(snapshot, true); engine.AwaitFrame(snapshot);
			var field = snapshot.Field.ToArray(); var intensity = snapshot.Intensity.ToArray();
			Require(field.Length == 48 * 32 && field.All(Half.IsFinite) && field.Any(v => v != (Half)0), "GPU-preferred engine field is zero/non-finite or has wrong size");
			Require(snapshot.HasIntensity && intensity.All(v => Half.IsFinite(v) && v >= (Half)0) && intensity.Any(v => v > (Half)0), "GPU-preferred engine intensity is absent, zero, negative or non-finite");
		}
		finally { (engine as IDisposable)?.Dispose(); }
	}

	private static void TestProjects()
	{
		var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Examples"), "*.wdproj");
		Require(files.Length > 0, "No upstream example fixtures copied");
		foreach (string file in files)
		{
			var p = Project.Load(file);
			string path = Path.Combine(Temp, Path.GetFileName(file));
			p.Save(path);
			var reloaded = Project.Load(path);
			using var a = new MemoryStream(); using var b = new MemoryStream();
			p.Save(a); reloaded.Save(b);
			Require(a.ToArray().SequenceEqual(b.ToArray()), "Project roundtrip changed serialized data: " + Path.GetFileName(file));
			Require(p.Seed == reloaded.Seed && p.Sources.Count == reloaded.Sources.Count && p.Parts.Count == reloaded.Parts.Count, "Project model mismatch");
		}
		Console.WriteLine("  Roundtripped " + files.Length + " real .wdproj examples");
	}

	private static void TestPainting()
	{
		var part = new RectanglePart { X = 24, Y = 16, Length = 12, Width = 10, RefractiveIndex = 1.5 };
		var painted = PartPainter.Paint(null, 48, 32, 1, new[] { part })!;
		int center = (16 * 48 + 24) * 4;
		Require(painted.Bgra[center + 2] == 150 && painted.Bgra[2] == 100, "Rectangle/background refractive index not painted");
		var original = painted.Bgra.ToArray();
		part.Mirror = true;
		var mirrored = PartPainter.Paint(painted, 48, 32, 1, new[] { part })!;
		Require(mirrored.Bgra[center + 2] == 0 && original.SequenceEqual(painted.Bgra), "Mirror paint did not copy input / set red zero");
		painted.Bgra[center] = 128; painted.Bgra[center + 1] = 70;
		var scene = SceneData.FromBgra(painted.Bgra, 48, 32, painted.StrideBytes, true, 0);
		Require(scene.SourceCount == 1 && scene.SourceX[0] == 24 && scene.SourceY[0] == 16 && scene.SourceLevel[0] == 70, "Image green source decode failed");
		Require(Math.Abs(scene.C2[16 * 48 + 24] - 1f / 2.25f) < 1e-6 && Math.Abs(scene.D[16 * 48 + 24] - (1 - 128f / 255)) < 1e-6, "Image red/blue material decode failed");
		Require(SceneData.FromBgra(painted.Bgra, 48, 32, painted.StrideBytes, false, 0).SourceCount == 0, "Image source disable ignored");
		var p = SmallProject(); p.Scene.Mode = SceneMode.Image; p.Scene.ImageSources = true;
		Require(RunSpec.Create(p, painted, preferGpu: false).Image == painted, "Image input missing from run spec");
	}

	private static void TestPngAndPaths()
	{
		string folder = Path.Combine(Temp, "scene assets ü"); Directory.CreateDirectory(folder);
		string png = Path.Combine(folder, "test frame.png");
		var pixels = new[] { unchecked((int)0xffff0000), unchecked((int)0xff00ff00), unchecked((int)0xff0000ff), unchecked((int)0xff123456) };
		using (var output = File.Create(png)) PngEncoder.Write(output, pixels, 2, 2, PngFilter.None);
		byte[] bytes = File.ReadAllBytes(png);
		Require(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG signature invalid");
		Require(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)) == 2 && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)) == 2, "PNG size invalid");
		using var compressed = new MemoryStream();
		for (int offset = 8; offset < bytes.Length;)
		{
			int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
			Require(length >= 0 && offset + 12L + length <= bytes.Length, "PNG chunk extends beyond file");
			if (System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4) == "IDAT") compressed.Write(bytes, offset + 8, length);
			offset += length + 12;
		}
		compressed.Position = 0;
		using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
		using var decoded = new MemoryStream(); zlib.CopyTo(decoded);
		Require(decoded.ToArray().SequenceEqual(new byte[] { 0, 255, 0, 0, 0, 255, 0, 0, 0, 0, 255, 18, 52, 86 }), "PNG RGB pixels changed");
		var scene = new Scene { ImagePath = Scene.StoredImagePath(Temp, png) };
		Require(!Path.IsPathRooted(scene.ImagePath) && scene.ResolveImagePath(Temp) == png, "Relative Unicode/space image path did not resolve");
		Require(Path.IsPathRooted(FrameCache.Directory), "Cache path is not absolute");
		if (!OperatingSystem.IsWindows()) Require(!FrameCache.Directory.Contains('\\'), "Cache path contains Windows separators");
	}

	private static void TestCacheSweep()
	{
		// Sweep enumerates the app cache, so never run this regression over somebody else's files.
		if (Directory.Exists(FrameCache.Directory) && Directory.EnumerateFileSystemEntries(FrameCache.Directory).Any())
		{
			Console.WriteLine("SKIP: cache sweep regression requires an empty app cache (use an isolated HOME in CI)");
			return;
		}
		string path = FrameCache.NewPath();
		try
		{
			using (var store = FrameStore.Create(path, 8, 8, FrameLayers.Field, 1, 1, ramBudget: 0))
			{
				var values = Enumerable.Repeat((Half)0.25, 64).ToArray();
				store.Write(0, values, ReadOnlySpan<Half>.Empty);
				Require(File.Exists(path), "Disk-backed store file missing before sweep");
				FrameCache.Sweep();
				Require(File.Exists(path), "Sweep deleted an active cache file");
				var read = new Half[64];
				Require(store.CopyLayer(0, FrameLayer.Field, read) && values.SequenceEqual(read), "Active cache unreadable after sweep");
			}
			Require(!File.Exists(path), "Dispose did not remove temporary cache file");
			File.WriteAllBytes(path, new byte[64]);
			FrameCache.Sweep();
			Require(!File.Exists(path), "Sweep did not remove orphan cache file");
			Console.WriteLine("PASS: active disk cache survives sweep; disposed/orphan cache is removed");
		}
		finally { if (File.Exists(path)) File.Delete(path); }
	}

	private static async Task Until(Func<bool> predicate, string description)
	{
		var watch = Stopwatch.StartNew();
		while (!predicate())
		{
			if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException(description);
			await Task.Delay(10);
		}
	}

	private sealed class QueuedContext : SynchronizationContext
	{
		private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();
		public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue(() => callback(state));
		public void Drain() { while (_queue.TryDequeue(out var callback)) callback(); }
	}

	private static async Task TestCancelledProbeTail()
	{
		// Cancel on observable stored data, rather than a sleep or a guessed simulation duration.
		// The worker must drain every sampled step/profile even when its last heartbeat has not run.
		foreach (bool queued in new[] { false, true })
		foreach (int every in new[] { 1, 4 })
		foreach (long budget in new[] { 0L, 8L << 20 })
		{
			var project = SmallProject();
			project.Simulation.Steps = 20000;
			project.Simulation.VisualizeEvery = every;
			var point = project.AddProbe(24, 16);
			var line = project.AddProbe(8, 16);
			line.Kind = ProbeKind.Line; line.BX = 40; line.BY = 16;
			var spec = RunSpec.Create(project, null, preferGpu: false);
			using var store = FrameStore.Create(Path.Combine(Temp, $"cancel-probes-{every}-{budget}.wdframes"), spec.Shape, spec.Layers, spec.StoredFrameCount, spec.CaptureEvery, ramBudget: budget);
			var context = queued ? new QueuedContext() : null;
			using var run = new SimulationRun(spec, store, context);
			int finished = 0;
			bool finalProbesComplete = false;
			run.Finished += (_, _) =>
			{
				finalProbesComplete = run.Traces[0].Series.Count == run.StepsDone && run.Traces[1].Profiles.Count == store.Count;
				finished++;
			};
			run.Start();
			Require(SpinWait.SpinUntil(() => store.Count > 0 || run.IsFinished, TimeSpan.FromSeconds(15)), "Probe cancellation run stored no frames");
			run.Stop();
			await run.Completion.WaitAsync(TimeSpan.FromSeconds(15));
			context?.Drain();
			Require(finalProbesComplete, "Finished observed incomplete probe tails: point rows=" + run.Traces[0].Series.Count + "/" + run.StepsDone + ", line rows=" + run.Traces[1].Profiles.Count + "/" + store.Count);
			Require(run.State == RunState.Cancelled && run.Error == null && finished == 1, "Probe cancellation did not finish cleanly");
			Require(run.StepsDone > 0 && run.StepsDone < spec.Steps, "Probe cancellation did not interrupt active stepping");
			Require(store.Count == (run.StepsDone + every - 1) / every, "Cancellation split a completed step from its frame");
			using var pointCsv = new StringWriter(); using var lineCsv = new StringWriter();
			Require(ProbeCsv.Write(pointCsv, point, run.Traces[0], every, 1) == run.StepsDone, "Cancelled point CSV lost samples");
			Require(ProbeCsv.Write(lineCsv, line, run.Traces[1], every, 1) == store.Count, "Cancelled line CSV row/frame mismatch");
			Console.WriteLine($"  Cancelled probes: {run.StepsDone} point rows; {store.Count} line rows/frames; every={every}, RAM budget={budget}, queued UI={queued}");
		}
		Console.WriteLine("PASS: cancelled probe tails and CSV rows match completed steps and frames");
	}

	private static async Task TestRunLifecycle()
	{
		foreach (long budget in new[] { 0L, 8L << 20 })
		{
			var spec = RunSpec.Create(SmallProject(), null, preferGpu: false, trackIntensity: true);
			using var store = FrameStore.Create(Path.Combine(Temp, "frames " + budget + ".wdframes"), spec.Shape, spec.Layers, spec.StoredFrameCount, spec.CaptureEvery, ramBudget: budget);
			using var run = new SimulationRun(spec, store, context: null);
			run.Pause(); run.Start();
			await Until(() => run.State == RunState.Paused || run.IsFinished, "Run never paused");
			Require(run.State == RunState.Paused && run.IsPauseRequested, "Run failed before pause: " + run.Error);
			int steps = run.StepsDone; await Task.Delay(50);
			Require(run.StepsDone == steps, "Paused run advanced");
			run.Resume(); await run.Completion.WaitAsync(TimeSpan.FromSeconds(15));
			Require(run.State == RunState.Completed && run.Error == null && run.StepsDone == spec.Steps, "Run failed: " + run.Error);
			Require(store.Count == spec.StoredFrameCount, "Wrong stored frame count");
			var field = new Half[spec.Width * spec.Height]; var intensity = new Half[field.Length];
			Require(run.Frames.CopyLayer(run.Frames.Count - 1, FrameLayer.Field, field) && run.Frames.CopyLayer(run.Frames.Count - 1, FrameLayer.Intensity, intensity), "Frame retrieval failed");
			Require(field.All(Half.IsFinite) && field.Any(v => v != (Half)0) && intensity.All(Half.IsFinite), "Stored frame invalid");
			Require(budget == 0 ? store.FramesOnDisk > 0 : store.FramesInMemory > 0, "Requested storage tier unused");
		}
		var cancelSpec = RunSpec.Create(SmallProject(), null, preferGpu: false);
		using var cancelStore = FrameStore.Create(Path.Combine(Temp, "cancel.wdframes"), cancelSpec.Shape, cancelSpec.Layers, cancelSpec.StoredFrameCount, cancelSpec.CaptureEvery, ramBudget: 8L << 20);
		var cancelRun = new SimulationRun(cancelSpec, cancelStore, context: null);
		try
		{
			cancelRun.Pause(); cancelRun.Start();
			await Until(() => cancelRun.State == RunState.Paused || cancelRun.IsFinished, "Cancel test never paused");
			Require(cancelRun.State == RunState.Paused, "Cancel test failed before pause: " + cancelRun.Error);
			cancelRun.Stop(); await cancelRun.Completion.WaitAsync(TimeSpan.FromSeconds(15));
			Require(cancelRun.State == RunState.Cancelled && cancelRun.Error == null, "Paused cancellation did not finish cleanly");
		}
		finally { cancelRun.Dispose(); }
		Require(cancelStore.Count == 0, "Pre-step cancellation wrote frames");
	}
}
