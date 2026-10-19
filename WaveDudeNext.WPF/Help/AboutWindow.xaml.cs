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


using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using NLog;
using WaveDudeNext.WPF.Properties;
using WaveDudeNext.WPF.Theming;

namespace WaveDudeNext.WPF.Help;

/// <summary>
/// The About dialog (Help > About): product, version and copyright from the assembly, the credits as in the source
/// header, the license sentence with the embedded LICENSE.md on request, and the system report of
/// <see cref="AboutInfo.SystemReport"/> with a Copy button. Modal, owned by the main window, closed by Esc or Enter;
/// every link and button is in the Tab cycle. UI thread only.
/// </summary>
public partial class AboutWindow : Window
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();
	/// <summary>The document box's height while the window is sized to its content (the XAML value).</summary>
	private const double LicenseBoxHeight = 260;
	private readonly string _report;

	/// <summary>Builds the dialog and gathers the system report once.</summary>
	public AboutWindow()
	{
		InitializeComponent();
		SourceInitialized += (_, _) => ThemeController.Instance.ApplyTitleBar(this);
		// the links inside the loaded license document bubble their navigation up to the viewer
		licenseView.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnNavigate));
		productText.Text = AboutInfo.Product;
		versionText.Text = string.Format(Label("AboutVersionFormat"), AboutInfo.Version);
		copyrightText.Text = AboutInfo.Copyright;
		_report = AboutInfo.SystemReport(Settings.Default.UseGpu, Label);
		systemText.Text = _report;
	}

	/// <summary>A string table entry, or the key itself when a table lacks it.</summary>
	private static string Label(string key) => Application.Current.TryFindResource(key) as string ?? key;

	/// <summary>Esc closes even while a text box has the focus (its edit behaviour would otherwise swallow the key).</summary>
	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
			return;
		}
		base.OnPreviewKeyDown(e);
	}

	private void OnNavigate(object sender, RequestNavigateEventArgs e)
	{
		Logger.Debug("About: opening {Uri}", e.Uri);
		try
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "About: could not open {Uri}", e.Uri);
		}
		e.Handled = true;
	}

	private void OnToggleLicense(object sender, RoutedEventArgs e)
	{
		bool show = licenseBox.Visibility != Visibility.Visible;
		if (show && licenseView.Document == null)
		{
			licenseView.Document = AboutInfo.LicenseDocument();
		}
		licenseBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		// the extra height of a resized window goes to the document while it is shown, else to the gap above the buttons
		licenseRow.Height = show ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
		fillerRow.Height = show ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
		licenseToggle.SetResourceReference(ContentProperty, show ? "AboutHideLicense" : "AboutShowLicense");
		// the window follows its content while the document is hidden (it grows by the box's fixed height when the
		// document appears and shrinks when it goes) and is sized by hand while the document is shown, since a window
		// sized to content snaps back after a resize and a star row under content sizing would take the whole
		// document; the switch waits for the layout that lets the window grow, then frees the box to fill its row
		if (show)
		{
			Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
			{
				SizeToContent = SizeToContent.Manual;
				licenseBox.ClearValue(HeightProperty);
			});
		}
		else
		{
			licenseBox.Height = LicenseBoxHeight;
			SizeToContent = SizeToContent.Height;
		}
	}

	private void OnCopy(object sender, RoutedEventArgs e)
	{
		try
		{
			Clipboard.SetText(_report);
			Logger.Info(Label("AboutCopied"));
		}
		catch (System.Runtime.InteropServices.ExternalException ex)
		{
			Logger.Warn(ex, "About: the clipboard is held by another program");
		}
	}
}
