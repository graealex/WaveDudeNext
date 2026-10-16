# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Alexander Graef <alex@graef.cc>
#
# This file is part of WaveDude Next, a 2D wave simulator.
#
# Based on the work of
#   JJM Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
#     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
#   Manuel (0x23, Diffraction Limited), author of WaveSimulator2D
#     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
#
# WaveDude Next is free software: you can redistribute it and/or modify it under
# the terms of the GNU General Public License as published by the Free Software
# Foundation, either version 3 of the License, or (at your option) any later
# version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
# <https://www.gnu.org/licenses/>.

# Runs a simulation in the WPF app through the keyboard and checks the preview follows it: F5 on a tiny
# project (64x48, 200 steps, a frame every 2 steps) must complete and leave the preview live on the last
# frame; stepping detaches, the picture differs between frames, F re-attaches. A second, long project
# exercises pause (F6), resume and stop (Esc) while the run is in progress. See Common.ps1 for the rules
# (no mouse, no activation, no UIA patterns). Prints PASS/FAIL per check and exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\RunCheck.ps1 [-Exe <path>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[switch]$Trace   # append foreground state and focused element to every line
)

. (Join-Path $PSScriptRoot "Common.ps1")

# ---------------------------------------------------------------- a short run to completion
Start-App $Exe (Join-Path $PSScriptRoot "tiny.wdproj")
try {
	Check "tiny project loaded: 100 frames" ((Frame) -eq "0 / 99") (Frame)
	Check "no run status before F5" ((RunStatus) -eq "") (RunStatus)
	# the source handle sits on the black frame at the source's canvas position, before any run
	$expected = CanvasPoint "PreviewFieldImage" 64 48 20 24
	$centre = CentreOf "Source:Source 1"
	Check "source marker at the source's canvas position on the black frame" ([math]::Abs($centre[0] - $expected[0]) -lt 3 -and [math]::Abs($centre[1] - $expected[1]) -lt 3) ("marker {0:0},{1:0} expected {2:0},{3:0}" -f $centre[0], $centre[1], $expected[0], $expected[1])
	$black = FieldHash
	FocusOn "Source:Source 1"
	Check "Tab reaches the source marker, which selects Source 1 in the tree" ((Focused "Source:Source 1") -and (TreeItemSelected "Source 1")) (FocusedId)
	# the Properties pane shows the source: its wavelength readout (level 120, c = 1, scale 2: 13.35 px)
	$lambda = (Find "SourceWavelength").Current.Name
	Check "the source view shows the estimated wavelength" ($lambda -match "13[,.]4 px") $lambda
	# the tree's check box: Tab to the selected row, Space flips Enabled (the check box follows), Space again restores it
	FocusOn "Tree:Source 1"
	Key $VK_SPACE 200
	Check "Space on the tree row disables the source (its check box clears)" (-not (ToggleOn "Enabled:Source 1"))
	Key $VK_SPACE 200
	Check "Space again enables it" (ToggleOn "Enabled:Source 1")
	# the number box: Up commits X + 1 at once (the marker follows), Down puts it back
	FocusOn "SourceX"
	$beforeX = (Pattern (Find "SourceX") "Value").Current.Value
	Key $VK_UP 200
	$afterX = (Pattern (Find "SourceX") "Value").Current.Value
	$upped = CentreOf "Source:Source 1"
	Check "Up in the X box adds one and moves the marker" (([int]$afterX) -eq ([int]$beforeX + 1) -and [math]::Abs(($upped[0] - $centre[0]) - $expected[2]) -lt 2) ("x $beforeX -> $afterX, marker dx {0:0.0}" -f ($upped[0] - $centre[0]))
	Key $VK_DOWN 200
	Check "Down puts it back" (((Pattern (Find "SourceX") "Value").Current.Value) -eq $beforeX)
	FocusOn "Source:Source 1"
	Key $VK_RIGHT 200
	$moved = CentreOf "Source:Source 1"
	Check "Right arrow moves the marker one canvas pixel" ([math]::Abs(($moved[0] - $centre[0]) - $expected[2]) -lt 2 -and [math]::Abs($moved[1] - $centre[1]) -lt 2) ("dx {0:0.0} expected {1:0.0}" -f ($moved[0] - $centre[0]), $expected[2])
	# no run yet: the edit alone makes the preview compute the shown frame from scratch
	Check "a scratch frame replaces the black canvas after the edit" (WaitFor { (FieldHash) -ne $black } 6000 200)
	Key $VK_LEFT 200
	$back = CentreOf "Source:Source 1"
	Check "Left arrow moves it back" ([math]::Abs($back[0] - $centre[0]) -lt 2) ("x {0:0} vs {1:0}" -f $back[0], $centre[0])
	# ------------------------------------------------------------ play without a run starts one
	FocusOn "PreviewTimeline"
	Key $VK_HOME 100; Key $VK_PRIOR 50; Key $VK_PRIOR 50; Key $VK_PRIOR 150   # frame 30
	Check "skipped to frame 30 with no run" ((Position) -eq 30) (Frame)
	$dark = FieldHash
	Key $VK_SPACE 100
	Check "Space starts a run when there is none" (WaitFor { (RunStatus) -like "Running:*" -or (RunStatus) -like "Completed:*" } 5000 50) (RunStatus)
	Check "playback went on from frame 30 once the frames existed" (WaitFor { (Position) -gt 30 } 5000 20) ("position {0}, {1}" -f (Position), (RunStatus))
	Check "play did not switch to live follow" (-not (LiveOn))
	Check "and ran to the end of the run" (WaitFor { -not (Playing) -and (Position) -eq 99 } 5000 50) (Frame)
	Check "the picture is not black any more" ((FieldHash) -ne $dark)
	FocusOn "PreviewPlay"
	Key $VK_F5 100
	Check "F5 starts the run (status appears)" (WaitFor { (RunStatus) -ne "" } 5000) (RunStatus)
	Check "run completes" (WaitFor { (RunStatus) -like "Completed:*" } 30000) (RunStatus)
	Check "status names the steps and the engine" ((RunStatus) -match "^Completed: 200 / 200 steps, (CPU|GPU)") (RunStatus)
	Start-Sleep -Milliseconds 200
	Check "preview followed the run to its last frame; Live is off again once it ended" (-not (LiveOn) -and (Frame) -eq "99 / 99") ("live {0}, {1}" -f (LiveOn), (Frame))
	Check "live button disabled after the run, play enabled" (-not (Find "PreviewLive").Current.IsEnabled -and (Find "PreviewPlay").Current.IsEnabled)
	$late = FieldHash
	Key $VK_COMMA 500
	Check "comma steps back to frame 98" ((Frame) -eq "98 / 99") (Frame)
	Key $VK_HOME 300
	Check "Home goes to frame 0" ((Frame) -eq "0 / 99") (Frame)
	$early = FieldHash
	Check "the field picture differs between frame 0 and the last frame" ($early -ne $late) ("{0} vs {1}" -f $early.Substring(0, 8), $late.Substring(0, 8))
	Key $VK_F 150
	Check "F does nothing once the run has ended" (-not (LiveOn) -and (Frame) -eq "0 / 99") ("live {0}, {1}" -f (LiveOn), (Frame))
	Key $VK_END 500
	Check "End goes to the last frame" ((Frame) -eq "99 / 99") (Frame)
	# the handle follows the run's frames too (same canvas size here)
	$expected = CanvasPoint "PreviewIntensityImage" 64 48 20 24
	$centre = CentreOf "Source:Source 1"
	$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "Source:Source 1")))
	Check "both pictures carry the source marker" ($all.Count -eq 2) ("found " + $all.Count)
	$second = $all[1].Current.BoundingRectangle
	Check "the intensity picture's marker sits at the source too" ([math]::Abs(($second.Left + $second.Width / 2) - $expected[0]) -lt 3 -and [math]::Abs(($second.Top + $second.Height / 2) - $expected[1]) -lt 3) ("marker {0:0},{1:0} expected {2:0},{3:0}" -f ($second.Left + $second.Width / 2), ($second.Top + $second.Height / 2), $expected[0], $expected[1])
	# the handles must follow a resize of the pictures in the same layout pass (they lagged one pass behind once)
	ResizeApp -160 -120
	$shrunk = CanvasPoint "PreviewFieldImage" 64 48 20 24
	$centre = CentreOf "Source:Source 1"
	Check "after shrinking the window the marker is still at the source" ([math]::Abs($centre[0] - $shrunk[0]) -lt 3 -and [math]::Abs($centre[1] - $shrunk[1]) -lt 3 -and [math]::Abs($shrunk[2] - $expected[2]) -gt 0.01) ("marker {0:0},{1:0} expected {2:0},{3:0} (scale {4:0.00} was {5:0.00})" -f $centre[0], $centre[1], $shrunk[0], $shrunk[1], $shrunk[2], $expected[2])
	ResizeApp 160 120
	$grown = CanvasPoint "PreviewFieldImage" 64 48 20 24
	$centre = CentreOf "Source:Source 1"
	Check "and after growing it back" ([math]::Abs($centre[0] - $grown[0]) -lt 3 -and [math]::Abs($centre[1] - $grown[1]) -lt 3) ("marker {0:0},{1:0} expected {2:0},{3:0}" -f $centre[0], $centre[1], $grown[0], $grown[1])
	# ------------------------------------------------------------ live: an edit after the run
	# both pictures compared below are taken with the marker focused, so its highlight is the same in both
	FocusOn "Source:Source 1"
	Start-Sleep -Milliseconds 200
	$stored = FieldHash
	Key $VK_RIGHT 100
	Check "moving the source marks the run stale" (WaitFor { (RunStatus) -like "*edited since*" } 3000) (RunStatus)
	Check "the shown frame is recomputed from scratch before any run" (WaitFor { (FieldHash) -ne $stored } 6000 200)
	Check "the stored run is redone automatically after the edit" (WaitFor { (RunStatus) -like "Completed:*" -and (RunStatus) -notlike "*edited since*" } 20000 200) (RunStatus)
	Check "the preview kept its position through the re-run" ((Frame) -eq "99 / 99") (Frame)
	Key $VK_LEFT 100
	Check "moving it back: stale, then re-run" ((WaitFor { (RunStatus) -like "*edited since*" } 3000) -and (WaitFor { (RunStatus) -like "Completed:*" -and (RunStatus) -notlike "*edited since*" } 20000 200)) (RunStatus)
	Start-Sleep -Milliseconds 200
	Check "the re-run reproduces the original frame (same picture)" ((FieldHash) -eq $stored) ("{0} vs {1}" -f (FieldHash).Substring(0, 8), $stored.Substring(0, 8))
	# ------------------------------------------------------------ an edit during playback (2026-10-16)
	# the whole run is stale, so playback stops at once where it is, the frame there is recomputed and the re-run
	# follows with the position kept (before, every playback step restarted the scratch timer, behind which the
	# automatic re-run waits, so the old frames played on until playback ended)
	FocusOn "PreviewPlay"
	Key $VK_L 150                                  # loop, so playback would not end by itself
	Key $VK_SPACE 100
	Check "playback runs (looping)" ((WaitFor { $p = Position; $p -gt 0 -and $p -lt 99 } 3000 20) -and (LoopOn)) (Frame)
	FocusOn "Source:Source 1"
	Key $VK_RIGHT 100
	Check "an edit during playback marks the run stale" (WaitFor { (RunStatus) -like "*edited since*" } 3000) (RunStatus)
	$halted = Position
	Start-Sleep -Milliseconds 400
	Check "and stops playback at once" ((Position) -eq $halted) ("{0} then {1}" -f $halted, (Position))
	Check "the re-run completes with the position kept" ((WaitFor { (RunStatus) -like "Completed:*" -and (RunStatus) -notlike "*edited since*" } 20000 200) -and (Position) -eq $halted) ("{0}, {1}" -f (RunStatus), (Frame))
	Key $VK_LEFT 100
	Check "moving it back: stale, then re-run" ((WaitFor { (RunStatus) -like "*edited since*" } 3000) -and (WaitFor { (RunStatus) -like "Completed:*" -and (RunStatus) -notlike "*edited since*" } 20000 200)) (RunStatus)
	Key $VK_L 150                                  # loop off again
	Check "loop off" (WaitFor { -not (LoopOn) } 1000)
	FocusOn "PreviewPlay"
	Key $VK_SPACE 100
	$moving = WaitFor { $p = Position; $p -gt 0 -and $p -lt 99 } 3000 20
	$live = LiveOn
	Key $VK_SPACE 100
	Check "Space plays the stored frames from the start (live off)" ($moving -and -not $live) (Frame)
	# a second F5 replaces the run and its frames
	Key $VK_F5 100
	Check "second run completes" (WaitFor { (RunStatus) -like "Completed:*" } 30000) (RunStatus)
	Start-Sleep -Milliseconds 200
	Check "the new run was followed to its last frame" ((Frame) -eq "99 / 99") ("live {0}, {1}" -f (LiveOn), (Frame))
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}

# ---------------------------------------------------------------- a long run: pause, resume, stop
Start-App $Exe (Join-Path $PSScriptRoot "tiny_long.wdproj")
try {
	Check "long project loaded: 2000 frames" ((Frame) -eq "0 / 1999") (Frame)
	FocusOn "PreviewPlay"
	Key $VK_F5 100
	Check "F5: running" (WaitFor { (RunStatus) -like "Running:*" } 10000) (RunStatus)
	# the Run button is Stop while the run is in progress (2026-10-16); same id, told apart by its name
	Check "the Run button turned into Stop" (WaitFor { (Find "PreviewRun").Current.Name -like "Stop the simulation*" } 2000) ((Find "PreviewRun").Current.Name)
	$null = WaitFor { (Position) -ge 2 } 5000 50
	$f1 = Position
	$advanced = WaitFor { (Position) -gt $f1 } 5000 50
	Check "live preview advances while the run is in progress" ((LiveOn) -and $advanced) ("{0} -> {1}" -f $f1, (Position))
	Key $VK_F6 100
	Check "F6 pauses" (WaitFor { (RunStatus) -like "Paused:*" } 5000) (RunStatus)
	$held = RunStatus
	Start-Sleep -Milliseconds 200
	Check "paused run holds its step count" ((RunStatus) -eq $held) (RunStatus)
	Key $VK_F6 100
	Check "F6 resumes" (WaitFor { (RunStatus) -like "Running:*" } 5000) (RunStatus)
	$null = WaitFor { (Position) -ge 40 } 20000 50   # enough frames for the scrub checks below, however fast the keys are
	FocusOn "PreviewRun"
	Key $VK_SPACE 100
	Check "Space on the Stop button stops the run" (WaitFor { (RunStatus) -like "Stopped:*" } 10000) (RunStatus)
	Check "the button is Run again" (WaitFor { (Find "PreviewRun").Current.Name -like "Run the simulation*" } 2000) ((Find "PreviewRun").Current.Name)
	Start-Sleep -Milliseconds 200
	$last = Position
	Check "the frames computed so far stay, the preview on the newest one, Live off" (-not (LiveOn) -and $last -gt 0 -and (Frame) -eq ("{0} / {0}" -f $last)) (Frame)
	Key $VK_HOME 500
	Check "Home scrubs back to frame 0 of the partial run" ((Frame) -eq ("0 / {0}" -f $last)) (Frame)
	# ------------------------------------------------------------ an edit during an automatic re-run stops it
	FocusOn "PreviewTimeline"
	Key $VK_HOME 100; Key $VK_PRIOR 50; Key $VK_PRIOR 50; Key $VK_PRIOR 150   # frame 30 of the 40 that exist
	Check "scrubbed to frame 30 of the stopped run" ((Position) -eq 30) (Frame)
	FocusOn "Source:Source 1"
	Key $VK_RIGHT 100
	Check "an edit after the stopped run starts an automatic re-run" (WaitFor { (RunStatus) -like "Running:*" } 6000 100) (RunStatus)
	Check "the position stayed at frame 30 and Live is off" ((Position) -eq 30 -and -not (LiveOn)) (Frame)
	Key $VK_RIGHT 100
	Check "another edit stops the automatic run at once" (WaitFor { (RunStatus) -like "Stopped:*" } 3000 50) (RunStatus)
	Check "the cancelled run does not pull the position back" ((Position) -eq 30) (Frame)
	Check "and a fresh automatic run starts after the quiet period" (WaitFor { (RunStatus) -like "Running:*" } 6000 100) (RunStatus)
	Check "the position is still frame 30" ((Position) -eq 30) (Frame)
	Key $VK_ESCAPE 150
	# ------------------------------------------------------------ an edit during a run the user started stops it too (2026-10-16)
	Check "Esc stops the automatic run" (WaitFor { (RunStatus) -like "Stopped:*" } 3000 50) (RunStatus)
	FocusOn "PreviewPlay"
	Key $VK_F5 100
	Check "F5 starts a run" (WaitFor { (RunStatus) -like "Running:*" } 6000 100) (RunStatus)
	FocusOn "Source:Source 1"
	Key $VK_RIGHT 100
	Check "an edit stops the run the user started at once" (WaitFor { (RunStatus) -like "Stopped:*" } 3000 50) (RunStatus)
	Check "and the automatic re-run follows after the quiet period" (WaitFor { (RunStatus) -like "Running:*" } 6000 100) (RunStatus)
	Key $VK_ESCAPE 150
	Key $VK_LEFT 50; Key $VK_LEFT 50; Key $VK_LEFT 150
	Key $VK_ESCAPE 100
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
	Write-Output ("{0} failure(s)" -f $script:failures)
}
exit $script:failures
