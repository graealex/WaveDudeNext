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

# UI harness for the status bar log target: starts the Debug build with a good project file
# (expects the "Opened" line in the bar) and with a missing one (expects the error line on red
# behind the message box), and screenshots the second case.
param(
	[string]$Dir = (Resolve-Path (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows")).Path,
	[string]$Screenshot = (Join-Path $PSScriptRoot "..\smoke.png")
)

$sample = Join-Path $Dir "sample.wdproj"
@'
<?xml version="1.0" encoding="utf-8"?>
<Project Version="1" Seed="12345">
	<Source Name="Point" X="10" Y="20" />
	<Probe Name="Centre" AX="960" AY="540" />
</Project>
'@ | Set-Content -Path $sample -Encoding utf8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public struct RECT { public int L, T, R, B; }
public static class W {
	[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
	[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Status($win) {
	$c = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "txtStatus")
	$e = $win.FindFirst($TS::Descendants, $c)
	if ($e) { $e.Current.Name } else { "(status text not found)" }
}
function MainWindowOf($p) {
	$A::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))
}

# 0. plain start: the closing start-up line
$p = Start-Process -FilePath (Join-Path $Dir "WaveDudeNext.WPF.exe") -PassThru
Start-Sleep -Seconds 5
$win = MainWindowOf $p
"plain start -> status: " + (Status $win)
$null = $p.CloseMainWindow(); $p.WaitForExit(5000) | Out-Null

# 1. good file
$p = Start-Process -FilePath (Join-Path $Dir "WaveDudeNext.WPF.exe") -ArgumentList "`"$sample`"" -PassThru
Start-Sleep -Seconds 5
$win = MainWindowOf $p
"good file -> status: " + (Status $win)
$null = $p.CloseMainWindow(); $p.WaitForExit(5000) | Out-Null

# 2. missing file: the error dialog is up, the bar behind it shows the error
$p = Start-Process -FilePath (Join-Path $Dir "WaveDudeNext.WPF.exe") -ArgumentList "`"$Dir\does-not-exist.wdproj`"" -PassThru
Start-Sleep -Seconds 5
# UI Automation lists the owned message box under the main window, not under the desktop
$main = MainWindowOf $p
$dialog = $main.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
"missing file -> dialog: " + $(if ($dialog) { $dialog.Current.Name } else { "(none)" })
"missing file -> status: " + (Status $main)
$p.Refresh()
$h = [IntPtr]$main.Current.NativeWindowHandle
$r = New-Object RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Screenshot)
$g.Dispose(); $bmp.Dispose()
if ($dialog) {
	# the box's buttons are not exposed through this proxy; closing the window is the same as OK here
	$dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
	Start-Sleep -Seconds 1
	$p.Refresh()
}
$null = $p.CloseMainWindow()
if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force -Confirm:$false }
"exit: " + $p.ExitCode
