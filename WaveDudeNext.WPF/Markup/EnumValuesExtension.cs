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

using System.Windows.Markup;
using System.Xaml;

namespace WaveDudeNext.WPF.Markup;

/// <summary>
/// All values of an enum, for an <c>ItemsSource</c>. The type is written with its XAML prefix,
/// for example <c>{meta:EnumValues model:SceneMode}</c>.
/// </summary>
public sealed class EnumValuesExtension : MarkupExtension
{
	/// <param name="type">The enum type with its XAML prefix.</param>
	public EnumValuesExtension(string type)
	{
		Type = type;
	}

	/// <summary>The enum type with its XAML prefix.</summary>
	[ConstructorArgument("type")]
	public string Type { get; set; }

	/// <inheritdoc/>
	public override object ProvideValue(IServiceProvider serviceProvider)
	{
		var resolver = (IXamlTypeResolver?)serviceProvider.GetService(typeof(IXamlTypeResolver))
			?? throw new InvalidOperationException("No XAML type resolver available.");
		Type type = resolver.Resolve(Type);
		if (!type.IsEnum)
		{
			throw new System.Windows.Markup.XamlParseException($"'{Type}' is not an enum.");
		}
		return Enum.GetValues(type);
	}
}
