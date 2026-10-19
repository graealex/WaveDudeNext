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

# UI harness for the Messages pane: shows the pane through View > Panes, checks that start-up
# lines are listed, that the level toggles carry counts, that the search box filters, and
# screenshots the result.
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
function ByName($scope, $name) { $scope.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name))) }
function ByType($scope, $type) { $scope.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type))) }
function Expand($e) { $e.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 600 }

$p = Start-Process -FilePath (Join-Path $Dir "WaveDudeNext.WPF.exe") -ArgumentList "`"$sample`"" -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
[W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
$win = $A::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))

# View > Panes > Messages, unless the saved layout already shows the pane (its search box exists)
if (ByName $win "Search") {
	"pane already shown"
} else {
	Expand (ByName $win "View")
	Expand (ByName $win "Panes")
	(ByName $win "Messages").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
	Start-Sleep -Seconds 2
}

function Rows { ByType $win ([System.Windows.Automation.ControlType]::DataItem) | Where-Object { $_.Current.Name -like "LogEntry*" } }
$rows = @(Rows)
"rows shown: " + $rows.Count
"toggles:    " + ((ByType $win ([System.Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -match '^\w+ \(\d+\)$' } | ForEach-Object { $_.Current.Name }) -join ", ")

$search = ByName $win "Search"
$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("Opened")
Start-Sleep -Seconds 1
$rows = @(Rows)
"rows for 'Opened': " + $rows.Count + " -> " + (($rows | ForEach-Object { ($_.Current.Name -split "Message = ")[1] -split ", Exception" | Select-Object -First 1 }) -join " | ")

$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("")
Start-Sleep -Seconds 1
$r = New-Object RECT
[W]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Screenshot)
$g.Dispose(); $bmp.Dispose()

$null = $p.CloseMainWindow()
if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force -Confirm:$false }
"exit: " + $p.ExitCode
