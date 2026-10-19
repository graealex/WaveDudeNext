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

# Drives the group selection (2026-10-19) on tiny.wdproj by keyboard alone: a second source is added, "Select all"
# from a tree row's context menu (the Apps key) puts both into the selection (the rows carry the item status
# "InSelection", the last one is the current item), Insert takes the current item out again, a Tab to a source
# handle on the preview keeps the selection, the arrow keys move both sources by the same amount, and Delete on
# the tree with both selected removes both. Ctrl, Shift and the mouse marquee cannot be posted and stay untested.
# Usage:
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\SelectionCheck.ps1 [-Exe <path>] [-Trace]
param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[switch]$Trace
)
. (Join-Path $PSScriptRoot "Common.ps1")
$VK_APPS = 0x5D; $VK_INSERT = 0x2D; $VK_DELETE = 0x2E
function Has([string]$id) { try { (Find $id) -ne $null } catch { $false } }
function Status([string]$id) { return (Find $id).Current.ItemStatus }
function PopupFocusedId() {
	$byProcess = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)
	$focused = New-Object System.Windows.Automation.PropertyCondition($A::HasKeyboardFocusProperty, $true)
	foreach ($top in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
		$e = $top.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $focused)
		if ($e -ne $null) { return $e.Current.AutomationId }
	}
	return "(none)"
}
# Tab until a tree row holds focus: the walk from the preview's handles back to the tree passes every transport
# control and the messages pane, more than the shared helper's 80 stops, and the tree lands on the row it focused
# last, whichever that is
function FocusOnTree() {
	for ($i = 0; $i -lt 250; $i++) {
		if ((FocusedId) -like "Tree:*") { return }
		Key $VK_TAB 30
	}
	throw "could not reach the tree with Tab; focus is at $(FocusedId)"
}
function Step([string]$what, [int]$vk, [int]$settle) {
	Key $vk $settle
	if ($Trace) { "    {0,-8} focus {1}  foreground {2}" -f $what, (FocusedId), (Foreground) }
}
# opens the focused row's context menu and picks the entry with the given id; whether it was reached lands in
# $script:menuReached (a return value would carry the trace lines along)
function PickFromRowMenu([string]$id) {
	Step "Apps" $VK_APPS 300
	for ($i = 0; $i -lt 12 -and (PopupFocusedId) -ne $id; $i++) { Step "Down" $VK_DOWN 120 }
	$script:menuReached = (PopupFocusedId) -eq $id
	Step "Enter" $VK_RETURN 300
}

Start-App $Exe (Join-Path $PSScriptRoot "tiny.wdproj")
try {
	# a second source, so there is a group to select
	FocusOn "ToolAddSource"
	Step "Space" $VK_SPACE 400
	$null = WaitFor { Has "Tree:Source 2" } 3000 100
	Check "Space on + Source adds Source 2" (Has "Tree:Source 2") (FocusedId)
	Check "a plain selection: the new Source 2 is the selection, Source 1 is not in it" ((Status "Tree:Source 2") -eq "InSelection" -and (Status "Tree:Source 1") -ne "InSelection") ("{0} / {1}" -f (Status "Tree:Source 1"), (Status "Tree:Source 2"))

	# Select all from a row's context menu (Tab into the tree lands on the current row, Source 2)
	FocusOn "Tree:Source 2"
	PickFromRowMenu "ContextSelectAll"
	Check "the row's context menu reaches Select all" $script:menuReached (FocusedId)
	Check "Select all puts both sources into the selection; the last one is the current item" ((Status "Tree:Source 1") -eq "InSelection" -and (Status "Tree:Source 2") -eq "InSelection" -and (TreeItemSelected "Source 2")) ("{0} / {1}, current 2: {2}" -f (Status "Tree:Source 1"), (Status "Tree:Source 2"), (TreeItemSelected "Source 2"))

	# Insert takes the current item out; the current item moves to the one left
	Step "Insert" $VK_INSERT 300
	Check "Insert takes the current item (Source 2) out of the selection; Source 1 is current and the only one left" ((Status "Tree:Source 2") -ne "InSelection" -and (Status "Tree:Source 1") -eq "InSelection" -and (TreeItemSelected "Source 1")) ("{0} / {1}" -f (Status "Tree:Source 1"), (Status "Tree:Source 2"))

	# both again, then Tab on to the preview: the handle of a selected item keeps the group
	PickFromRowMenu "ContextSelectAll"
	FocusOn "Source:Source 1"
	Check "Tab to the handle of Source 1 keeps both selected" ((Status "Tree:Source 1") -eq "InSelection" -and (Status "Tree:Source 2") -eq "InSelection" -and (Focused "Source:Source 1")) ("{0} / {1}, focus {2}" -f (Status "Tree:Source 1"), (Status "Tree:Source 2"), (FocusedId))
	$before1 = CentreOf "Source:Source 1"
	$before2 = CentreOf "Source:Source 2"
	for ($i = 0; $i -lt 3; $i++) { Step "Right" $VK_RIGHT 150 }
	Start-Sleep -Milliseconds 300
	$after1 = CentreOf "Source:Source 1"
	$after2 = CentreOf "Source:Source 2"
	$dx1 = $after1[0] - $before1[0]; $dx2 = $after2[0] - $before2[0]
	Check "Right three times moves both source handles right by the same amount (3 canvas pixels)" ($dx1 -gt 4 -and [math]::Abs($dx1 - $dx2) -lt 2 -and [math]::Abs($after1[1] - $before1[1]) -lt 1) ("dx {0:0.0} / {1:0.0}" -f $dx1, $dx2)
	for ($i = 0; $i -lt 3; $i++) { Step "Left" $VK_LEFT 150 }
	Start-Sleep -Milliseconds 300
	$back1 = CentreOf "Source:Source 1"
	$back2 = CentreOf "Source:Source 2"
	Check "Left three times brings both back" ([math]::Abs($back1[0] - $before1[0]) -lt 1 -and [math]::Abs($back2[0] - $before2[0]) -lt 1) ("{0:0.0} vs {1:0.0}, {2:0.0} vs {3:0.0}" -f $back1[0], $before1[0], $back2[0], $before2[0])

	# Delete on the tree with both selected removes both: back into the tree (its row takes a plain selection), Select all, Delete
	FocusOnTree
	PickFromRowMenu "ContextSelectAll"
	Step "Delete" $VK_DELETE 400
	Check "Delete with both selected removes both sources" (-not (Has "Tree:Source 1") -and -not (Has "Tree:Source 2")) ("1: {0}, 2: {1}" -f (Has "Tree:Source 1"), (Has "Tree:Source 2"))
	Check "the app never took the foreground" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
