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

# Drives the colour picker of the scheme editor's stop rows by keyboard (2026-10-15) on tiny_scheme.wdproj, whose
# project scheme "Harness scheme" has a middle stop #4080C0 (hue 210, saturation 2/3, value 0.753): selects the
# scheme in the tree, Tabs to the stop's swatch, Space opens the popup with the focus on the saturation/value
# square, ten Left arrows lower the saturation by a tenth (the hex box beside the swatch shows #538AC0 at once),
# Escape puts #4080C0 back and closes, a second opening Tabs to the red channel box, five Up arrows make it #4580C0
# and Enter keeps it; renaming the scheme in its Name box keeps the field picture's colours, since the layer follows
# the rename; the project is dirty afterwards. See Common.ps1 for the rules (no mouse, no activation, no
# UIA patterns). Prints PASS/FAIL per check and exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\ColorPickerCheck.ps1 [-Exe <path>] [-Shot <png>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[string]$Shot = (Join-Path $env:TEMP "wavedude_color_picker.png"),   # the window with the picker open, for the look
	[switch]$Trace
)

. (Join-Path $PSScriptRoot "Common.ps1")

# the popup is a top-level window of the process; its elements are found by id across those windows
function PopupFind([string]$id) {
	$byProcess = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)
	$c = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
	foreach ($top in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
		$e = $top.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
		if ($e -ne $null) { return $e }
	}
	return $null
}
# the top-level window (the popup) holding the element of this id
function PopupTop([string]$id) {
	$byProcess = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)
	$c = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
	foreach ($top in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
		if ($top.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c) -ne $null) { return $top }
	}
	return $null
}
function PopupFocusedId() {
	$byProcess = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)
	$focused = New-Object System.Windows.Automation.PropertyCondition($A::HasKeyboardFocusProperty, $true)
	foreach ($top in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
		$e = $top.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $focused)
		if ($e -ne $null) { return $e.Current.AutomationId }
	}
	return "(none)"
}
function Hex([string]$id) { return (Pattern (Find $id) "Value").Current.Value }
function Step([string]$what, [int]$vk, [int]$settle) {
	Key $vk $settle
	if ($Trace) { "    {0,-8} focus {1}  popup focus {2}  foreground {3}" -f $what, (FocusedId), (PopupFocusedId), (Foreground) }
}

Start-App $Exe (Join-Path $PSScriptRoot "tiny_scheme.wdproj")
try {
	# the stop ids carry the position in the locale's spelling ("0,5" where the decimal separator is a comma)
	$half = (0.5).ToString([System.Globalization.CultureInfo]::CurrentCulture)
	$swatch = "StopSwatch:$half"; $hexBox = "StopColor:$half"
	# the tree is one tab stop (its selected item); End goes to its last visible item, the scheme in its expanded folder
	FocusOn "Tree:Project"
	Step "End" $VK_END 300
	$null = WaitFor { try { (Find "SchemeAddStop") -ne $null } catch { $false } } 5000 100
	Check "End in the tree selects the scheme, whose editor appears" ((FocusedId) -eq "Tree:Harness scheme" -and (Find "SchemeAddStop") -ne $null) (FocusedId)
	# a rename of the scheme takes the field layer along: the picture keeps its colours (the default scheme would recolour it)
	$before = FieldHash
	FocusOn "SchemeName"   # the first stop after the tree
	Key $VK_END 100
	for ($i = 0; $i -lt 14; $i++) { Key $VK_BACK 30 }   # "Harness scheme"
	foreach ($ch in "Renamed".ToCharArray()) { [Native]::Char($hwnd, $ch) }
	Start-Sleep -Milliseconds 600
	# the tree row's id is bound once (the item, not its name), so the row is found by the old id and read by its text
	$row = (Find "Tree:Harness scheme").Current.Name
	Check "typing a new scheme name renames the tree row" ($row -eq "Renamed") $row
	Check "and the field picture keeps its colours: the layer followed the rename" ((FieldHash) -eq $before) ("hash {0} -> {1}" -f $before, (FieldHash))
	FocusOn $swatch
	Check "Tab reaches the middle stop's swatch, whose hex box says #4080C0" ((Hex $hexBox) -eq "#4080C0") (Hex $hexBox)

	Step "Space" $VK_SPACE 400
	Check "Space opens the picker with the focus on the saturation/value square" ((PopupFocusedId) -eq "ColorSquare") (PopupFocusedId)
	for ($i = 0; $i -lt 10; $i++) { Step "Left" $VK_LEFT 40 }
	Start-Sleep -Milliseconds 200
	Check "ten Left arrows lower the saturation by a tenth: the stop is #538AC0 at once" ((Hex $hexBox) -eq "#538AC0") (Hex $hexBox)
	[Native]::Capture($hwnd, $Shot)
	Write-Output "screenshot: $Shot"
	# the popup is its own window: captured apart, for the look of the picker
	$popupShot = [System.IO.Path]::ChangeExtension($Shot, $null) + "popup.png"
	[Native]::Capture([IntPtr](PopupTop "ColorSquare").Current.NativeWindowHandle, $popupShot)
	Write-Output "screenshot: $popupShot"
	Step "Escape" $VK_ESCAPE 400
	Check "Escape puts #4080C0 back, closes the popup and returns the focus to the swatch" ((Hex $hexBox) -eq "#4080C0" -and (PopupFind "ColorSquare") -eq $null -and (FocusedId) -eq $swatch) ("{0}, focus {1}" -f (Hex $hexBox), (FocusedId))

	Step "Space" $VK_SPACE 400
	Step "Tab" $VK_TAB 150
	Check "Tab moves to the hue strip" ((PopupFocusedId) -eq "ColorHue") (PopupFocusedId)
	Step "Tab" $VK_TAB 150
	Check "and on to the red channel box" ((PopupFocusedId) -eq "ColorRed") (PopupFocusedId)
	for ($i = 0; $i -lt 5; $i++) { Step "Up" $VK_UP 60 }
	Start-Sleep -Milliseconds 200
	Check "five Up arrows on the red box make the stop #4580C0" ((Hex $hexBox) -eq "#4580C0") (Hex $hexBox)
	Step "Enter" $VK_RETURN 400
	Check "Enter keeps #4580C0, closes the popup and returns the focus to the swatch" ((Hex $hexBox) -eq "#4580C0" -and (PopupFind "ColorSquare") -eq $null -and (FocusedId) -eq $swatch) ("{0}, focus {1}" -f (Hex $hexBox), (FocusedId))
	Check "the project is dirty" ($win.Current.Name -like "*`**") $win.Current.Name
	Check "app never became the foreground window" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
