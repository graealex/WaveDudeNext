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

# Drives the export (2026-10-14) on tiny.wdproj (64 x 48, 200 steps, a frame every 2 = 100 frames): File > Export >
# Export field... with no run yet (a run is started first, then 100 PNGs land in <temp>\wd_export\tiny_field), the
# title's asterisk after an edit, and Export both... from the picture's context menu (the Apps key on a focused
# handle) on the now stale run, which re-runs and writes both sequences; then tiny_probes.wdproj's field with the
# probe graph boxes drawn in (a frame is kept as %TEMP%\export_probes.png), the same as an MP4 and as JPEGs, and its
# probes' recordings as CSV from the graph box's and the tree's context menus (2026-10-18). The Save dialog cannot be driven by
# posted keys, so the app takes the path from WAVEDUDE_EXPORT_PATH ("{name}" = the name it would suggest).
# See Common.ps1 for the rules (no mouse, no activation, no UIA patterns). Exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\ExportCheck.ps1 [-Exe <path>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[switch]$Trace
)

. (Join-Path $PSScriptRoot "Common.ps1")

$VK_APPS = 0x5D
$out = Join-Path $env:TEMP "wd_export"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
$env:WAVEDUDE_EXPORT_PATH = Join-Path $out "{name}.png"

function MenuItemVisible([string]$name) {
	$c = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
	return ($script:win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c) -ne $null)
}
# a context menu's popup is a top-level window of the process, not a descendant of the main window
function PopupItemVisible([string]$name) {
	$byProcess = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)
	foreach ($top in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
		$c = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
		if ($top.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c) -ne $null) { return $true }
	}
	return $false
}
# the automation id of the focused element in any of the process's top-level windows (a popup's entries included)
function PopupFocusedId() {
	$byProcess = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)
	$focused = New-Object System.Windows.Automation.PropertyCondition($A::HasKeyboardFocusProperty, $true)
	foreach ($top in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
		$e = $top.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $focused)
		if ($e -ne $null) { return $e.Current.AutomationId }
	}
	return "(none)"
}
function Step([string]$what, [int]$vk, [int]$settle) {
	Key $vk $settle
	if ($Trace) { "    {0,-12} focus {1}  foreground {2}  export '{3}'" -f $what, (FocusedId), (Foreground), (ExportStatus) }
}
function ExportStatus() { return (Find "StatusExport").Current.Name }
function Pngs([string]$folder) { if (Test-Path $folder) { return @(Get-ChildItem $folder -Filter *.png).Count } else { return 0 } }
function IsPng([string]$path) { $b = [System.IO.File]::ReadAllBytes($path); return ($b.Length -gt 8 -and $b[1] -eq 0x50 -and $b[2] -eq 0x4E -and $b[3] -eq 0x47) }

Start-App $Exe (Join-Path $PSScriptRoot "tiny.wdproj")
try {
	Check "the title names the file, no asterisk" ($win.Current.Name -like "*tiny.wdproj") $win.Current.Name

	# File > Export > Export field...: F10 focuses the menu bar, Down opens File on New; New, Open, Recent, Examples (2026-10-29), Save, Save As, Export
	Step "F10" 0x79 150; Step "Down" $VK_DOWN 150
	for ($i = 0; $i -lt 6; $i++) { Step "Down" $VK_DOWN 80 }
	Step "Right" $VK_RIGHT 150
	Check "File > Export opens on 'Export field...'" (MenuItemVisible "Export intensity...")
	Step "Enter" $VK_RETURN 200
	$field = Join-Path $out "tiny_field"
	Check "with no run yet a run is made and 100 PNGs land in tiny_field" (WaitFor { (Pngs $field) -eq 100 } 60000) ("{0} files, status '{1}'" -f (Pngs $field), (ExportStatus))
	Check "the export ends and its status item clears" (WaitFor { (ExportStatus) -eq "" } 10000) (ExportStatus)
	$msg = (Find "StatusMessage").Current.Name
	Check "the status bar reports it" ($msg -like "Exported 100 frames to *tiny_field*") $msg
	Check "the files are tiny_field_0000.png to _0099.png and PNGs" ((Test-Path (Join-Path $field "tiny_field_0000.png")) -and (Test-Path (Join-Path $field "tiny_field_0099.png")) -and (IsPng (Join-Path $field "tiny_field_0050.png")))
	Check "the run made for the export stays in the preview (100 frames)" ((Frame) -like "* / 99") (Frame)
	Check "and is reported completed" ((RunStatus) -like "Completed*") (RunStatus)

	# File > Export last (Ctrl+E, 2026-10-28): the last export again, to the same folder, without the question about a folder with files in it
	$before = (Get-Item (Join-Path $field "tiny_field_0099.png")).LastWriteTimeUtc
	Step "F10" 0x79 150; Step "Down" $VK_DOWN 150
	for ($i = 0; $i -lt 7; $i++) { Step "Down" $VK_DOWN 80 }
	Check "'Export last' sits below Export in the File menu" ((PopupFocusedId) -eq "MenuExportLast") (PopupFocusedId)
	Step "Enter" $VK_RETURN 200
	Check "Export last rewrites the 100 PNGs without asking about the folder" (WaitFor { (Pngs $field) -eq 100 -and (Get-Item (Join-Path $field "tiny_field_0099.png")).LastWriteTimeUtc -gt $before } 60000) ("{0} files, status '{1}'" -f (Pngs $field), (ExportStatus))
	Check "and ends" (WaitFor { (ExportStatus) -eq "" } 10000) (ExportStatus)

	# an edit: the title gets its asterisk and the run goes stale
	FocusOn "Source:Source 1"
	Step "Right" $VK_RIGHT 300
	Check "an edit puts an asterisk after the file name" ($win.Current.Name -like "*tiny.wdproj*") $win.Current.Name
	Check "and marks the run stale" ((RunStatus) -like "*edited since*") (RunStatus)

	# Export both... from the picture's context menu, the Apps key on the focused handle: the stale run is redone first
	Remove-Item -Recurse -Force $field
	$null = WaitFor { (RunStatus) -notlike "Running*" } 10000   # the automatic re-run after the edit, if it started, must not be mid-way
	FocusOn "Source:Source 1"
	Step "Apps" $VK_APPS 300
	Check "the Apps key opens the picture's context menu with the export entries" (WaitFor { PopupItemVisible "Export both..." } 3000)
	# opened by keyboard, no entry is highlighted yet: Down walks Brightness, Auto brightness, This frame, Field, Intensity, Both
	for ($i = 0; $i -lt 8 -and (PopupFocusedId) -ne "ContextExportBoth"; $i++) { Step "Down" $VK_DOWN 120 }
	Check "Down reaches 'Export both...'" ((PopupFocusedId) -eq "ContextExportBoth") (PopupFocusedId)
	Step "Enter" $VK_RETURN 200
	$intensity = Join-Path $out "tiny_intensity"
	Check "both sequences arrive, 100 files each" (WaitFor { (Pngs $field) -eq 100 -and (Pngs $intensity) -eq 100 } 60000) ("{0} + {1} files, status '{2}'" -f (Pngs $field), (Pngs $intensity), (ExportStatus))
	Check "the export ends" (WaitFor { (ExportStatus) -eq "" } 10000) (ExportStatus)
	Check "the run is fresh again" ((WaitFor { (RunStatus) -like "Completed*" -and (RunStatus) -notlike "*edited since*" } 10000)) (RunStatus)
	Check "the intensity files are PNGs" (IsPng (Join-Path $intensity "tiny_intensity_0099.png"))

	# the still: the context menu's first entry writes the shown frame (frame 0, step 0) as one file, from the run
	FocusOn "Source:Source 1"
	Step "Apps" $VK_APPS 300
	for ($i = 0; $i -lt 4 -and (PopupFocusedId) -ne "ContextExportFrame"; $i++) { Step "Down" $VK_DOWN 120 }
	Check "'Export this frame...' is the context menu's first export entry, after the two brightness entries" ((PopupFocusedId) -eq "ContextExportFrame") (PopupFocusedId)
	Step "Enter" $VK_RETURN 200
	$still = Join-Path $out "tiny_field_0000.png"
	Check "the shown frame lands as tiny_field_0000.png (the frame number, padded like a sequence's files)" (WaitFor { Test-Path $still } 20000) ((Find "StatusMessage").Current.Name)
	Check "and is a PNG, with the status bar reporting one frame" ((IsPng $still) -and (WaitFor { (Find "StatusMessage").Current.Name -like "Exported 1 frames to *tiny_field_0000.png*" } 5000)) ((Find "StatusMessage").Current.Name)

	# File > Examples (2026-10-29): the shipped examples embedded in the app; New, Open, Recent, Examples; Right opens the list
	# on the first one; Enter replaces the project (the edits are discarded through WAVEDUDE_DISCARD_EDITS) with an unsaved one
	Step "F10" 0x79 150; Step "Down" $VK_DOWN 150
	for ($i = 0; $i -lt 3; $i++) { Step "Down" $VK_DOWN 80 }
	Check "'Examples' sits below Recent in the File menu" ((PopupFocusedId) -eq "MenuExamples") (PopupFocusedId)
	Step "Right" $VK_RIGHT 300
	Check "the list opens on the first example" ((PopupFocusedId) -eq "MenuExample01") (PopupFocusedId)
	Check "whose entry reads '1. Source Mirror Probe'" (PopupItemVisible "1. Source Mirror Probe")
	Step "Enter" $VK_RETURN 500
	Check "the example replaces the project: frames 0 to 1249, its file name in the title (2026-10-29)" ((WaitFor { (Frame) -like "* / 1249" } 5000) -and ($win.Current.Name -like "*Example01_Source_Mirror_Probe.wdproj")) ("{0}; title '{1}'" -f (Frame), $win.Current.Name)
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}

# tiny_probes.wdproj: the probe graph boxes are drawn into the frames (the painter renders on the export thread),
# first as PNGs (one frame is kept as %TEMP%\export_probes.png for a look), then the same as an MP4 through
# Media Foundation, whose writer must live on the export thread (it refused the UI thread's, 2026-10-14)
Start-App $Exe (Join-Path $PSScriptRoot "tiny_probes.wdproj")
try {
	Step "F10" 0x79 150; Step "Down" $VK_DOWN 150
	for ($i = 0; $i -lt 6; $i++) { Step "Down" $VK_DOWN 80 }
	Step "Right" $VK_RIGHT 150; Step "Enter" $VK_RETURN 200
	$probes = Join-Path $out "tiny_probes_field"
	Check "the probes project exports its field with the graph boxes drawn" (WaitFor { (Find "StatusMessage").Current.Name -like "Exported * frames to *tiny_probes_field*" } 60000) ((Find "StatusMessage").Current.Name)
	$last = Get-ChildItem $probes -Filter *.png | Sort-Object Name | Select-Object -Last 1
	Check "the last frame is a PNG" ($last -ne $null -and (IsPng $last.FullName)) $last.Name
	if ($last -ne $null) { Copy-Item $last.FullName (Join-Path $env:TEMP "export_probes.png") -Force }
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
$env:WAVEDUDE_EXPORT_PATH = Join-Path $out "{name}.mp4"
Start-App $Exe (Join-Path $PSScriptRoot "tiny_probes.wdproj")
try {
	Step "F10" 0x79 150; Step "Down" $VK_DOWN 150
	for ($i = 0; $i -lt 6; $i++) { Step "Down" $VK_DOWN 80 }
	Step "Right" $VK_RIGHT 150; Step "Enter" $VK_RETURN 200
	$mp4 = Join-Path $out "tiny_probes_field.mp4"
	Check "the same export as an MP4 completes" (WaitFor { (Find "StatusMessage").Current.Name -like "Exported 50 frames to *tiny_probes_field.mp4*" } 60000) ((Find "StatusMessage").Current.Name)
	Check "the MP4 file is there and not empty" ((Test-Path $mp4) -and (Get-Item $mp4).Length -gt 1000) $(if (Test-Path $mp4) { (Get-Item $mp4).Length } else { "missing" })
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
# a JPEG sequence through the app: the WIC codec with workers in parallel (2026-10-15). The PNG section filled the
# same sequence folder, and a folder with files in it makes the app ask "replace?" (a message box the harness cannot
# answer; it stalled there once), so it goes first
Remove-Item -Recurse -Force (Join-Path $out "tiny_probes_field") -ErrorAction SilentlyContinue
$env:WAVEDUDE_EXPORT_PATH = Join-Path $out "{name}.jpg"
Start-App $Exe (Join-Path $PSScriptRoot "tiny_probes.wdproj")
try {
	Step "F10" 0x79 150; Step "Down" $VK_DOWN 150
	for ($i = 0; $i -lt 6; $i++) { Step "Down" $VK_DOWN 80 }
	Step "Right" $VK_RIGHT 150; Step "Enter" $VK_RETURN 200
	$jpgs = Join-Path $out "tiny_probes_field"
	Check "the same export as a JPEG sequence completes" (WaitFor { (Find "StatusMessage").Current.Name -like "Exported 50 frames to *tiny_probes_field*" } 60000) ((Find "StatusMessage").Current.Name)
	$files = @(Get-ChildItem $jpgs -Filter *.jpg)
	$b = if ($files.Count -gt 0) { [System.IO.File]::ReadAllBytes((Join-Path $jpgs "tiny_probes_field_0049.jpg")) } else { @() }
	Check "50 JPEG files, the last one starting with the JPEG marker" ($files.Count -eq 50 -and $b.Length -gt 4 -and $b[0] -eq 0xFF -and $b[1] -eq 0xD8) ("{0} files" -f $files.Count)
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
	Remove-Item Env:\WAVEDUDE_EXPORT_PATH -ErrorAction SilentlyContinue
}
# a probe's recording as CSV (2026-10-18), from the graph box's context menu (the Apps key on the focused box) and from
# the tree's, where the entry is the last one and only a probe node shows it; the run is made first (a fresh app has
# none). A point probe gives step;amplitude with one row per step, a line probe frame;step;<distances> with one row per frame
$env:WAVEDUDE_EXPORT_PATH = Join-Path $out "{name}.csv"
Start-App $Exe (Join-Path $PSScriptRoot "tiny_probes.wdproj")
try {
	FocusOn "ProbeBox:Probe 1"
	Step "Apps" $VK_APPS 300
	Check "the Apps key on a graph box opens its context menu with the CSV entry" (WaitFor { PopupItemVisible "Export probe data as CSV..." } 3000)
	for ($i = 0; $i -lt 2 -and (PopupFocusedId) -ne "ContextExportProbeCsv"; $i++) { Step "Down" $VK_DOWN 120 }
	Check "Down reaches the CSV entry" ((PopupFocusedId) -eq "ContextExportProbeCsv") (PopupFocusedId)
	Step "Enter" $VK_RETURN 200
	$csv1 = Join-Path $out "tiny_probes_Probe 1.csv"
	Check "the point probe's CSV lands once the run was made" (WaitFor { (Find "StatusMessage").Current.Name -like "Probe data written: 100 rows of 'Probe 1' to *" } 60000) ((Find "StatusMessage").Current.Name)
	$lines = if (Test-Path $csv1) { @([System.IO.File]::ReadAllLines($csv1)) } else { @() }
	Check "step;amplitude header, 100 data rows, semicolons and a decimal point" ($lines.Count -eq 101 -and $lines[0] -eq "step;amplitude" -and $lines[1] -match "^0;-?[0-9]+(\.[0-9]+)?(E[-+]?[0-9]+)?$" -and $lines[100] -like "99;*") ("{0} lines: {1} | {2}" -f $lines.Count, $lines[0], $lines[1])
	# the line probe from the tree's context menu
	FocusOn "Tree:Probe 2"
	Step "Apps" $VK_APPS 300
	for ($i = 0; $i -lt 12 -and (PopupFocusedId) -ne "ContextExportProbeCsv"; $i++) { Step "Down" $VK_DOWN 120 }
	Check "the tree's context menu reaches the CSV entry on a probe node" ((PopupFocusedId) -eq "ContextExportProbeCsv") (PopupFocusedId)
	Step "Enter" $VK_RETURN 200
	$csv2 = Join-Path $out "tiny_probes_Probe 2.csv"
	Check "the line probe's CSV lands, one row per frame" (WaitFor { (Find "StatusMessage").Current.Name -like "Probe data written: 50 rows of 'Probe 2' to *" } 60000) ((Find "StatusMessage").Current.Name)
	$lines = if (Test-Path $csv2) { @([System.IO.File]::ReadAllLines($csv2)) } else { @() }
	Check "frame;step;<distances> header, 50 data rows, the last for frame 49 at step 98" ($lines.Count -eq 51 -and $lines[0] -like "frame;step;0;*" -and $lines[50] -like "49;98;*") ("{0} lines: {1}" -f $lines.Count, ($lines[0] -replace "^(.{40}).*", '$1...'))
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
	Remove-Item Env:\WAVEDUDE_EXPORT_PATH -ErrorAction SilentlyContinue
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
