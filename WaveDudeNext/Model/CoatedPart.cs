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
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace WaveDudeNext.Model;

/// <summary>
/// How the index runs through an anti-reflection band (2026-10-29, the user's request: the author's drawn scenes give
/// a glass face a graded ramp, and the parts had only the layer).
/// </summary>
public enum CoatingProfile
{
	/// <summary>A band at the index √n throughout: a quarter-wave layer when it is λ / (4 √n) thick, the least reflecting then.</summary>
	Layer,
	/// <summary>
	/// The index rises over the band from the medium outside to the glass as a raised cosine, (1 − cos πt) / 2 of the
	/// depth over the thickness (<see cref="Parts.PartShape.GradedProfile"/>): broadband, and the thicker the better; the
	/// figures are under "Coatings" in OpticalParts.md.
	/// </summary>
	Graded,
}

/// <summary>
/// A refractive part with an anti-reflection band along its outline (a lens, a shape, a GRIN lens; 2026-10-29, the
/// user's request): the band's <see cref="Coating"/> thickness, its <see cref="CoatingProfile"/>, and which faces carry
/// it. A face is classed by its outward normal in the part frame: within 45° of −x', the side a source on the left
/// meets, it is the front, within 45° of +x' the back, otherwise a side; so a lens's first surface, second surface and
/// rim, and a rectangle's four faces when its length runs along the beam. Every face is coated by default.
/// </summary>
public abstract class CoatedPart : OpticalPart
{
	/// <summary>Default <see cref="CoatingProfile"/>.</summary>
	public const CoatingProfile DefaultCoatingProfile = CoatingProfile.Layer;

	private int _coating;
	private CoatingProfile _coatingProfile = DefaultCoatingProfile;
	private bool _frontCoated = true;
	private bool _backCoated = true;
	private bool _sidesCoated = true;

	/// <summary>Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) thick.</summary>
	[XmlAttribute]
	[DefaultValue(0)]
	[Range(0, MaxCoating)]
	[Category("Material")]
	[DisplayName("AR coating")]
	[Description("Thickness in pixels of the anti-reflection band just inside the outline; 0 for none. A quarter-wave layer is λ / (4 √n) pixels thick, λ being the wavelength the source form shows; a graded band is the better the thicker, from a wavelength on.")]
	public int Coating
	{
		get => _coating;
		set => SetProperty(ref _coating, value);
	}

	/// <summary>How the index runs through the band: a layer at √n, or a graded rise from the medium outside to the glass.</summary>
	[XmlAttribute]
	[DefaultValue(DefaultCoatingProfile)]
	[Category("Material")]
	[DisplayName("Coating profile")]
	[Description("How the index runs through the band: a layer at √n, the quarter-wave idea, or a graded rise from the medium outside to the glass. A graded band reflects less than a layer once it is a wavelength thick or more, at any wavelength; a layer only at the wavelength it is cut for.")]
	public CoatingProfile CoatingProfile
	{
		get => _coatingProfile;
		set => SetProperty(ref _coatingProfile, value);
	}

	/// <summary>Whether the front faces carry the band: those whose outward normal lies within 45° of −x', the side a source on the left meets.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the front")]
	[Description("Put the band on the front: the faces turned toward a source on the left, a lens's first surface. Clear it to leave them bare, as the inner faces of a cavity are.")]
	public bool FrontCoated
	{
		get => _frontCoated;
		set => SetProperty(ref _frontCoated, value);
	}

	/// <summary>Whether the back faces carry the band: those whose outward normal lies within 45° of +x'.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the back")]
	[Description("Put the band on the back: the faces turned away from a source on the left, a lens's second surface. Clear it to leave them bare.")]
	public bool BackCoated
	{
		get => _backCoated;
		set => SetProperty(ref _backCoated, value);
	}

	/// <summary>Whether the other faces carry the band: a lens's rim, a slab's narrow ends.</summary>
	[XmlAttribute]
	[DefaultValue(true)]
	[Category("Material")]
	[DisplayName("Coat the sides")]
	[Description("Put the band on the sides: the faces that are neither front nor back, a lens's rim, a slab's narrow ends. Clear it to leave them bare.")]
	public bool SidesCoated
	{
		get => _sidesCoated;
		set => SetProperty(ref _sidesCoated, value);
	}

	/// <inheritdoc/>
	public override void Normalize()
	{
		base.Normalize();
		Coating = Math.Clamp(Coating, 0, MaxCoating);
		if (!Enum.IsDefined(CoatingProfile))
		{
			CoatingProfile = DefaultCoatingProfile;
		}
	}
}
