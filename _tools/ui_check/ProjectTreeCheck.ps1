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

# UI harness for the Project pane: drives add, move, duplicate, remove and drag-and-drop
# through UI Automation against the Debug build and prints the tree order after each step.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File ProjectTreeCheck.ps1
param(
	[string]$Dir = (Resolve-Path (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows")).Path,
	[string]$Screenshot = (Join-Path $PSScriptRoot "..\smoke.png")
)

$sample = Join-Path $Dir "sample.wdproj"
@'
<?xml version="1.0" encoding="utf-8"?>
<Project Version="1" Seed="12345">
	<Source Name="Array" X="400" Y="300" Phase="15" Mode="Pulsed">
		<Array SizeX="3" SpacingX="12" RandomFrequency="10" RandomPhase="20" RandomPosition="2" />
	</Source>
	<Source Name="Point" X="10" Y="20" />
	<Probe Name="Centre" AX="960" AY="540" />
	<Probe Name="Edge" Kind="Line" AX="0" AY="0" BX="100" BY="100" />
</Project>
'@ | Set-Content -Path $sample -Encoding utf8

$log = Join-Path $Dir "WaveDudeNext.WPF.log"
$before = (Get-Content $log -ErrorAction SilentlyContinue | Measure-Object -Line).Lines

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
	[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
	[DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
}
"@

$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$treeItemCond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::TreeItem)

function Items { ($win.FindAll($TS::Descendants, $treeItemCond) | ForEach-Object { $_.Current.Name }) -join ", " }
function Item($name) { $win.FindAll($TS::Descendants, $treeItemCond) | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }
function Btn($scope, $name) { $scope.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name))) }
function Invoke-Btn($b) { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1200 }
function Selected { ($win.FindAll($TS::Descendants, $treeItemCond) | Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } | ForEach-Object { $_.Current.Name }) -join "," }
function Drag($from, $to) {
	# press on the row centre, glide to 3 px below the top edge of the target row, release
	$sx = [int]($from.Left + 30); $sy = [int]($from.Top + $from.Height / 2)
	$tx = [int]($to.Left + 30);   $ty = [int]($to.Top + 3)
	[W]::SetCursorPos($sx, $sy) | Out-Null
	[W]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
	Start-Sleep -Milliseconds 150
	foreach ($i in 1..10) {
		[W]::SetCursorPos($sx + [int](($tx - $sx) * $i / 10), $sy + [int](($ty - $sy) * $i / 10)) | Out-Null
		Start-Sleep -Milliseconds 60
	}
	Start-Sleep -Milliseconds 300
	[W]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
	Start-Sleep -Seconds 1
}

$p = Start-Process -FilePath (Join-Path $Dir "WaveDudeNext.WPF.exe") -ArgumentList "`"$sample`"" -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
[W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
$win = $A::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))

"0 start:    " + (Items)

Invoke-Btn (Btn $win "Add a source (appended at the end)")
"1 add:      " + (Items)
$f = $A::FocusedElement
"  focused:  " + $f.Current.ControlType.ProgrammaticName + " value=" + $f.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value

Invoke-Btn (Btn (Item "Source 1") "Move up (Alt+Up); drag to reorder")
"2 move up:  " + (Items)

(Item "Source 1").SetFocus()
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait("^d")
Start-Sleep -Seconds 1
"3 Ctrl+D:   " + (Items)

Invoke-Btn (Btn (Item "Source 1 copy") ("Rem" + "ove (Del)"))
"4 minus:    " + (Items)
"  selected: " + (Selected)

Drag (Item "Edge").Current.BoundingRectangle (Item "Centre").Current.BoundingRectangle
"5 drag:     " + (Items)

(Item "Point").SetFocus()
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait("{DELETE}")
Start-Sleep -Seconds 1
"6 Del key:  " + (Items)
"  selected: " + (Selected)

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
"=== errors this run ==="
Get-Content $log | Select-Object -Skip $before | Where-Object { $_ -match "System.Windows.Data|`tError`t|`tWarn`t|`tFatal`t" } | ForEach-Object { ($_ -split "`t")[2..4] -join " | " } | Select-Object -First 10
