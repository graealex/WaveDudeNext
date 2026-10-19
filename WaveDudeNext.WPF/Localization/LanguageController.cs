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

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using NLog;
using WaveDudeNext.WPF.Commands;

namespace WaveDudeNext.WPF.Localization;

/// <summary>One entry of the Language menu: a language code ("" for the system language) and the name shown.</summary>
/// <param name="Code">Two-letter code of the string table, "" for the system's language.</param>
/// <param name="Name">The language's own name ("Deutsch"), or the localised word for "System".</param>
public sealed record LanguageOption(string Code, string Name)
{
	/// <summary>The name, for a menu that shows the record itself.</summary>
	public override string ToString() => Name;
}

/// <summary>
/// The user-interface language (2026-10-14): swaps the <c>Strings/Strings.XX.xaml</c> dictionary among the
/// application's merged resources, like <see cref="Theming.ThemeController"/> swaps the colour table, and
/// persists the choice through <c>Settings.Language</c>. "" follows the Windows display language, falling
/// back to English. Everything that reads its text through a <c>DynamicResource</c> changes at once; the
/// few texts taken through <c>StaticResource</c> or at construction (dock pane titles, the tree's folder
/// labels, an open picker list) change at the next start. The Properties pane's labels come from the
/// model's attributes and are English only. UI thread.
/// </summary>
public sealed class LanguageController : INotifyPropertyChanged
{
	private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>The string tables that exist, by code, with the language's own name.</summary>
	private static readonly (string Code, string Name)[] Tables =
	[
		("en", "English"),
		("de", "Deutsch"),
		("fr", "Français"),
		("nl", "Nederlands"),
	];

	private string _current = "";

	private LanguageController()
	{
		SelectCommand = new RelayCommand<string>(code => Current = code ?? "");
	}

	/// <summary>The single controller of the application.</summary>
	public static LanguageController Instance { get; } = new();

	/// <summary>Menu entries: "System" (in the current language) first, then every string table. A fresh list on each read, so a menu bound to it re-labels after a change.</summary>
	public IReadOnlyList<LanguageOption> Options =>
		[new LanguageOption("", Application.Current.TryFindResource("LanguageSystem") as string ?? "System"), .. Tables.Select(t => new LanguageOption(t.Code, t.Name))];

	/// <summary>Selects the language whose code is the parameter.</summary>
	public ICommand SelectCommand { get; }

	/// <summary>The chosen code, "" for the system language; setting it swaps the strings at once.</summary>
	public string Current
	{
		get => _current;
		set
		{
			value ??= "";
			if (_current == value)
			{
				return;
			}
			_current = value;
			Logger.Info("Language changed to '{Language}' (strings: {Table})", value.Length == 0 ? "system" : value, Effective);
			ApplyResources();
			OnPropertyChanged();
			OnPropertyChanged(nameof(Effective));
			OnPropertyChanged(nameof(Options));
		}
	}

	/// <summary>The string table in use: <see cref="Current"/>, or for "" the Windows display language when a table exists for it, else "en".</summary>
	public string Effective
	{
		get
		{
			string code = _current.Length > 0 ? _current : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
			return Tables.Any(t => t.Code == code) ? code : "en";
		}
	}

	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Applies the current choice to the application resources (start-up).</summary>
	public void Refresh()
	{
		Logger.Debug("Language: '{Language}' (strings: {Table})", _current.Length == 0 ? "system" : _current, Effective);
		ApplyResources();
	}

	private void ApplyResources()
	{
		var table = new ResourceDictionary { Source = new Uri($"/WaveDudeNext.WPF;component/Strings/Strings.{Effective.ToUpperInvariant()}.xaml", UriKind.Relative) };
		var merged = Application.Current.Resources.MergedDictionaries;
		for (int i = 0; i < merged.Count; i++)
		{
			string? source = merged[i].Source?.OriginalString;
			if (source != null && source.Contains("/Strings/Strings.", StringComparison.OrdinalIgnoreCase))
			{
				merged[i] = table;
				return;
			}
		}
		merged.Add(table);
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
