// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

// Contributors: Vinicius Miguel


using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using NLog;
using WaveDudeNext.Engine;
using WaveDudeNext.Runs;

namespace WaveDudeNext.CrossPlatform;

/// <summary>Product credits, license and system report. UI thread only.</summary>
internal sealed class AboutWindow : Window
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
	private readonly string _report;
	private readonly TextBox _license;
	private readonly Button _licenseToggle;

	/// <summary>Builds the dialog and captures the machine facts once.</summary>
	public AboutWindow(bool useGpu)
	{
		Title = Text("AboutTitle"); Width = 640; Height = 650; MinWidth = 440; MinHeight = 360;
		WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
		AutomationProperties.SetAutomationId(this, "AboutWindow");
		Assembly assembly = typeof(AboutWindow).Assembly;
		string product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "WaveDude Next";
		string version = FormatVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "");
		string copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright (C) 2026 Alexander Graef";
		_report = SystemReport(product, version, useGpu);
		var body = new StackPanel { Margin = new Thickness(20), Spacing = 6 };
		var productText = new TextBlock { Text = product, FontSize = 20, FontWeight = FontWeight.SemiBold };
		AutomationProperties.SetAutomationId(productText, "AboutProduct"); body.Children.Add(productText);
		var versionText = new TextBlock { Text = string.Format(CultureInfo.CurrentCulture, Text("AboutVersionFormat"), version) };
		AutomationProperties.SetAutomationId(versionText, "AboutVersion"); body.Children.Add(versionText);
		var copyrightText = new TextBlock { Text = copyright }; AutomationProperties.SetAutomationId(copyrightText, "AboutCopyright"); body.Children.Add(copyrightText);
		body.Children.Add(new TextBlock { Text = Text("AboutTagline") });
		body.Children.Add(Heading("AboutCredits"));
		body.Children.Add(new TextBlock { Text = "Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026", TextWrapping = TextWrapping.Wrap });
		body.Children.Add(Links(("youtube.com/@HuygensOptics", "https://www.youtube.com/@HuygensOptics"), ("huygensoptics.com", "https://www.huygensoptics.com")));
		body.Children.Add(new TextBlock { Text = "0x23 (Diffraction Limited), author of WaveSimulator2D", TextWrapping = TextWrapping.Wrap });
		body.Children.Add(Links(("youtube.com/@DiffractionLimited", "https://www.youtube.com/@DiffractionLimited"), ("github.com/0x23/WaveSimulator2D", "https://github.com/0x23/WaveSimulator2D")));
		body.Children.Add(Heading("AboutLicense"));
		body.Children.Add(new TextBlock { Text = Text("AboutLicenseNotice"), TextWrapping = TextWrapping.Wrap });
		body.Children.Add(Links(("gnu.org/licenses/gpl-3.0.html", "https://www.gnu.org/licenses/gpl-3.0.html")));
		_license = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 260, IsVisible = false };
		AutomationProperties.SetAutomationId(_license, "AboutLicenseText");
		_licenseToggle = new Button { HorizontalAlignment = HorizontalAlignment.Left, Command = new ControlCommand(ToggleLicense, () => true) };
		_licenseToggle.Bind(ContentControl.ContentProperty, _licenseToggle.GetResourceObservable("AboutShowLicense"));
		AutomationProperties.SetAutomationId(_licenseToggle, "AboutLicenseToggle"); body.Children.Add(_licenseToggle); body.Children.Add(_license);
		body.Children.Add(Heading("AboutSystem"));
		var system = new TextBox { Text = _report, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160 };
		AutomationProperties.SetAutomationId(system, "AboutSystemText"); body.Children.Add(system);
		var copy = new Button { Command = new OpenCommand(CopySystem) };
		copy.Bind(ContentControl.ContentProperty, copy.GetResourceObservable("AboutCopy")); copy.Bind(ToolTip.TipProperty, copy.GetResourceObservable("TipAboutCopy")); AutomationProperties.SetAutomationId(copy, "AboutCopy");
		var close = new Button { Command = new ControlCommand(Close, () => true), IsDefault = true, IsCancel = true };
		close.Bind(ContentControl.ContentProperty, close.GetResourceObservable("AboutClose")); AutomationProperties.SetAutomationId(close, "AboutClose");
		body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { copy, close } });
		Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
		Opened += (_, _) => close.Focus();
	}
	private static string Text(string key) => Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is string text ? text : key;
	private static TextBlock Heading(string key) => new() { Text = Text(key), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
	private static StackPanel Links(params (string Label, string Uri)[] links)
	{
		var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
		foreach (var link in links) panel.Children.Add(new HyperlinkButton { Content = link.Label, NavigateUri = new Uri(link.Uri) });
		return panel;
	}
	private void ToggleLicense()
	{
		if (!_license.IsVisible && _license.Text == null)
		{
			using Stream stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("Documents/LICENSE.md") ?? throw new InvalidOperationException("The license resource is missing.");
			using var reader = new StreamReader(stream); _license.Text = reader.ReadToEnd();
		}
		_license.IsVisible = !_license.IsVisible;
		_licenseToggle.Bind(ContentControl.ContentProperty, _licenseToggle.GetResourceObservable(_license.IsVisible ? "AboutHideLicense" : "AboutShowLicense"));
	}
	private async Task CopySystem()
	{
		try { if (Clipboard != null) { await Clipboard.SetTextAsync(_report); Logger.Info(Text("AboutCopied")); } }
		catch (Exception ex) { Logger.Warn(ex, "Could not copy system information"); }
	}
	private static string FormatVersion(string informational)
	{
		int plus = informational.IndexOf('+');
		if (plus < 0) return informational;
		string hash = informational[(plus + 1)..];
		if (hash.Length > 7) hash = hash[..7];
		return hash.Length == 0 ? informational[..plus] : $"{informational[..plus]} ({hash})";
	}
	private static string SystemReport(string product, string version, bool useGpu)
	{
		string devices;
		try
		{
			var found = VulkanDeviceInfo.FindAll();
			devices = found.Count == 0 ? Text("AboutNone") : string.Join("; ", found.Select(device => $"{device.Name} (Vulkan {device.ApiVersion >> 22}.{device.ApiVersion >> 12 & 0x3ff}.{device.ApiVersion & 0xfff})"));
		}
		catch (Exception ex) { Logger.Debug(ex, "Vulkan device enumeration is unavailable"); devices = Text("AboutOpenClMissing").Replace("OpenCL", "Vulkan", StringComparison.Ordinal); }
		return string.Join(Environment.NewLine,
			$"{Text("AboutProductLine")}: {product} {version}",
			$"{Text("AboutRuntime")}: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}",
			$"{Text("AboutOperatingSystem")}: {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}",
			$"{Text("AboutProcessor")}: {Environment.ProcessorCount} {Text("AboutCores")}",
			$"{Text("AboutMemory")}: {FrameMemory.Gigabytes(FrameMemory.AvailableBytes())} {Text("AboutMemoryFree")} {FrameMemory.Gigabytes(FrameMemory.PhysicalBytes())}",
			$"{Text("AboutGpuSetting").Replace("OpenCL", "Vulkan", StringComparison.Ordinal)}: {Text(useGpu ? "AboutOn" : "AboutOff")}",
			$"{Text("AboutOpenClDevices").Replace("OpenCL", "Vulkan", StringComparison.Ordinal)}: {devices}");
	}
	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
		base.OnKeyDown(e);
	}
}
