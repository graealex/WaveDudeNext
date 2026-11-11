# Optical parts

The reference for the optical parts: how they reach the simulation, what each kind is, and why it was built so, with
the measurements behind the choices. The code's `///` docs carry the details of each member; `CLAUDE.md` holds a summary
and the working rules, which apply here too. The plan this file began as (2026-10-18, stages 1 and 2) is in git history.

Jeroen Vleggaar proposed parts as project objects, added like sources, to demonstrate optics without an external image
editor ("a cavity of mirrors keeps a wave for ever"); the user decided their form. The gates and the zone plate came
from Jeroen's sessions (2026-10-21/22), merged from `jv/optical-logic`. The diffuser, the interference filter, the
gratings, the meta surfaces, the Fresnel lens and the probabilistic detectors came from his sessions of 2026-11-07/08,
integrated on 2026-11-09 from his patch against 0.1.3 (delivered as "Diffusor", renamed before any release). In the
sections of his sessions, and in the comments of their code, "the user" is Jeroen.

## Adding a part kind

The class is the kind (2026-10-22: the `PartKind` enum was removed).

1. A concrete subclass of `OpticalPart` (or `ShapePart`, `LogicGatePart`) in a file of its own in `Model/`, named after
   the class, with its enums and item classes (`FiberPart.cs` holds `FiberEnds` and `FiberNode`); it has `NameStem`
   (the English stem of new names, "Zone plate"), `PrepareNew()` if a new part needs something a loaded one must not
   get (a fibre's default path: `XmlSerializer` appends a file's items to the list it finds), and its properties under
   `CLAUDE.md`'s model conventions (`[Range]` beside every clamp in `Normalize()`).
2. Its shape in `Parts/PartShape.cs` (`PartShape.For`) and, if it has one, its size grip in `SceneOverlay.GripOf` and
   `SetGrip`. A shape whose `Depth` and `InnerEdgeDistance` are true distances (they change by at most the distance
   moved) says so with `DepthIsDistance`, and the painter settles runs of pixels from one centre's depths; one that is
   not stays pixel by pixel, which is always right.
3. An `[XmlElement("Name", typeof(…))]` line on `Project.Parts`: one element name per kind.
4. Its entry in `PartMenuEntries` (`Panes/ProjectTree.xaml`), where the app decides which kinds it offers and in which
   order; both menus take their entries from there.
5. A form in `Views/` with its `DataTemplate` in `PropertiesPane.xaml` (a template for a base class serves its
   subclasses: one for `LogicGatePart`), and its strings (`MenuAdd…`, `Label.`, `Hint.`, `Enum.` for its enums) in all
   four tables.
6. A 16 px drawing in `WaveDudeNext.WPF/Themes/Icons.xaml` (2026-10-23; the one icon file of both heads since 2026-11-10, the
   Avalonia head builds its copy from it): `Icon<Name>` stroked and, for solid parts, `Icon<Name>Fill`, `<Name>` the class
   name without "Part", 1 px lines on half pixels, the ink in rows and columns 0 to 14; the menu entry names it
   (`controls:MenuIcon.Drawing`, in both heads) and the tree row finds it by the class name (`PartIconConverter`).

`library_check` finds every part class by reflection: it fails one without its file element, one that `AddPart`
cannot add as "<stem> 1", and a clamped number without its `[Range]`. The menu entry, the form and the drawing are not
checked.

## How parts reach the simulation

- **Painted into the scene pixels.** The scene reaches the engines only as BGRA pixels (`SceneData.FromBgra`: red is
  the index, green the image sources, blue the damping). `PartPainter.Paint` paints the enabled parts, in tree order,
  later over earlier, into a copy of the scene image or a blank canvas; the engines, the glowing outline, the export
  outline and the scratch frames see them as part of the scene. `MainWindow.Live`'s `LoadSceneImage` caches the result
  by (image, parts version, size, background), so an unchanged scene stays the same reference and the scratch runner
  keeps its engine. A part edit is a simulation edit.
- **Red is the index**, 1.00 to 2.55 in steps of 0.01 (red = index × 100, Jeroen's range); **red 0 is a mirror**
  (index 1000, Jeroen's V0.30 rule); red 1..99 is index 1. **Blue is damping** (each step a cell's velocity times
  1 − blue / 255), painted by absorbing parts keeping the larger of their value and the blue already there. **Green is
  never touched**: its shades are source frequencies (WaveSimulator2D's warning: never anti-alias green).
- **Rasterisation**: 4 × 4 supersampling per pixel for refractive parts, each sample classified (outside, coating,
  body, a fibre's or GRIN lens's zone) and the indices averaged (an existing red 0 counting as index 1), rounded to red.
  A mirror uses the pixel-centre test alone and writes red 0: a half-covered mirror pixel would otherwise round to an
  index and be transparent. A part's centre is the corner of pixel (X, Y), so even sizes, every default, cover whole
  pixels. A part whose index is not painted (`PaintsIndex` false, the zone plate) writes blue alone. Only pixels near
  an edge are supersampled: the shape's depth at the pixel centre is a distance, so a pixel farther than a sample's
  reach (0.6 px) from the outline, the coating band's inner edge and every inner edge (a fibre's core, a stack's layers)
  takes its centre's index in one go. And where the shape says its depths are distances (`PartShape.DepthIsDistance`:
  the polygon shapes, the fibre, the Bragg stack, the GRIN lens, the zone plate; not the lens, whose surface depth is
  the radius beyond a convex surface's centre and minus infinity beyond a concave one's, nor the gate's first-order arm
  distance), one pixel centre settles a run of the pixels after it along the row (2026-11-04): a centre k pixels on is
  at most k nearer to every edge, so the next floor(e − 0.6 − allowance) pixels, e the nearest edge's distance, are
  outside too or body too, and only the body index is asked of them (`BodyIndexAt`); a fibre's `SkipAllowance` covers
  the one place its depth jumps, the wedge between a flat face's plane and the end chord's perpendicular at a bent end,
  by (R + margin) tan²φ / 2 per end, φ the angle between the face's tangent and the chord. `library_check` paints every
  example's parts and every kind at four angles both ways, with runs and pixel by pixel, and the bytes must agree. A
  fibre sample runs its chords once for depth, index and face together (`Evaluate`), with each chord's run and length
  precomputed; on a blank Full HD canvas the painting of a 400 × 330 outline went 3.6 → 0.8 ms, a hexagon 2.3 → 0.7, an
  S-bent fibre across the canvas 19 → 9.6.
- **The angle** is in degrees, counter-clockwise on the screen, 0 pointing the part's axis x' along +x (the composite
  source's convention); y' is across. Shapes face a source on the left (-x').
- **The AR coating** (`CoatedPart`: lens, shapes, GRIN lens) is a band of `Coating` pixels just inside the outline: with
  `CoatingProfile` `Layer` at the index √n (rounded to 0.01), the quarter-wave idea without a tie to a wavelength, a
  quarter-wave band being λ / (4 √n) pixels thick, λ the wavelength the source form shows; with `Graded` (2026-10-29, the
  user's request, after Jeroen's drawn scenes, whose glass faces ramp) the index rises from the medium outside, the index
  the pixel had before the part, to the body as a raised cosine of the depth over the thickness
  (`PartShape.GradedProfile`, measured under Coatings). `FrontCoated`, `BackCoated` and `SidesCoated` choose the faces,
  classed by their outward normal in the part frame: within 45° of −x' the front, of +x' the back, else a side (a lens's
  first surface, second surface and rim; a rectangle's four faces when its length runs along the beam); inside the band
  the nearest face decides, so a cavity's inner faces can stay bare. The fibre is a `CoatedPart` too (its start face the
  front, its end face the back, the tube the sides). Mirrors and Bragg reflectors ignore it; the gates' bands are their
  own measured design, a layer on the ends they coat.

## Absorption

A lens or shape absorbs with `Absorption` (0..255 as blue) rising over `AbsorptionRamp` pixels from 0 at the outline as
blue = round(Absorption × (δ / ramp)²), δ the depth of the pixel centre (no supersampling: the profile is continuous
and 0 at the outline); a part thinner than twice the ramp reaches its full value in its middle (Jeroen's point 11,
2026-10-20).

A jump in damping reflects like a jump in index, so the profile and strength were measured (2026-10-20): a pulse from
an open grid onto a deep absorbing slab, its reflected peak against a mirror slab's, for waves of level 120 and 60. At
absorption 255 every profile reflects a lot; over a 40 px ramp: linear 9 / 25 %, quadratic 2 / 11 %, cubic
1.7 / 9 %, a smooth step 4.7 / 20 %, an error-function step 4.2 / 18 %, Jeroen's √ band 4.5 / 16 %. At 64 a quadratic
80 px ramp reflects 0.5 / 1.5 % and lets nothing through 160 px; at 16 the reflection is lower still, but up to 6 %
passes through 160 px. Quadratic was chosen (`PartShape.AbsorptionProfile`); 64 is the strength of the gates' absorbing
cladding rim (`LogicGateDesign.Absorption`).

## Coatings

The graded band was measured the same way (2026-10-29): a plane pulse of six cycles, a column of emitters, on a deep
slab whose face carries the band, the reflected pulse's peak amplitude in a box before the face against a mirror face's,
for indices 2.55 and 1.50 at wavelengths of 10, 20 and 32 px with the 4th-order stencil. Amplitude as a share of the
mirror's (the reflectance is its square); the band profiles in the order linear, smooth step, quintic, raised cosine:

| face, n = 2.55 | λ 10 px | λ 20 px | λ 32 px |
|---|---|---|---|
| bare (theory 43.7 %) | 54 % | 46 % | 45 % |
| quarter-wave layer at √n, rounded to whole pixels (2, 3, 5) | 25 % | 8.8 % | 7.4 % |
| graded, ½ λ thick | 10.4 / 12.5 / 13.9 / 12.2 % | 11.2 / 11.8 / 13.5 / 11.6 % | 10.9 / 11.2 / 12.8 / 11.3 % |
| graded, 1 λ | 5.6 / 4.3 / 4.0 / 4.1 % | 6.1 / 4.1 / 4.4 / 4.1 % | 6.1 / 4.1 / 4.1 / 3.6 % |
| graded, 2 λ | 2.8 / 1.5 / 0.6 / 1.0 % | 3.2 / 0.9 / 0.8 / 1.0 % | 3.2 / 1.1 / 0.5 / 0.7 % |

For n = 1.50 (bare 20 to 22 %) the layer reflects 3.6 to 4.1 %, a ½ λ band 3.4 to 7.3 %, a 1 λ band 0.9 to 2.2 % and a
2 λ band 0.3 to 1.2 %, the profiles ordered as above. The raised cosine, (1 − cos πt) / 2, was chosen
(`PartShape.GradedProfile`): best or tied at one wavelength and within a few tenths of a percent of the quintic at two,
where the linear ramp reflects three to four times as much; the linear ramp wins only at half a wavelength, by a percent.
A layer beats every band only when the band is thinner than half a wavelength, and at λ = 10 px the rounding of its
thickness to 2 px (1.6 is the quarter wave) costs it most. Jeroen's drawn scenes ramp linearly over 20 to 22 px.

## The kinds

Every part has `Name`, `Enabled` (a disabled part is not painted), `X`, `Y` (its centre, ≥ 0) and `Angle` (−180, 180].
Defaults and ranges are in the attributes beside each property; the figures below are the defaults.

**Lens** (`LensPart`, `<Lens>`): `Diameter` 200 across the axis, `Thickness` 40 on the axis, signed radii `R1` 300 (the
left surface) and `R2` −300 (R1 > 0 and R2 < 0 bulge outward, 0 is flat), `RefractiveIndex` 1.50, the coating of
`CoatedPart`, `Absorption`, `AbsorptionRamp` 40, `Mirror`. The strip |y'| ≤ D/2 between the surfaces x'₁ = −t/2 + s₁(y') and
x'₂ = t/2 + s₂(y'), the sag s(y') = R − sign(R)·√(R² − y'²). The diameter and the centre thickness are the bulk: a radius
too curved for them is raised to the smallest that fits (`FitRadii`), so the lens fills its diameter and the edge is
never negative (a diameter of 2000 with radii of 300 had given a tiny lens). The form shows the edge thickness, and a
"Concave" box flips both radii. A curved mirror is the same shape with `Mirror` on; ticking the box renames the part, Lens to Mirror and back
(`LensPart.MirrorName`, applied by the box's command, 2026-10-29, the user's design), and the tree row's icon is the
part's own profile, each surface convex, flat or concave by its sag at the rim (`ShapeOf`: flat under half a pixel, so
Example 1's mirror of radii 14401 on 104 px reads flat, the user's report of 2026-10-29). On the pictures the size grip
stretches the lens as a rectangle's grip does: across, the diameter with the profile held; along, the whole profile
with its sags and edge, so a thinner lens is the same lens squashed; diagonally, the lens as a whole; the arcs are
refitted as circles through the stretched rim and vertex, and nothing stops the grip (`SetExtents`; 2026-10-26, Jeroen's
report: a grip dragged a little inward had the radii fitted flat and the lens was a rectangle, and held sags made a
stretched lens a slab). A handle on each surface's vertex sets its radius
(`SetVertex`, Jeroen's proposal): dragged along the axis with the rim and the other surface held, the sag at the rim
gives the radius by the sagitta, outward bulging, through the rim plane flat, then concave, a hemisphere at most; the
centre thickness follows and the position moves to the new centre. A drag holds the rim and the other vertex as it
found them (`MarkerThumb.DragAnchors`), so the position's rounding never creeps into them. A radius a grip or a vertex
handle sets is rounded to a hundredth of a pixel (`RadiusOfSag`; 2026-10-28, Jeroen's report: sixteen decimals in the
box and the file), R1 towards +∞ and R2 towards −∞, so a convex surface gets a hair flatter and a concave one a hair
deeper, never the other way, and the rounding never brings on the refit of a lens whose surfaces meet at the rim.

**Shapes** (`ShapePart`, a `CoatedPart`: `Mirror`, `RefractiveIndex` 1.50, the coating, `Absorption`, `AbsorptionRamp`
40, over an outline each kind generates). One class per shape, not a shape mode inside one class (the user's point, 2026-10-18: a
side length alone made every rectangle a square):
- `PolygonPart` (`<Polygon>`): regular, `Sides` 6 (3..64) of `Side` 60 px, a face centred on -x'; for an odd count the
  first vertex lies on +x'.
- `RectanglePart` (`<Rectangle>`): `Length` 60 along, `Width` 60 across. A flat mirror is a long, narrow one.
- `EllipsePart` (`<Ellipse>`, 2026-10-20): `Length` and `Width` 60; outlined as a polygon of a vertex per 3 px of
  circumference, 24 to 180. Rectangle and ellipse carry `Linked`, the image editors' chain between width and length,
  which keeps them equal through the boxes, the grip and the file (a square, a circle).
- `TrianglePart` (`<Triangle>`): a `Base` of 60 across the axis toward -x', the apex along +x', base angles `AngleA` (at
  -y') and `AngleB`; `Type` presets (equilateral, right isosceles, 30-60-90, isosceles with one typed angle, custom); the
  form shows the apex angle.
- `OutlinePart` (`<Outline>`): free `Points` relative to the centre before rotation; fewer than three paint nothing.

**Diffuser** (`DiffuserPart`, `<Diffuser>`, 2026-11-07, the user's request, among the main parts after the zone plate): a
`ShapePart` (the material, the coating and the absorption of the shapes) of `Length` 50 along the axis and `Width` 500
across, whose front face (−x') is rough and whose back is flat; a new one coats the back alone (`PrepareNew`; the user's
defaults of 2026-11-07, as are the sizes and the roughness). The roughness is a sum of sinusoids across the face:
one at `CenterWavelength` 16 px with `Amplitude` 1 px, and about it wavelengths spread by `Sigma` 6 px, every half sigma
out to three sigmas (13 in all, `SpreadSigmas`, `StepsPerSigma`), each weighted by the bell e^(−(λ − centre)² / 2σ²),
wavelengths below 2 px left out (the profile is sampled a pixel apart); sigma 0 is the centre wavelength alone. The
phases are random, from the project's seed and the part's name through `Pcg32` as a source's jitter is (`ISeeded`: the
project hands `ProjectSeed` to such a part when it is added, loaded, pasted or reseeded; never stored), drawn per
component in a fixed order, so a change of the width or the sigma keeps the phases of the components that stay. `Profile`
(the displacement at every whole pixel across the face, positive into the plate) is computed once and kept until the
width, a roughness value, the name or the seed changes (the user's rule), the array shared by a clone; the form shows
its height from crest to trough and the number of ripples. `FrontFace` holds the face at least a pixel before the back.
Its shape (`DiffuserShape`) is the polygon of the face, a vertex per pixel, with the three straight edges, the depth the
exact signed distance (`DepthIsDistance`, so the runs settle), found by a search of the face's segments outward from the
one under the point while their span in y' can still come nearer, a handful a sample where the vertex shape would visit
them all (proven against the plain polygon distance at every pixel centre by `library_check`); the face's segments are
the front, the flat back the back, the two short ends the sides, whatever the ripple's slope and at any angle, the
faces being classed in the part frame (the user's rule, 2026-11-07: the front coating is the rough face's, the back
coating the flat face's), so the band on the front follows the roughness. With no amplitude it paints byte for byte as the rectangle of its size. The size grip sits on
the back corner.

**Interference filter** (`InterferenceFilterPart`, `<InterferenceFilter>`, 2026-11-07, the user's request, after the
diffuser): a carrier plate, a `ShapePart` of `Length` 50 along the axis and `Width` 500 across (the user's defaults, as
the diffuser's), with a stack of `Layers` (`<Layer Thickness RefractiveIndex>` children, sparse, defaults 2.00 px and
1.50) on its front face (−x'). The user's choices of 2026-11-07: the stack lies outside the plate, so the plate keeps
its size and the part grows by the stack (`StackThickness`, the sum, shown in the form); the first layer in the list is
the outermost, the one a wave from the left meets first; thicknesses take two decimals (a boundary inside a pixel gives
it the samples' mean index, a layer of 0 is left out); a new filter (`PrepareNew`) gets a quarter-wave stack for λ 16 px,
the usual design that begins and ends with the high index (the user's choice the same evening, replacing ten layers with
the low index outermost): 2.00 at 2.00 px and 1.50 at 2.67 px five times from the outside in and one more 2.00 on the
plate, eleven layers, by thin-film theory about 92 % reflected at λ 16 on the default plate in air against 70 % for the
earlier ten; a reflector (`Mirror`) makes the plate red 0 and leaves the
layers glass in front of it. The plate's AR coating lies on its back alone (only `BackCoated` counts; the form hides the
other two boxes). It paints as two shapes (`PartShape.PiecesOf`): the plate (`FilterPlateShape`, the rectangle with the
exact distance, its absorption and band) and the stack (`FilterStackShape`, each layer its index, the boundaries between
layers inner edges, so the pixels near them are supersampled); a glass plate first and the stack over it, so the pixels
at the plate's front average both, a mirror plate last, so its pixels stay red 0. Both depths are distances, so the runs
settle. Without layers it paints byte for byte as the rectangle of its plate. The form lists the layers with a grip to
drag a row through the list, up and down buttons (the keyboard's way; the focus stays on the moved row's button), a
remove button, an add button that appends a copy of the last layer, and Remove all, which empties the list in one step
(the user's request, so another design need not start by removing eleven layers; Undo brings them back). The size grip sits on the plate's back corner.

**Facetted grating** (`FacettedGratingPart`, `<FacettedGrating>`, 2026-11-08, the user's request, the second entry of the
Diffraction gratings submenu): the diffuser's plate (`Length` 50, `Width` 500, a `ShapePart`'s material) with one
wavelength instead of a spread: `Pattern` sawtooth (the default, a ramp then a step), square wave or sinusoidal,
repeated every `Pitch` 32 px across the face with `Amplitude` 4 px either side of the mean (the user's choices after a
first look at 16 and 2), the back flat. The AR band
lies on the back alone (the user's rule; `BackCoated` alone counts, the form hides the other boxes). Both plates
implement `IProfiledPlate` (length, width, the front face a sample per pixel) and are painted by the one
`DiffuserShape`, which takes its face flags from a private constructor (the diffuser's own, the grating's back only);
a step of a square wave or a sawtooth stands between two samples, a vertical segment of the polygon. Its grip is the
back corner. `library_check` has its section ("facetted grating"), and the every-kind painter-runs case has two.

**Meta lens** (`MetaLensPart`, `<MetaLens>`, 2026-11-08, the user's request, the one entry of a "Meta surfaces" submenu
among the optics after the gratings; the user's choices: the fill computed from a design wavelength and a focal length,
ridges in front of the plate, the submenu with this one entry for now): the filter's carrier plate (`Length` 50, `Width`
500, a `ShapePart`'s material, the band on the back) with a sub-wavelength pattern on its front: a ridge of
`PatternIndex` 2.00 in every cell of `Pitch` 4 px across the face, `Height` 32 px in front of the plate, its width the
cell's fill factor times the pitch. The fill of cell j at y'ⱼ (the cells laid from one end, centred together on the
axis): the lens phase Φ = (2π/λ)(√(F² + y'²) − F) with λ from `DesignFrequency` 50 through the simulation's frequency
scale (`IFrequencyDesigned`, as the zone plate's) and `FocalLength` 500 (the user's choice), wrapped to a turn; the cell's delay a full turn
less that for `LensType` Converging (the fill greatest at the centre, falling outward within each zone), the wrapped
phase itself for Diverging; the effective index the ridge must give, 1 + (delay / 2π)·λ / H; and the fill from the
lamellar mean of n², f = (n_eff² − 1) / (n_p² − 1), clamped to 0..1 (`FillFactors`; a delay of exactly a full turn, the converging lens's and the axicon's centre cell, is kept as one and not wrapped to 0, so that cell is solid among its nearly solid neighbours; 2026-11-08, found by the `library_check` written that day). A full turn needs H = λ / (n_p − 1)
(`FullTurnHeight`, a readout beside the `ZoneCount`, each zone a turn to the rim); the default height is that for λ 32
and n 2.0. Painted as two shapes through `PiecesOf`: the plate (`FilterPlateShape`, given a private constructor the
filter's and the lens's share) and the pattern (`MetaPatternShape`: the union of the ridges' rectangles, the depth the
greatest of the three nearby ridges' distances, exact since the ridges stay in their cells, `DepthIsDistance`; the band
on the ridges' tops alone, `FrontCoated`; a mirror plate last, as the filter's). The form hides the sides box
(`ShapeMaterialView.SidesHidden`). The wave sees a cell as its mean index only while the pitch is well below the
wavelength, and the painter's 4 × 4 samples make a ridge's edge pixels a mean too, so the fill is realised to a
sixteenth of a pixel. `library_check` has its section ("meta surfaces"), and the every-kind painter-runs case has two.

**Meta prism and meta axicon** (`MetaPrismPart`, `<MetaPrism>`, `MetaAxiconPart`, `<MetaAxicon>`, 2026-11-08, the user's
requests, the second and third entries of the Meta surfaces submenu): the meta lens's plate and pattern with another
phase, so the three share the abstract `MetaSurfacePart` (the plate, the pattern, the design frequency and the fill
from a subclass's `Delay`; the lens's shared strings moved to `MetaSurfacePart.*`). The prism asks a line across the
face, −(2π/λ)·y'·sin θ wrapped, `Deflection` θ 20° counter-clockwise on the screen for a positive angle, so a plane
wave leaves turned by θ as a blazed grating's first order; a zone is λ / sin θ wide (`ZoneWidth`). The axicon asks a
full turn less (2π/λ)·|y'|·sin β wrapped, `ConeAngle` β 10°, so each half of the wave turns toward the axis and the
halves cross along a line of about half the width over tan β behind the surface (`LineLength`). Checks and figures
wait for the end with the lens's.

**Fresnel lens** (`FresnelLensPart`, `<FresnelLens>`, 2026-11-07, the user's request, in the menu after the lens): the lens
with each curved surface cut into `Zones` 4 zones from the axis to the rim (the user's choices: both curved surfaces, a
flat one staying flat), the arc restarted at the vertex plane at each zone's inner edge, so the glass between the arcs
is cut away. The zones are divided as `Division` says (`ZoneDivision`, the user's request the same day for both):
`EqualWidth`, the default, every zone the same width, the outer one the deepest; or `EqualDepth`, every zone the same
share of the rim's sag, the boundaries where the arc reaches it (`ZoneBound`: √(R² − (R − k·S/N)²)), the zones narrowing
outward as a lighthouse lens's elements do; each surface has its own boundaries by its radius. A new one is a plate of
`Thickness` 20, `Diameter` 500 (the user's choice: about 500) with radii of ±1500, at which four zones of equal width have
an outer sag of 9.2 px a surface, so the two fit in the plate (`PrepareNew`, so a loaded one keeps the file's values;
the lens's defaults 200 and ±300 would be refit at once to a far flatter lens). The lens's Concave box is not shown
for it (the user's choice: a negative radius hollows a surface). It derives from `LensPart`, now unsealed: every lens
setting, the Mirror box, the size grip and the two vertex handles are inherited, the form is the lens's with a Zones and
a Zone division row shown by `LensPartView.ZonesShown` (a template given its content only then, so a plain lens binds
nothing it has not), and the Fresnel template wins over the lens's for the form and the tree's icon (a sawtooth plate,
not the lens's profile). The lens reckons everywhere with `ZoneCount` and `ZoneDivision` (1 and equal width for itself)
through the deepest zone's sag (`ZoneSag`: for equal widths the sag at the rim less the sag at the outer zone's inner
edge, since the sag grows ever faster outward; for equal depths the rim's sag over the count): the fit of the radii
(`FitRadii` with a count and a division: a 10 px plate holds radii of about 450 where a whole lens needs about 1000),
the edge thickness, the rim of the profile (`RimX`), the reach of the rotation handle, and the radius a vertex handle or
the grip asks for (`RadiusOfZoneSag`: the sagitta of the whole sag for equal depths, by bisection for equal widths, the
outer zone's sag falling steadily with the radius). The outline is the stepped profile. In `LensShape`, `ZonedDepth`
finds the point's zone by |y'| (`ZoneIndex`), takes the surface's circle moved along the axis so it starts at the vertex
plane at the zone's inner edge, and bounds the zone's glass by the step across the axis: at the zone's inner edge for
a convex surface (the arc recedes outward, the next zone restarts ahead of it), at its outer edge for a concave one
(the arc advances, the zone's glass ends there), the distance along y' or to the step's inner corner; the sign is exact
everywhere, the distance inside the glass, so the band and the ramp follow the steps too. With one zone the circle is
unmoved and the lens paints as before (`library_check`: byte for byte, coating and absorption included). Sampled pixel
by pixel as the lens is (its surface depth is not a distance).

**Simple 2D grating** (`SimpleGratingPart`, `<SimpleGrating>`, 2026-11-07, the user's request, the one entry of a
"Diffraction gratings" submenu among the optics after the interference filter): a lattice of round dots, `Columns` 1
along the axis by `Rows` 31 across, `ColumnSpacing` and `RowSpacing` 16 px, `Diameter` 2 px (the user's defaults; the
composite source's lattice: an odd count keeps the middle dot on the position, an even one straddles it), nothing
between the dots and no carrier (the user's choice over a plate behind them). The dots are of one material, that of a
`ShapePart` (the user's choice over mirror alone or absorbing alone: `Mirror`, `RefractiveIndex`, `Absorption`,
`AbsorptionRamp`; a new grating is a mirror, `PrepareNew`); the coating of `CoatedPart` is not used and its rows are
hidden (`ShapeMaterialView.CoatingShown`). The diameter is at most half the smaller spacing (the user's rule), 1 at
least, a bound that depends on the spacings, so `Normalize()` clamps it without a `[Range]` (`MaxDiameter`, in
`library_check`'s `dependentBounds`); the form's box takes it as its top. A 2 px dot on a whole-pixel position (the
part's centre is a pixel corner, the spacings whole) is the 2 × 2 pixels about its centre, a mirror dot by the
pixel-centre test (a mirror dot under 2 px covers no centre and paints nothing, which the hint says); a glass dot's rim
pixels take their samples' mean index. Its shape (`GratingShape`) is the union of the discs: the depth is the radius less
the distance to the nearest dot centre, found by rounding along each axis of the lattice and clamping to its ends (the
lattice is axis-aligned in the part frame, so the nearest centre is the nearest along each axis), an exact signed
distance (`DepthIsDistance`), so the runs settle and only the pixels at a dot's rim are sampled. `Vertices()` is the box
around the lattice plus the radius, for the overlay's outline and the rotation handle's reach; no size grip (the extents
are counts and spacings).

**Fibre** (`FiberPart`, `<Fiber>`): `CoreIndex` 1.50, `CoreDiameter` 20, `CladdingIndex` 1.45, `CladdingThickness` 10
(0 for a bare core), the coating of `CoatedPart` (2026-10-29: the start face is the front, the end face the back, the
tube the sides; a graded band on the back alone is the author's output taper), `Absorption` and `AbsorptionRamp`
(2026-10-29, the user's request, after the author's fibre scene, whose fibres run between absorbing strips: the
cladding's absorbing band, a hump in its outer part 2 × ramp wide that rises and falls by the quadratic profile, so the
guided mode's tail in the inner cladding is untouched and stray light is eaten from either side without a damping step;
nothing on a bare core; a mode stripper), `Ends` Flat or Round. The path (2026-10-22, the user's request, replacing a polyline whose
joints were sharp bends) is cubic Bézier segments through `Nodes` (`<Node X Y InX InY OutX OutY Smooth>`, the handles
offsets from the node, sparse); a new fibre gets two nodes at (−100, 0) and (100, 0) with handles a third of the way
along. A smooth node (the default) lines its handles up when switched on (`AlignNode`; a node without handles is seeded
from its neighbours) and then turns the other handle opposite the one dragged or typed, keeping its length as captured
at the drag's start, so rounding does not creep in; a node added or loaded is taken as it is, so a file never changes on
loading. A flat end is cut across the curve's tangent at the endpoint, the coating following the face; a round end is
the distance test. The painter flattens the curve into chords within 0.05 px, bucketed on a 32 px cell grid (an S-bent
fibre across a Full HD canvas: 51 ms against 1.3 s without the grid), and supersamples the core edge (`HasInnerEdges`).

**Bragg reflector** (`BraggReflectorPart`, `<BraggReflector>`): `Pairs` 10 of layers `Thickness1` and `Thickness2`
10 px with `Index1` 1.50 and `Index2` 2.00, stacked along +x' and centred, `Width` 200 across. Thicknesses are pixels as
typed; a quarter-wave layer is λ / (4 n).

**GRIN lens** (`GrinLensPart`, `<GrinLens>`): a rectangle of `Length` 100 and `Diameter` 100 whose index falls from
`CentreIndex` 1.80 to `EdgeIndex` 1.50 as n(y') = n_c − (n_c − n_e)·(2y'/D)²; the coating of `CoatedPart`, its end
faces the front and back, its long sides the sides.

**Logic gates** (`LogicGatePart`: `XorGatePart`, `AndGatePart`, `OrGatePart`; `<XorGate>`, `<AndGate>`, `<OrGate>`;
under "+ Part" > Optical logic). One 2 × 2 directional coupler, the combining half of a Mach-Zehnder (the user's
choice, 2026-10-21): input A above the axis, B below, a phase section putting B `PhaseShift` ahead of A, and a 3 dB
coupler whose upper output carries (a + i·b) / √2 and lower (i·a + b) / √2 of two coherent inputs. The phase shift
alone makes the kind: +90° gives the upper output a − b (XOR), −90° a + b (AND, 4 : 1 at best; a passive linear gate
has no truer AND), +30° the waves at 120° (OR: ½, ½, ½). The phase section sits in a quarter-period stretch in all
three, so they share dimensions and ports and tile together (measured at 120 × 544 px: XOR 0.01; AND 1.88 against
0.50; OR 0.49 against 0.55).
- **Design**: entered as a source frequency (`DesignFrequency` 50); the wavelength follows from
  `Simulation.SourceFrequencyScale`, which `GatePortFollower` hands each gate (`FrequencyScale`), and the form shows it.
  `LogicGateDesign` lays the gate out with `WaveguideModes`, a mode solver on the engine's own discrete equations
  (4th-order stencil; its β matched a simulated guide to 1e-5 when measured, `library_check` holds it within 2e-3): smooth S-bends whose radius follows the mode's
  confinement, a measured coupling excess of 0.07 rad taken off. Defaults `CoreIndex` 2.55 in `CladdingIndex` 1.45,
  the phase section then lowered on arm A: port spacing 60, 120 × 544 px; both inputs leave about 1 % at the XOR output,
  the sum 1.9× (the user found 1600 px unacceptable).
- **Ends**: an AR band like a lens's (`Coating` 6 px, √n of core and cladding, the measured least reflection: 3.4 %
  against 24 % bare), which replaced an output taper; switchable per end (`InputCoated`, `OutputCoated`): a band in a
  junction keeps the wave from passing from one unit into the next. Both ends are open (the user's choice, 2026-10-22:
  sources are to be coupled in from outside by a fibre later), so staggered gates cascade; a source placed in the unit
  sits half a wavelength in, its backward wave leaving through the coated input end.
- **Outputs**: each can be switched off (`UpperOutputActive`, `LowerOutputActive`): an absorber rising over 2 λ in its
  arm's last stretch (0.03 % reflected, nothing out). The cladding's rim absorbs (`PartShape.AbsorptionShareAt`).
- **Tiling**: the unit is twice the port spacing wide, the ports at ¼ and ¾ (the user's request, for staggered tiling);
  the side absorber fills out to there, at least λ/2 (without it 7.6 % reached the XOR output). `PortSpacing` is raised
  to the design's minimum; the form's box edits `PortSpacingUsed`, so it shows the spacing used and every step moves the
  ports.
- **Inputs** are ordinary sources on the ports the form lists. `GatePortFollower` (one per project in the window) moves
  a source within 1 px of an input onto the input's new position after any gate edit; `Movable.MoveAll` moves parts
  last, so a group move does not move such a source twice.

**Zone plate** (`ZonePlatePart`, `<ZonePlate>`, 2026-10-22, among the main parts beside the lens): Fresnel zones at
rₙ = √(nλf + (nλ/2)²) from `DesignFrequency` 50 (λ through the frequency scale, as for the gates; `IFrequencyDesigned`)
and `FocalLength` 600, across `Diameter` 800, `Thickness` 32 deep (64 until 2026-11-08, Jeroen's choice; a project file up to version 3
that names no thickness or rise keeps 64 and 32, `Project.Load`). Every second
zone is an absorbing block (the user's choice over mirror or phase blocks), the central zone open, or a block when
`Inverse`; blue only. A block absorbs 96 in its middle, rising quadratically over `Coating` from the faces the wave
meets and leaves (0 by default since 2026-11-08, Jeroen's choice, 32 before; `library_check` still measures at 64 and
32), sharp at its sides, as the outer zones are narrow (measured: 0.4 % through, 1.0 % back at 64 px thick with a 32 px
rise). In 2D a zone plate is
cylindrical: the central zone dominates (the Cornu spiral), so the inverse plate focuses weakly (about 1 × the free
wave), the default plate with its 8 zones 4.6 ×. Light passing outside the diameter pushes the focus on (5 zones +30 %,
8 zones +3 %); the optional absorbing `Mount` (`MountWidth` 200 either side, off by default) stops it (5 zones: +12 % in
the check's small canvas, +6 % in a wide one).

## In the app

- **Tree**: a "Parts" folder between Sources and Probes; rows like a source's (enabled box, name, move, remove).
- **Menus**: the "+ Part" button and the Parts folder's plus open the menu of kinds (Space opens it on the menu itself,
  the arrows walk it, Enter picks); a tree row's context menu has the same entries under "Add part". Each entry's tooltip is the kind's description
  (the `Kind.<Type>` string, 2026-10-29, the user's choice), shown again at the top of the part's form under a header
  whose chevron turns while it is open; open or closed is a user setting. The entries are
  written once (`PartMenuEntries`, an unshared `x:Array`) and added to each menu's items by `ProjectTree.MenuEntries`,
  as real children: as an `ItemsSource` they made a submenu opened by keyboard start on its last entry the first time
  (2026-10-22). Each entry passes its class to `ProjectCommands.AddPart`; `Project.AddPart(Type)` adds it centred on
  the canvas, named "<stem> n".
- **Pictures**: a handle at the centre and a rotation lever; while selected, the outline as painted in the accent
  colour, a size grip (a rectangle's or ellipse's corner, the lens's thickness and diameter, a GRIN lens, Bragg stack or
  zone plate corner, a polygon's first vertex, the second end of a triangle's base), a lens's two vertex handles for
  the radii (2026-10-26), a handle per outline point (2026-10-19) and per fibre node with its two Bézier handles on
  dotted lines (2026-10-22); otherwise the shape shows through the glowing outline. Arrows nudge, Escape during a drag
  puts things back.
- **Examples**: `Examples/Example18_XOR_AND_OR_logic.wdproj`, the curated form of Jeroen's `logical_elements_example.wdproj`
  (2026-10-22: XOR and AND gates cascaded with pulsed sources and probes; the original is in git history up to 02d0e2d).

## Open points

- A `PartCheck.ps1` harness (add a part by keys, edit a value, the run goes stale and re-runs, the handle moves with
  the arrows) and example projects for Jeroen (a lens, a cavity, a bent fibre); the planned stage 3.
- Bragg thicknesses in pixels as typed, without an automatic quarter-wave from a source's wavelength.
- Where example projects live: settled (2026-10-29/31): the curated set in `Examples/`, embedded in the app; Jeroen's
  originals are out of the repository (git history up to 02d0e2d), the four the import checks read sit in
  `_tools/legacy_examples/`.
