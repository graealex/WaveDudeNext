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
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>Where the canvas comes from.</summary>
public enum SceneMode
{
	/// <summary>A blank canvas of <see cref="Scene.Width"/> × <see cref="Scene.Height"/> pixels.</summary>
	Canvas,
	/// <summary>The canvas is built from <see cref="Scene.Image"/>.</summary>
	Image,
}

/// <summary>What the outermost cells of the grid do with a wave that reaches them (2026-10-19, the user's request).</summary>
public enum EdgeMode
{
	/// <summary>The wave walks off the grid (Mur's first-order absorbing condition on the ring outside the canvas): a pulse returns well under a tenth of a percent, as good as a void.</summary>
	Open,
	/// <summary>A hard wall: the wave comes back in full, as in Wave Dude 2D; a damping edge in front of it softens that.</summary>
	Reflective,
}

/// <summary>
/// The simulation canvas: its origin (blank or image), the damping border and the options for
/// sources painted into the image. Positions throughout the project are canvas pixels with the
/// origin at the top left.
/// </summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class Scene : ObservableObject
{
	/// <summary>Default <see cref="Width"/> in pixels.</summary>
	public const int DefaultWidth = 1920;
	/// <summary>Default <see cref="Height"/> in pixels.</summary>
	public const int DefaultHeight = 1080;
	/// <summary>Smallest accepted canvas width or height in pixels.</summary>
	public const int MinSize = 8;
	/// <summary>Largest accepted canvas width or height in pixels.</summary>
	public const int MaxSize = 16384;
	/// <summary>Damping frame thickness the editor proposes when the frame is switched on.</summary>
	public const int DefaultDampingEdge = -32;
	/// <summary>Largest accepted <see cref="DampingEdge"/> in pixels.</summary>
	public const int MaxDampingEdge = 8192;
	/// <summary>Default <see cref="ImageSourceScale"/>.</summary>
	public const double DefaultImageSourceScale = 1.0;
	/// <summary>Largest accepted <see cref="ImageSourceScale"/>.</summary>
	public const double MaxImageSourceScale = 1000.0;
	/// <summary>Default <see cref="RefractiveIndex"/>.</summary>
	public const double DefaultRefractiveIndex = 1.0;
	/// <summary>Smallest accepted <see cref="RefractiveIndex"/>.</summary>
	public const double MinRefractiveIndex = 1.0;
	/// <summary>Largest accepted <see cref="RefractiveIndex"/>.</summary>
	public const double MaxRefractiveIndex = 2.55;

	private SceneMode _mode = SceneMode.Canvas;
	private string _image = "";
	private string _imagePath = "";
	private int _width = DefaultWidth;
	private int _height = DefaultHeight;
	private int _dampingEdge = DefaultDampingEdge;
	private bool _dampingEdgeRead;
	private EdgeMode _edges = EdgeMode.Open;
	private double _refractiveIndex = DefaultRefractiveIndex;
	private bool _imageSources;
	private double _imageSourceScale = DefaultImageSourceScale;
	private bool _objectSources = true;
	private byte[]? _imageData;

	/// <summary>Blank canvas of <see cref="Width"/> × <see cref="Height"/>, or a canvas built from <see cref="Image"/>.</summary>
	[XmlAttribute]
	[DefaultValue(SceneMode.Canvas)]
	[Category("Canvas")]
	[Description("Blank canvas of the given size, or a canvas built from the scene image.")]
	public SceneMode Mode
	{
		get => _mode;
		set => SetProperty(ref _mode, value);
	}

	/// <summary>
	/// Scene image path, relative to the project folder when inside it; "" when none. Red is the
	/// refractive index, green marks sources, blue marks damping.
	/// </summary>
	[XmlAttribute]
	[DefaultValue("")]
	[Category("Canvas")]
	[DisplayName("Scene image")]
	[Description("Image the canvas is built from: red = refractive index (red 0 = a mirror), green = sources, blue = damping. The name of the embedded image, or a path relative to the project folder.")]
	public string Image
	{
		get => _image;
		set => SetProperty(ref _image, value);
	}

	/// <summary>
	/// The scene image file embedded in the project (2026-10-13): its bytes as they were on disk (PNG, BMP,
	/// JPEG…), base64 in the XML; null when the scene has no image or refers to one by path through
	/// <see cref="Image"/>. The application decodes it. Replacing it is a simulation-affecting change.
	/// </summary>
	[XmlElement("ImageData", DataType = "base64Binary")]
	[Browsable(false)]
	public byte[]? ImageData
	{
		get => _imageData;
		set => SetProperty(ref _imageData, value);
	}

	/// <summary>Only an embedded image is written.</summary>
	public bool ShouldSerializeImageData() => _imageData != null;

	/// <summary>
	/// Where the embedded image came from (2026-10-15): the file's path, relative to the project file when it lies
	/// in that folder (<see cref="StoredImagePath"/>), absolute otherwise, "" when unknown. The application
	/// watches it while the project is open and re-embeds the file when it changes, and compares it with the
	/// embedded bytes when the project is opened. Changing the path changes nothing in the simulation.
	/// </summary>
	[XmlAttribute]
	[DefaultValue("")]
	[Category("Canvas")]
	[DisplayName("Image file")]
	[Description("Where the embedded image came from; the embedded image is what is simulated, a missing file changes nothing. The file is watched while the project is open and re-embedded when it changes; on opening the project a changed file replaces the embedded image too.")]
	public string ImagePath
	{
		get => _imagePath;
		set => SetProperty(ref _imagePath, value ?? "");
	}

	/// <summary>The form of <paramref name="fullPath"/> stored in a project: relative to <paramref name="projectFolder"/> when it lies inside it (case-insensitive), else as given.</summary>
	/// <param name="projectFolder">The project file's folder; null or empty for an unsaved project.</param>
	/// <param name="fullPath">The image file's absolute path.</param>
	public static string StoredImagePath(string? projectFolder, string fullPath)
	{
		if (string.IsNullOrEmpty(projectFolder) || !Path.IsPathRooted(fullPath))
		{
			return fullPath;
		}
		string relative = Path.GetRelativePath(projectFolder, fullPath);
		return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? fullPath : relative;
	}

	/// <summary>The image file's full path: <see cref="ImagePath"/> as given when rooted, resolved against <paramref name="projectFolder"/> when relative; "" when there is no path or no folder to resolve against.</summary>
	public string ResolveImagePath(string? projectFolder)
	{
		if (ImagePath.Length == 0)
		{
			return "";
		}
		if (Path.IsPathRooted(ImagePath))
		{
			return ImagePath;
		}
		return string.IsNullOrEmpty(projectFolder) ? "" : Path.GetFullPath(ImagePath, projectFolder);
	}

	/// <summary>Turns an absolute <see cref="ImagePath"/> inside <paramref name="projectFolder"/> into a relative one (called when the project is saved).</summary>
	public void RelativizeImagePath(string? projectFolder)
	{
		if (Path.IsPathRooted(ImagePath))
		{
			ImagePath = StoredImagePath(projectFolder, ImagePath);
		}
	}

	/// <summary>
	/// Drops the scene image (2026-10-26, Jeroen's report: there was no way): the name, the file path and the embedded
	/// bytes go, and the scene is a blank canvas of <see cref="Width"/> × <see cref="Height"/> again (the application sets
	/// those to the image's size first, so the canvas keeps its size and the sources their places).
	/// </summary>
	public void RemoveImage()
	{
		Image = "";
		ImagePath = "";
		ImageData = null;
		Mode = SceneMode.Canvas;
	}

	/// <summary>Width of a blank canvas in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultWidth)]
	[Range(MinSize, MaxSize)]
	[Category("Canvas")]
	[Description("Width of a blank canvas in pixels.")]
	public int Width
	{
		get => _width;
		set => SetProperty(ref _width, value);
	}

	/// <summary>Height of a blank canvas in pixels.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultHeight)]
	[Range(MinSize, MaxSize)]
	[Category("Canvas")]
	[Description("Height of a blank canvas in pixels.")]
	public int Height
	{
		get => _height;
		set => SetProperty(ref _height, value);
	}

	/// <summary>
	/// Thickness of the absorbing frame in pixels: positive inside the canvas, where it shows as a dark margin (Jeroen's
	/// band), negative outside the canvas, where the engine simulates that much more and shows none of it (2026-10-19,
	/// the user's rule for the sign); 0 turns it off. A new project has a rim of 32 outside (2026-10-22, the user's choice:
	/// at wavelengths of a few cells the open ring alone reflects, see <see cref="Engine.SceneData.ApplyRim"/>).
	/// </summary>
	[XmlAttribute]
	[DefaultValue(DefaultDampingEdge)]
	[Range(-MaxDampingEdge, MaxDampingEdge)]
	[Category("Canvas")]
	[DisplayName("Damping edge")]
	[Description("Thickness in pixels of the absorbing frame: positive inside the canvas, where it is visible, negative outside it, where it is not. 0 turns it off.")]
	public int DampingEdge
	{
		get => _dampingEdge;
		set
		{
			_dampingEdgeRead = true;
			if (SetProperty(ref _dampingEdge, value))
			{
				OnPropertyChanged(nameof(HasRim));
			}
		}
	}

	/// <summary>Whether a damping rim lies outside the canvas (a negative <see cref="DampingEdge"/>, <see cref="Runs.RunSpec.Rim"/> cells), which the preview frames in blue (2026-10-25).</summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool HasRim => _dampingEdge < 0;

	/// <summary>
	/// Whether <see cref="DampingEdge"/> has been set since construction: right after a file is read, whether the file
	/// carried the attribute. A version-2 file left it out for 0, its default then, which <see cref="Project.Load(System.IO.Stream)"/>
	/// restores (2026-10-22, when the default became -32).
	/// </summary>
	[XmlIgnore]
	[Browsable(false)]
	internal bool DampingEdgeRead => _dampingEdgeRead;

	/// <summary>What the edge of the grid does with a wave: lets it leave, or reflects it.</summary>
	[XmlAttribute]
	[DefaultValue(EdgeMode.Open)]
	[Category("Canvas")]
	[DisplayName("Edges")]
	[Description("Open lets a wave leave the grid; Reflective is a hard wall, as in Wave Dude 2D.")]
	public EdgeMode Edges
	{
		get => _edges;
		set => SetProperty(ref _edges, value);
	}

	/// <summary>Refractive index of a blank canvas, 1.0 to 2.55; the wave speed is 1/n cells per step.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultRefractiveIndex)]
	[Range(MinRefractiveIndex, MaxRefractiveIndex)]
	[Category("Canvas")]
	[DisplayName("Refractive index")]
	[Description("Refractive index of a blank canvas, 1.0 to 2.55. The wave speed is 1/n cells per step.")]
	public double RefractiveIndex
	{
		get => _refractiveIndex;
		set => SetProperty(ref _refractiveIndex, value);
	}

	/// <summary>Whether the green channel of the scene image emits waves, the pixel value being the frequency level.</summary>
	[XmlAttribute]
	[DefaultValue(false)]
	[Category("Sources")]
	[DisplayName("Image sources")]
	[Description("Let the green channel of the scene image emit waves; the pixel value is the frequency level.")]
	public bool ImageSources
	{
		get => _imageSources;
		set => SetProperty(ref _imageSources, value);
	}

	/// <summary>Peak amplitude of the image sources.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultImageSourceScale)]
	[Range(0.0, MaxImageSourceScale)]
	[Category("Sources")]
	[DisplayName("Image source amplitude")]
	[Description("Peak amplitude of the image sources.")]
	public double ImageSourceScale
	{
		get => _imageSourceScale;
		set => SetProperty(ref _imageSourceScale, value);
	}

	/// <summary>Whether the project's <see cref="Source"/>s take part in the run.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Sources")]
	[DisplayName("Object sources")]
	[Description("Use the sources defined in the project.")]
	public bool ObjectSources
	{
		get => _objectSources;
		set => SetProperty(ref _objectSources, value);
	}

	/// <summary>True when every value is at its default, so the element can be left out of the file.</summary>
	[XmlIgnore]
	[Browsable(false)]
	public bool IsDefault =>
		Mode == SceneMode.Canvas && Image.Length == 0 && ImagePath.Length == 0 && ImageData == null && Width == DefaultWidth && Height == DefaultHeight
		&& DampingEdge == DefaultDampingEdge && Edges == EdgeMode.Open && RefractiveIndex == DefaultRefractiveIndex && !ImageSources
		&& ImageSourceScale == DefaultImageSourceScale && ObjectSources;

	/// <summary>Clamps every value into its valid range.</summary>
	public void Normalize()
	{
		Image ??= "";
		ImagePath ??= "";
		Width = Math.Clamp(Width, MinSize, MaxSize);
		Height = Math.Clamp(Height, MinSize, MaxSize);
		DampingEdge = Math.Clamp(DampingEdge, -MaxDampingEdge, MaxDampingEdge);
		RefractiveIndex = Math.Clamp(RefractiveIndex, MinRefractiveIndex, MaxRefractiveIndex);
		ImageSourceScale = Math.Clamp(ImageSourceScale, 0, MaxImageSourceScale);
	}

	/// <summary>The image path, or the canvas size, for display in a property grid.</summary>
	public override string ToString() => Mode == SceneMode.Image ? Image : $"{Width} × {Height}";
}
