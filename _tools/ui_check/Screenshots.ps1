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

# Makes the manual's figures: screenshots of the app, keyboard-driven on Common.ps1, each written as an SVG under
# Assets\ with the PNG inlined as a data URI and numbered badges as vector circles, plus Assets\Legend.md naming
# what each number marks. The badges sit at UI Automation rectangles, so a run after a UI change moves them along.
# Two sessions: the example project for the window, the menus, the tree and the preview (a scratch frame at -Frame
# puts waves into the pictures), then figures.wdproj (one object of every kind) with a short tree, so each
# properties form has the pane's full height. The window is captured through PrintWindow and may lie behind other
# windows; a menu popup is a window of its own, captured the same way, or from the screen when that comes back
# empty. The user settings are set for the figures (the saved window size and pane arrangement, the dark theme,
# English, the default preview quality) and put back afterwards.
#
#   powershell -ExecutionPolicy Bypass -File _tools\ui_check\Screenshots.ps1 [-Exe <path>] [-Example <wdproj>]
#       [-Out <folder>] [-Frame <n>] [-PngFolder <folder>]
#
# -PngFolder also writes each figure's PNG (without badges) there, for a look at the captures.

param(
	[string]$Exe = (Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe"),
	[string]$Example = "C:\Work\Examples_WDN\Example14_wave_in_2_boxes.wdproj",
	[string]$Out = (Join-Path $PSScriptRoot "..\..\Assets"),
	[int]$Frame = 1200,
	[string]$PngFolder = ""
)
. (Join-Path $PSScriptRoot "Common.ps1")

Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Shot
{
	[DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
	[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
	[DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
	[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
	// screen position of the last Window() capture's top-left pixel
	public static int LastLeft, LastTop;

	// the window as DWM composes it (PW_RENDERFULLCONTENT), cropped to its visible frame: GetWindowRect includes the
	// invisible resize border, DWMWA_EXTENDED_FRAME_BOUNDS (9) does not
	public static Bitmap Window(IntPtr hwnd)
	{
		RECT full, visible;
		GetWindowRect(hwnd, out full);
		if (DwmGetWindowAttribute(hwnd, 9, out visible, Marshal.SizeOf(typeof(RECT))) != 0) visible = full;
		var bmp = new Bitmap(full.Right - full.Left, full.Bottom - full.Top, PixelFormat.Format32bppArgb);
		using (var g = Graphics.FromImage(bmp))
		{
			IntPtr hdc = g.GetHdc();
			PrintWindow(hwnd, hdc, 2);
			g.ReleaseHdc(hdc);
		}
		var crop = new Rectangle(visible.Left - full.Left, visible.Top - full.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);
		crop.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
		Bitmap result = bmp.Clone(crop, PixelFormat.Format24bppRgb);
		bmp.Dispose();
		LastLeft = full.Left + crop.X;
		LastTop = full.Top + crop.Y;
		return result;
	}
	public static Bitmap Screen(int x, int y, int w, int h)
	{
		var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
		using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, bmp.Size);
		return bmp;
	}
	// whether a capture is one flat colour (PrintWindow on a window it cannot render leaves that)
	public static bool IsBlank(Bitmap bmp)
	{
		Color first = bmp.GetPixel(0, 0);
		for (int i = 1; i < 16; i++)
		{
			if (bmp.GetPixel(bmp.Width * i / 16, bmp.Height * i / 16) != first) return false;
		}
		return true;
	}
	public static Bitmap Crop(Bitmap src, int x, int y, int w, int h)
	{
		var r = new Rectangle(x, y, w, h);
		r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
		return src.Clone(r, PixelFormat.Format24bppRgb);
	}
	public static void Paste(Bitmap dst, Bitmap src, int x, int y)
	{
		using (var g = Graphics.FromImage(dst)) g.DrawImageUnscaled(src, x, y);
	}
}
"@ -ReferencedAssemblies System.Drawing

$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$VK_F10 = 0x79
$BadgeRadius = 9
# the legend's heading per figure: the view or feature shown
$Headings = @{
	"main-window" = "The main window"; "project-tree" = "The project tree"; "preview" = "The preview"; "timeline" = "The timeline"
	"menu-file" = "The File menu"; "menu-file-export" = "File > Export"; "menu-file-examples" = "File > Examples"; "menu-edit" = "The Edit menu"; "menu-view" = "The View menu"
	"menu-simulation" = "The Simulation menu"; "menu-help" = "The Help menu"
	"properties-project" = "Project"; "properties-scene" = "Scene"; "properties-simulation" = "Simulation"; "properties-display" = "Display"
	"properties-source" = "Source"; "properties-composite-source" = "Composite source"
	"properties-lens" = "Lens"; "properties-polygon" = "Polygon"; "properties-rectangle" = "Rectangle"; "properties-ellipse" = "Ellipse"
	"properties-triangle" = "Triangle"; "properties-outline" = "Outline"; "properties-fibre" = "Fibre"; "properties-bragg-reflector" = "Bragg reflector"
	"properties-grin-lens" = "GRIN lens"; "properties-xor-gate" = "XOR gate"; "properties-and-gate" = "AND gate"; "properties-or-gate" = "OR gate"
	"properties-zone-plate" = "Zone plate"; "properties-probe" = "Probe"; "properties-line-probe" = "Line probe"; "properties-colour-scheme" = "Colour scheme"
}
$script:legend = New-Object System.Text.StringBuilder
$script:figureCount = 0
$script:problems = 0

function Problem([string]$text) { $script:problems++; Write-Output ("PROBLEM  " + $text) }
function Rect($e) { return $e.Current.BoundingRectangle }
function Has([string]$id) { try { (Find $id) -ne $null } catch { $false } }
function Cond([string]$property, $value) { return New-Object System.Windows.Automation.PropertyCondition($A::$property, $value) }
function FindByClass([string]$className) {
	for ($i = 0; $i -lt 10; $i++) {
		$e = $script:win.FindFirst($TS::Descendants, (Cond "ClassNameProperty" $className))
		if ($e -ne $null) { return $e }
		Start-Sleep -Milliseconds 200
	}
	throw "no element of class '$className'"
}
function ByType($root, [string]$type) { return @($root.FindAll($TS::Descendants, (Cond "ControlTypeProperty" $CT::$type))) }
# the process's top-level windows other than the main one: open menu popups
function Popups() {
	$result = @()
	foreach ($top in $A::RootElement.FindAll($TS::Children, (Cond "ProcessIdProperty" $script:p.Id))) {
		if ([int64]$top.Current.NativeWindowHandle -ne $script:hwnd.ToInt64()) { $result += $top }
	}
	return $result
}
# the open menu popups: a tooltip is a popup window too (the mouse may rest on the app), a menu holds menu entries
function Menu-Popups() { return @(Popups | Where-Object { @(ByType $_ "MenuItem").Count -gt 0 } | Sort-Object { $_.Current.BoundingRectangle.Left }) }
function PopupFocusedId() {
	foreach ($top in Popups) {
		$e = $top.FindFirst($TS::Descendants, (Cond "HasKeyboardFocusProperty" $true))
		if ($e -ne $null) { return $e.Current.AutomationId }
	}
	return "(none)"
}
# Tab until a tree row holds focus (the cycle passes every control of the window)
function FocusOnTree() {
	for ($i = 0; $i -lt 250; $i++) {
		if ((FocusedId) -like "Tree:*") { return }
		Key $VK_TAB 30
	}
	throw "could not reach the tree with Tab; focus is at $(FocusedId)"
}
# what a tree row shows, from its automation id (a row's UI Automation name is its object's ToString)
function Row-Label([string]$id) {
	switch -Wildcard ($id) {
		"Tree:Project" { return "Project" }
		"Tree:TreeColorSchemes" { return "Colour schemes" }
		"Tree:Tree*" { return $id.Substring(9) }
		default { return $id.Substring(5) }
	}
}
# selects the tree row with this automation id by keyboard: Home, then Down, expanding each group on the way
function Select-TreeRow([string]$id) {
	FocusOnTree
	Key $VK_HOME 120
	for ($i = 0; $i -lt 80 -and (FocusedId) -ne $id; $i++) {
		if ((FocusedId) -like "Tree:Tree*") { Key $VK_RIGHT 60 }
		if ((FocusedId) -eq $id) { break }
		Key $VK_DOWN 60
	}
	if ((FocusedId) -ne $id) { throw "tree row '$id' not reached; focus is at $(FocusedId)" }
	Start-Sleep -Milliseconds 500
}

# --- the user settings for the figures -------------------------------------------------------------------------
$settingsPath = Join-Path $env:LOCALAPPDATA "WaveDudeNext\Settings.xml"
$script:settingsBackup = if (Test-Path $settingsPath) { [IO.File]::ReadAllText($settingsPath) } else { $null }
function Set-FigureSettings([int]$treeHeight, [int]$propertiesHeight) {
	$xml = New-Object System.Xml.XmlDocument
	$xml.LoadXml($(if ($script:settingsBackup) { $script:settingsBackup } else { "<Settings />" }))
	$root = $xml.DocumentElement
	$root.SetAttribute("WindowWidth", "1314")
	$root.SetAttribute("WindowHeight", "1126")
	$root.SetAttribute("Theme", "Dark")
	$root.SetAttribute("Language", "en")
	foreach ($name in "WindowState", "PreviewResolution", "PreviewFrames", "PreviewSampleBytes", "PreviewDitherBits", "PreviewOrientation") { $root.RemoveAttribute($name) }
	$old = $root.SelectSingleNode("Layout")
	if ($old -ne $null) { $root.RemoveChild($old) | Out-Null }
	$layout = $xml.CreateElement("Layout")
	$layout.InnerXml = '<Split Length="1" Orientation="Horizontal"><Split Length="406" Orientation="Vertical">' +
		"<Group Length=`"$treeHeight`" Active=`"project`"><Item>project</Item></Group>" +
		"<Group Length=`"$propertiesHeight`" Active=`"properties`"><Item>properties</Item></Group>" +
		'</Split><Document Length="888" /></Split>'
	$root.PrependChild($layout) | Out-Null
	New-Item -ItemType Directory -Force (Split-Path $settingsPath) | Out-Null
	$xml.Save($settingsPath)
}
function Restore-Settings() {
	if ($script:settingsBackup -ne $null) { [IO.File]::WriteAllText($settingsPath, $script:settingsBackup) }
	elseif (Test-Path $settingsPath) { Remove-Item $settingsPath }
}

# --- figures ----------------------------------------------------------------------------------------------------
# a figure: a bitmap, where its top-left pixel lies on the screen, and the badges (screen rectangle, anchor, label)
function New-Figure([string]$name, [string]$title, [System.Drawing.Bitmap]$bitmap, [int]$left, [int]$top) {
	return @{ Name = $name; Title = $title; Bitmap = $bitmap; Left = $left; Top = $top; Items = (New-Object System.Collections.ArrayList) }
}
# crops a window capture to a screen rectangle
function Crop-Figure([string]$name, [string]$title, $capture, [double]$l, [double]$t, [double]$r, [double]$b) {
	$x = [int][math]::Floor($l) - $capture.Left; $y = [int][math]::Floor($t) - $capture.Top
	$w = [int][math]::Ceiling($r) - [int][math]::Floor($l); $h = [int][math]::Ceiling($b) - [int][math]::Floor($t)
	$bmp = [Shot]::Crop($capture.Bitmap, $x, $y, $w, $h)
	return New-Figure $name $title $bmp ([int][math]::Floor($l)) ([int][math]::Floor($t))
}
function Capture-Window() {
	$bmp = [Shot]::Window($script:hwnd)
	return @{ Bitmap = $bmp; Left = [Shot]::LastLeft; Top = [Shot]::LastTop }
}
# anchors: on (centre), tl (inside the top-left corner), corner (on the top-right corner), left, right, above, below
# (just outside that edge), gutter (left of the picture, on the element's first line), rgutter (right of the picture,
# at the element's centre line), bottom (below the picture)
# the hint is the control's tooltip (its UI Automation help text), the legend's description of the feature
function Add-Badge($fig, $rect, [string]$anchor, [string]$label, [string]$hint = "") {
	if ($rect -eq $null -or $rect.IsEmpty) { Problem ("{0}: no rectangle for '{1}'" -f $fig.Name, $label); return }
	$null = $fig.Items.Add(@{ Rect = $rect; Anchor = $anchor; Label = $label; Hint = $hint })
}
function Add-BadgeFor($fig, [string]$id, [string]$anchor, [string]$label) {
	if (-not (Has $id)) { Problem ("{0}: element '{1}' not found" -f $fig.Name, $id); return }
	$e = Find $id
	Add-Badge $fig (Rect $e) $anchor $label $e.Current.HelpText
}
# a form row's tooltip: the label has none, the editor beside it (its next sibling in the control view) has
$script:walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
function Row-Hint($row) {
	$hint = $row.Current.HelpText
	if ($hint -eq "" -and $row.Current.ClassName -eq "TextBlock") {
		$next = $script:walker.GetNextSibling($row)
		if ($next -ne $null) { $hint = $next.Current.HelpText }
	}
	return $hint
}
function Badge-Centre($fig, $item) {
	$R = $BadgeRadius
	$l = $item.Rect.Left - $fig.Left; $t = $item.Rect.Top - $fig.Top; $w = $item.Rect.Width; $h = $item.Rect.Height
	$cx = $l + $w / 2; $cy = $t + $h / 2
	switch ($item.Anchor) {
		"tl" { $cx = $l + $R + 4; $cy = $t + $R + 4 }
		"corner" { $cx = $l + $w - 2; $cy = $t + 2 }
		"left" { $cx = $l - $R - 3 }
		"right" { $cx = $l + $w + $R + 3 }
		"above" { $cy = $t - $R - 3 }
		"below" { $cy = $t + $h + $R + 3 }
		"gutter" { $cx = -$R - 3; $cy = $t + [math]::Min($h, 23) / 2 }
		"rgutter" { $cx = $fig.Bitmap.Width + $R + 3 }
		"bottom" { $cy = $fig.Bitmap.Height + $R + 3 }
	}
	return @([int][math]::Round($cx), [int][math]::Round($cy))
}
function Save-Figure($fig) {
	$R = $BadgeRadius
	$W = $fig.Bitmap.Width; $H = $fig.Bitmap.Height
	$ms = New-Object System.IO.MemoryStream
	$fig.Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
	$png = $ms.ToArray(); $ms.Dispose()
	if ($PngFolder -ne "") { [IO.File]::WriteAllBytes((Join-Path $PngFolder ($fig.Name + ".png")), $png) }
	$minX = 0; $minY = 0; $maxX = $W; $maxY = $H
	$badges = New-Object System.Text.StringBuilder
	$n = 0
	$heading = if ($Headings.ContainsKey($fig.Name)) { $Headings[$fig.Name] } else { $fig.Name }
	$null = $script:legend.AppendLine("## " + $heading).AppendLine().AppendLine(("![{0}]({0}.svg)" -f $fig.Name)).AppendLine().AppendLine($fig.Title).AppendLine()
	foreach ($item in $fig.Items) {
		$n++
		$c = Badge-Centre $fig $item
		$minX = [math]::Min($minX, $c[0] - $R - 2); $minY = [math]::Min($minY, $c[1] - $R - 2)
		$maxX = [math]::Max($maxX, $c[0] + $R + 2); $maxY = [math]::Max($maxY, $c[1] + $R + 2)
		$title = [System.Security.SecurityElement]::Escape($item.Label)
		$null = $badges.AppendLine(('  <g><title>{0}</title><circle cx="{1}" cy="{2}" r="{3}" fill="#FFC107" stroke="#202020" stroke-width="1"/><text x="{1}" y="{2}" dy=".36em" fill="#000">{4}</text></g>' -f $title, $c[0], $c[1], $R, $n))
		# the feature in bold, its description (the tooltip) on the next line of the item
		$null = $script:legend.AppendLine(("{0}. **{1}**" -f $n, $item.Label) + $(if ($item.Hint -ne "") { "  " } else { "" }))
		if ($item.Hint -ne "") { $null = $script:legend.AppendLine("   " + $item.Hint) }
	}
	$null = $script:legend.AppendLine()
	$svg = New-Object System.Text.StringBuilder
	$null = $svg.AppendLine(('<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{0}" height="{1}" viewBox="{2} {3} {0} {1}">' -f ($maxX - $minX), ($maxY - $minY), $minX, $minY))
	$null = $svg.AppendLine(('  <image x="0" y="0" width="{0}" height="{1}" xlink:href="data:image/png;base64,{2}"/>' -f $W, $H, [Convert]::ToBase64String($png)))
	if ($n -gt 0) {
		$null = $svg.AppendLine('  <g font-family="Segoe UI, Helvetica, Arial, sans-serif" font-size="11" font-weight="bold" text-anchor="middle">')
		$null = $svg.Append($badges.ToString()).AppendLine('  </g>')
	}
	$null = $svg.AppendLine('</svg>')
	[IO.File]::WriteAllText((Join-Path $Out ($fig.Name + ".svg")), $svg.ToString())
	$fig.Bitmap.Dispose()
	$script:figureCount++
	Write-Output ("figure {0}: {1}x{2}, {3} badges" -f $fig.Name, $W, $H, $n)
}

# the dock group holding a pane: the title bar row (21 px) above the pane's content, inside a 1 px border
function Pane-Rect([string]$paneId, [string]$contentClass) {
	$title = Rect (Find ("DockTitle:" + $paneId))
	$content = Rect (FindByClass $contentClass)
	$top = $title.Top + $title.Height / 2 - 11.5
	return @(($content.Left - 1), $top, ($content.Right + 1), ($content.Bottom + 1))
}

# an English string of the app, by its key in the string table (for elements a harness finds by name)
$script:enStrings = $null
function En-String([string]$key) {
	if ($script:enStrings -eq $null) {
		$script:enStrings = New-Object System.Xml.XmlDocument
		$script:enStrings.Load((Join-Path $PSScriptRoot "..\..\WaveDudeNext.WPF\Strings\Strings.EN.xaml"))
	}
	$node = $script:enStrings.SelectSingleNode("//*[@*[local-name()='Key']='$key']")
	if ($node -eq $null) { throw "no string '$key' in the English table" }
	return $node.InnerText
}
# an element of the properties pane: by automation id, or by name for "key:<string key>" (the first match)
function Find-InPane($pane, [string]$key) {
	$c = if ($key.StartsWith("key:")) { Cond "NameProperty" (En-String $key.Substring(4)) } else { Cond "AutomationIdProperty" $key }
	return $pane.FindFirst($TS::Descendants, $c)
}

# the properties form's rows: the labels and full-width check boxes at the label column (the smallest Left among the
# form's TextBlocks), top to bottom; section headings are Labels and the editors sit in the right column. The
# extras, pairs of an element key (see Find-InPane) and a label, get badges in the right gutter: the controls of a
# row without a label of its own (a stop, a point) and the buttons.
function Properties-Figure([string]$name, [string]$title, $extras = @()) {
	$pane = FindByClass "PropertiesPane"
	$all = @($pane.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition))
	$texts = @($all | Where-Object { $_.Current.ClassName -eq "TextBlock" -and -not $_.Current.IsOffscreen })
	if ($texts.Count -eq 0) { Problem "$name`: no text in the properties pane"; return }
	$column = ($texts | ForEach-Object { $_.Current.BoundingRectangle.Left } | Measure-Object -Minimum).Minimum
	# a section heading is a Label, whose text is a TextBlock inside it (its parent in the control view is the Label);
	# a hint paragraph wraps, so a row is a single line
	$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
	$rows = @($all | Where-Object { ($_.Current.ClassName -eq "TextBlock" -or $_.Current.ClassName -eq "CheckBox") -and [math]::Abs($_.Current.BoundingRectangle.Left - $column) -le 2 -and $_.Current.BoundingRectangle.Height -le 24 -and $walker.GetParent($_).Current.ControlType -ne $CT::Text } | Sort-Object { $_.Current.BoundingRectangle.Top })
	$offscreen = @($rows | Where-Object { $_.Current.IsOffscreen })
	if ($offscreen.Count -gt 0) { Problem ("{0}: {1} rows lie outside the pane (form taller than the pane)" -f $name, $offscreen.Count) }
	# cropped below the form's last control (the containers span the pane; the form's margin is 8 px)
	$bottom = ($all | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.ControlType -ne $CT::Pane -and $_.Current.ControlType -ne $CT::Custom -and $_.Current.ControlType -ne $CT::Group } | ForEach-Object { $_.Current.BoundingRectangle.Bottom } | Measure-Object -Maximum).Maximum
	$capture = Capture-Window
	$rect = Pane-Rect "properties" "PropertiesPane"
	$fig = Crop-Figure $name $title $capture $rect[0] $rect[1] $rect[2] ([math]::Min($rect[3], $bottom + 9))
	$capture.Bitmap.Dispose()
	foreach ($row in $rows) { if (-not $row.Current.IsOffscreen) { Add-Badge $fig (Rect $row) "gutter" $row.Current.Name (Row-Hint $row) } }
	foreach ($extra in $extras) {
		$e = Find-InPane $pane $extra[0]
		if ($e -eq $null) { Problem ("{0}: element '{1}' not found" -f $name, $extra[0]); continue }
		Add-Badge $fig (Rect $e) "rgutter" $extra[1] $e.Current.HelpText
	}
	Save-Figure $fig
}

# opens the k-th menu of the bar (0 = File) by keyboard: F10 enters the menu mode, Right moves along the headers,
# Down opens the popup on its first entry
function Open-Menu([int]$index) {
	for ($attempt = 0; $attempt -lt 3; $attempt++) {
		[Native]::SysKey($script:hwnd, $VK_F10, $false); Start-Sleep -Milliseconds 300
		for ($i = 0; $i -lt $index; $i++) { Key $VK_RIGHT 150 }
		Key $VK_DOWN 400
		if (WaitFor { (Menu-Popups).Count -gt 0 } 1500 100) { return }
		Write-Output ("note: menu {0} did not open (focus {1}, foreground {2}); trying again" -f $index, (FocusedId), (Foreground))
		Close-Menus
	}
}
function Close-Menus() { Key $VK_ESCAPE 150; Key $VK_ESCAPE 150; Key $VK_ESCAPE 150 }
# a popup's pixels: PrintWindow on its own window, else the screen (a WPF popup is topmost)
function Capture-Popup($popup) {
	$r = Rect $popup
	$bmp = [Shot]::Window([IntPtr][int64]$popup.Current.NativeWindowHandle)
	if ([Shot]::IsBlank($bmp)) {
		$bmp.Dispose()
		$bmp = [Shot]::Screen([int]$r.Left, [int]$r.Top, [int]$r.Width, [int]$r.Height)
		return @{ Bitmap = $bmp; Left = [int]$r.Left; Top = [int]$r.Top; FromScreen = $true }
	}
	return @{ Bitmap = $bmp; Left = [Shot]::LastLeft; Top = [Shot]::LastTop; FromScreen = $false }
}
# the figure of the open menus: the menu bar with the popups composed over the window; the entries of the last
# (innermost) popup get the badges, left of the popup
function Menu-Figure([string]$name, [string]$title) {
	$popups = Menu-Popups
	if ($popups.Count -eq 0) { Problem "$name`: no menu popup is open"; return }
	$capture = Capture-Window
	$bar = Rect @(ByType $script:win "Menu")[0]
	$right = $bar.Left + 260; $bottom = 0
	foreach ($popup in $popups) {
		$shot = Capture-Popup $popup
		if ($shot.FromScreen) { Write-Output ("note: popup of {0} read from the screen" -f $name) }
		[Shot]::Paste($capture.Bitmap, $shot.Bitmap, ($shot.Left - $capture.Left), ($shot.Top - $capture.Top))
		$shot.Bitmap.Dispose()
		$pr = Rect $popup
		$right = [math]::Max($right, $pr.Right + 12); $bottom = [math]::Max($bottom, $pr.Bottom + 12)
	}
	$fig = Crop-Figure $name $title $capture $capture.Left ($bar.Top - 1) $right $bottom
	$capture.Bitmap.Dispose()
	$last = $popups[$popups.Count - 1]
	foreach ($item in (ByType $last "MenuItem")) {
		if (-not $item.Current.IsOffscreen) { Add-Badge $fig (Rect $item) "left" $item.Current.Name $item.Current.HelpText }
	}
	Save-Figure $fig
}

New-Item -ItemType Directory -Force $Out | Out-Null
if ($PngFolder -ne "") { New-Item -ItemType Directory -Force $PngFolder | Out-Null }
$null = $script:legend.AppendLine("# Figures").AppendLine().AppendLine("Generated by ``_tools/ui_check/Screenshots.ps1``; under each figure, what its numbered badges mark, each with the control's tooltip where it has one.").AppendLine()

try {
	# ----- session 1: the example project ---------------------------------------------------------------------
	Set-FigureSettings 475 563
	Start-App $Exe $Example
	try {
		# waves in the pictures: a scratch frame at the wanted position (PageUp moves ten frames)
		FocusOn "PreviewTimeline"
		Key $VK_HOME 100
		for ($i = 0; $i -lt [int]($Frame / 10); $i++) { Key $VK_PRIOR 10 }
		Start-Sleep -Milliseconds 300
		if (-not (WaitFor { (Position) -eq $Frame } 5000 100)) { Problem ("position is {0}, wanted {1}" -f (Position), $Frame) }
		# the frame has arrived when the picture stops changing (the busy dimming and spinner move until then)
		$h0 = FieldHash
		$settled = WaitFor { Start-Sleep -Milliseconds 700; $h1 = FieldHash; ($h1 -ne $h0) -and ($h1 -eq (FieldHash)) } 60000 100
		if (-not $settled) { Problem "the scratch frame did not arrive" }
		# the Project row selected, as after opening, with the focus on it
		Select-TreeRow "Tree:Project"

		$capture = Capture-Window
		$fig = New-Figure "main-window" "The main window with the example project at frame $Frame." $capture.Bitmap $capture.Left $capture.Top
		$headers = @(ByType @(ByType $script:win "Menu")[0] "MenuItem")
		Add-Badge $fig (Rect $headers[$headers.Count - 1]) "right" "Menu bar"
		Add-BadgeFor $fig "DockTitle:project" "right" "Project pane: the tree of the project's objects"
		Add-BadgeFor $fig "DockTitle:properties" "right" "Properties pane: the selected object's form"
		Add-BadgeFor $fig "PreviewFieldImage" "tl" "Field picture"
		Add-BadgeFor $fig "PreviewIntensityImage" "tl" "Intensity picture"
		Add-BadgeFor $fig "PreviewFieldImageBrightness" "on" "Header strip of a picture: title, brightness, Auto"
		Add-BadgeFor $fig "PreviewTimeline" "on" "Timeline"
		Add-BadgeFor $fig "PreviewFrame" "on" "Transport controls, position readout, speed, layout"
		Add-BadgeFor $fig "StatusMessage" "right" "Status bar"
		Save-Figure $fig

		# the project tree: pane title, toolbar, every row, and the controls of one group row and one item row
		$capture = Capture-Window
		$rect = Pane-Rect "project" "ProjectTree"
		# cropped below the last row (the root row's rectangle spans every row)
		$lastRow = (ByType (ByType $script:win "Tree")[0] "TreeItem" | ForEach-Object { $_.Current.BoundingRectangle.Bottom } | Measure-Object -Maximum).Maximum
		$fig = Crop-Figure "project-tree" "The Project pane with the example project." $capture $rect[0] $rect[1] $rect[2] ([math]::Min($rect[3], $lastRow + 9))
		$capture.Bitmap.Dispose()
		Add-BadgeFor $fig "DockTitle:project" "right" "Pane title"
		Add-BadgeFor $fig "ToolAddSource" "corner" "Add a source"
		Add-BadgeFor $fig "ToolAddProbe" "corner" "Add a probe"
		Add-BadgeFor $fig "ToolAddPart" "corner" "Add a part (a menu of the kinds)"
		Add-BadgeFor $fig "ToolMoveUp" "corner" "Move the selected object up"
		Add-BadgeFor $fig "ToolMoveDown" "corner" "Move the selected object down"
		Add-BadgeFor $fig "ToolRemove" "corner" "Remove the selected object"
		$tree = (ByType $script:win "Tree")[0]
		$rowsDone = @{}
		foreach ($row in (ByType $tree "TreeItem")) {
			$id = $row.Current.AutomationId
			Add-Badge $fig (Rect $row) "gutter" ("Row " + (Row-Label $id))
			if (-not $rowsDone.ContainsKey("group") -and $id -like "Tree:Tree*") {
				$rowsDone["group"] = $true
				$plus = ByType $row "Button"
				if ($plus.Count -gt 0) { Add-Badge $fig (Rect $plus[0]) "on" "Add to this group" $plus[0].Current.HelpText }
			}
			elseif (-not $rowsDone.ContainsKey("item") -and (Has ("Enabled:" + $row.Current.Name))) {
				$rowsDone["item"] = $true
				Add-BadgeFor $fig ("Enabled:" + $row.Current.Name) "on" "Enabled"
				$buttons = @(ByType $row "Button" | Sort-Object { $_.Current.BoundingRectangle.Left })
				$labels = "Move up", "Move down", "Remove"
				for ($i = 0; $i -lt [math]::Min(3, $buttons.Count); $i++) { Add-Badge $fig (Rect $buttons[$i]) "on" $labels[$i] $buttons[$i].Current.HelpText }
			}
		}
		Save-Figure $fig

		# the preview: both pictures with their header strips, handles, a graph box and the splitter
		$capture = Capture-Window
		$fieldImage = Rect (Find "PreviewFieldImage"); $intensityImage = Rect (Find "PreviewIntensityImage")
		$fieldStrip = Rect (Find "PreviewFieldImageBrightness"); $intensityStrip = Rect (Find "PreviewIntensityImageBrightness")
		$left = [math]::Min($fieldStrip.Left, $intensityStrip.Left) - 6
		$top = [math]::Min($fieldImage.Top, $fieldStrip.Top) - 10
		$fig = Crop-Figure "preview" "The preview: field and intensity pictures of the example project at frame $Frame." $capture $left $top ($fieldImage.Right + 8) ($intensityImage.Bottom + 8)
		$capture.Bitmap.Dispose()
		Add-BadgeFor $fig "PreviewFieldImage" "tl" "Field picture"
		Add-BadgeFor $fig "PreviewFieldImageBrightness" "on" "Brightness of the field picture"
		Add-BadgeFor $fig "PreviewFieldImageAutoBrightness" "on" "Auto brightness (fit to the frame shown)"
		Add-BadgeFor $fig "PreviewIntensityImage" "tl" "Intensity picture"
		Add-BadgeFor $fig "PreviewIntensityImageBrightness" "on" "Brightness of the intensity picture"
		Add-BadgeFor $fig "PreviewIntensityImageAutoBrightness" "on" "Auto brightness of the intensity picture"
		Add-BadgeFor $fig "PreviewSplitter" "on" "Splitter between the pictures"
		$handles = @($script:win.FindAll($TS::Descendants, (Cond "ControlTypeProperty" $CT::Thumb)) | Where-Object { $_.Current.AutomationId -like "Source:*" -or $_.Current.AutomationId -like "Part:*" -or $_.Current.AutomationId -like "Probe:*" -or $_.Current.AutomationId -like "ProbeBox:*" })
		$kindsDone = @{}
		foreach ($h in $handles) {
			$kind = $h.Current.AutomationId.Split(":")[0]
			if ($kindsDone.ContainsKey($kind)) { continue }
			$kindsDone[$kind] = $true
			$name = $h.Current.AutomationId.Substring($kind.Length + 1) -replace ":([AB])$", ', end $1'
			$label = switch ($kind) { "Source" { "Source handle ($name)" } "Part" { "Part handle ($name)" } "Probe" { "Probe handle ($name)" } "ProbeBox" { "Probe graph box ($name)" } }
			Add-Badge $fig (Rect $h) $(if ($kind -eq "ProbeBox") { "above" } else { "right" }) $label $h.Current.HelpText
		}
		Save-Figure $fig

		# the timeline and the transport row
		$capture = Capture-Window
		$timeline = Rect (Find "PreviewTimeline"); $run = Rect (Find "PreviewRun"); $stacked = Rect (Find "PreviewStacked")
		$fig = Crop-Figure "timeline" "The timeline with the playhead and the in and out points, and the transport controls." $capture ($run.Left - 6) ($timeline.Top - 22) ($stacked.Right + 6) ($run.Bottom + 5)
		$capture.Bitmap.Dispose()
		# the playhead and the brackets, placed by the bar's own mapping: a one-pixel inset at both ends, then
		# frame × track width / maximum; the bar reports the playhead and the maximum through its range value and the
		# in and out points through its item status
		$bar = Find "PreviewTimeline"
		$range = Pattern $bar "RangeValue"
		$maximum = [math]::Max(1, $range.Current.Maximum)
		$marks = New-Object System.Collections.ArrayList
		$null = $marks.Add(@("Playhead: the position shown", $range.Current.Value))
		if ($bar.Current.ItemStatus -match "^range (\d+)\.\.(\d+)$") { $null = $marks.Add(@("In point", [int]$Matches[1])); $null = $marks.Add(@("Out point", [int]$Matches[2])) }
		else { Problem "the timeline reports no in/out range" }
		foreach ($mark in $marks) {
			$x = $timeline.Left + 1 + $mark[1] * ($timeline.Width - 2) / $maximum
			Add-Badge $fig (New-Object System.Windows.Rect(($x - 1), $timeline.Top, 2, $timeline.Height)) "above" $mark[0]
		}
		foreach ($pair in @(
			@("PreviewRun", "Run the simulation"), @("PreviewPlay", "Play"), @("PreviewPause", "Pause"),
			@("PreviewStepBack", "One frame back"), @("PreviewStepForward", "One frame forward"),
			@("PreviewSetIn", "Set the in point"), @("PreviewSetOut", "Set the out point"), @("PreviewClearRange", "Clear the in and out points"),
			@("PreviewLoop", "Loop"), @("PreviewLive", "Live: follow a running run"),
			@("PreviewFrame", "Position: frame of frames"), @("PreviewStep", "Step of the position"),
			@("PreviewSpeed", "Playback speed"), @("PreviewShowField", "Show the field"), @("PreviewShowIntensity", "Show the intensity"),
			@("PreviewSideBySide", "Pictures side by side"), @("PreviewStacked", "Pictures stacked"))) {
			Add-BadgeFor $fig $pair[0] "bottom" $pair[1]
		}
		Save-Figure $fig

		# the menus
		$menuNames = "File", "Edit", "View", "Simulation", "Help"
		for ($m = 0; $m -lt $menuNames.Count; $m++) {
			Open-Menu $m
			Menu-Figure ("menu-" + $menuNames[$m].ToLower()) ("The " + $menuNames[$m] + " menu.")
			Close-Menus
		}
		Open-Menu 0
		for ($i = 0; $i -lt 12 -and (PopupFocusedId) -ne "MenuExport"; $i++) { Key $VK_DOWN 120 }
		if ((PopupFocusedId) -eq "MenuExport") {
			Key $VK_RIGHT 400
			Menu-Figure "menu-file-export" "The File > Export submenu."
		}
		else { Problem "the Export entry of the File menu was not reached" }
		Close-Menus
		Open-Menu 0
		for ($i = 0; $i -lt 12 -and (PopupFocusedId) -ne "MenuExamples"; $i++) { Key $VK_DOWN 120 }
		if ((PopupFocusedId) -eq "MenuExamples") {
			Key $VK_RIGHT 400
			Menu-Figure "menu-file-examples" "The File > Examples submenu: the shipped example projects."
		}
		else { Problem "the Examples entry of the File menu was not reached" }
		Close-Menus
	}
	finally { Stop-App }

	# ----- session 2: one object of every kind, a short tree, the properties pane at full height ------------------
	Restore-Settings
	Set-FigureSettings 140 898
	Start-App $Exe (Join-Path $PSScriptRoot "figures.wdproj")
	try {
		$tree = (ByType $script:win "Tree")[0]
		$ids = @(ByType $tree "TreeItem" | ForEach-Object { $_.Current.AutomationId })
		Write-Output ("tree rows: " + ($ids -join ", "))
		# tree row, figure name, title, extras (element key and label, badges in the right gutter)
		foreach ($spec in @(
			@("Tree:Project", "properties-project", "The project's own properties.", @(, @("ProjectNewSeed", "New seed: a fresh random seed"))),
			@("Tree:Scene", "properties-scene", "The Scene form (an image scene).", @(, @("SceneBrowse", "The image buttons: the dots pick an image file, the save glyph writes the embedded image out, the X drops it"))),
			@("Tree:Simulation", "properties-simulation", "The Simulation form.", @()),
			@("Tree:Display", "properties-display", "The Display form.", @()),
			@("Tree:Source 1", "properties-source", "A point source.", @()),
			@("Tree:Source 2", "properties-composite-source", "A composite (array) source, pulsed.", @()),
			@("Tree:Lens*", "properties-lens", "A lens or mirror.", @()),
			@("Tree:Polygon*", "properties-polygon", "A polygon.", @()),
			@("Tree:Rectangle*", "properties-rectangle", "A rectangle.", @()),
			@("Tree:Ellipse*", "properties-ellipse", "An ellipse.", @()),
			@("Tree:Triangle*", "properties-triangle", "A triangle.", @()),
			@("Tree:Outline*", "properties-outline", "An outline (free points).", @(@("key:TipRemovePoint", "A point: X, Y, remove"), @("PartAddPoint", "Add a point"))),
			@("Tree:Fib*", "properties-fibre", "A fibre on a Bézier path.", @(@("key:TipRemoveNode", "Per node: smooth, remove"), @("PartAddNode", "Add a node"))),
			@("Tree:Bragg*", "properties-bragg-reflector", "A Bragg reflector.", @()),
			@("Tree:GRIN*", "properties-grin-lens", "A GRIN lens.", @()),
			@("Tree:XOR*", "properties-xor-gate", "An XOR gate.", @()),
			@("Tree:AND*", "properties-and-gate", "An AND gate.", @()),
			@("Tree:OR *", "properties-or-gate", "An OR gate.", @()),
			@("Tree:Zone*", "properties-zone-plate", "A zone plate.", @()),
			@("Tree:Probe 1", "properties-probe", "A point probe of the field.", @()),
			@("Tree:Probe 2", "properties-line-probe", "A line probe of the intensity, logarithmic.", @()),
			@("Tree:Scheme*", "properties-colour-scheme", "A colour scheme of the project.", @(@("StopSwatch:0", "A stop: position, colour swatch (opens the picker), hex value, remove"), @("SchemeAddStop", "Add a stop"))))) {
			$id = @($ids | Where-Object { $_ -like $spec[0] })
			if ($id.Count -eq 0) { Problem ("no tree row matches '{0}'" -f $spec[0]); continue }
			Select-TreeRow $id[0]
			Properties-Figure $spec[1] $spec[2] $spec[3]
		}
	}
	finally { Stop-App }
}
finally {
	Restore-Settings
	[IO.File]::WriteAllText((Join-Path $Out "Legend.md"), $script:legend.ToString())
}
Write-Output ("{0} figures written to {1}; {2} problems" -f $script:figureCount, (Resolve-Path $Out), $script:problems)
