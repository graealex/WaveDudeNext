# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Alexander Graef <alex@graef.cc>
#
# This file is part of WaveDude Next, a 2D wave simulator.
#
# Based on the work of
#   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
#     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
#   0x23 (Diffraction Limited), author of WaveSimulator2D
#     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
#
# WaveDude Next is free software: you can redistribute it and/or modify it under
# the terms of the GNU General Public License as published by the Free Software
# Foundation, either version 3 of the License, or (at your option) any later
# version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
# <https://www.gnu.org/licenses/>.

# The embedded scene image follows its file (2026-10-15): a project whose embedded image differs from the file its
# path names takes the file when opened (the project turns dirty: asterisk in the title), and while the project is
# open a change of that file on disk is re-embedded within a second (the picture changes, the status bar says so).
# The project and its image live in a temporary folder; the embedded image is the ball lens, the file the race
# track, then the file becomes the ball lens again. A second project without a path but with the image by name in
# a scene_images folder beside it (a project saved before the path existed) must find it there. See Common.ps1
# for the rules (no mouse, no activation, no UIA patterns). The app is dirty at the end of each phase, so
# Stop-App's close runs into the save prompt and kills it after 8 s.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\SceneImageCheck.ps1 [-Exe <path>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[switch]$Trace
)

. (Join-Path $PSScriptRoot "Common.ps1")

# two scene images from Jeroen's examples, kept with the import fixtures (2026-10-31)
$examples = Join-Path $PSScriptRoot "..\legacy_examples"
$raceTrack = Join-Path $examples "Example12_Dispersion\scene_images\frequency_race_track (2).png"
$ballLens = Join-Path $examples "Ball_lens_high_RI_AR.png"
$folder = Join-Path $env:TEMP ("wd-scene-" + [Guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $folder
Copy-Item $raceTrack (Join-Path $folder "scene.png")
$embedded = [Convert]::ToBase64String([IO.File]::ReadAllBytes($ballLens))
$project = Join-Path $folder "watch.wdproj"
@"
<?xml version="1.0" encoding="utf-8"?>
<Project Version="1" Seed="7">
	<Scene Mode="Image" Image="scene.png" ImagePath="scene.png" DampingEdge="64">
		<ImageData>$embedded</ImageData>
	</Scene>
	<Simulation Steps="100" VisualizeEvery="2" />
	<Source Name="Source 1" X="150" Y="140" Frequency="100" />
</Project>
"@ | Set-Content -Path $project -Encoding UTF8

Start-App $Exe $project
try {
	Check "opening a project whose image file differs from the embedded image takes the file: the project is dirty" (WaitFor { $win.Current.Name -like "*`*" } 5000 100) $win.Current.Name
	$status = (Find "StatusMessage").Current.Name
	Check "the status bar says the image was re-embedded on opening" ($status -like "*re-embedded*changed since the project was saved*") $status
	FocusOn "Tree:Project"
	Key $VK_DOWN 400   # the Scene node: the Properties pane shows the scene view
	Check "the scene view shows the image file's path" ((Find "SceneImagePath").Current.Name -eq "scene.png") (Find "SceneImagePath").Current.Name
	$null = WaitFor { (Find "StatusExport").Current.Name -eq "" } 20000 200
	Start-Sleep -Milliseconds 500
	$before = FieldHash
	# the file changes on disk while the project is open: back to the ball lens
	Copy-Item $ballLens (Join-Path $folder "scene.png") -Force
	Check "a change of the file on disk is re-embedded within a moment" (WaitFor { (Find "StatusMessage").Current.Name -like "*re-embedded*changed on disk*" } 6000 100) (Find "StatusMessage").Current.Name
	Check "and the field picture changes with it" (WaitFor { (FieldHash) -ne $before } 10000 200)
	Check "app never became the foreground window" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
# a project from before the path existed: the image only by name, the file in scene_images beside the project
$null = New-Item -ItemType Directory -Path (Join-Path $folder "scene_images")
Copy-Item $raceTrack (Join-Path $folder "scene_images\old.png")
$old = Join-Path $folder "old.wdproj"
@"
<?xml version="1.0" encoding="utf-8"?>
<Project Version="1" Seed="7">
	<Scene Mode="Image" Image="old.png" DampingEdge="64">
		<ImageData>$embedded</ImageData>
	</Scene>
	<Simulation Steps="100" VisualizeEvery="2" />
	<Source Name="Source 1" X="150" Y="140" Frequency="100" />
</Project>
"@ | Set-Content -Path $old -Encoding UTF8
Start-App $Exe $old
try {
	Check "a project without a path finds its image by name in scene_images and takes the changed file" (WaitFor { (Find "StatusMessage").Current.Name -like "*re-embedded*" } 5000 100) (Find "StatusMessage").Current.Name
	FocusOn "Tree:Project"
	Key $VK_DOWN 400
	Check "the found path is stored relative to the project" ((Find "SceneImagePath").Current.Name -eq "scene_images\old.png") (Find "SceneImagePath").Current.Name
	Check "app never became the foreground window" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
	Remove-Item -Recurse -Force $folder -ErrorAction SilentlyContinue
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
