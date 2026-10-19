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

# Drives the WPF preview pane with keystrokes posted to its window and reads the result back through
# UI Automation; see Common.ps1 for the rules (no mouse, no activation, no UIA patterns). Runs on the
# default (new) project, so no simulation is involved: playback moves over the placeholder pictures.
# Prints PASS/FAIL per check and exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\PreviewCheck.ps1 [-Exe <path>] [-Shot <png>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[string]$Shot = (Join-Path $env:TEMP "wavedude_preview_check.png"),
	[switch]$Trace   # append foreground state and focused element to every line
)

. (Join-Path $PSScriptRoot "Common.ps1")
Start-App $Exe

try {
	Check "initial readout" ((Frame) -eq "0 / 1249") (Frame)
	# the split orientation is a persisted user setting: start from side by side whatever the last session left
	if (Selected "PreviewStacked") { FocusOn "PreviewStacked"; Key $VK_LEFT 200 }
	Check "side by side to begin with" (Selected "PreviewSideBySide")
	Check "play enabled, pause disabled" ((Find "PreviewPlay").Current.IsEnabled -and -not (Playing))
	Check "live is disabled without a run" (-not (Find "PreviewLive").Current.IsEnabled)
	Check "no run status" ((RunStatus) -eq "") (RunStatus)

	# ------------------------------------------------------------ transport
	Write-Output ("focus before any key: {0}" -f (FocusedId))
	FocusOn "PreviewPlay"
	Check "Tab reaches play while the window stays inactive" (((FocusedId) -eq "PreviewPlay") -and ((Foreground) -ne "APP")) (FocusedId)
	$order = @()
	for ($i = 0; $i -lt 12; $i++) { Key $VK_TAB 120; $order += (FocusedId) }
	# pause, clear-range and live are disabled and skipped; the split group is one stop (its checked radio); timeline; splitter; then the pane is left
	Check "tab order play > step back/forward > in > out > loop > speed > show field/intensity > split > timeline > splitter > out of the pane" (($order[0..10] -join ",") -eq "PreviewStepBack,PreviewStepForward,PreviewSetIn,PreviewSetOut,PreviewLoop,PreviewSpeed,PreviewShowField,PreviewShowIntensity,PreviewSideBySide,PreviewTimeline,PreviewSplitter" -and $order[11] -notlike "Preview*") ($order -join ",")
	# the picture toggles: Space on the intensity toggle hides that picture, again shows it
	FocusOn "PreviewShowIntensity"
	$fwBoth = (Find "PreviewFieldImage").Current.BoundingRectangle.Width
	Key $VK_SPACE 200
	$fwAlone = (Find "PreviewFieldImage").Current.BoundingRectangle.Width
	Check "Space on the intensity toggle hides the intensity picture and the field takes the room" (-not (ToggleOn "PreviewShowIntensity") -and ((FindRaw "PreviewIntensityImage").Current.IsOffscreen) -and $fwAlone -gt $fwBoth * 1.5) ("field width {0} -> {1}" -f $fwBoth, $fwAlone)
	Key $VK_SPACE 200
	Check "and shows it again at the old size" ((ToggleOn "PreviewShowIntensity") -and -not ((Find "PreviewIntensityImage").Current.IsOffscreen) -and [math]::Abs((Find "PreviewFieldImage").Current.BoundingRectangle.Width - $fwBoth) -lt 2)

	FocusOn "PreviewPlay"
	Key $VK_SPACE 2000
	$pos = Position
	# the clock is the project's video frame rate, 60 fps by default
	Check "Space on play: ~120 frames in 2 s at speed 1" ($pos -ge 90 -and $pos -le 150) "position $pos"
	Check "pause enabled while playing, focus handed to pause" ((Playing) -and -not (Find "PreviewPlay").Current.IsEnabled -and (Focused "PreviewPause")) (FocusedId)
	Key $VK_SPACE 150
	$held = Position
	Start-Sleep -Milliseconds 700
	Check "Space on pause: position holds, focus handed back to play" (-not (Playing) -and (Position) -eq $held -and (Focused "PreviewPlay")) ("position {0}, focus {1}" -f $held, (FocusedId))
	Check "step readout shows frame x visualize every" ((StepText) -eq ("step {0}" -f ($held * 2))) (StepText)

	# ------------------------------------------------------------ speed
	FocusOn "PreviewSpeed"
	# the drop-down lists the presets in the user's locale ("0,25" where the decimal separator is a comma)
	$quarter = (0.25).ToString([System.Globalization.CultureInfo]::CurrentCulture)
	Key $VK_F4 200
	$item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $quarter)))
	Key $VK_ESCAPE 150
	Check "drop-down lists the presets in the current locale" ($item -ne $null -and $item.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) "looked for '$quarter'"
	$onePointFive = (1.5).ToString([System.Globalization.CultureInfo]::CurrentCulture)
	for ($i = 0; $i -lt 6; $i++) { Key $VK_BACK 30 }
	foreach ($ch in $onePointFive.ToCharArray()) { [Native]::Char($hwnd, $ch) }
	Start-Sleep -Milliseconds 200
	Check "typed 1.5 in the locale's spelling is accepted and shown as typed" ((SpeedText) -eq $onePointFive) ("typed '{0}', shows '{1}'" -f $onePointFive, (SpeedText))
	for ($i = 0; $i -lt 6; $i++) { Key $VK_BACK 30 }
	[Native]::Char($hwnd, [char]"4"); Start-Sleep -Milliseconds 200
	Check "typed speed 4 accepted" ((SpeedText) -eq "4") (SpeedText)
	Key $VK_SPACE 150
	Check "Space in the speed box neither plays nor types" (-not (Playing) -and (SpeedText) -eq "4") (SpeedText)
	$before = Position
	FocusOn "PreviewPlay"
	Key $VK_SPACE 2000
	Key $VK_SPACE 150   # focus was handed to pause
	$delta = (Position) - $before
	Check "speed 4: ~480 frames in 2 s" ($delta -ge 400 -and $delta -le 560) "delta $delta"

	# ------------------------------------------------------------ timeline keys
	FocusOn "PreviewTimeline"
	Key $VK_END 150
	Check "timeline End goes to the last frame" ((Frame) -eq "1249 / 1249") (Frame)
	Check "step readout follows (last frame x visualize every)" ((StepText) -eq "step 2498") (StepText)
	Key $VK_HOME 100
	Key $VK_RIGHT; Key $VK_RIGHT 150
	Check "timeline Home then Right twice gives frame 2" ((Position) -eq 2) (Frame)
	Key $VK_PRIOR 150
	Check "timeline PageUp steps 10 frames" ((Position) -eq 12) (Frame)
	Key $VK_NEXT 150
	Check "timeline PageDown steps back 10 frames" ((Position) -eq 2) (Frame)
	Key $VK_SPACE 1000
	$playing = Playing
	Key $VK_SPACE 150
	Check "Space on the timeline starts playback, Space again pauses" ($playing -and -not (Playing)) (Frame)

	# ------------------------------------------------------------ frame steps and in/out range (gestures work from any transport control)
	FocusOn "PreviewPlay"
	Key $VK_HOME 100
	Key $VK_PERIOD; Key $VK_PERIOD; Key $VK_PERIOD 150
	Check "period steps forward three frames from play button" ((Position) -eq 3) (Frame)
	Key $VK_COMMA 150
	Check "comma steps back one frame" ((Position) -eq 2) (Frame)
	Key $VK_PRIOR 100   # no effect here: PageUp is only the timeline's key
	FocusOn "PreviewTimeline"
	Key $VK_HOME 100; Key $VK_PRIOR 150   # frame 10
	Key $VK_I 150
	Check "I sets the in point at frame 10 (clear range becomes enabled)" ((Find "PreviewClearRange").Current.IsEnabled) (Frame)
	Key $VK_END 150
	for ($i = 0; $i -lt 5; $i++) { Key $VK_LEFT 60 }
	Start-Sleep -Milliseconds 200
	Key $VK_O 150
	Check "O sets the out point at frame 1244" ((Position) -eq 1244) (Frame)
	Key $VK_HOME 150
	Check "Home now jumps to the in point" ((Position) -eq 10) (Frame)
	Key $VK_END 150
	Check "End now jumps to the out point" ((Position) -eq 1244) (Frame)
	Key $VK_HOME 100
	Key $VK_L 150
	Check "L turns looping on" (LoopOn)
	Key $VK_SPACE 2500   # 2.5 s at speed 4 = ~600 frames through a range of 1234: still inside, and still playing
	$pos = Position
	$still = Playing
	Key $VK_SPACE 150
	Check "looped playback stays inside the range and keeps running" ($still -and $pos -ge 10 -and $pos -le 1244) ("position {0}, playing {1}" -f $pos, $still)
	Key $VK_L 150
	Check "L turns looping off" (-not (LoopOn))
	Key $VK_X 150
	Check "X clears the range (clear range disabled again)" (-not (Find "PreviewClearRange").Current.IsEnabled)
	Key $VK_END 150
	Check "End is the last frame again" ((Position) -eq 1249) (Frame)
	Key $VK_HOME 150

	# ------------------------------------------------------------ split
	FocusOn "PreviewSideBySide"
	Key $VK_RIGHT 200
	Check "Right arrow focuses and selects the stacked radio" ((Focused "PreviewStacked") -and (Selected "PreviewStacked") -and -not (Selected "PreviewSideBySide")) (FocusedId)
	# stacked: the intensity picture sits below the field picture, both centred on the same axis
	$fr = (Find "PreviewFieldImage").Current.BoundingRectangle; $ir = (Find "PreviewIntensityImage").Current.BoundingRectangle
	Check "stacked layout: intensity picture below the field picture, same left" ($ir.Top -gt $fr.Bottom -and [math]::Abs($ir.Left - $fr.Left) -lt 2) ("field {0},{1} intensity {2},{3}" -f $fr.Left, $fr.Top, $ir.Left, $ir.Top)
	$cell = (Find "PreviewSplitter").Current.BoundingRectangle
	Check "stacked layout: splitter is a horizontal bar between them" ($cell.Width -gt $cell.Height -and $cell.Top -ge $fr.Bottom -and $cell.Bottom -le $ir.Top) ("splitter {0}x{1} at {2},{3}" -f $cell.Width, $cell.Height, $cell.Left, $cell.Top)
	[Native]::Capture($hwnd, $Shot)
	Write-Output "screenshot: $Shot"
	# Tab must enter the group at the checked radio, which is now the second one (the intensity toggle precedes the group)
	FocusOn "PreviewShowIntensity"
	Key $VK_TAB 150
	Check "Tab enters the split group at the checked (stacked) radio" (Focused "PreviewStacked") (FocusedId)
	Key $VK_LEFT 200
	$fr = (Find "PreviewFieldImage").Current.BoundingRectangle; $ir = (Find "PreviewIntensityImage").Current.BoundingRectangle
	Check "Left arrow selects side by side again, pictures on one row, same top" ((Selected "PreviewSideBySide") -and (Focused "PreviewSideBySide") -and [math]::Abs($ir.Top - $fr.Top) -lt 2 -and $ir.Left -gt $fr.Right) ("field {0},{1} intensity {2},{3}" -f $fr.Left, $fr.Top, $ir.Left, $ir.Top)

	# ------------------------------------------------------------ splitter
	FocusOn "PreviewSplitter"
	$fw0 = (Find "PreviewFieldImage").Current.BoundingRectangle.Width
	Key $VK_RIGHT; Key $VK_RIGHT; Key $VK_RIGHT 200
	$fw1 = (Find "PreviewFieldImage").Current.BoundingRectangle.Width
	Check "Right arrow on the splitter widens the field picture" ($fw1 -gt $fw0 + 20) ("width {0} -> {1}" -f $fw0, $fw1)
	Key $VK_RETURN 200
	$fw2 = (Find "PreviewFieldImage").Current.BoundingRectangle.Width
	$iw2 = (Find "PreviewIntensityImage").Current.BoundingRectangle.Width
	Check "Enter on the splitter resets to half" ([math]::Abs($fw2 - $iw2) -lt 2 -and [math]::Abs($fw2 - $fw0) -lt 2) ("field {0} intensity {1}" -f $fw2, $iw2)

	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
	Write-Output ("{0} failure(s)" -f $script:failures)
}
exit $script:failures
