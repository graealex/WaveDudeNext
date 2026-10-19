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

# Checks the probe graph boxes drawn over the preview pictures (2026-10-14): on tiny_probes.wdproj (320x240,
# 100 steps, a point probe with a custom 120x60 box on the field, a line probe with a Tiny box at its own
# position on the intensity) the boxes must sit on the right picture at the probe's box position, show the
# scratch frame's data before any run and the run's data after it, move with the arrow keys when focused,
# grow from their corner grip, and a box at its probe's position must move with the probe. A probe move leaves
# the run valid (a probe never changes the picture): the graph is recomputed from scratch, no run is started,
# and Play afterwards starts one to refresh the recordings. The composite source's rotation handle turns it. Also saves a screenshot
# of the window after the run for judging the look. See Common.ps1 for the rules (no mouse, no activation,
# no UIA patterns). Prints PASS/FAIL per check and exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\ProbeGraphCheck.ps1 [-Exe <path>] [-Shot <png>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[string]$Shot = (Join-Path $env:TEMP "probe_graph.png"),
	[switch]$Trace
)

. (Join-Path $PSScriptRoot "Common.ps1")

# all elements with an automation id
function FindAll([string]$id) {
	return $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)))
}
# whether $inner lies inside $outer (screen rectangles)
function Inside($inner, $outer) {
	return ($inner.Left -ge $outer.Left - 1) -and ($inner.Top -ge $outer.Top - 1) -and ($inner.Right -le $outer.Right + 1) -and ($inner.Bottom -le $outer.Bottom + 1)
}
function BoxHash([string]$id) {
	$r = (Find $id).Current.BoundingRectangle
	return [Native]::Hash($script:hwnd, [int]$r.Left, [int]$r.Top, [int]$r.Width, [int]$r.Height)
}

Start-App $Exe (Join-Path $PSScriptRoot "tiny_probes.wdproj")
try {
	Check "project loaded: 50 frames" ((Frame) -eq "0 / 49") (Frame)
	$field = (Find "PreviewFieldImage").Current.BoundingRectangle
	$intensity = (Find "PreviewIntensityImage").Current.BoundingRectangle
	$box1 = FindAll "ProbeBox:Probe 1"
	$box2 = FindAll "ProbeBox:Probe 2"
	Check "one box per probe, each on its own picture" ($box1.Count -eq 1 -and $box2.Count -eq 1) ("found {0} and {1}" -f $box1.Count, $box2.Count)
	$r1 = $box1[0].Current.BoundingRectangle
	$r2 = $box2[0].Current.BoundingRectangle
	Check "the field probe's box lies on the field picture" (Inside $r1 $field) ("box {0:0},{1:0} {2:0}x{3:0} picture {4:0},{5:0} {6:0}x{7:0}" -f $r1.Left, $r1.Top, $r1.Width, $r1.Height, $field.Left, $field.Top, $field.Width, $field.Height)
	Check "the intensity probe's box lies on the intensity picture" (Inside $r2 $intensity) ("box {0:0},{1:0} {2:0}x{3:0}" -f $r2.Left, $r2.Top, $r2.Width, $r2.Height)
	# the custom 120 x 60 box at canvas (8, 8): its top-left corner is the edge of canvas pixel (8, 8), its size 120 x 60 canvas pixels
	$corner = CanvasPoint "PreviewFieldImage" 320 240 7.5 7.5
	$scale = $corner[2]
	Check "custom box at its canvas position and size" ([math]::Abs($r1.Left - $corner[0]) -lt 2 -and [math]::Abs($r1.Top - $corner[1]) -lt 2 -and [math]::Abs($r1.Width - 120 * $scale) -lt 2 -and [math]::Abs($r1.Height - 60 * $scale) -lt 2) ("box {0:0},{1:0} {2:0}x{3:0} expected {4:0},{5:0} {6:0}x{7:0}" -f $r1.Left, $r1.Top, $r1.Width, $r1.Height, $corner[0], $corner[1], (120 * $scale), (60 * $scale))
	# the Tiny box (200 x 100) fits the canvas and is drawn at its size (a box was held to half the canvas width until 2026-10-18)
	$scale2 = (CanvasPoint "PreviewIntensityImage" 320 240 0 0)[2]
	Check "the Tiny box is drawn at its 200 x 100" ([math]::Abs($r2.Width - 200 * $scale2) -lt 2 -and [math]::Abs($r2.Height - 100 * $scale2) -lt 2) ("{0:0}x{1:0} expected {2:0}x{3:0}" -f $r2.Width, $r2.Height, (200 * $scale2), (100 * $scale2))
	$grip = (Find "ProbeBoxCorner:Probe 1").Current.BoundingRectangle
	Check "the resize grip sits in the box's bottom-right corner" ([math]::Abs($grip.Right - $r1.Right) -lt 2 -and [math]::Abs($grip.Bottom - $r1.Bottom) -lt 2) ("grip {0:0},{1:0} box corner {2:0},{3:0}" -f $grip.Right, $grip.Bottom, $r1.Right, $r1.Bottom)
	$empty = BoxHash "ProbeBox:Probe 1"
	# ------------------------------------------------------------ before any run: the scratch frame's recording
	FocusOn "PreviewTimeline"
	Key $VK_HOME 100; Key $VK_PRIOR 50; Key $VK_PRIOR 50; Key $VK_PRIOR 150   # frame 30
	Check "skipped to frame 30 with no run" ((Position) -eq 30) (Frame)
	Check "the graph shows the scratch frame's samples (box changed)" (WaitFor { (BoxHash "ProbeBox:Probe 1") -ne $empty } 8000 200)
	Key $VK_HOME 300
	# ------------------------------------------------------------ a run fills the graphs
	FocusOn "PreviewPlay"
	Key $VK_F5 100
	Check "run completes" (WaitFor { (RunStatus) -like "Completed:*" } 30000) (RunStatus)
	Start-Sleep -Milliseconds 300
	Check "preview on the last frame" ((Frame) -eq "49 / 49") (Frame)
	$full = BoxHash "ProbeBox:Probe 1"
	Check "the point probe's box changed once the run had data" ($full -ne $empty)
	[Native]::Capture($hwnd, $Shot)
	Write-Output ("screenshot: " + $Shot)
	Key $VK_HOME 400
	$first = BoxHash "ProbeBox:Probe 1"
	Check "the graph at frame 0 differs from the graph at the last frame (it follows the position)" ($first -ne $full)
	Key $VK_END 400
	# ------------------------------------------------------------ keyboard: move and resize
	FocusOn "ProbeBox:Probe 1"
	Check "Tab reaches the box, which selects Probe 1 in the tree" ((Focused "ProbeBox:Probe 1") -and (TreeItemSelected "Probe 1")) (FocusedId)
	Key $VK_RIGHT 200
	$moved = (Find "ProbeBox:Probe 1").Current.BoundingRectangle
	Check "Right arrow moves the box one canvas pixel" ([math]::Abs(($moved.Left - $r1.Left) - $scale) -lt 1.5 -and [math]::Abs($moved.Top - $r1.Top) -lt 1.5) ("dx {0:0.0} expected {1:0.0}" -f ($moved.Left - $r1.Left), $scale)
	Key $VK_LEFT 200
	$back = (Find "ProbeBox:Probe 1").Current.BoundingRectangle
	Check "Left arrow moves it back" ([math]::Abs($back.Left - $r1.Left) -lt 1.5) ("x {0:0} vs {1:0}" -f $back.Left, $r1.Left)
	# the graph settings are no simulation edit: the run must still be there, not stale
	Check "moving the box did not mark the run stale" ((RunStatus) -like "Completed:*" -and (RunStatus) -notlike "*edited*") (RunStatus)
	FocusOn "ProbeBoxCorner:Probe 1"
	Key $VK_RIGHT 200
	Key $VK_DOWN 200
	$grown = (Find "ProbeBox:Probe 1").Current.BoundingRectangle
	Check "arrows on the corner grip grow the box by a canvas pixel each way" ([math]::Abs(($grown.Width - $r1.Width) - $scale) -lt 1.5 -and [math]::Abs(($grown.Height - $r1.Height) - $scale) -lt 1.5) ("dw {0:0.0} dh {1:0.0} expected {2:0.0}" -f ($grown.Width - $r1.Width), ($grown.Height - $r1.Height), $scale)
	Key $VK_LEFT 200
	Key $VK_UP 200
	$restored = (Find "ProbeBox:Probe 1").Current.BoundingRectangle
	Check "and shrink it back" ([math]::Abs($restored.Width - $r1.Width) -lt 1.5 -and [math]::Abs($restored.Height - $r1.Height) -lt 1.5)
	# moving the probe itself: the picture stays (a probe never changes it), only the recordings are stale: the graph
	# is recomputed from scratch and no run is started
	$beforeMove = BoxHash "ProbeBox:Probe 1"
	FocusOn "Probe:Probe 1:A"
	for ($i = 0; $i -lt 10; $i++) { Key $VK_RIGHT 40 }
	Start-Sleep -Milliseconds 200
	Check "moving the probe leaves the run valid (no 'edited since')" ((RunStatus) -like "Completed:*" -and (RunStatus) -notlike "*edited*") (RunStatus)
	Check "the graph is recomputed from scratch after the probe move" (WaitFor { (BoxHash "ProbeBox:Probe 1") -ne $beforeMove } 8000 200)
	$still = $true
	for ($i = 0; $i -lt 10; $i++) { Start-Sleep -Milliseconds 250; if ((RunStatus) -notlike "Completed: 100 / 100*") { $still = $false } }
	Check "no automatic re-run follows a probe move" $still (RunStatus)
	# probe 2's box sits at the probe's own position: moving the probe moves the box along
	$box2 = (Find "ProbeBox:Probe 2").Current.BoundingRectangle
	FocusOn "Probe:Probe 2:A"
	Key $VK_RIGHT 300
	$box2Moved = (Find "ProbeBox:Probe 2").Current.BoundingRectangle
	Check "moving a probe whose box sits at its position moves the box along" ([math]::Abs(($box2Moved.Left - $box2.Left) - $scale2) -lt 1.5 -and [math]::Abs($box2Moved.Top - $box2.Top) -lt 1.5) ("dx {0:0.0} expected {1:0.0}" -f ($box2Moved.Left - $box2.Left), $scale2)
	Key $VK_LEFT 300
	Check "and back" ([math]::Abs((Find "ProbeBox:Probe 2").Current.BoundingRectangle.Left - $box2.Left) -lt 1.5)
	# Play after probe edits: the recordings are stale, so Play starts a run to refresh them (status: "Re-run started" then Running/Completed)
	FocusOn "PreviewPlay"
	Key $VK_SPACE 20
	Check "Play after a probe edit starts a run to refresh the recordings" (WaitFor { (RunStatus) -like "Running:*" -or ((Find "StatusMessage").Current.Name -like "Re-run started*") } 5000 10) ((Find "StatusMessage").Current.Name)
	Check "that run completes" (WaitFor { (RunStatus) -like "Completed: 100 / 100*" } 15000 100) (RunStatus)
	Key $VK_SPACE 100   # stop playback
	# ------------------------------------------------------------ a probe the run does not record
	# Probe 2 (selected) off: the run keeps its recording, so nothing but the recordings is stale; Play then makes a run without it
	FocusOn "ProbeActive"
	Key $VK_SPACE 300
	Check "switching a recorded probe off leaves the run valid" ((RunStatus) -like "Completed:*" -and (RunStatus) -notlike "*edited*") (RunStatus)
	Check "its box is gone" ((FindAll "ProbeBox:Probe 2").Count -eq 0)
	FocusOn "PreviewPlay"
	Key $VK_SPACE 20
	Check "Play makes a run without it" (WaitFor { (RunStatus) -like "Completed: 100 / 100*" -and (RunStatus) -notlike "*edited*" } 20000 100) (RunStatus)
	Key $VK_SPACE 100   # stop playback
	# Probe 2 on again: this run has no recording for it, so the edit counts as a simulation edit (the user's choice, 2026-10-18)
	FocusOn "ProbeActive"
	Key $VK_SPACE 300
	Check "switching on a probe the run does not record marks the run stale" (WaitFor { (RunStatus) -like "*edited*" } 3000) (RunStatus)
	Check "and the automatic re-run records it" (WaitFor { (RunStatus) -like "Completed: 100 / 100*" -and (RunStatus) -notlike "*edited*" } 20000 200) (RunStatus)
	Check "its box is back" ((FindAll "ProbeBox:Probe 2").Count -eq 1)
	# the composite source's rotation handle: only while the source is selected; on the row axis right of the centre at
	# 0 degrees; arrows turn the array
	$angleCondition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "SourceAngle:Source 2")
	Check "the rotation handle is hidden while Source 2 is not selected" ($null -eq $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $angleCondition))
	FocusOn "Source:Source 2"
	Check "selecting Source 2 shows its rotation handle" (WaitFor { $null -ne $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $angleCondition) } 3000 100)
	$centre2 = CentreOf "Source:Source 2"
	$handle = CentreOf "SourceAngle:Source 2"
	Check "the rotation handle sits on the row axis, right of the array centre" ($handle[0] -gt $centre2[0] + 20 -and [math]::Abs($handle[1] - $centre2[1]) -lt 2) ("handle {0:0},{1:0} centre {2:0},{3:0}" -f $handle[0], $handle[1], $centre2[0], $centre2[1])
	FocusOn "SourceAngle:Source 2"
	Check "Tab reaches the rotation handle, which selects Source 2 in the tree" ((Focused "SourceAngle:Source 2") -and (TreeItemSelected "Source 2")) (FocusedId)
	for ($i = 0; $i -lt 10; $i++) { Key $VK_RIGHT 40 }
	Start-Sleep -Milliseconds 300
	$turned = CentreOf "SourceAngle:Source 2"
	$reach = $handle[0] - $centre2[0]
	$rise = $reach * [math]::Sin(10 * [math]::PI / 180)
	Check "ten Right arrows turn the array by 10 degrees: the handle rises on its arc" ([math]::Abs(($centre2[1] - $turned[1]) - $rise) -lt 2 -and [math]::Abs(($turned[0] - $centre2[0]) - $reach * [math]::Cos(10 * [math]::PI / 180)) -lt 2) ("rise {0:0.0} expected {1:0.0}" -f ($centre2[1] - $turned[1]), $rise)
	Check "turning the array marks the run stale (the picture changes)" ((RunStatus) -like "*edited*") (RunStatus)
	Check "app never became the foreground window" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
