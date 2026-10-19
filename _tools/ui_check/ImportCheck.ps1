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

# Opens one of the author's projects (Wave_Dude_Examples/Example12_Dispersion/configuration.json) through the
# command line and checks the import landed in the app (2026-10-14): the title shows no file (the project is
# unsaved), the tree lists the four sources and two probes, the preview's canvas is the embedded image's
# 1920 x 1080, and the status bar carries the import message. See Common.ps1 for the rules (no mouse, no
# activation, no UIA patterns). Prints PASS/FAIL per check and exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\ImportCheck.ps1 [-Exe <path>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[switch]$Trace
)

. (Join-Path $PSScriptRoot "Common.ps1")

Start-App $Exe (Join-Path $PSScriptRoot "..\..\Wave_Dude_Examples\Example12_Dispersion\configuration.json")
try {
	Check "the imported project is unsaved: the title is the bare application name" ($win.Current.Name -eq "WaveDude Next") $win.Current.Name
	$sources = @("Source 1", "Source 2", "Source 3", "Source 4") | Where-Object { try { (Find ("Tree:" + $_)) -ne $null } catch { $false } }
	Check "the tree lists the four sources" ($sources.Count -eq 4) ("found " + $sources.Count)
	$probes = @("Probe 1", "Probe 2") | Where-Object { try { (Find ("Tree:" + $_)) -ne $null } catch { $false } }
	Check "and the two probes" ($probes.Count -eq 2) ("found " + $probes.Count)
	Check "the probes came in inactive" (-not (ToggleOn "Enabled:Probe 1") -and -not (ToggleOn "Enabled:Probe 2"))
	Check "the sources came in enabled" ((ToggleOn "Enabled:Source 1") -and (ToggleOn "Enabled:Source 4"))
	# 15000 steps every 2: 7500 frames
	Check "the timeline spans the imported run length (7500 frames)" ((Frame) -eq "0 / 7499") (Frame)
	# the canvas is the embedded 1920 x 1080 image: the field picture keeps a 16:9 aspect
	$r = (Find "PreviewFieldImage").Current.BoundingRectangle
	Check "the preview shows the embedded image's 16:9 canvas" ([math]::Abs($r.Width / $r.Height - 16 / 9) -lt 0.02) ("{0:0}x{1:0}" -f $r.Width, $r.Height)
	# the source marker sits where the file says: Source 1 at (150, 140) of 1920 x 1080
	$expected = CanvasPoint "PreviewFieldImage" 1920 1080 150 140
	$centre = CentreOf "Source:Source 1"
	Check "Source 1's marker sits at (150, 140) of the canvas" ([math]::Abs($centre[0] - $expected[0]) -lt 3 -and [math]::Abs($centre[1] - $expected[1]) -lt 3) ("marker {0:0},{1:0} expected {2:0},{3:0}" -f $centre[0], $centre[1], $expected[0], $expected[1])
	$status = (Find "StatusMessage").Current.Name
	Check "the status bar reports the import" ($status -like "*Imported the legacy project 'Example12_Dispersion'*") $status
	Check "app never became the foreground window" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
