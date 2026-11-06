// SPDX-License-Identifier: GPL-3.0-or-later
// Desktop port of WaveDude Next, Copyright (C) 2026 Alexander Graef.
// Based on Wave Dude 2D by Jeroen Vleggaar and WaveSimulator2D by 0x23.
// Distributed WITHOUT ANY WARRANTY; see ../LICENSE.md.
using WaveDudeNext.Model;
using WaveDudeNext.Rendering;
using WaveDudeNext.Runs;

namespace WaveDudeNext.Desktop;

internal static class DesktopSmokeTest
{
    public static async Task<int> Run()
    {
        try
        {
            var project = new Project(); project.Scene.Width = 64; project.Scene.Height = 48;
            project.Scene.DampingEdge = -8; project.Simulation.Steps = 24; project.Simulation.VisualizeEvery = 2;
            project.AddSource();
            using var xml = new MemoryStream(); project.Save(xml); xml.Position = 0;
            project = Project.Load(xml);
            var spec = RunSpec.Create(project, SceneImages.Build(project, null), preferGpu: false, trackIntensity: true);
            using var store = FrameStore.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wdframes"), spec.Shape, spec.Layers, spec.StoredFrameCount, spec.CaptureEvery, ramBudget: 64 * 1024 * 1024);
            using var run = new SimulationRun(spec, store, null); run.Start();
            await run.Completion.WaitAsync(TimeSpan.FromSeconds(60));
            if (run.State != RunState.Completed || store.Count < 2) throw new Exception($"Run failed: {run.State}: {run.Error}");
            var values = new Half[store.Width * store.Height];
            store.CopyLayer(store.Count - 1, FrameLayer.Field, values.AsSpan());
            if (!values.Any(v => float.IsFinite((float)v) && Math.Abs((float)v) > 0)) throw new Exception("The field is empty.");
            var pixels = new int[values.Length];
            FrameRenderer.Render(values, store.Width, store.Height, ColorLut.Grey, SampleKind.Field, 30, pixels);
            var png = SceneImages.EncodePng(store.Width, store.Height, pixels);
            var decoded = SceneImages.Decode(png);
            if (decoded.Width != 64 || decoded.Height != 48) throw new Exception("PNG round trip failed.");
            Console.WriteLine($"PASS macOS preview 2: CPU simulation ({store.Count} frames), project round trip, frame rendering, PNG encode/decode. {run.EngineDescription}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
}
