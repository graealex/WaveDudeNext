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

# The keyboard gaps closed on 2026-10-15: the toolbars of the Project and Messages panes are in the Tab cycle and the
# dock pane titles are visible to UI Automation. On the default project: Tab walks the Project toolbar (the disabled
# buttons skipped) into the tree ("+ Part" between the two since 2026-10-18), Space on "+ Source" adds Source 2 and Space on the remove button takes it away, the
# Messages pane is opened through View > Panes by keyboard, Tab walks its toolbar left to right, Space flips a level
# and clears the list, and both pane titles read through their ids. See Common.ps1 for the rules (no mouse, no
# activation, no UIA patterns). Prints PASS/FAIL per check and exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\KeyboardCheck.ps1 [-Exe <path>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[switch]$Trace
)

. (Join-Path $PSScriptRoot "Common.ps1")

function Has([string]$id) { try { (Find $id) -ne $null } catch { $false } }
function ListCount() {
	$c = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)   # a ListView's rows
	return (Find "MessagesList").FindAll([System.Windows.Automation.TreeScope]::Children, $c).Count
}
function Step([string]$what, [int]$vk, [int]$settle) {
	Key $vk $settle
	if ($Trace) { "    {0,-8} focus {1}  foreground {2}" -f $what, (FocusedId), (Foreground) }
}

Start-App $Exe
try {
	Check "the Project pane's title reads through its id" ((Find "DockTitle:project").Current.Name -eq "Project") (Find "DockTitle:project").Current.Name

	# ------------------------------------------------------------ the Project toolbar (the default project has no sources)
	FocusOn "ToolAddSource"
	$order = @(); for ($i = 0; $i -lt 3; $i++) { Key $VK_TAB 120; $order += (FocusedId) }
	Check "Tab walks + Source > + Part > + Probe > the tree (move and remove are disabled with nothing selected and skipped)" (($order -join ",") -eq "ToolAddPart,ToolAddProbe,tree") ($order -join ",")
	FocusOn "ToolAddSource"
	Step "Space" $VK_SPACE 400
	$null = WaitFor { Has "Tree:Source 1" } 3000 100
	Check "Space on + Source adds Source 1, selected in the tree, the focus on its first editor" ((Has "Tree:Source 1") -and (TreeItemSelected "Source 1")) (FocusedId)
	FocusOn "ToolAddSource"   # round through the preview pane and back
	Step "Space" $VK_SPACE 400
	$null = WaitFor { Has "Tree:Source 2" } 3000 100
	Check "a second Space adds Source 2" ((Has "Tree:Source 2") -and (TreeItemSelected "Source 2"))
	# the walk to the toolbar passes the preview's source handles, and focusing a handle selects its source: the toolbar
	# acts on whatever is selected when it is reached, so that is read first and the action checked against it
	FocusOn "ToolAddProbe"
	Key $VK_TAB 150
	$move = FocusedId
	$sel = if (TreeItemSelected "Source 2") { "Source 2" } else { "Source 1" }
	$other = if ($sel -eq "Source 2") { "Source 1" } else { "Source 2" }
	$selBelow = (Find "Tree:$sel").Current.BoundingRectangle.Top -gt (Find "Tree:$other").Current.BoundingRectangle.Top
	Check "after + Probe, Tab reaches the one move button that applies: up for the lower item, down for the upper (the other is disabled and skipped)" (($move -eq "ToolMoveUp") -eq $selBelow -and ($move -eq "ToolMoveDown") -eq (-not $selBelow)) ("{0}, {1} selected, below: {2}" -f $move, $sel, $selBelow)
	Step "Space" $VK_SPACE 300
	$selBelowNow = (Find "Tree:$sel").Current.BoundingRectangle.Top -gt (Find "Tree:$other").Current.BoundingRectangle.Top
	Check "Space on it swaps the two sources, the selection kept" ($selBelowNow -ne $selBelow -and (TreeItemSelected $sel)) ("{0} below: {1} -> {2}" -f $sel, $selBelow, $selBelowNow)
	FocusOn "ToolRemove"
	$victim = if (TreeItemSelected "Source 2") { "Source 2" } else { "Source 1" }
	$kept = if ($victim -eq "Source 2") { "Source 1" } else { "Source 2" }
	Step "Space" $VK_SPACE 400
	Check "Space on the remove button takes the selected source away and selects the other" ((-not (Has "Tree:$victim")) -and (Has "Tree:$kept") -and (TreeItemSelected $kept)) ("removed {0}, focus {1}" -f $victim, (FocusedId))

	# ------------------------------------------------------------ the Messages pane, toggled through View > Panes
	# the dock layout is a persisted user setting, so the pane may be open from the last run: the menu entry toggles it,
	# the toggle is checked either way, and the pane is opened for the rest when the toggle closed it
	function ToggleMessagesPane() {
		FocusOn "PreviewPlay"
		Step "F10" 0x79 150                 # the menu bar, File header
		Step "Down" $VK_DOWN 150            # opens File
		Step "Right" $VK_RIGHT 150          # moves to View and opens it on its first entry (Panes)
		Step "Right" $VK_RIGHT 150          # opens the Panes submenu on its first entry
		for ($i = 0; $i -lt 5 -and (FocusedId) -notlike "*'Messages')"; $i++) { Step "Down" $VK_DOWN 120 }
		$listed = (FocusedId) -like "*'Messages')"
		Step "Enter" $VK_RETURN 500
		Start-Sleep -Milliseconds 600   # the dock host rebuilds its tree when a pane opens or closes
		return $listed
	}
	$openBefore = Has "MessagesSearch"
	$listed = ToggleMessagesPane
	Check "View > Panes lists Messages" $listed (FocusedId)
	$openAfter = Has "MessagesSearch"
	Check ("Enter toggles the Messages pane (it was {0} to begin with)" -f $(if ($openBefore) { "open" } else { "closed" })) ($openAfter -ne $openBefore)
	if (-not $openAfter) { $null = ToggleMessagesPane; $null = WaitFor { Has "MessagesSearch" } 5000 100; Start-Sleep -Milliseconds 800 }
	Check "the Messages pane is open, titled" ((Has "MessagesSearch") -and ((Find "DockTitle:messages").Current.Name -eq "Messages"))
	FocusOn "MessagesLevel:Trace"
	$order = @(); for ($i = 0; $i -lt 9; $i++) { Key $VK_TAB 150; $order += (FocusedId) }
	Check "Tab walks the levels, then search, regex, auto-scroll and clear, left to right" (($order -join ",") -eq "MessagesLevel:Debug,MessagesLevel:Info,MessagesLevel:Warn,MessagesLevel:Error,MessagesLevel:Fatal,MessagesSearch,MessagesRegex,MessagesAutoScroll,MessagesClear") ($order -join ",")
	FocusOn "MessagesLevel:Info"
	$was = ToggleOn "MessagesLevel:Info"
	Step "Space" $VK_SPACE 200
	Check "Space flips a level toggle" ((ToggleOn "MessagesLevel:Info") -ne $was)
	Step "Space" $VK_SPACE 200
	$before = ListCount
	FocusOn "MessagesClear"
	Step "Space" $VK_SPACE 400
	Check "Space on the clear button empties the list" ($before -gt 0 -and (ListCount) -eq 0) ("{0} -> {1} entries" -f $before, (ListCount))
	Check "app never became the foreground window" ((Foreground) -ne "APP") (Foreground)
	# leave the layout as it was found
	if (-not $openBefore) { $null = ToggleMessagesPane }
}
finally {
	Stop-App
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
