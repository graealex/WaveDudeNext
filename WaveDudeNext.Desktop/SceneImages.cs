// SPDX-License-Identifier: GPL-3.0-or-later
// Desktop port of WaveDude Next, Copyright (C) 2026 Alexander Graef.
// Based on Wave Dude 2D by Jeroen Vleggaar and WaveSimulator2D by 0x23.
// Distributed WITHOUT ANY WARRANTY; see ../LICENSE.md.
using System.Runtime.InteropServices;
using SkiaSharp;
using WaveDudeNext.Model;
using WaveDudeNext.Parts;
using WaveDudeNext.Runs;

namespace WaveDudeNext.Desktop;

public static class SceneImages
{
    public static ScenePixels Decode(byte[] bytes)
    {
        using var input = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("This image could not be decoded.");
        using var bgra = input.Copy(SKColorType.Bgra8888) ?? throw new InvalidDataException("Cannot convert the scene image to BGRA.");
        var data = new byte[bgra.RowBytes * bgra.Height];
        Marshal.Copy(bgra.GetPixels(), data, 0, data.Length);
        return new ScenePixels(bgra.Width, bgra.Height, bgra.RowBytes, data);
    }

    public static ScenePixels? Build(Project project, string? projectPath)
    {
        ScenePixels? image = null;
        if (project.Scene.Mode == SceneMode.Image)
        {
            var bytes = project.Scene.ImageData;
            if (bytes == null)
            {
                var path = project.Scene.ResolveImagePath(Path.GetDirectoryName(projectPath));
                if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(project.Scene.Image))
                    path = Path.IsPathRooted(project.Scene.Image) ? project.Scene.Image : Path.Combine(Path.GetDirectoryName(projectPath) ?? "", project.Scene.Image);
                if (!File.Exists(path)) throw new FileNotFoundException("The scene image is missing. Use Import image to embed it.", path);
                bytes = File.ReadAllBytes(path);
            }
            image = Decode(bytes);
        }
        return PartPainter.Paint(image, project.Scene.Width, project.Scene.Height, project.Scene.RefractiveIndex, project.Parts.ToArray());
    }

    public static byte[] EncodePng(int width, int height, int[] pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
