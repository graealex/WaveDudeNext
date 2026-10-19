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
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Xaml;

namespace WaveDudeNext.WPF.Markup;

/// <summary>
/// Base of the markup extensions that read a model property's <see cref="System.ComponentModel"/>
/// metadata, so labels and tooltips in the views come from the one place that defines them.
/// The member is written as <c>prefix:Type.Property</c>, resolved with the XAML namespaces in scope.
/// </summary>
public abstract class ModelMemberExtension : MarkupExtension
{
	/// <param name="member">Type and property, for example <c>model:Scene.Width</c>.</param>
	protected ModelMemberExtension(string member)
	{
		Member = member;
	}

	/// <summary>Type and property, for example <c>model:Scene.Width</c>.</summary>
	[ConstructorArgument("member")]
	public string Member { get; set; }

	/// <inheritdoc/>
	public override object ProvideValue(IServiceProvider serviceProvider)
	{
		int dot = Member.LastIndexOf('.');
		if (dot <= 0 || dot == Member.Length - 1)
		{
			throw new System.Windows.Markup.XamlParseException($"'{Member}' is not of the form prefix:Type.Property.");
		}
		var resolver = (IXamlTypeResolver?)serviceProvider.GetService(typeof(IXamlTypeResolver))
			?? throw new InvalidOperationException("No XAML type resolver available.");
		Type type = resolver.Resolve(Member[..dot]);
		string name = Member[(dot + 1)..];
		PropertyInfo property = type.GetProperty(name)
			?? throw new System.Windows.Markup.XamlParseException($"'{type.Name}' has no property '{name}'.");
		// a string table entry wins over the attribute and follows the language (a DynamicResource); the attribute
		// is the English source and the fallback (2026-10-14; the tables are generated from the attributes)
		string key = $"{Prefix}.{type.Name}.{name}";
		if (Application.Current?.TryFindResource(key) != null)
		{
			return new DynamicResourceExtension(key).ProvideValue(serviceProvider);
		}
		return Provide(property);
	}

	/// <summary>The resource key prefix of this kind of text ("Label" or "Hint").</summary>
	protected abstract string Prefix { get; }

	/// <summary>Produces the value for the resolved property.</summary>
	protected abstract object Provide(PropertyInfo property);
}

/// <summary>The property's <see cref="DisplayNameAttribute"/>, or its name.</summary>
public sealed class LabelExtension : ModelMemberExtension
{
	/// <inheritdoc/>
	public LabelExtension(string member) : base(member)
	{
	}

	/// <inheritdoc/>
	protected override string Prefix => "Label";

	protected override object Provide(PropertyInfo property) =>
		property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name;
}

/// <summary>The property's <see cref="DescriptionAttribute"/> text, or null without one (no tooltip).</summary>
public sealed class HintExtension : ModelMemberExtension
{
	/// <inheritdoc/>
	public HintExtension(string member) : base(member)
	{
	}

	/// <inheritdoc/>
	protected override string Prefix => "Hint";

	protected override object Provide(PropertyInfo property) =>
		property.GetCustomAttribute<DescriptionAttribute>()?.Description!;
}
