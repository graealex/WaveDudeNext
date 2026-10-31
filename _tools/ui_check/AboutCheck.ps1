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

# Opens Help > About by keyboard (F10, Left to the Help header, Down, Enter) and checks the dialog (2026-10-15):
# it is a child of the main window in UI Automation, shows the version with the build's commit hash and the copyright
# line, names the two contributors with their links, puts the focus on Close, cycles with Tab through links, the
# license toggle, the document and the buttons, shows the license document on Space, carries the system report, gives
# the height of a resize (SetWindowPos) to the document, and closes on Esc. Keys are posted to the dialog's own window handle. See Common.ps1 for the rules (no mouse, no activation,
# no UIA patterns). Prints PASS/FAIL per check and exits with the failure count.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\AboutCheck.ps1 [-Exe <path>] [-Shot <png>] [-Trace]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[string]$Shot = (Join-Path $env:TEMP "wavedude_about.png"),   # the dialog with the license text shown, for the look
	[switch]$Trace
)

. (Join-Path $PSScriptRoot "Common.ps1")

# the About dialog: UI Automation lists an owned window under its owner, so it is a child of the main window
# (a popup menu, by contrast, is a top-level window of the process); $null while it is not open
function Dialog() {
	$c = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "AboutWindow")
	$e = $script:win.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
	if ($e -ne $null) { return $e }
	$byProcess = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:p.Id)
	foreach ($top in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byProcess)) {
		if ($top.Current.AutomationId -eq "AboutWindow") { return $top }
	}
	return $null
}
function DFind([string]$id) {
	$c = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
	for ($i = 0; $i -lt 10; $i++) {
		$e = $script:dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
		if ($e -ne $null) { return $e }
		Start-Sleep -Milliseconds 200
	}
	throw "element '$id' not found in the dialog"
}
function DHas([string]$id) { try { (DFind $id) -ne $null } catch { $false } }
function DFocusedId() {
	$focused = New-Object System.Windows.Automation.PropertyCondition($A::HasKeyboardFocusProperty, $true)
	$e = $script:dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $focused)
	if ($e -eq $null) { return "(none)" }
	return $e.Current.AutomationId
}
function DKey([int]$vk, [int]$settle = 80) { [Native]::Key($script:dlgHwnd, $vk); Start-Sleep -Milliseconds $settle }
function DValue([string]$id) { return (Pattern (DFind $id) "Value").Current.Value }
function DText([string]$id) { return (Pattern (DFind $id) "Text").DocumentRange.GetText(-1) }
function Step([string]$what, [int]$vk, [int]$settle) {
	Key $vk $settle
	if ($Trace) { "    {0,-8} focus {1}  foreground {2}" -f $what, (FocusedId), (Foreground) }
}

Start-App $Exe
try {
	FocusOn "PreviewPlay"
	Step "F10" 0x79 150                 # the menu bar, File header
	Step "Left" $VK_LEFT 150            # wraps to the last header, Help
	Step "Down" 0x28 150                # opens Help on its first entry, About (Manual is below it)
	Step "Enter" $VK_RETURN 400         # About
	$script:dlg = $null
	$null = WaitFor { $script:dlg = Dialog; $script:dlg -ne $null } 5000 100
	Check "Help > About by keyboard opens the dialog" ($script:dlg -ne $null)
	if ($script:dlg -eq $null) { throw "no dialog" }
	$script:dlgHwnd = [IntPtr]$script:dlg.Current.NativeWindowHandle
	Check "the dialog is titled" ($script:dlg.Current.Name -eq "About WaveDude Next") $script:dlg.Current.Name
	# a window Windows never activated has no focused element; a posted WM_SETFOCUS gives WPF its keyboard focus
	if ((DFocusedId) -eq "(none)") { [Native]::SetFocusMessage($script:dlgHwnd); Start-Sleep -Milliseconds 200 }

	# ------------------------------------------------------------ content
	$expectedHash = (& git -C (Join-Path $PSScriptRoot "..\..") rev-parse --short=7 HEAD).Trim()
	$version = (DFind "AboutVersion").Current.Name
	Check "the version line reads 'Version x.y.z (hash)' with the commit of the build" ($version -match "^Version \d+\.\d+\.\d+ \([0-9a-f]{7}\)$" -and $version -like "*($expectedHash)*") ("'{0}', HEAD {1}" -f $version, $expectedHash)
	Check "the product line" ((DFind "AboutProduct").Current.Name -eq "WaveDude Next") (DFind "AboutProduct").Current.Name
	# the umlaut from its code point, so the script stays ASCII whatever PowerShell reads the file as (2026-10-31)
	Check "the copyright line" ((DFind "AboutCopyright").Current.Name -eq ("Copyright (C) 2026 Alexander Gr" + [char]0xE4 + "f")) (DFind "AboutCopyright").Current.Name
	$vleggaar = (DFind "AboutCreditVleggaar").Current.Name
	$author0x23 = (DFind "AboutCreditWaveSimulator2D").Current.Name
	Check "the credits name Jeroen with his copyright and 0x23, as the source header does" (($vleggaar -like "Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026*") -and ($author0x23 -like "0x23 (Diffraction Limited), author of WaveSimulator2D*")) ("'{0}' / '{1}'" -f $vleggaar, $author0x23)
	$links = @("AboutLinkHuygensYouTube", "AboutLinkHuygensSite", "AboutLinkDiffractionYouTube", "AboutLinkWaveSimulator2D", "AboutLinkGpl") | Where-Object { DHas $_ }
	Check "the five links exist" ($links.Count -eq 5) ("found " + $links.Count)
	$system = DValue "AboutSystemText"
	$lines = @($system -split "`r?`n")
	Check "the system report has its eight lines, naming the runtime and the OpenCL devices" ($lines.Count -eq 8 -and $system -like "*.NET*" -and $system -like "*OpenCL devices:*" -and $system -like "*H.264 encoders:*") ("{0} lines" -f $lines.Count)
	Check "the license text is hidden to begin with" (-not (DHas "AboutLicenseText"))

	# ------------------------------------------------------------ keyboard
	Check "the focus starts on Close" ((DFocusedId) -eq "AboutClose") (DFocusedId)
	$order = @()
	for ($i = 0; $i -lt 9; $i++) { DKey $VK_TAB 100; $order += (DFocusedId) }
	Check "Tab cycles links > license toggle > system text > Copy > Close" (($order -join ",") -eq "AboutLinkHuygensYouTube,AboutLinkHuygensSite,AboutLinkDiffractionYouTube,AboutLinkWaveSimulator2D,AboutLinkGpl,AboutLicenseToggle,AboutSystemText,AboutCopy,AboutClose") ($order -join ",")
	for ($i = 0; $i -lt 6; $i++) { DKey $VK_TAB 60 }   # back to the license toggle
	Check "Tab reaches the license toggle" ((DFocusedId) -eq "AboutLicenseToggle") (DFocusedId)
	DKey $VK_SPACE 300
	$shown = DHas "AboutLicenseText"
	$license = if ($shown) { DText "AboutLicenseText" } else { "" }
	Check "Space shows the license as a document: the GPL from LICENSE.md, its Markdown gone" ($shown -and $license -like "*GNU GENERAL PUBLIC LICENSE*" -and $license -like "*Version 3, 29 June 2007*" -and $license -notlike "*`n#*" -and $license -notlike "*<https://fsf.org/>*") ("{0} characters" -f $license.Length)
	# resizable: 200 px more height go to the document, the buttons stay at the bottom
	# (the document viewer's own rectangle is its document's extent, so the elements below the box are measured)
	$system0 = (DFind "AboutSystemText").Current.BoundingRectangle
	$close0 = (DFind "AboutClose").Current.BoundingRectangle
	$w = [Native]::WindowWidth($script:dlgHwnd); $h = [Native]::WindowHeight($script:dlgHwnd)
	[Native]::Resize($script:dlgHwnd, $w, $h + 200); Start-Sleep -Milliseconds 400
	$h1 = [Native]::WindowHeight($script:dlgHwnd)
	$system1 = (DFind "AboutSystemText").Current.BoundingRectangle
	$close1 = (DFind "AboutClose").Current.BoundingRectangle
	Check "a taller window gives the height to the license document: the system block and Close move down with it" ($h1 -eq $h + 200 -and [math]::Abs($system1.Top - $system0.Top - 200) -lt 4 -and [math]::Abs($close1.Top - $close0.Top - 200) -lt 4) ("window {0} -> {1}, system top {2} -> {3}, Close top {4} -> {5}" -f $h, $h1, $system0.Top, $system1.Top, $close0.Top, $close1.Top)
	[Native]::Capture($script:dlgHwnd, $Shot)
	Write-Output "screenshot: $Shot"
	Write-Output ("system report:`n" + $system)
	[Native]::Resize($script:dlgHwnd, $w, $h); Start-Sleep -Milliseconds 300
	DKey $VK_SPACE 400
	Check "Space again hides it, and the window shrinks back to its content" ((-not (DHas "AboutLicenseText")) -and [Native]::WindowHeight($script:dlgHwnd) -lt $h) ("window {0}, was {1} with the document" -f [Native]::WindowHeight($script:dlgHwnd), $h)
	DKey $VK_ESCAPE 300
	$gone = WaitFor { (Dialog) -eq $null } 3000 100
	Check "Esc closes the dialog" $gone
	Check "the main window is still there" ($win.Current.Name -like "WaveDude Next - *") $win.Current.Name
	Check "app never became the foreground window" ((Foreground) -ne "APP") (Foreground)
}
finally {
	Stop-App
}
Write-Output ("{0} failure(s)" -f $script:failures)
exit $script:failures
