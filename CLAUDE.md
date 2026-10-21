# WaveDude Next — project notes for Claude

Last updated 2026-10-16. The long history is in git (`git show a6f85dc:CLAUDE.md`). Keep this file short.

## What this is

A 2D wave-equation simulator (field and intensity views, scenes from images, point and composite sources, probes
with live graphs, video export): a .NET 10 library (`WaveDudeNext/`) plus a WPF app (`WaveDudeNext.WPF/`), the
successor of Jeroen Vleggaar's (Huygens Optics) **Wave Dude 2D**, GPL-3.0-or-later, by Alexander Graef. Jeroen's
VB source and the C# port of his binary live in git history up to `d3ac0d8`; read his VB there when his intent
matters. The harnesses are `_tools/library_check` (console, not in the `.sln`) and `_tools/ui_check` (PowerShell).
The user works in Visual Studio with Claude Code in the chat, permissions skipped, often at the machine while
harnesses run.

## How to work here (the user's rules)

- **Git only on his word for the change at hand.** "Commit" in his latest message means commit now; otherwise
  report the work as uncommitted. Never commit, push, reset, amend, stash, checkout, rebase or merge on your own.
  Enforced by `.claude/hooks/git-guard.py` (needs Python on the PATH, else every shell command is blocked); a shell
  command whose text merely mentions git plus such a word is denied too. End commit messages with
  `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- **No design changes on your own.** Anything beyond a mechanical change goes to him as options with pros and
  cons, complete, with links when naming a project, then built as chosen. Never invent a feature nobody asked for
  (the scratch budget that skipped "too expensive" frames was one; torn out). Bugs you find: fix and prove.
- **Verification matches the change**: a clean build (0 warnings; `///` docs and the header are enforced) plus one
  targeted probe or screenshot for a local change, `library_check` (20 s) for a library change, a UI harness only
  when its flow changed, **once**. Never loop a harness for a flake, never run the suite after an edit.
- **Measure properly**: warm the JIT; never poll UI Automation while measuring (it loads the UI thread it
  measures); the app's own log lines are the measurement.
- **Harnesses are keyboard-only**: post `WM_KEYDOWN`/`WM_CHAR` to the window, read state through UIA properties,
  never UIA patterns, the mouse, `SendKeys` or `SetForegroundWindow` (each activates the window). Modifiers cannot
  be posted, so every command a harness presses has an unmodified key. Everything a harness needs has an
  `AutomationId`. Keep a failing run's output; a filtered run cannot be read afterwards.
- **Keyboard first in the app**: every control reachable with Tab and the arrows, focus visible; a mouse-only
  control is a bug. The menu bar is out of the Tab cycle (F10).
- **Commands writing outside the repo** hit the sandbox prompt: set `dangerouslyDisableSandbox` up front.
- **No scratch files in the repo tree** (2026-10-19, after screenshots and probe scripts were left behind for the user to
  clean up): edit scripts, one-off probes, test projects and screenshots live under `%TEMP%\WaveDudeNext` (create it,
  `dangerouslyDisableSandbox`), are named once with their full path when the user should look, and are deleted by the
  session that made them; a harness project file beside `Common.ps1` is deleted in the script's `finally`.
- **He writes his own prose** (README, mails): offer verified facts and screenshots; never "I" sentences for him,
  never claims about him he has not made.
- **Spelling**: British in the UI, except **"license"** everywhere (the GPL's own word). No talk of BOMs or line
  endings.
- **Dependencies**: no package named after a person, no AGPL/GPL libraries, flag vibe-coded candidates; in-house
  preferred (docking, encoders, colour picker are in-house). Microsoft packages are fine.
- **WPF idioms**: commands not `_Click`, bindings not hand-pushed state, `DynamicResource` for every theme brush and
  string, `XmlSerializer` with sparse XML (attributes, repeated children, `[DefaultValue]`), Segoe UI 9 pt, the
  dnSpy look (its sources are GPL: look, do not copy). Inside a `DataTemplate` reference the string keys directly
  (`{DynamicResource Hint.Type.Member}`): the `meta:Label`/`meta:Hint` extensions wrap a resource reference a template
  rejects for a string property and shows as text for an object one (2026-10-22, found on the fibre's node rows).
- **Model conventions**: a new property gets its XML attribute with a default, `[Category]`/`[DisplayName]`/
  `[Description]`, a `///` summary, a clamp in `Normalize()` with the matching `[Range]` beside the default (2026-10-21:
  the bounds are readable from outside, `library_check` proves each against `Normalize()`; a bound that depends on another
  value stays in code alone), and `Label.`/`Hint.`/`Enum.<Type>.<Member>` keys in all four string tables. Randomness from
  `Project.Seed` through `Pcg32`; generated data is never persisted.
- **C# style**: modern and concise, expression-bodied accessors, no `this.`, no `IDisposable` on interfaces,
  `out` not `ref`, tabs, Allman; `///` docs state threading, units and coordinate systems. `grep -E`, not `-P`.
- **Log decisions at Debug** (scratch requests, stale marks, postponed re-runs, export steps). Info and up reach
  the status bar; Trace only in Debug builds. Log messages are not localised.
- **Do not resurrect**: a probe signal lane, timeline tracks, zoom and pan of the axis, thumbnails, a run progress
  bar, the scratch budget and its "not computed" note, a GPU JPEG front end, a Direct3D display path, a bent
  rotation lever. **Do not change**: the readout showing both frame and step (undecided).
- Collect the reusable rules for other projects only when he asks.

## Build and run

- `dotnet build WaveDudeNext.sln` → 0 warnings. Harnesses use `WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe`.
- `dotnet run --project _tools/library_check -c Release` (`-- export`, `-- bench [w h]`, `-- bench-render [w h]`,
  `-- bench-export [w h]`); `powershell -ExecutionPolicy Bypass -File _tools/ui_check/<script>.ps1` (`-Trace`).
- Publish: `dotnet publish WaveDudeNext.WPF/WaveDudeNext.WPF.csproj -p:PublishProfile=SingleFile`. Version in
  `Directory.Build.props`; the commit hash lands in About. The license header is the `.editorconfig`
  `file_header_template` (its warranty sentence ends in a full stop: Roslyn cuts the value at `;`). LICENSE.md is
  the FSF's GPL-3.0 Markdown verbatim, nothing else (GitHub's detector rejects additions); the About dialog shows
  it as a document built by `Build/LicenseDocument.targets`.
- The library is compiled optimised in every configuration (flip it locally to step through engine code). Each exe
  path has its own `user.config` under `%LOCALAPPDATA%\WaveDudeNext.WPF`. Close Visual Studio before renaming a
  project folder.

## Architecture

```
Project (Model/) --RunSpec.Create--> RunSpec (immutable snapshot); the scene pixels come from MainWindow.Live's
   LoadSceneImage: the decoded image (or a blank canvas) with the enabled optical parts painted into red by
   PartPainter (Parts/), cached by (image, parts version, size, background) so an unchanged scene is the same reference
StartRun: memory budget -> DiscardRun (returns the old store's release task) -> FrameStore -> SimulationRun
SimulationRun: stepping loop + writer threads; IWaveEngine (WaveSimulator CPU | GpuWaveSimulator OpenCL);
   CaptureFrame/AwaitFrame -> FrameStore.Write; one heartbeat post (<= 30/s) at Background priority; final post sets
   FrameCount, then State
ScratchRunner: one kept engine; "frame k of the project as it is now" from step 0, probes sampled; newest request
   wins; every request is computed
FramePresenter: CopyLayer -> FrameRenderer (Vector128, 32-row blocks) -> the WriteableBitmap, copied on the pool
   between Lock/Unlock on the UI thread; only the newest request is rendered
ExportJob (own STA thread): CopyLayer -> FrameRenderer -> outline -> probe boxes/markers -> sinks (PNG, JPEG, MPEG-1, AVI, MP4, MOV)
FrameStore: native memory up to the budget, the rest in a temp file (%LOCALAPPDATA%\WaveDudeNext\cache, DeleteOnClose)
```

- **Engine contract**: padded grid (`Stride = Width + 2 + 2 × Rim`: a negative damping edge is a rim of that many cells
  outside the canvas, simulated and never shown, the canvas starting at (Rim, Rim); `Scene.Edges` Open puts Mur's
  first-order absorbing condition on the ring, Reflective is Jeroen's wall; a new project is Open with no damping edge
  (2026-10-19, the user's choice: measured on a pulse, the open ring alone returns 0.05 to 0.09 % of the amplitude, a
  rim at its best the same, a rim damped to a standstill 1 %, the reflective box 72 %); a version-1 file loads as
  Reflective, an import as Reflective with his band; one driver thread at a time, no locking; captures
  deliver planes in the spec's `FrameShape` (halves or palette index bytes, the GPU converting on the card);
  `Reset`, `SetEmitters`, `SetProbes`. Numerics are Jeroen's (dx = dt = 1, leapfrog, 9-point or 4th-order
  Laplacian, soft sources, EMA intensity; a scene pixel with red 0 is a mirror, n = 1000, his V0.30 rule,
  2026-10-18), verified CPU = GPU by the harness; seeds from `Pcg32` make CPU = GPU
  bit for bit on random gaps. A source's frequency level, amplitude and phase take decimals (2026-10-19, Jeroen's point 10); the
  level is capped at 255, above which the grid cannot sample the wave; object sources carry their frequency as a float on
  the GPU, the 256-entry table serves the image sources. The importer's fallbacks differ from the model's defaults on purpose.
- **Frames vs steps**: frame k is the state after step `k × VisualizeEvery`.
- **The engine choice is a user setting** (Simulation > Use the GPU, default on, CPU fallback logged).
- **Frame store**: budget = the lesser of free physical memory and available commit (2026-10-19: Windows refuses an
  allocation at the commit limit first, and a run that filled the physical measure left no room for the preview's
  bitmap, which took the app down; a failed bitmap is now logged and the picture stays empty) minus 2 GB, capped by
  Simulation > RAM cache size (Auto or a share); the
  outgoing run's frames count as reclaimed and the new store waits for their release. No memory mapping, no OS
  cache reliance (both wrote at disk speed). Disposed on the thread pool. Full HD, 2500 steps, both planes: 10.4 GB
  in 16-bit, half in 8-bit.
- **Reduced preview** (View menu): resolution 1/2/4, frames 1/2/4 (the timeline never changes), samples 16-bit or
  **8-bit palette index, the default**: the same pixels at the captured brightness, half the store, half the
  colouring; 16-bit keeps the brightness turnable after a run. In 8-bit a brightness edit is a simulation edit. The
  index is computed from the sample rounded to a half, on the CPU and in the kernel (a bit-level rounding there), and
  the GPU program is built with a correctly rounded square root where the card has one, so an 8-bit run exports the
  same bytes as a 16-bit run of the same project (2026-10-19, two such exports had differed by a palette step in half a
  percent of the pixels).
  A picture export takes an 8-bit run as it is (the same pixels; 2026-10-18); an EXR data export, a reduced resolution or
  skipped frames make the export run the simulation again at full quality first.

## The app's rules

- **Live editing**: a simulation edit (scene, solver, seed, sources) marks the run stale, **stops playback and any
  active run**, requests a scratch frame for the position (120 ms debounce) and re-runs automatically after 1.5 s
  of quiet (setting, default on); an automatic run never moves the preview. A probe edit never starts a run: only
  the recordings are stale, the scratch frame refreshes the graph; except that an active probe the run does not
  record (switched on, added) is a simulation edit (2026-10-18). No scratch frame while playback runs. Any run
  is paused while a scratch frame computes. Every scratch request is computed, however long.
- **A picture changes only when a frame for its position arrives**; otherwise the last picture stays. A long wait
  dims the pictures under a spinner, mirrored on the taskbar.
- **Brightness** (2026-10-20, Jeroen's point 9) sits in each picture's header strip (`HeaderStrip`: title, a caption that
  gives way first, the box and **Auto** at the far end; turned on its side with the title); Auto fits once to the frame
  shown (the 99.5th percentile of |field| or √intensity at the palette's top, the user's choice of the variants), an
  8-bit preview computing that frame in 16-bit first; the context menu has Brightness and Auto brightness.
- **Play means "show me the simulation"**: it starts an automatic run when needed and holds at the newest frame
  until the run catches up. Live follows a running run until the user scrubs; it ends with a last follow to the
  final frame. A replaced project goes to frame 0; a discarded run keeps the position. In/out points are steps.
- **Handles and overlays**: sources, probes, emitter dots and the rotation handle are drawn over the pictures in
  canvas pixels, over a dark halo, clipped to the picture; a probe's handles and line appear only on the picture of
  the layer it measures (2026-10-18), sources on both; graph boxes lie over every handle and dot, the selected
  item is drawn last among its kind, except the handles themselves (Panel.ZIndex breaks the Tab cycle, 2026-10-18);
  arrows nudge, selection syncs with the tree, a click on
  empty space deselects. **Group selection** (2026-10-19): one shared `SelectedItems` list on the window beside the
  current `SelectedItem` (the properties pane shows the current one); in the tree Ctrl+click, Insert or Ctrl+Space
  toggles a row, Shift+click or Shift+arrows selects the range from the anchor, Ctrl+A everything, the row's context
  menu has both; on the pictures a drag on empty space is a marquee (Shift adds, Ctrl toggles), Ctrl+click on a handle
  toggles, Shift+click adds; a drag of the handle of one of several selected items, or the arrows on it, moves them all
  through `IMovable` with the delta limited so none leaves the canvas (the formation holds); Escape during a drag puts
  things back; Remove, Duplicate and Enabled act on the whole selection when their target is in it; the lever, grips
  and point handles belong to the current item alone. Probe graph boxes follow their probe while attached, resize by a grip, keep their text
  size; an amplitude axis is symmetric about zero; an intensity graph's value axis can be logarithmic (2026-10-20,
  Jeroen's point 8: `Decades` below the fitted top, zeros on the bottom line, powers-of-ten ticks; the field stays linear).
- **Colour schemes** are stop lists in `.wdscheme` XML, shipped ones embedded, user ones in
  `%LOCALAPPDATA%\WaveDudeNext\ColorSchemes`, project ones in the `.wdproj`; an in-house picker edits stops;
  renaming a project scheme repoints its layers. **The scene image** is embedded and follows its file on disk;
  the "glowing edges" outline overlays the pictures. **Import** of Jeroen's `configuration.json` is key for key; a source whose position was
  never picked (`has_position` false) stays enabled at its x/y, since his program simulated it there (Example 11's
  lattice came in disabled and ran black until 2026-10-16).
  **Languages**: four string tables, live switch; converter output needs the language as an extra binding input.
- **Export**: field, intensity or both, a still of the shown frame, from the current run when fresh and full,
  else a run is made first; in the dialog's order (2026-10-19) H.264 MP4 via Media Foundation (the only
  Windows-bound library piece besides WIC JPEG, which has an in-house fallback), MPEG-1, OpenDML AVI, QuickTime
  MOV with the Animation codec or the PNG codec (2026-10-19, each sample a PNG, for a lossless hand-over to Resolve,
  Premiere and Final Cut; both share `.mov`, so the dialog's filter decides the format and the extension follows it,
  while a scripted path goes by its extension, `.mov` meaning Animation), PNG/JPEG sequences (the files numbered by timeline frame, a range export from its first
  frame, and a still padded the same way, `name_0655.png`; never a step number, 2026-10-19), and
  **probe data as CSV** (2026-10-18: a probe's context menu in the tree and on its graph box; semicolons, decimal
  point, a header row; a point probe one row per step, a line probe one row per frame; the full run like the
  pictures, started first when needed),
  **JPEG is 4:4:4 in both encoders** (2026-10-18: WIC called natively for its subsampling option, which WPF's
  wrapper never set; 4:2:0 averaged the colour, where a wave picture's information is, into blocky fringes even
  at quality 100; 4:4:4 at 90 beat it in every channel at half the size), and
  **OpenEXR sequences or stills** (2026-10-18, the user's choice for data export to NumPy, MATLAB, Octave add-ons
  and viewers such as DJV): the stored halves themselves, one uncompressed "Y" channel per file, no colours,
  brightness, outline or boxes; a sink that `WantsHalves` gets the plane from the job unrendered. **Source markers**
  (2026-10-21, Display, off by default, the user's choices): the preview's marker, a composite source's dots and the
  name, enabled sources only, scaled like the probe labels, under the probe markers and boxes. Harness hooks:
  `WAVEDUDE_EXPORT_PATH`, `WAVEDUDE_DISCARD_EDITS=1`.
- **Optical parts** (2026-10-18, stages 1 and 2 of `OpticalParts.md`): `OpticalPart` subclasses `LensPart` (mirror flag),
  the shapes under `ShapePart` (`PolygonPart` regular, `RectanglePart`, `EllipsePart` as a polygon of a vertex per 3 px of
  circumference, 2026-10-20; both carry a `Linked` flag, the chain toggle between width and length of the image editors,
  which keeps them equal through the boxes, the grip and the file, `TrianglePart` by base and base angles with presets, `OutlinePart` by points), `FiberPart`, `BraggReflectorPart`, `GrinLensPart` in `Project.Parts`, one XML element
  per kind; a lens or shape can absorb (2026-10-20, Jeroen's point 11: Absorption 0..255 painted into blue over a quadratic
  ramp from the outline, the larger blue kept; measured, see `OpticalParts.md` 6a); painted into the scene's red channel (4 × 4 supersampled, a mirror by the pixel-centre test as red 0, a
  coating band of √n, later parts over earlier ones; a part's centre is the corner of pixel (X, Y), so even sizes cover
  whole pixels). A part edit is a simulation edit. Tree folder "Parts", "+ Part" opens a menu of kinds (Space, arrows,
  Enter), a handle plus a rotation lever on the pictures; while selected, the outline as painted in the accent colour,
  a size grip on a corner or vertex (rectangle, lens, polygon, triangle, GRIN, Bragg) and a handle per point of an outline
  (2026-10-19); the shape shows through the glowing outline otherwise. **A fibre's path** (2026-10-22, the user's request,
  replacing the polyline with its sharp bends) is a chain of cubic Bézier segments through `FiberNode`s, each with an in and an
  out handle as offsets (ints, `<Node X Y InX InY OutX OutY Smooth>`): on the pictures a node handle plus its two handles on
  dotted lines, a smooth node (default) lined up when switched on (`FiberPart.AlignNode`, zero handles seeded from the
  neighbours) and turning the other handle with the one moved or typed, keeping its length (captured at the drag's start, so
  rounding does not creep in; a node added or loaded is taken as it is), a corner node free; the pane lists each node with both handles; "Add node"
  continues the path. The ends are `Ends` Flat (default: cut across the curve's tangent at the endpoint, the coating
  following the face) or Round. The painter flattens the curve into chords (0.05 px) bucketed on a 32 px cell grid, and the
  core edge is supersampled (`HasInnerEdges`).
- File types `.wdproj` / `.wdscheme` are registered per user at start-up; unsaved edits ask on close.

## Numbers (workstation: 10-core Xeon without AVX2, RTX 2080, 64 GB)

GPU step at 1080p 0.25–0.5 ms, CPU 6–14 ms; OpenCL build 0.3 s; a full-HD run of 2500 steps 5.4 s in RAM, bound by
the GPU readback. Renderer per 2880×1080 layer on ten cores: 8-bit 1.0 ms, 16-bit 2.0; vector rows gain 2× per
core over scalar. Presenter about 7 ms a frame quiet, 20 during a run. JPEG 1080p at 4:4:4: WIC 26 ms a frame
single-threaded (9 ms a frame in a sequence job on half the cores), in-house 9.8 ms on ten cores (15 in a job);
MP4 job 16 ms a frame. Part painter on a Full HD canvas (ten cores): a 400 × 330 outline 30 ms, a 400 px lens 5, a hexagon 8, an S-bent fibre across the canvas 51 (156 chords on the cell grid; 1.3 s without it);
the glowing edges 22 ms; the preview's scene pictures about 100 ms more, so the scene rebuild is coalesced at background priority. Laptop (2 cores, HD 520, HDD): iGPU 4.3 ms a step against CPU 14.7; the ball
lens run 105 s full quality (disk-bound), 61 s at half resolution with every second frame, in RAM. Settled by these
numbers: no GPU JPEG, no OpenCL colouring at presentation, no `Vector256`, no Direct3D display path.

## Harnesses

`library_check` (100+ checks: engines, store, renderer, schemes, probes, scratch, reduced preview, importer, export, parts, group move, open edges, ranges).
UI, all on `Common.ps1`, with their check counts: `PreviewCheck` 43, `RunCheck` 71, `LanguageCheck` 11,
`ProbeGraphCheck` 39, `ImportCheck` 10, `ExportCheck` 38, `PreviewQualityCheck` 35, `SceneImageCheck` 8,
`ColorPickerCheck` 13, `KeyboardCheck` 13, `AboutCheck` 18, `SelectionCheck` 10. Test projects `tiny*.wdproj` sit beside them. A harness
sets the persisted settings it expects (split orientation, preview quality). Known flake: a second app start in one
script sometimes comes up active; only the foreground guard fails then.

## People, name, license

Alexander Graef (the user), author of WaveDude Next. Jeroen Vleggaar, Huygens Optics, author of Wave Dude 2D,
Copyright (C) 2026. 0x23 (Diffraction Limited), author of WaveSimulator2D, which Jeroen's numerics followed; named by his handle only, at his wish (2026-10-19).
Private addresses stay out of files. The name was agreed with Jeroen (he proposed "Pro"); the GitHub repository
`graealex/WaveDude` still has the old name. The header text is the editorconfig template, identical in every file,
never reworded per file.

## Open items

README and a documentation file (he writes them); release (single-file publish, portable archive, signed installer,
curated examples from Jeroen, his program as preserved assets, repository rename); an integer
upscale in exports; HEVC; the start-up stall (log milestones first); the GPU readback (a second OpenCL queue);
automatic quality lowering when a run would spill (undecided); dropping the 9-point Laplacian (undecided);
**optical parts**: stages 1 and 2 of `OpticalParts.md` built 2026-10-18 (uncommitted); stage 3 (a `PartCheck.ps1`
harness, three example projects for Jeroen) and its open points (the "+ Part" menu versus five buttons, Bragg thicknesses
in pixels, where examples live) wait for the user.
