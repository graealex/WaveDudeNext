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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using NLog;
using WaveDudeNext.App.Commands;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Localization;

/// <summary>One entry of the Language menu: a language code ("" for the system language) and the name shown.</summary>
/// <param name="Code">Two-letter code of the string table, "" for the system's language.</param>
/// <param name="Name">The language's own name ("Deutsch"), or the localised word for "System".</param>
public sealed record LanguageOption(string Code, string Name)
{
	/// <summary>The name, for a menu that shows the record itself.</summary>
	public override string ToString() => Name;
}

/// <summary>
/// The user-interface language (View > Language): swaps the <c>Strings/Strings.XX.axaml</c> table among the merged
/// dictionaries of the application resources, as upstream did, and writes the choice to <see cref="Settings.Language"/>
/// (nothing is saved here; the window saves on close). "" follows the system's display language, falling back to
/// English. Everything that reads its text through DynamicResource changes at once: replacing an entry of
/// <c>MergedDictionaries</c> raises the resources-changed notification Avalonia's DynamicResource listens to (verified
/// in 12.1.5: ResourceDictionary's merged list is an AvaloniaList whose Replace removes the old owner and adds the
/// new one, each notifying the application and, through it, every window). Texts taken once in code are the window's
/// to refresh; it listens to <see cref="PropertyChanged"/>.
/// <para>
/// The table in place is found by content, not by its file name: the compiler turns the <c>ResourceInclude</c> in
/// App.axaml into a plain ResourceDictionary with no Source left to read (Avalonia's XamlIncludeGroupTransformer), so
/// the strings dictionary is the merged provider that resolves a key every table has (<c>MenuFile</c>); the includes
/// this class puts in its place keep their Source and match by it as well. UI thread only.
/// </para>
/// </summary>
public sealed class LanguageController : INotifyPropertyChanged
{
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	/// <summary>A key every string table defines, used to recognise the table among the merged dictionaries.</summary>
	private const string ProbeKey = "MenuFile";

	/// <summary>The string tables that exist, by code, with the language's own name, in menu order.</summary>
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
		// the menu may hand over the option it shows or the bare code; anything else means the system language
		SelectCommand = new RelayCommand<object>(parameter => Current = parameter switch
		{
			LanguageOption option => option.Code,
			string code => code,
			_ => "",
		});
	}

	/// <summary>The single controller of the application.</summary>
	public static LanguageController Instance { get; } = new();

	/// <summary>
	/// Menu entries: "System" first (its name the English word; the menu shows the <c>LanguageSystem</c> text of the
	/// language in use through <c>LanguageNameConverter</c>), then every string table. One list for the controller's
	/// life (2026-11-11): a fresh list per change, as the WPF head's controller gives, made Avalonia re-create the
	/// open menu's entries under the one being clicked, and the menu stayed open, since its handler closes the menu by
	/// the clicked entry's parent after the click, and the entry had none any more.
	/// </summary>
	public IReadOnlyList<LanguageOption> Options { get; } =
		[new LanguageOption("", "System"), .. Tables.Select(t => new LanguageOption(t.Code, t.Name))];

	/// <summary>Selects the language of its parameter: a <see cref="LanguageOption"/> or its code.</summary>
	public ICommand SelectCommand { get; }

	/// <summary>The chosen code, "" for the system language; setting it swaps the strings at once and writes the setting.</summary>
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
			Settings.Default.Language = value;
			Logger.Info("Language changed to '{Language}' (strings: {Table})", value.Length == 0 ? "system" : value, Effective);
			ApplyResources();
			OnPropertyChanged();
			OnPropertyChanged(nameof(Effective));
		}
	}

	/// <summary>The string table in use: <see cref="Current"/>, or for "" the system's display language when a table exists for it, else "en".</summary>
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

	/// <summary>Takes the language from the settings and applies it; called once at start-up, before the main window shows.</summary>
	public void Apply()
	{
		_current = Settings.Default.Language ?? "";
		Logger.Debug("Language: '{Language}' (strings: {Table})", _current.Length == 0 ? "system" : _current, Effective);
		ApplyResources();
		OnPropertyChanged(nameof(Current));
		OnPropertyChanged(nameof(Effective));
	}

	/// <summary>The address of a string table's XAML in the application assembly.</summary>
	public static Uri TableUri(string code) =>
		new($"avares://WaveDudeNext.App/Strings/Strings.{code.ToUpperInvariant()}.axaml");

	private void ApplyResources()
	{
		if (Application.Current is not { } app)
		{
			return;
		}
		var table = new ResourceInclude((Uri?)null) { Source = TableUri(Effective) };
		IList<IResourceProvider> merged = app.Resources.MergedDictionaries;
		int index = IndexOfStringsTable(merged);
		if (index >= 0)
		{
			merged[index] = table;
		}
		else
		{
			merged.Add(table);
		}
	}

	/// <summary>The merged dictionary holding the strings: an include of a table by its Source, or the compiled table by a key it defines; -1 when there is none.</summary>
	private static int IndexOfStringsTable(IList<IResourceProvider> merged)
	{
		for (int i = 0; i < merged.Count; i++)
		{
			IResourceProvider provider = merged[i];
			if (provider is ResourceInclude { Source: { } source })
			{
				if (source.OriginalString.Contains("/Strings/Strings.", StringComparison.OrdinalIgnoreCase))
				{
					return i;
				}
				continue;
			}
			if (provider.TryGetResource(ProbeKey, null, out _))
			{
				return i;
			}
		}
		return -1;
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
