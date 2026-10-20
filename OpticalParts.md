# Optical parts — design and work plan

Written 2026-10-18 for a session that starts with an empty context. Read `CLAUDE.md` first: its working rules
(no git on your own, options before design changes, verification matched to the change, keyboard-only harnesses,
model conventions, four string tables) apply to every step here.

**Status (2026-10-18, later the same day, uncommitted):** stages 1 and 2 are built as written below: the model for all five
kinds (`Model/OpticalPart.cs`), the painter for every kind (`Parts/PartPainter.cs`, `Parts/PartShape.cs`), the plumbing
(`RunSpec`, `MainWindow.Live.cs`), the tree with its "+ Part" menu, the forms, the handles with the rotation lever, the
strings in four tables and 32 library checks (`CheckParts`). Two choices made on the way: a part's centre is the top-left
corner of pixel (X, Y), not its middle, so even sizes (every default) cover whole pixels; and the shapes read out-of-range
values guarded instead of normalising the model from inside the painter. After the user's review (a side length alone made every
rectangle a square; a shape mode inside one class would be the kind combo we rejected): the polygon became four classes
under `ShapePart` (mirror, index, coating), each its own kind, element and form: `PolygonPart` (regular, side count and
side length, a face centred on -x'), `RectanglePart` (length, width, a chain toggle that keeps them equal: a square), `EllipsePart` (length, width, the same chain: a circle; outlined as a polygon of a vertex per 3 px of circumference), `TrianglePart` (base across the
axis toward -x', two base angles, a type preset: equilateral, right isosceles, 30-60-90, isosceles, custom; the apex angle
shown), `OutlinePart` (points). The "+ Part" menu lists the optics, a separator, the shapes. The handle glyph is the
source's circular crosshair with an inner ring (a lens-shaped glyph read as squashed). A lens's diameter and centre
thickness are its bulk: radii too curved for them are raised to the smallest that fits (`LensPart.FitRadii`), so the lens
always fills its diameter; the form shows the edge thickness (a diameter of 2000 with radii of 300 had given a tiny lens).
A "Concave (diverging)" box shows the lens's state from the lensmaker's sign and, ticked, flips both radii.
While a part is selected the overlay draws its outline (`OpticalPart.Outline()`, the painted shape) in the accent colour,
a size grip on a corner or vertex sets the extents by drag or arrow keys, and an outline's or fibre's points each have a
handle (2026-10-19). Not built: stage 3 (`PartCheck.ps1`, the three
examples). The open points at the end still stand.

## What is asked

Jeroen Vleggaar (author of Wave Dude 2D) proposed, and Alexander Graef (the user) decided, that WaveDude Next gets
**optical parts as project objects**, added like sources: a lens or mirror, a polygon, a fiber, a Bragg reflector,
a gradient-index lens. Each has a position and an angle plus kind-specific properties. The aim, in Jeroen's words:
demonstrating optics without an external image editor; a cavity of mirrors keeps a wave "for ever". His two
sketches of the property form (not in the repository; they showed a Type combo, an
"AR thickn. (px)" field, Position X/Y/Angle, then groups: Lens/mirror with refractive index, diameter, R1, R2,
centre thickness; Polygon with refractive index, number of surfaces, surface size; Fiber with core index and
diameter, cladding index and thickness; a "Shape coordinates (list)" box). Mirrors have no refractive index.

## Decisions taken (with the user)

1. **Parts are painted into the scene pixels.** The scene reaches the engines only as BGRA pixels today
   (`RunSpec.Image`, a `ScenePixels`; `SceneData.FromBgra` maps red to the refractive index). A new library step
   rasterises the enabled parts into a copy of the scene image, or into a blank canvas for an image-less scene,
   before the pixels go anywhere. Both engines, the glowing outline over the pictures (`GlowingEdges.Compute` in
   `PreviewPane.OnScenePixelsChanged`), the export outline (`MainWindow.Export.cs`) and the scratch frames then
   work unchanged. The price is Jeroen's own range: index 1.00 to 2.55 in steps of 0.01 (red = index × 100).
2. **Red only.** The painter writes the red channel and never touches green or blue. Green shades are source
   frequencies (WaveSimulator2D's warning: never anti-alias green), blue is damping; both stay as the image has them.
3. **A red value of 0 is a mirror** (index 1000, commit `5dbefa6`, Jeroen's V0.30 rule). Red 1..99 is index 1.
4. **One subclass per kind, one XML element name per kind** (the user's choice: a schema can then say which
   attributes each element requires). `OpticalPart` is abstract; `LensPart`, `PolygonPart`, `FiberPart`,
   `BraggReflectorPart`, `GrinLensPart` derive from it. `Project.Parts` is one `ObservableCollection<OpticalPart>`
   with five `[XmlElement("Lens", typeof(LensPart))]`-style attributes, so the file holds `<Lens …/>`,
   `<Polygon …/>`, `<Fiber …/>`, `<BraggReflector …/>`, `<GrinLens …/>` in tree order, like `<Source>` today.
   A part's kind is chosen when it is added; there is no kind combo on an existing part.
5. **Angle** in degrees, positive counter-clockwise on screen, 0 = the part's axis along +x, the convention of
   the composite source's rotation handle (`Source.Array`, `MarkerRole.SourceAngle`).
6. **AR coating** ("AR thickn." in the sketch): a band of the given thickness in pixels just inside the part's
   outer boundary gets the index √n (rounded to 0.01), the quarter-wave idea without a wavelength tie. The hint
   may say that a quarter-wave coating is λ / (4 √n) pixels thick, λ being the wavelength the source form shows.
   Mirrors and Bragg reflectors ignore it.
7. **Rasterisation**: 4 × 4 supersampling per pixel for refractive parts; each sample is classified (outside,
   coating, body, or a GRIN/fibre zone), the sample indices are averaged (an existing red of 0 counts as index 1 in
   that average) and rounded to red. A mirror part uses the pixel-centre test alone and writes red 0 (a half-covered
   mirror pixel would otherwise clamp to index 1 and be transparent). Later parts paint over earlier ones.
8. **Stages**: (1) model for all five kinds, painter with lens/mirror and polygon, plumbing, tree, forms, handles,
   strings, library checks; (2) painter for fiber, Bragg reflector, GRIN lens; (3) a UI harness and three example
   projects for Jeroen (a lens, a cavity, a bent fiber). No time estimates (the user's rule).

## The model (`WaveDudeNext/Model/OpticalPart.cs`, one file per class or one file; follow `Source.cs`)

Every property follows `CLAUDE.md`'s model conventions: `[XmlAttribute]` with a `[DefaultValue]`, `[Category]`,
`[DisplayName]`, `[Description]`, a `///` summary with units, a clamp in `Normalize()`, and `Label.<Type>.<Member>` /
`Hint.<Type>.<Member>` keys in all four string tables (`WaveDudeNext.WPF/Strings/Strings.{EN,DE,FR,NL}.xaml`).
The label markup (`Markup/ModelMemberExtension.cs`) builds the key from the type named in XAML, so base members are
written `model:OpticalPart.X` and kind members `model:LensPart.R1`. Positions and lengths are canvas pixels.

**`OpticalPart` (abstract, `ObservableObject`)**

| Member | Type | Default | Range | Notes |
|---|---|---|---|---|
| `Name` | string | "Lens 1" etc. | | `Project.NumberedName` per kind |
| `Enabled` | bool | true | | a disabled part is not painted |
| `X`, `Y` | int | canvas centre | ≥ 0 | the part's centre |
| `Angle` | double | 0 | −180..180 | degrees, see decision 5 |
| `Kind` | enum, `[XmlIgnore]` | | | read-only, for the tree row and the overlay |
| `Normalize()` | abstract | | | |

**`LensPart`** — Mirror (bool, false), RefractiveIndex (1.00..2.55, 1.50; hidden/disabled while Mirror),
Coating (int px, 0..50, 0), Diameter (int, 200, ≥ 2), R1 (double, 300), R2 (double, −300), Thickness (int, 40, ≥ 1).
R is signed: R1 > 0 and R2 < 0 bulge outward (biconvex); 0 means flat; |R| < Diameter / 2 is clamped to Diameter / 2.
Shape in the part frame (x' along the axis, y' across): the strip |y'| ≤ D/2 between the surfaces
x'₁(y') = −t/2 + s₁(y') and x'₂(y') = t/2 + s₂(y'), with the sag s(y') = R − sign(R)·√(R² − y'²) for R ≠ 0 and
0 for flat; a point is inside when x'₁ ≤ x' ≤ x'₂ (empty where convex surfaces cross, which caps a thin lens).
A curved mirror is the same shape with Mirror on, e.g. R1 = −400, R2 = 0, Thickness 20.

**`PolygonPart`** — Mirror, RefractiveIndex, Coating as the lens; Sides (int, 6, 3..64), Side (int px, 60, ≥ 2);
`Points` (a list, `[XmlElement("Point")]` of a small `PartPoint { X, Y }` record class, relative to the centre,
before rotation; empty by default). A non-empty list replaces the regular polygon. Regular: circumradius
R = Side / (2 sin(π / Sides)), the first vertex on +x'. A flat mirror is a thin rectangle from four points.

**`FiberPart`** — CoreIndex (1.50), CoreDiameter (20, ≥ 2), CladdingIndex (1.45), CladdingThickness (10, ≥ 0),
Coating (0), `Points` path (default two points (−100, 0) and (100, 0)). Core: distance to the polyline ≤ D/2;
cladding: ≤ D/2 + thickness (round ends follow from the distance; acceptable). Angle rotates the whole path.

**`BraggReflectorPart`** — Pairs (int, 10, 1..200), Thickness1, Thickness2 (int px, 10, ≥ 1), Index1 (1.50),
Index2 (2.00), Width (int, 200, ≥ 2). Layers stacked along +x', the stack centred on the part centre, Width across.

**`GrinLensPart`** — Length (100), Diameter (100), CentreIndex (1.80), EdgeIndex (1.50), Coating (0).
Rectangle Length × Diameter; n(y') = n_c − (n_c − n_e)·(2y'/D)².

`Project`: `Parts` collection (serialised between `Sources` and `Probes`), `AddPart(PartKind)` using
`NumberedName`, `Duplicate(OpticalPart)`, `Normalize()` over the parts. `ProjectWatcher`: subscribe the parts and
their `Points` lists (`Resubscribe`, the property-change filter list); `ProjectChange.AffectsSimulation` true for a
part, a `PartPoint`, the parts list and `Project.Parts`. `LegacyProject` (Jeroen's `configuration.json`) has no
counterpart: nothing to import.

## The painter (`WaveDudeNext/Parts/PartPainter.cs`, library)

`ScenePixels PartPainter.Paint(ScenePixels? image, int width, int height, double backgroundIndex,
IReadOnlyList<OpticalPart> parts)`:
- no enabled part → returns `image` itself (the same reference; see plumbing) or null;
- otherwise a new `ScenePixels` of the canvas size: a copy of the image, or a blank canvas with red =
  round(backgroundIndex × 100), green 0, blue 0, alpha 255; then each enabled part in list order per decision 7.
- Pure geometry, no WPF; parallel over rows is fine (`Parallel.For`), the arrays are its own.
- Keep the per-kind inside/zone tests in small static classes or methods so `library_check` can test them.

## Plumbing (WPF)

- `MainWindow.Live.cs`: `LoadSceneImage()` becomes the composed scene: the decoded image (as now) through
  `PartPainter.Paint`, cached by (image reference, a parts version) so unchanged parts return the **same**
  `ScenePixels` reference. `RunSpec.SameSceneAs` compares `Image` by reference and the scratch runner keeps its
  engine on that; a new reference per scratch request would rebuild the engine (an OpenCL compile) every time.
  Bump the version on any part change (the watcher reports it).
- `RunSpec`: `Image` is now set for an image-less scene with parts too. `ImageSources` must stay false then
  (it is `image != null && Scene.ImageSources`; the green-sources warning in `Create` would otherwise fire). Pass
  `Scene.Mode == SceneMode.Image` through, or compose only the red of a blank canvas and keep a flag.
- `OnProjectChanged`: a part change calls `UpdateScenePixels()` like a `Scene` change, so the outline follows live;
  the rest (stale run, scratch, re-run) comes from `AffectsSimulation`.
- Export: `MainWindow.Export.cs` takes the composed pixels for the outline (it calls `LoadSceneImage` already).

## UI

- **Tree** (`Panes/ProjectTree.xaml`): a "Parts" folder between Sources and Probes (`NamedCollections` converter
  parameter and bindings; `TreeParts` string), a row template for `OpticalPart` like the `Source` one (enabled
  check box, name, move up/down, remove; `AutomationId` "Enabled:<name>" and `TreeId`). The folder's plus and a
  "+ Part" toolbar button (`ToolAddPart`) open a menu of the five kinds (a `ContextMenu` opened by the button,
  keyboard: Space opens, arrows, Enter); `ProjectCommands.AddPart` takes the kind. `MainWindow.Project.cs`:
  `ListOf`, `OnAdd`, `CanEditItem`, `CanToggleEnabled`, `OnToggleEnabled`, `OnDuplicate` learn `OpticalPart`.
- **Forms** (`Views/`): one `UserControl` per kind (`LensPartView` …) with `FormPanel` sections as in
  `SourceView.xaml`; the common part (name, enabled, X, Y, angle) in a shared `PartCommonView` placed first;
  `PropertiesPane.xaml` gets a `DataTemplate` per kind. Points lists: a small editable list (one row per point with
  two `NumberBox`es, add/remove) rather than free text, every control reachable by Tab. Lens: the index box
  disabled while Mirror is on (a `DataTrigger`, see `PulsedOnlyText`).
- **Handles** (`Controls/SceneOverlay.cs`, `MarkerThumb.cs`): `MarkerRole.Part` at the centre and
  `MarkerRole.PartAngle` on a lever like `SourceAngle` (visible while selected), styles in `PreviewPane.xaml`
  (`SourceMarker`/`AngleMarker` as models), arrows nudge, selection syncs with the tree, `AutomationId`
  "Part:<name>" and "PartAngle:<name>". The shape itself shows through the glowing outline; drawing the selected
  part's outline in the accent colour is a welcome extra if cheap (the painter can expose a polyline per part).
- **Strings**: `TreeParts`, `ToolAddPart`, `TipToolAddPart`, `MenuAddLens` … (five), `Label.`/`Hint.` for every
  member above, `Enum.PartKind.*`, in all four tables. British spelling in the UI.
- **Live editing** rules of `CLAUDE.md` hold: a part edit is a simulation edit.

## Verification

- `dotnet build WaveDudeNext.sln` with 0 warnings (headers and `///` docs are enforced).
- `_tools/library_check` (`dotnet run --project _tools/library_check -c Release`, once per change set): the
  painter returns the same reference without parts; green and blue untouched under a part; a lens's extent and
  centre thickness in pixels; a regular hexagon's area within 1 % of the formula; a mirror polygon ring around a
  source keeps the field inside after 300 steps and leaves the outside below 1e-4 of it (the cavity); the coating
  band's red is round(100 √n); CPU = GPU on a scene with parts (both engines take the same pixels, so this is the
  existing comparison on a painted scene); a project with every kind round-trips through `XmlSerializer` sparsely.
- UI: `_tools/ui_check/KeyboardCheck.ps1` asserts the toolbar Tab order "ToolAddProbe,tree" after `ToolAddSource`;
  with `ToolAddPart` between them it changes. Stage 3 adds `PartCheck.ps1` on `Common.ps1` (add a lens by keys,
  edit a value, the run goes stale and re-runs, the handle moves with arrows). Update the check counts in
  `CLAUDE.md`.
- Screenshots of a lens, a cavity and a fiber for the user, not run through harness loops.

## Open points for the user

- The "+ Part" menu as the way to choose a kind (versus five toolbar buttons).
- Bragg thicknesses in pixels as typed (no automatic quarter-wave from a source's wavelength).
- Round fiber ends from the distance test.
- Where the example projects live (Jeroen's own sit in `Wave_Dude_Examples/`).
