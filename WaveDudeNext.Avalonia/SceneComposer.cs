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
using SkiaSharp;
using WaveDudeNext.Model;
using WaveDudeNext.Parts;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>Decodes and composes the scene for a run. Call on the UI thread while the project is not being edited.</summary>
internal static class SceneComposer
{
	/// <summary>Builds the scene pixels, including embedded or linked images and enabled optical parts.</summary>
	/// <param name="project">The project whose scene and parts are composed.</param>
	/// <param name="projectFolder">The containing folder for a relative linked image, or null without one.</param>
	public static ScenePixels? Compose(Project project, string? projectFolder)
	{
		Scene scene = project.Scene;
		ScenePixels? image = null;
		if (scene.Mode == SceneMode.Image)
		{
			if (scene.ImageData is { } data)
			{
				using var stream = new MemoryStream(data, writable: false);
				image = Decode(stream);
			}
			else
			{
				string path = scene.ResolveImagePath(projectFolder);
				using FileStream stream = File.OpenRead(path);
				image = Decode(stream);
			}
		}
		return PartPainter.Paint(image, scene.Width, scene.Height, scene.RefractiveIndex, project.Parts);
	}

	/// <summary>Decodes the first image into straight-alpha BGRA, preserving the channels used by the simulation.</summary>
	/// <param name="stream">An encoded image stream, left open.</param>
	public static ScenePixels Decode(Stream stream)
	{
		using var nativeStream = new SKManagedStream(stream, disposeManagedStream: false);
		using SKCodec codec = SKCodec.Create(nativeStream) ?? throw new InvalidDataException("The scene image could not be decoded.");
		int width = codec.Info.Width;
		int height = codec.Info.Height;
		int stride = checked(width * 4);
		var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
		using var bitmap = new SKBitmap(info);
		SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
		if (result != SKCodecResult.Success)
		{
			throw new InvalidDataException("The scene image could not be decoded: " + result);
		}
		var pixels = new byte[checked(stride * height)];
		for (int y = 0; y < height; y++)
		{
			Marshal.Copy(IntPtr.Add(bitmap.GetPixels(), y * bitmap.RowBytes), pixels, y * stride, stride);
		}
		return new ScenePixels(width, height, stride, pixels);
	}
}
