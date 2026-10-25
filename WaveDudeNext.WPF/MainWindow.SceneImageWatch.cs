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

using System.IO;
using System.Windows;
using System.Windows.Threading;
using WaveDudeNext.Model;
using WaveDudeNext.Runs;
using WaveDudeNext.WPF.Rendering;

namespace WaveDudeNext.WPF;

/// <summary>
/// Keeps the embedded scene image in step with the file it came from (the user's rule, 2026-10-15): the file
/// named by <see cref="Scene.ImagePath"/> is watched while the project is open and re-embedded when it changes
/// on disk (debounced, read again while the editor still holds it), and when a project is opened the file is
/// compared with the embedded bytes once and taken instead when it differs. Either way the change goes
/// through the model, so it marks the project dirty, makes the run stale and asks the scratch runner for the
/// frame, like a scene edit. The embedded bytes are the baseline, so "changed" is a byte comparison; no hash. A file
/// that is gone changes nothing but is reported (<see cref="SceneImageFileMissing"/>, 2026-10-26, Jeroen's report: a
/// deleted file still showed its path, which is right, but nothing said so). UI thread; the watcher's events are
/// marshalled to it.
/// </summary>
public partial class MainWindow
{
	/// <summary>Identifies the <see cref="SceneImageFileMissing"/> property.</summary>
	public static readonly DependencyProperty SceneImageFileMissingProperty = DependencyProperty.Register(
		nameof(SceneImageFileMissing), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

	/// <summary>How long after the last file event the image is read: editors write in several steps.</summary>
	private static readonly TimeSpan SceneImageDebounce = TimeSpan.FromMilliseconds(400);

	private FileSystemWatcher? _sceneImageWatcher;
	private DispatcherTimer? _sceneImageTimer;

	/// <summary>Folders beside the project file where a project without a path may find its image by name: the folder itself and the author's layout.</summary>
	private static readonly string[] SceneImageFolders = ["", "scene_images"];

	/// <summary>
	/// Whether the scene's image file (<see cref="Scene.ImagePath"/>) is named but not on disk: the scene form says so beside
	/// the path. The embedded image is what is simulated, so nothing else follows. An unsaved project with a relative path
	/// cannot tell and says no. Kept up to date by the watch.
	/// </summary>
	public bool SceneImageFileMissing
	{
		get => (bool)GetValue(SceneImageFileMissingProperty);
		private set => SetValue(SceneImageFileMissingProperty, value);
	}

	/// <summary>Arms the watch on the current scene image file (after a project, path or file name change) and takes the file at once when it differs from the embedded image.</summary>
	private void SyncSceneImage(string reason)
	{
		LocateSceneImageFile();
		ArmSceneImageWatch();
		ReloadSceneImageIfChanged(reason);
	}

	/// <summary>
	/// A project saved before the path existed knows its image only by name: looks for that name beside the
	/// project file and in a <c>scene_images</c> folder next to it (the author's layout, where an imported
	/// project is usually saved) and adopts the first match as the path, so the file is watched and compared
	/// from now on. Nothing happens without an embedded image, a name, or a project folder.
	/// </summary>
	private void LocateSceneImageFile()
	{
		Scene scene = Project.Scene;
		if (scene.ImagePath.Length > 0 || scene.Image.Length == 0 || scene.ImageData == null || ProjectFolder is not { } folder)
		{
			return;
		}
		foreach (string sub in SceneImageFolders)
		{
			string candidate = Path.Combine(folder, sub, scene.Image);
			if (File.Exists(candidate))
			{
				scene.ImagePath = Scene.StoredImagePath(folder, candidate);
				Logger.Info("Scene image file found for '{Name}': '{Path}' (the project had no path; it is watched from now on)", scene.Image, scene.ImagePath);
				return;
			}
		}
	}

	/// <summary>Watches the scene image's source file; nothing when the project has no embedded image or the path does not resolve.</summary>
	private void ArmSceneImageWatch()
	{
		DisarmSceneImageWatch();
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(ProjectFolder);
		SceneImageFileMissing = IsSceneImageFileMissing();
		if (path.Length == 0 || scene.ImageData == null)
		{
			return;
		}
		string? folder = Path.GetDirectoryName(path);
		if (folder == null || !Directory.Exists(folder))
		{
			return;
		}
		try
		{
			_sceneImageWatcher = new FileSystemWatcher(folder, Path.GetFileName(path))
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
			};
			_sceneImageWatcher.Changed += OnSceneImageFileEvent;
			_sceneImageWatcher.Created += OnSceneImageFileEvent;
			_sceneImageWatcher.Renamed += OnSceneImageFileEvent;
			_sceneImageWatcher.Deleted += OnSceneImageFileEvent;
			_sceneImageWatcher.EnableRaisingEvents = true;
			Logger.Debug("Watching the scene image file '{Path}'", path);
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
		{
			Logger.Debug("The scene image file '{Path}' cannot be watched: {Message}", path, ex.Message);
			_sceneImageWatcher = null;
		}
	}

	private void DisarmSceneImageWatch()
	{
		_sceneImageTimer?.Stop();
		if (_sceneImageWatcher != null)
		{
			_sceneImageWatcher.EnableRaisingEvents = false;
			_sceneImageWatcher.Dispose();
			_sceneImageWatcher = null;
		}
	}

	/// <summary>A file event (on the watcher's thread): restarts the debounce on the UI thread; the file is then read again, or found gone.</summary>
	private void OnSceneImageFileEvent(object sender, FileSystemEventArgs e) => Dispatcher.BeginInvoke(() =>
	{
		_sceneImageTimer ??= MakeTimer(SceneImageDebounce, () =>
		{
			_sceneImageTimer!.Stop();
			ReloadSceneImageIfChanged("changed on disk");
			SceneImageFileMissing = IsSceneImageFileMissing();
		});
		_sceneImageTimer.Stop();
		_sceneImageTimer.Start();
	});

	/// <summary>Whether the scene names an image file that is not on disk (see <see cref="SceneImageFileMissing"/>).</summary>
	private bool IsSceneImageFileMissing()
	{
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(ProjectFolder);
		return path.Length > 0 && scene.ImageData != null && !File.Exists(path);
	}

	/// <summary>
	/// Reads the scene image's source file and embeds it when its bytes differ from the embedded ones; a file
	/// that is not an image is reported and left alone. True when the image was replaced.
	/// </summary>
	private bool ReloadSceneImageIfChanged(string reason)
	{
		Scene scene = Project.Scene;
		string path = scene.ResolveImagePath(ProjectFolder);
		if (path.Length == 0 || scene.ImageData == null || !File.Exists(path))
		{
			return false;
		}
		byte[]? bytes = TryReadAll(path);
		if (bytes == null || bytes.AsSpan().SequenceEqual(scene.ImageData))
		{
			return false;
		}
		try
		{
			ScenePixels pixels = SceneImageLoader.Load(bytes);
			scene.ImageData = bytes;
			scene.Image = Path.GetFileName(path);
			Logger.Info("Scene image '{Name}' re-embedded from '{Path}' ({Reason}): {Width}×{Height}, {Size} KB", scene.Image, path, reason, pixels.Width, pixels.Height, Math.Max(1, bytes.Length / 1024));
			return true;
		}
		catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
		{
			Logger.Warn(ex, "The scene image file '{Path}' changed but cannot be read as an image: {Message}", path, ex.Message);
			return false;
		}
	}

	/// <summary>Reads the whole file, trying again for a moment while the program writing it still holds it.</summary>
	private static byte[]? TryReadAll(string path)
	{
		for (int attempt = 0; attempt < 8; attempt++)
		{
			try
			{
				return File.ReadAllBytes(path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				Thread.Sleep(100);
			}
		}
		return null;
	}
}
