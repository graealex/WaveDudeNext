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

# Opens the preview's speed combo by keyboard (Tab to it, F4, Down) and saves a screenshot of the
# open list, for looking at list colours. Keystrokes are posted to the app window; the popup has
# no window handle of its own in UI Automation, so its screen rectangle (from the item bounds) is
# copied instead. The app never takes the foreground.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\ComboListShot.ps1 [-Exe <path>] [-Shot <png>]

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[string]$Shot = (Join-Path $env:TEMP "wavedude_combo_list.png")
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class N {
	[DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
	[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
	public static void Key(IntPtr h, int vk) { PostMessage(h, 0x100, (IntPtr)vk, (IntPtr)1); PostMessage(h, 0x101, (IntPtr)vk, (IntPtr)(1 | (1 << 30) | (1 << 31))); }
}
"@
$A = [System.Windows.Automation.AutomationElement]
$p = Start-Process -FilePath $Exe -PassThru
$win = $null
for ($i = 0; $i -lt 100 -and $win -eq $null; $i++) { Start-Sleep -Milliseconds 200; $win = $A::RootElement.FindFirst("Children", (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id))) }
$p.Refresh(); $hwnd = $p.MainWindowHandle; Start-Sleep -Milliseconds 800
$park = New-Object System.Windows.Forms.Form; $park.StartPosition = "Manual"; $park.Location = New-Object System.Drawing.Point(-3000, -3000); $park.Size = New-Object System.Drawing.Size(10, 10); $park.ShowInTaskbar = $false
$park.Show(); $park.Activate(); [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 400
for ($i = 0; $i -lt 3 -and [N]::GetForegroundWindow() -eq $hwnd; $i++) { [N]::ShowWindow($hwnd, 6) | Out-Null; Start-Sleep -Milliseconds 300; $park.Activate(); [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 300; [N]::ShowWindow($hwnd, 4) | Out-Null; Start-Sleep -Milliseconds 500 }
try {
	$speed = $win.FindFirst("Descendants", (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "PreviewSpeed")))
	for ($i = 0; $i -lt 60; $i++) {
		$c = New-Object System.Windows.Automation.PropertyCondition($A::HasKeyboardFocusProperty, $true)
		if ($speed.FindFirst("Descendants", $c) -ne $null) { break }
		[N]::Key($hwnd, 0x09); Start-Sleep -Milliseconds 150
	}
	[N]::Key($hwnd, 0x73); Start-Sleep -Milliseconds 500      # F4 opens the list
	[N]::Key($hwnd, 0x28); Start-Sleep -Milliseconds 300      # Down highlights an item
	$items = $speed.FindAll("Descendants", (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
	"list items: " + (($items | ForEach-Object { $n = $_.Current.Name; if ($_.Current.HasKeyboardFocus) { $n += "*" }; $n }) -join " ")
	$rects = $items | ForEach-Object { $_.Current.BoundingRectangle }
	$l = ($rects | Measure-Object -Property Left -Minimum).Minimum; $t = ($rects | Measure-Object -Property Top -Minimum).Minimum
	$r = ($rects | Measure-Object -Property Right -Maximum).Maximum; $b = ($rects | Measure-Object -Property Bottom -Maximum).Maximum
	$bmp = New-Object System.Drawing.Bitmap ([int]($r - $l + 8)), ([int]($b - $t + 8))
	$g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]($l - 4), [int]($t - 4), 0, 0, $bmp.Size); $g.Dispose()
	$bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
	"screenshot: $Shot"
	"foreground is app: " + ([N]::GetForegroundWindow() -eq $hwnd)
	[N]::Key($hwnd, 0x1B)
}
finally {
	$p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(5000)) { $p.Kill() }; $park.Close()
}
