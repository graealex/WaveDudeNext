# WaveDude Next — project notes for Claude

Last updated 2026-10-23. The long history is in git (`git show a6f85dc:CLAUDE.md`). Keep this file short.

## What this is

A 2D wave-equation simulator (field and intensity views, scenes from images, point and composite sources, probes
with live graphs, video export): a .NET 10 library (`WaveDudeNext/`), a UI-neutral app library (`WaveDudeNext.UI/`, see Port) plus a WPF app (`WaveDudeNext.WPF/`), the
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
  measures); the app's own log lines are the measurement. **Quick JIT is off** (`TieredCompilationQuickJit` in
  `Directory.Build.props`, 2026-11-03, the user's choice, after the part painter took 31 ms in a probe and 115–460 ms in the
  app, the same call: the runtime's tiering counts calls only after 100 ms without a first call of any method, ours or the
  framework's, and interrupts its background compiler at every such call (`tieredcompilation.cpp`), so a window under
  interaction keeps this code at tier 0; at a start 1158 of its methods were, none promoted; seen with
  `DOTNET_JitDisasmSummary=1` and `DOTNET_JitStdOutFile=<file>`, which list every jitted method with its tier): every method
  of this code is compiled optimised at its first call, in the app, `library_check` and the harnesses alike, so a probe
  and the app run the same code; the framework's precompiled code keeps tiering up; dynamic PGO is off, it needs quick
  JIT. Every build starts 0.45 s later for it, the published ones too, since they are plain IL (see Publish). The
  `AggressiveOptimization` attributes the painter's path had carried since that morning are gone (the user's request,
  once provably a no-op: the runtime's `method.cpp` makes every method of a module without precompiled code ineligible
  for tiering when quick JIT is off, `library_check` compiled the painter's methods the same with and without them, and
  Example 16 opened in the published EXE painted in the same milliseconds). A hot path would need the attribute again
  only with ReadyToRun: its precompiled code must stay valid across servicing of the runtime, so it inlines nothing from
  outside its assembly and reaches every framework method and struct layout through an indirection, which made the
  per-pixel path 5–10× slower than a first-call JIT (`Math.Round`, `Math.Clamp`, a span conversion and even our own
  `SegmentDistance` as a call per pixel, the vertices a `ValueTuple` of unknown size); seen by printing the JIT's listing
  from crossgen2 (`--codegenopt:JitDisasm=<patterns> --codegenopt:JitStdOutFile=<file> --parallelism:1`) beside the
  runtime's (`DOTNET_JitDisasm`, `DOTNET_JitStdOutFile`).
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
  rejects for a string property and shows as text for an object one (2026-10-22, found on the fibre's node rows). An
  The XAML designer never runs a window's constructor: what the preview must show lives in XAML, as the dock host's
  default arrangement does (`DockHost.DefaultLayout` in `MainWindow.xaml`, the one source for the first start and the
  designer, which then shows every hidden pane at its default side, Messages at the bottom, 2026-10-26; nested types
  cannot be XML names, so the layout nodes are top-level classes). An
  implicit style never reaches a class derived from `Window`, so every window names the theme's
  (`Style="{StaticResource {x:Type Window}}"`, 2026-10-23: without its layout rounding the pictures sat at fractional
  pixels and WPF resampled them; its Display text formatting had never applied either).
- **Model conventions**: a new property gets its XML attribute with a default, `[Category]`/`[DisplayName]`/
  `[Description]`, a `///` summary, a clamp in `Normalize()` with the matching `[Range]` beside the default (2026-10-21:
  the bounds are readable from outside, `library_check` proves each against `Normalize()`; a bound that depends on another
  value stays in code alone and is named in its `dependentBounds`; since 2026-10-22 it finds every part kind itself and
  fails a persisted number that `Normalize()` clamps without a `[Range]`), and `Label.`/`Hint.`/`Enum.<Type>.<Member>` keys in all four string tables. Randomness from
  `Project.Seed` through `Pcg32`; generated data is never persisted. **A changed default is a new file version**
  (2026-11-09, the user's choice at the integration of Jeroen's drop): the XML is sparse, so an attribute left out means
  the default of the version that wrote the file. `Project.CurrentVersion` is 4 for "Outline in the export" on and a zone
  plate 32 px thick without a rise (Jeroen's choices of 2026-11-08; off, 64 and 32 before): `Load` gives a file up to
  version 3 the old values where it names none (`OverlayInExportRead`, `ThicknessRead`, `CoatingRead`, set by the setters
  as `Scene.DampingEdgeRead` is), and it is saved as version 4 with them written out; `library_check` proves it. The
  examples up to 24 and the harnesses' projects stay version 3, `figures.wdproj` and Examples 25 to 33 are 4. Clipboard fragments carry no version and
  read with today's defaults.
- **C# style**: modern and concise, expression-bodied accessors, no `this.`, no `IDisposable` on interfaces,
  `out` not `ref`, tabs, Allman; `///` docs state threading, units and coordinate systems. `grep -E`, not `-P`.
- **Log decisions at Debug** (scratch requests, stale marks, postponed re-runs, export steps). Info and up reach
  the status bar; Trace only in Debug builds. Log messages are not localised, and their numbers are invariant (NLog's
  DefaultCultureInfo, 2026-10-27, the user's rule: a report's reader or tool must not trip over a decimal comma). **The log must let a report be
  diagnosed** (2026-10-27, the user's rule after Jeroen's log named the property edited but not the value, nor the
  grid): every engine logs its grid, canvas, rim, edges, stride, sub-steps and work groups when built; a GPU device's
  facts (versions, units, clock, memory, limits) once per process; the scratch frame names the newest edit with its
  value on the line of the frame shown (every request at Trace; since 2026-11-07 a frame is asked for at every change,
  so the line names the edit whose frame got computed); every change at Trace as old → new
  (`PropertyValueChangedEventArgs` from `SetProperty`, carried by `ProjectChange.Values`, `library_check` proves it). **A malformed project, colour scheme or settings file is
  quoted in its error's entry** (2026-11-03, the user's rule): the catch logs an `UnreadableFileException` wrapping the error,
  whose `ToString` appends the file's text, the first 64 K characters and the line an XML error names beyond them, so the
  log file and the Messages pane's tooltip carry it while the status bar and the dialog keep the one-line message; the
  settings file needs it most, since the next save writes the defaults over it. Moving an unreadable file aside was
  proposed and refused (2026-11-03, the user's decision: nobody would find that file, which is exactly why its text is in
  the log).
- **Do not resurrect**: a probe signal lane, timeline tracks, zoom and pan of the axis, thumbnails, a run progress
  bar, the scratch budget and its "not computed" note, a GPU JPEG front end, a Direct3D display path, a bent
  rotation lever, an unreadable settings file moved aside (2026-11-03: nobody would find it, the log quotes it), a quiet
  period or any other timer that decides when an undo step ends (2026-11-06: an edit is a step when it has finished), a
  debounce before a scratch frame or a quiet period before the automatic re-run (2026-11-07: a frame is asked for at
  every change, the re-run comes when the edit has finished), the abandonment of a scratch frame in flight for a newer
  request (2026-11-09: it is finished, the newest follows), a
  quiet period or a retry with pauses before the changed scene image file is read (2026-11-06: it is read when it has been
  written and closed, at once), Ideal
  text formatting for the manual at 100 % (2026-11-05: less readable, see UI zoom). **Do not
  change**: the readout showing both frame and step (undecided).
- **Jeroen's drops** (2026-11-09, the first since the port's preparation: `C:\Work\WDN-0.1.3-JV_20261108`, his folder,
  a patch against the release it started from and a list of changes): the patch is applied to master's working tree
  (`git apply`; 56 of 57 files fitted, 53 of them untouched by master and then equal to his folder), and what master
  has moved is ported by hand into the models (`ProjectItems` for his window code). What a plain apply does not show
  and each drop is read for: a changed `[DefaultValue]` (a new file version, see Model conventions); the menus'
  mnemonics in all four tables (German had N twice in the Add part menu); the non-ASCII characters of the Markdown
  files, which his session's tooling had written back double-encoded (180 sequences in `MANUAL.md` and
  `OpticalParts.md`, repaired by the exact inverse; the code and the string tables were clean, his French additions
  in numeric entities); names that become file format; a fix without a reproduction (said so where it stays); and
  "the user" in his notes and comments, who is Jeroen: named in this file, left in the code's comments as the gates'
  and the zone plate's were, with a line in `OpticalParts.md` saying so. He works on from the merged tree, not from
  the old release: his sessions' window code otherwise lands where the logic no longer is.
- Collect the reusable rules for other projects only when he asks.

## Build and run

- `dotnet build WaveDudeNext.sln` → 0 warnings. Harnesses use `WaveDudeNext.WPF\bin\Debug\net10.0-windows\WaveDudeNext.WPF.exe`.
- `dotnet run --project _tools/library_check -c Release` (`-- export`, `-- bench [w h]`, `-- bench-render [w h]`,
  `-- bench-export [w h]`); `powershell -ExecutionPolicy Bypass -File _tools/ui_check/<script>.ps1` (`-Trace`).
- Publish: `dotnet publish WaveDudeNext.WPF/WaveDudeNext.WPF.csproj -p:PublishProfile=SingleFile` (framework-dependent;
  `SingleFileBundled` carries the runtime). **No ReadyToRun** (2026-11-03, the user's choice after a day with it): it
  bought 0.55 s of start (1.90 s to the first idle of the window against 2.44 for the plain IL) and cost the painter's
  per-pixel path 5–10× until a tier-1 promotion a busy window postpones (see Measure properly), so the shipped EXE runs
  the code the probes measure: 7.3 MB, 2.44 s; the bundled one 146 MB. Should it return: for a self-contained single file
  the SDK defaults to one composite image of the runtime and the app (168 MB, 1.78 s, the one form of precompiled code
  that may inline the framework, untested on the painter), and its ReadyToRun step is incremental on
  `obj\...\win-x64\R2R`, which both profiles share: a publish after a composite one reused the composite's component stubs
  without their image, so the EXE died at start with a fail-fast before its first log line (both profiles;
  `DOTNET_ReadyToRun=0` let it start, which named the cause); that folder would need a clean before every publish. Version in
  `Directory.Build.props`; the commit hash lands in About. **A release adds its version to the problem form before its tag**
  (2026-11-05, the user's choice): the Version dropdown in `.github/ISSUE_TEMPLATE/problem.yml` is a fixed list, since GitHub's
  forms cannot read releases, so the new version goes in at the top, in the commit that bumps `Directory.Build.props`. The license header is the `.editorconfig`
  `file_header_template` (its warranty sentence ends in a full stop: Roslyn cuts the value at `;`). LICENSE.md is
  the FSF's GPL-3.0 Markdown verbatim, nothing else (GitHub's detector rejects additions); the About dialog shows
  it as a document built by `Build/Documents.targets`, whose converter `Build/MarkdownToFlowDocument.cs` (an inline
  MSBuild task, C# 7.3 and netstandard2.0 for Visual Studio's MSBuild; excluded from the app's compile) makes MANUAL.md
  the manual's document too.
- The library is compiled optimised in every configuration (flip it locally to step through engine code). The user
  settings are one sparse XML file, `%LOCALAPPDATA%\WaveDudeNext\Settings.xml` (`Settings.cs`, `XmlSerializer`, 2026-10-26, the
  user's choice: .NET's `ApplicationSettingsBase` kept a `user.config` per exe path and version under a folder named after
  the assembly's company, his name by default; Authors and Company are now the product's name, nothing personal in folder
  names). Folders under `%LOCALAPPDATA%\Alexander_Graef` and `%LOCALAPPDATA%\WaveDudeNext.WPF` are leftovers of that. Close Visual Studio before renaming a
  project folder.

## Architecture

```
Project (Model/) --RunSpec.Create--> RunSpec (immutable snapshot); the scene pixels come from
   SceneComposer.Load (WaveDudeNext.UI): the decoded image (or a blank canvas) with the enabled optical parts painted into red by
   PartPainter (Parts/), cached by (image, parts version, size, background) so an unchanged scene is the same reference
StartRun: memory budget -> DiscardRun (returns the old store's release task) -> FrameStore -> SimulationRun
SimulationRun: stepping loop + writer threads; IWaveEngine (WaveSimulator CPU | GpuWaveSimulator OpenCL);
   CaptureFrame/AwaitFrame -> FrameStore.Write; one heartbeat post (<= 30/s) at Background priority; final post sets
   FrameCount, then State
ScratchRunner: one kept engine; "frame k of the project as it is now" from step 0, probes sampled; one request computes
   at a time, to its end, the newest made meanwhile follows it, those between are dropped (2026-11-09)
FramePresenter: CopyLayer -> 8-bit: FrameRenderer.RenderIndexed at the store's size, WPF scales; 16-bit: ScreenRenderer at
   the picture's size on screen -> the WriteableBitmap, copied on the pool between Lock/Unlock on the UI thread; only
   the newest request is rendered
ExportJob (own STA thread): CopyLayer -> FrameRenderer -> outline -> probe boxes/markers -> sinks (PNG, JPEG, MPEG-1, AVI, MP4, MOV)
FrameStore: native memory up to the budget, the rest in a temp file (%LOCALAPPDATA%\WaveDudeNext\cache, DeleteOnClose)
```

- **Engine contract**: padded grid (`Stride = Width + 2 + 2 × Rim`: a negative damping edge is a rim of that many cells
  outside the canvas, simulated and never shown, the canvas starting at (Rim, Rim); `Scene.Edges` Open puts Mur's
  first-order absorbing condition on the ring, Reflective is Jeroen's wall; a new project is Open with a 32 px rim outside
  (2026-10-22, the user's choice, after the rim measurements below; no damping edge from 2026-10-19 until then, when the
  open ring was measured on a long-wave pulse: the open ring alone returns 0.05 to 0.09 % of the
  amplitude, a rim at its best the same, a rim damped to a standstill 1 %, the reflective box 72 %). The rim's loss rises
  with the square of the depth to a half per step at the outer ring (2026-10-22, the user's choice: at wavelengths of a few
  cells, Jeroen's usual, the ring's Mur condition assumes a speed the stencil does not have and reflects almost
  everything, so the rim absorbs alone; measured against a reference canvas too wide to reflect, Example 3's beam got
  1 % back from the earlier linear ramp to a tenth at -64, 0.09 % from the quadratic one, 0.03 % at -128; the measuring
  method is in `library_check`); a version-1 file loads as
  Reflective, a version-2 file without a damping edge with none (its default then; files were version 3 from 2026-10-22 and are version 4 since 2026-11-09, see Model conventions), an
  import as Reflective with his band; one driver thread at a time, no locking; the kernels over the grid and the captures run in explicit 32 × 8 work groups over a global size rounded up to whole groups (2026-10-27, Jeroen's report: left to the driver, the group had to divide the grid, and a prime width, 1789 for a 1725 px canvas with the rim, got groups one cell wide, 13–19× slower on the RTX 2080; every candidate shape from 16 × 16 to 256 × 1 measured the same); captures
  deliver planes in the spec's `FrameShape` (halves or palette index bytes, the GPU converting on the card) into snapshots
  the engine made (`IWaveEngine.CreateSnapshot`, 2026-11-03, the user's choice): the GPU's planes are page-locked host
  memory (`CL_MEM_ALLOC_HOST_PTR`, mapped for the engine's life, released with it, a later read throws), so the capture's
  read is a DMA the stepping thread does not wait for, on a transfer queue of its own so it overlaps the next steps (two
  staging sets in turn, the capture kernel waiting for the read that last left its set, the read for the kernel, each
  queue flushed before the other waits on its event, as the spec requires; 1.9 → 1.3 ms a full-HD frame on the card); a
  .NET-pinned array is pageable to the driver, which drained the queue and copied on the stepping thread inside the
  "non-blocking" enqueue, 2.9 of a full-HD frame's 2.96 ms (the old note blamed the single queue: a second queue alone
  changed nothing). The scratch runner's snapshots stay ordinary, since a result outlives its engine;
  `Reset`, `SetEmitters`, `SetProbes`, `TryReplaceScene` (another scene of the same grid, edges and image-source count
  swapped into the kept engine: the GPU re-uploads c², damping and the source tables into its buffers and keeps the
  compiled program, the sub-steps re-derived; 2026-11-03, the user's choice: the scratch runner uses it, 55 ms for
  Example 16 against the 180–350 ms OpenCL build every part edit had cost; `library_check` proves a replaced engine
  steps like a fresh one on both engines). The scene reaches the card as compact maps (2026-11-03, the user's choice: the
  step kernel is bound by the card's bandwidth, about 400 GB/s at 84 bytes a cell a step at 4th order with intensity): a
  byte index into the table of the scene's at most 256 distinct c² values (the image's red levels) and a 16-bit index
  into the table of its damping values (the blue levels and the rim's depths, a few hundred; room for 4096, a scene with
  more rebuilds), the tables holding the CPU's floats, so the arithmetic is unchanged; and the intensity's blend of its
  two stages is made where it is read (the captures, the probes, `PrepareFrame`), not written every step: 69 bytes a
  cell, the step 0.47 → 0.38 ms at full HD and 4th order, 0.25 → 0.21 at 9-point. `PrepareFrame`, which no path of the
  app uses, reads both stages and blends on the host. **Measured against the engine before these commits** (2026-11-04,
  the user's question whether the day's engine work changed a result: the library at d0a42e3 built from a `git archive`
  export under %TEMP%, both stepping the same painted scene with two sources 200 steps, a 32 px rim, open edges, both
  stencils): the field bit for bit the same, through `PrepareFrame` and through a capture; the captured intensity, the
  app's path, bit for bit the same, 0 of 2,073,600 halves differing; `PrepareFrame`'s host blend alone differs in about
  a thousand values of two million by one ulp at 1e-45, where the card flushes subnormals and the host does not. Numerics are Jeroen's (dx = dt = 1, leapfrog, 9-point or 4th-order
  Laplacian, soft sources, EMA intensity; a scene pixel with red 0 is a mirror, n = 1000, his V0.30 rule,
  2026-10-18), verified CPU = GPU by the harness; seeds from `Pcg32` make CPU = GPU
  bit for bit on random gaps. A source's frequency level, amplitude and phase take decimals (2026-10-19, Jeroen's point 10); the
  level is capped at 255, above which the grid cannot sample the wave; object sources carry their frequency as a float on
  the GPU, the 256-entry table serves the image sources. The importer's fallbacks differ from the model's defaults on purpose.
  **The smoothed square's envelope is no longer Jeroen's formula** (2026-11-10, his report with a project of one source,
  20 cycles on and 2 off, which emitted for 11 and paused for 11): his formula, the same in his VB on both engines and
  ported line for line, took the rising step by the nearest rising edge, switching at half the period, and the falling
  step by the edge last passed, right only inside the on-span of a burst no longer than its gap: a longer burst was cut
  at half the period by a step of the whole amplitude, and each edge's outer half was missing, a step of 0.49 unseen at
  phase 0, where the wave is 0 at both edges. Now a position takes the burst nearest in time, the gap shared at its
  middle (`WaveSimulator.PulseEnvelope` and the kernel): continuous for any widths, and the old values bit for bit in
  an on-span up to half the period. Measured on his file at the source's cell, both engines: 11 loud cycles of 22
  before, 20 after, the gap's two at 0.24 to 0.37 of the top (the atan edge has a long tail: the middle of a 2-cycle
  gap is at 0.107 of the amplitude, of a 10-cycle gap at 0.022). Of the examples, 16 (the figures' project: one burst,
  detached inside it) and the Gaussian ones are bit for bit the same; 1, 18 and 23 (10/10, 10/20, 15/15) gain the outer
  halves, 1 to 2 % of a burst's energy. `library_check` proves the shape for six pairs of widths and 20/2 through both
  engines.
- **Frames vs steps**: frame k is the state after step `k × VisualizeEvery`.
- **The engine choice is a user setting** (Simulation > Use the GPU, default on, CPU fallback logged).
- **Frame store**: budget = the lesser of free physical memory and available commit (2026-10-19: Windows refuses an
  allocation at the commit limit first, and a run that filled the physical measure left no room for the preview's
  bitmap, which took the app down; a failed bitmap is now logged and the picture stays empty) minus 2 GB, capped by
  Simulation > RAM cache size (Auto or a share); the
  outgoing run's frames count as reclaimed and the new store waits for their release. No memory mapping, no OS
  cache reliance (both wrote at disk speed). Disposed on the thread pool. Full HD, 2500 steps, both planes: 10.4 GB
  in 16-bit, half in 8-bit. The producer touches a fresh block's pages on up to four cores (2026-11-03: Windows zeroes
  pages at 5 GB/s for one thread, 1.7 ms a full-HD block, which had been the writer's clock); the writer's copy into the
  block stays one plain `CopyTo`, the clock of a 16-bit run in RAM, since every parallel copy slowed the card's DMA by more
  than it gained (see Numbers). **Do not resurrect**: a parallel or dedicated-thread copy in `FrameStore.Write`.
- **Reduced preview** (View menu): resolution 1/2/4, frames 1/2/4 (the timeline never changes), samples 16-bit or
  **8-bit palette index, the default**: the stored samples at the palette's 255 colours and the captured brightness,
  half the store; 16-bit keeps the brightness turnable after a run and shows every level of the gradient. In 8-bit a
  brightness edit is a simulation edit. **A 16-bit intensity plane holds √I** (2026-10-23, the user's choice: the
  renderer colours √I, and as I the dark end lay among half's subnormals, below 6.1e-5, up to a palette level between
  neighbours; as √I the floor is at I = 3.7e-9, the brightest parts a bit coarser): `FrameShape.IntensityRoot`, from the
  run's `PreviewQuality`, produced by `FrameConversion` and `capture_half`, reported by every `IFrameSource` through its
  `Shape`; the renderer, the screen path (which blends it as energy) and the fit go by its `SampleKind`. **The 8-bit
  index is the palette index of the half a 16-bit frame would store** (the field, √I), computed on the CPU and in the
  kernel (a bit-level rounding there, the GPU program built with a correctly rounded square root where the card has one),
  so CPU and GPU store the same bytes (2026-10-19) and both previews start from one value (2026-10-23, the user's choice:
  rounding I to a half first had merged the darkest indices at a high brightness, 2e-8 at 30 coming out 0 instead of 1).
  Every export needs the 16-bit samples (2026-10-23, the user's choice, so a picture has every level of the gradient):
  an 8-bit run, a reduced resolution or skipped frames make the export run the simulation again at full quality first.
  Only an OpenEXR export of I makes a run that stores I itself (squaring √I back would lose a bit); as coded, that run is
  then the one the preview shows until the next edit or run (undecided whether it should be a side run).
- **First start** (2026-10-31, the user's design): with no settings file, the three quality settings and the dithering are
  chosen once from the machine (`PreviewDefaults.TierOf` on `MachineFacts.Read`; `library_check` proves the table): 32 GB
  or more → 16-bit samples dithered to 16 bits; else any of under 16 GB, a cache drive with a seek penalty (`StorageDevice`:
  `IOCTL_STORAGE_QUERY_PROPERTY` on the volume, then on its disks, no elevation; unknown counts as none; on the workstation
  the HDD D: answers yes, the SSD E: no, and C: on the PERC H310's RAID virtual disk nothing, as Windows itself calls its media
  type unspecified), no OpenCL GPU or ≤ 4 threads →
  half resolution, every second frame, 8-bit; else the fixed defaults. Tested at 30 and 15 GiB, since Windows reports
  under the nominal size. The settings file is written at once, so deleting it redoes the choice; the fixed defaults stay
  the initialisers (the figure harness strips the attributes, the designer); one Info line after "ready" names the facts
  and the choice. The levers buy memory and readback, never step time: the divisors shrink the captured frames, not the
  grid (at the defaults 11 of the 24 examples exceed 6 GB, Example 7 needs 19 GB; at Low the largest is 2.4 GB).

## The app's rules

- **Live editing**: a simulation edit (scene, solver, seed, sources) marks the run stale, **stops playback and any
  active run**, asks for the scratch frame of the position at once (the scratch runner keeps its engine across a
  scene edit of the same grid, `TryReplaceScene`, and rebuilds only for another grid, edges or options) and re-runs
  automatically **when the edit has finished** (setting, default on; 2026-11-07, the user's choice, replacing a 120 ms
  debounce of the requests and 1.5 s of quiet before the re-run: **nothing in the live rules keeps time**). "Finished"
  is the undo history's boundary, `Platform/EditBoundaries` (a mouse button released or pressed, the focus leaving,
  Enter in a text box, a toggle button's click, a command before and after, a drop, the two edits that arrive without
  input), handed to
  `LivePreview.EditFinished` by the window's `OnEditFinished`; a re-run due while a frame computes, a run is active or an
  export writes follows that thing's completion event. So arrow-key nudges on a handle or in a number box re-run when
  the focus leaves or Enter is pressed, a drag's release, Ctrl+Z or a check box ticked with Space at once, a View menu change at the command's end.
  Every change asks for its frame and the runner coalesces: **the frame in flight is finished and the newest request
  taken after it** (2026-11-09, the user's decision, dropping the 2026-10-16 rule that a request arriving while one
  computed abandoned it within 16 steps: that served the one request a pause of the hand made under the debounce, but
  with a request per change it starved every frame longer than the gap between two edits, frame 100 of Example 16
  showing nothing during a steady burst). Measured (a burst of 120 source edits at 32 a second, the edit's cost on the
  UI thread 0.24 ms median and 2.8 ms worst on Example 7's lattice): frames are shown at the engine's rate during the
  drag, one per frame time plus 5–9 ms of capture and hand-over (Example 16 at frame 0, a 20 ms frame: 31 a second;
  at frame 100, 59 ms: one per 64 ms; at frame 400, 221 ms: one per 234 ms; Example 7 at frame 50, 44 ms: one per
  53 ms), and the final frame comes 1.5–2.5 frame times after the last edit (31, 125, 377 and 110 ms), the price of
  finishing the frame in flight. A part or scene edit asks for its frame when the composition is
  done (`SceneComposer.Composed`, `IsComposing`), never waiting for the worker on the UI thread, so a steady part drag
  shows frames only where the composer idles (Example 16: 6 in 3.6 s; Example 23's fibres: at the end). An automatic
  run never moves the preview. A probe edit never starts a run: only
  the recordings are stale, the scratch frame refreshes the graph; except that an active probe the run does not
  record (switched on, added) is a simulation edit (2026-10-18). No scratch frame while playback runs. Any run
  is paused while a scratch frame computes. Every scratch request taken is computed in full, however long (no
  time budget, 2026-10-16); since 2026-11-09 none is abandoned for a newer one (see above). **A new scratch result is
  a new picture, whichever of the runner's two snapshots it came in** (`PreviewPane._scratchShown`, 2026-11-09, found
  when the manual's figures came out with black pictures under graphs of frame 1200): the pane had made a new source
  for the presenter only when the result's snapshot was another object than the one on screen; since the runner
  finishes the frame in flight, a scrub leaves outcomes for positions already left, which are not shown but take
  their turn of the two snapshots, so the frame the scrub ends on came, as often as not, in the snapshot on screen
  and nothing was drawn. A probe of eight bursts of Home and forty PageUp, read from the log (the live rules' "frame N
  from scratch" followed by the presenter's new source): the last picture drawn in 0 and 1 of 8 before, 8 of 8 after;
  `PreviewCheck` 43 of 43. Left as it is: a picture kept on screen during such a burst belongs to a snapshot the
  runner has reused meanwhile (`ScratchResult.Snapshot` is valid until the next outcome), so a re-render of it before
  the burst's last frame arrives, a resize in mid-scrub, would read the newer frame.
- **A picture changes only when a frame for its position arrives**; otherwise the last picture stays. A long wait
  dims the pictures under a spinner, mirrored on the taskbar.
- **Brightness** (2026-10-20, Jeroen's point 9) sits in each picture's header strip (`HeaderStrip`: title, a caption that
  gives way first, the box and **Auto** at the far end; turned on its side with the title); Auto fits once to the frame
  shown (the 99.5th percentile of |field| or √intensity at the palette's top, the user's choice of the variants), an
  8-bit preview computing that frame in 16-bit first; the context menu has Brightness and Auto brightness. The box
  steps by a factor (`NumberBox.Logarithmic`, 2026-10-26): two percent a key, a fifth a page, three significant digits.
- **Edge line** (2026-10-25, the user's choice): a 1 px line around each preview picture in the colour of what a wave
  meets at the canvas edge: pure red for a reflective wall (the index's channel), pure blue for a damping rim outside
  the canvas (the absorbers' channel; `Scene.HasRim`; a rim wins over a wall), nothing for open edges or a damping band
  inside the canvas; the scene's colours, not the theme's; preview only. For it the picture's cell is sized to the
  picture (the overlays measure to the uniform fit and remeasure on a canvas change) while `PictureFrame` fills its
  cell, so the heading stays at the top.
- **Play means "show me the simulation"**: it starts an automatic run when needed and holds at the newest frame
  until the run catches up. Space plays or pauses with the focus in the preview, the pane's own binding; a window-level
  Space that took the key wherever the focused element left it alone was built and reverted the same day (2026-11-04,
  the user's decision). **The run's and the playback's controls are in both places** (2026-11-04, the user's request):
  the transport bar has Run, then Pause the simulation (F6, the same `TogglePause` command as the menu's, its tooltip
  and glyph switching to Resume on a paused run), then the timeline's Play and Pause; the Simulation menu has Run, Pause,
  Stop, then Play (F7, `PreviewCommands.PlayPause`, a window command handed to the preview pane's own
  `MediaCommands.TogglePlayPause` binding, so it works from anywhere; the entry reads Pause playback while playing).
  The timeline figure's badges and the manual's two lists count the new entries. **The playback clock is a thread timer whose ticks are posted to the
  UI thread at Render priority** (2026-11-04, the user's report: playback stuttered while the mouse moved, and only a hand
  on the mouse showed it): a `DispatcherTimer` is a Win32 timer message, which Windows delivers only when no input is
  queued, so a mouse reporting a thousand times a second starved the clock and no frame came for seconds (a gap of 3.2 s
  measured under injected moves every millisecond; 63–87 ms at a move every 8 ms); a posted operation is taken before
  input, and the same flood now leaves gaps of 32–47 ms, 68 at a move every tenth of a millisecond. **The run's
  heartbeat is posted at Loaded priority** for the same reason (2026-11-04, the user's report: the Live follow of a
  running run stuttered the same way): the dispatcher's background range is Background and Input, served only when no
  input is pending, else through a 1 ms Win32 timer that queued input starves; at Background a flood of 4 s on Example 7
  left the status at the step where the flood began, no picture came, and the run's Completed state landed 7 s after the
  stepping ended; at Loaded, the lowest priority ahead of input, the follow went on at 100 frames in 3.3 s with a
  worst gap of 71 ms, the status counting. Background had been chosen on 2026-10-16 against the opposite, keys and
  clicks delivered after the run, when the progress was several posts a second each posted the moment the last was
  handled; the one paced heartbeat leaves input its turn at any priority (RunCheck's 71 checks, which press keys
  during a run, pass). The scene composition's coalescing stays at Background: it is meant to wait for the hand to
  rest. **The presenter's
  log line carries the block's worst** beside its averages, the longest frame, the longest wait at the bitmap and the
  longest gap between two presented frames: a stutter is a gap, which an average over 100 frames hides (it had shown 28
  frames a second across a 3-second freeze). Measured on the way and settled: the app's input handling is not the cost,
  injected moves at ten thousand a second changed the frame times by nothing once the clock was posted. Live follows a running run until the user scrubs; it ends with a last follow to the
  final frame. A replaced project goes to frame 0; a discarded run keeps the position. In/out points are steps.
- **Probabilistic detectors** (2026-11-07, Jeroen's addition, integrated 2026-11-09; his choices: Poisson statistics, the counting
  detector's steps drawn from its frame's rate, a Probabilistic submenu under "+ Probe", the imaging detector's picture
  accumulated to the frame shown): `Probe.Detector` Standard, Counting or Imaging, with `Sensitivity` (expected
  detections per unit of intensity per step at a point); a detector is a line probe of the integrated intensity (the
  setter and `Normalize` force kind, layer and measurement; the form greys them). The engines do nothing new: the line
  is integrated as an integrated line probe is, and `ProbeCollector` turns the totals into detections through
  `PoissonProcess` (a unit-rate process walked along the cumulative expectation, seeded from the project's seed and the
  probe's name: the count at a position is Poisson of it, counts never fall, and one asking at the end gives what
  frame-by-frame asking gives, so the scratch frame and the run agree; above 100,000 expected events a call jumps by a
  normal approximation). A counting detector's series: the frame's increase of the line's total, shared evenly over its
  steps, each step the process's events in its share (the scratch runner collects profiles frame by frame when one
  exists, `ProbeSampler.HasCounting`); an imaging detector's profile: each pixel's process up to its total. The graph,
  the CSV and the layout choose series or profile by `Probe.ShowsProfile`, not the kind, **and so does the form**
  (2026-11-10, Jeroen's report that "Full" could not be chosen for a detector: Step axis and Auto-scale steps were greyed
  by the kind, a line for every detector, though a counting detector's graph runs over the steps and honoured both; the
  WPF styles `SeriesOnlyCombo` and `SeriesOnlyCheck` and the Avalonia form's `!ShowsProfile`, the two hints and the
  manual now say "point probes and counting detectors only"; proven by a keyboard probe on `tiny_probes.wdproj`, the
  point probe stepped through the Detector box: enabled as Standard and Counting, greyed as Imaging). Going back to
  Standard changes the detector alone: the kind, the layer and the measurement stay what the detector set, a standard
  line probe of the integrated intensity, whose graph is a profile, so the two stay greyed until the kind is set to
  Point (his report's second half). **That stays** (2026-11-10, the user's choice of three: the probe left behind
  measures what the detector's counts are drawn from, on the same line; remembering the earlier kind, layer and
  measurement would be lost on save and undo or become file format, and a reset to a point probe on the field would
  discard the line).
  `AddProbe(x, y, detector)`
  names a detector "Detector n"; the "+ Probe" button and the Probes folder's plus open a menu of kinds as the parts' do
  (`ProbeMenuEntries`; `ProjectItems.HasKinds` and `AddProbe(x, y, detector)`). The default sensitivity is 20 (2026-11-08, Jeroen's report: 0.05, set from a plane
  wave's 0.175 intensity a pixel, showed nothing, since a point source's wave at a hundred pixels is a few hundredths
  of a percent of that; he needed 10 to 50). An imaging detector has `BinSize` (1 to 20 px, default 4, his
  request the same day): the bin's pixels' detections added up and shown at each of them, the last bin shorter. `library_check` proves the statistics (mean, dispersion), the monotone image, the
  agreement of once and frame by frame, the seeds and the CSV.
- **A new source, probe or part lands at the centre of the canvas the preview shows** (2026-10-26, Jeroen's report: one
  at the corner went unnoticed inside the rim); the model's parameterless adders take the scene's size, the window passes
  the shown canvas's centre, which an image scene sizes; a new probe's line end lies 100 px to the right.
- **Handles and overlays**: sources, probes, emitter dots and the rotation handle are drawn over the pictures in
  canvas pixels, over a dark halo, clipped to the picture, in a light grey, the selected item's in a bright yellow
  (`PictureHandle`, `PictureSelection` in `Controls.xaml`, 2026-10-26, Jeroen's tester: the accent blue was duller than
  the rest and lost on a blue scheme and to colour-blind eyes; the keyboard focus keeps the accent); a probe's handles and line appear only on the picture of
  the layer it measures (2026-10-18), sources on both; graph boxes lie over every handle and dot, the selected
  item is drawn last among its kind, except the handles themselves (Panel.ZIndex breaks the Tab cycle, 2026-10-18).
  **A graph box's two thumbs are clipped by the boxes drawn in front of it** (2026-11-10, the user's report: a box's
  resize grip was drawn on a box in front): the boxes are one drawing under all the thumbs, the selected probe's last,
  while the thumbs stay children in the project's order for the Tab cycle, so a rear box's grip showed through the front
  box's transparent thumb, and with the front box the earlier one in the project a press on its own grip or anywhere on
  the overlap took the box behind. `ProbeGraphOverlay.DrawnBoxes` is the one order for the drawing and the clips
  (`Uncovered`, set in `ArrangeOverride`, re-arranged at a selection change); a clip counts for drawing and hit testing
  in WPF and in Avalonia, both heads the same. Proven in both by a startup-hook probe on `tiny_probes.wdproj` with two
  overlapping boxes, asking the framework what a press hits (`InputHitTest`) for no selection and for each probe
  selected, and rendering the picture's cell: before, the grip on the front box and "Probe 2" hit on the selected
  Probe 1's grip and overlap; after, no grip there and Probe 1's own thumbs hit (the Avalonia "before" by taking the
  clips away in the probe). A thumb covered altogether is reached with Tab, which selects its probe and brings its box
  to the front;
  arrows nudge, Del on a handle or a graph box removes its item or the selection it belongs to as the tree's Del does
  (2026-10-26), selection syncs with the tree, a click on empty space deselects. **Display > Handles in the preview and
  Labels in the preview** (2026-10-29, the user's request, both on by default) hide every item's handles, lines, emitter
  dots and levers, or the names beside the handles, except a selected item's, which shows them either way (the tree is
  where hidden items are picked; collapsed handles are out of the Tab cycle); the graph boxes are not affected. **Group selection** (2026-10-19): one shared `SelectedItems` list on the window beside the
  current `SelectedItem` (the properties pane shows the current one); in the tree Ctrl+click, Insert or Ctrl+Space
  toggles a row, Shift+click or Shift+arrows selects the range from the anchor, Ctrl+A everything, the row's context
  menu has both; on the pictures a drag on empty space is a marquee (Shift adds, Ctrl toggles), Ctrl+click on a handle
  toggles, Shift+click adds; a drag of the handle of one of several selected items, or the arrows on it, moves them all
  through `IMovable` with the delta limited so none leaves the canvas (the formation holds); Escape during a drag puts
  things back; Remove, Duplicate and Enabled act on the whole selection when their target is in it; the lever, grips
  and point handles belong to the current item alone. Probe graph boxes follow their probe while attached, resize by a grip, keep their text
  size; an amplitude axis is symmetric about zero; an intensity graph's value axis can be logarithmic (2026-10-20,
  Jeroen's point 8: `Decades` below the fitted top, zeros on the bottom line, powers-of-ten ticks; the field stays linear).
- **Undo and redo** (2026-10-22, the user's request, unlimited): `ProjectHistory` keeps one snapshot per step, the
  project file's XML without the scene image plus the image's bytes by reference (equal bytes shared). **An edit becomes a step
  when it has finished, and time has no say in it** (2026-11-06, the user's rule, replacing a quiet period of 0.5 s
  with no mouse button held, which he called garbage): the changes gather in a step in progress until it is clear that
  editing the value is over. `Platform/EditBoundaries` tells the history so from the window's input, no control taking
  part: a mouse button released (a drag, a scrub, a click; heard through the input manager's `PostProcessInput`, since
  WPF raises no bubbling event for a release or a key whose preview event a control handled, as the number box does for
  its scrub and the property editors for Enter), a mouse button pressed (before it changes anything: what the keys or the
  wheel were editing is finished), the focus leaving the element (so arrow keys on a handle, taps or held, the keys and
  the wheel on a number box and typing in a text box are one step for as long as the focus stays), Enter in a
  single-line text box, a check box or another toggle button clicked (2026-11-10, the user's rule: it has written its
  value and nothing more can follow, yet ticked with a key it had waited for the focus to leave, while the mouse's
  release finished it at once; heard as the button's Click, raised after the toggle by the mouse, Space or the access
  key, and never by a change from the model; a check box's + and - go by its key handler and are heard there; a combo
  box stepped with the arrows still waits for the focus to leave), a command before and after it (not a text box's
  own editing commands), a drop; and the window
  ends the step itself where an edit comes without input (a brightness fit computed later, the scene image file
  changed on disk). Undo and Redo end a step in progress first, so an edit can be undone while it is going on. Undo and Redo (Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z; a text
  box keeps its own) write the step back into the same project object, the lists refilled and the settings objects
  replaced, so the watcher reports it as an edit (stale run, scratch frame, re-run) and nothing is recorded; the
  selection follows by name, then by index; the title's asterisk clears at the saved step. The Edit menu (between File
  and View) holds Undo, Redo, Cut, Copy, Paste, Delete and Select all (2026-11-04, the user's request; Ctrl+A is the
  command's gesture, so it works in the preview too, a text box keeping its own); the UI harnesses step over it. **Every
  entry of a menu has a mnemonic of its own, in all four languages** (2026-11-04: the tree's context menu had A, T, P
  and D shared, so a press moved the highlight and the command needed a second press; a shared string, Cut, Copy, Paste,
  Select all, the three exports, must be unique in every menu it appears in, the Edit menu, the File > Export submenu,
  the tree's and the pictures' context menus; proven by posting the Apps key and the letter once on a tree row). **A
  click on a window's title bar while a menu or a context menu is open lands** (`MenuClickThrough`, 2026-11-04, the
  user's request: the close button had needed a second click): an open menu holds the mouse for Windows too, so the
  dismissing click arrives as a client click; it is hit-tested with WM_NCHITTEST and, on the caption, the system menu
  or a button, posted again as WM_NCLBUTTONDOWN behind the menu's closing; the harnesses post no mouse, so the user
  tested it by hand (2026-11-05: it works, and it is the behaviour he wants, as other applications have it: a click in
  the non-client area while a menu is open closes the menu and still lands, rather than being swallowed).
- **Copy, cut, paste, drag and drop** (2026-10-23, the user's choices): `ProjectClipboard` writes sources, parts,
  probes and schemes (the `INamed` items) and the Scene, Simulation and Display objects as a fragment of their own file
  elements, in the app's format and as plain text (read back from text too, so a text editor takes and gives the XML).
  Paste goes behind the current item, before it for a drop on a row's upper half, renames only on a clash, emitters
  from the target's seed; a settings object replaces only on its own row or the project root. A tree drag carries the
  same text and nothing else; the target decides (2026-10-23, the user's choice, replacing a live item in the drag data
  and a refusal of the own drag by the pictures): the own tree reorders the item it keeps in a field, another window's
  tree pastes, and a picture of this window or another pastes and moves the objects as a group onto the drop pixel
  (`PlaceAt`, held inside the canvas), so a drag from the tree to the picture duplicates. `ObjectsData` reads a drag or
  the clipboard and parses each text once; the window reads the clipboard once per change of Windows' clipboard
  sequence number. The tree and the pictures claim a drag only when they take it; a file dropped anywhere is the window's,
  which opens it (2026-10-26, Jeroen's report: for three days a file over the tree or a picture was refused).
  **The tree's drop mark is one adorner for the whole drag, its line on the boundary of the two rows** (2026-11-10/11,
  two reports of the user's). It jumped: drawn inside the row under the pointer, the slot between two rows showed 2 px
  higher over the lower half of the upper row than over the upper half of the lower, never on the boundary, and in the
  gutter left of a row, the room of its indent and of the chevron a leaf does not show, no row was under the pointer and
  the drag lost its target and its line. Then, steady, it flickered: WPF takes every element under the pointer for a
  drop target of its own (`AllowDrop` is inherited; `OleDragOver` in WPF's `DragDrop.cs`, read at the source) and at
  each crossing from one to the next raises DragLeave and DragEnter and no DragOver, so the tree, which heard DragOver
  alone, took its line away at the leave and showed it again only at the pointer's next move; and it made a new adorner
  at every event. Now `InsertionAdorner` adorns the rows' panel inside the tree's scroll viewer, is made once and
  kept (`ProjectTree.Mark`), and is told its mark in the panel's coordinates, a line centred on the edge of the row's
  strip (the template's `Bd`, Avalonia's `PART_LayoutRoot`) or an outline around a folder's header; "after this row"
  and "before the next" are the same numbers, so it is not even drawn again while the pointer stays about a slot. The
  tree handles DragEnter as DragOver, a DragLeave only hides the mark, and the row is found by the pointer's height, the
  first met from the pointer to the right (`RowAt`). Never the DragLeave's position: WPF gives it relative to the new
  target. The Avalonia tree has the same one mark; there the adorner layer is the window's and sets an adorner's `Clip`
  itself, so the mark clips its own drawing to the tree's view (a row wider than the tree had its line drawn past the
  tree's edge). **Avalonia inherits `AllowDrop` too** (`DragDrop.AllowDropProperty.Inherits`, read by reflection from
  the 12.1.5 assembly) and its drag device raises DragLeave and DragEnter without a DragOver at a change of the target
  as WPF does; assumed otherwise on 2026-11-10, with the leave-and-enter case reported as "not applicable", the
  Avalonia tree went on flickering until the user said so the next day, and now hears DragEnter as well. Proven in both heads by a startup-hook probe that puts the tree through a drag's events: WPF before, y
  184–185 or 186–187 about a boundary at 186 and nothing in the gutter; after, y 185–186, and over 60 events about one
  slot the same adorner, asked to draw again at none, never without its line, kept through a DragLeave followed by a
  DragEnter and hidden by a DragLeave alone. A real drag cannot be made by a probe in WPF (OLE reads the real mouse
  button), so the user tries it by hand. **In Avalonia a probe drives the real drag device**: a startup hook sets the
  tree's `_dragItem` and gives `DragDropDevice.Instance.ProcessRawEvent` a `RawDragEvent` per pointer position (the
  window as the input root, an empty data transfer made by reflection), and the device finds the target and raises the
  events as in a drag. Over 244 moves about one slot the target changed at 26: before, the mark was gone after each of
  the 26 and did not show on entering; after, never gone.
- **Icons** (2026-10-22/23): a menu entry gets one through the attached `MenuIcon.Drawing`, the name of an `Icon<Name>` (and
  `Icon<Name>Fill`) `StreamGeometry` in **`WaveDudeNext.WPF/Themes/Icons.xaml`, the one icon dictionary of both heads**
  (2026-11-10, the user's choice: merged into `Controls.xaml`, and the Avalonia head builds its `Themes/Icons.axaml` from it
  at build, see Port), which makes an `IconView`, which the tree's rows and toolbar use directly (`PlusBadge` on the add
  buttons) and which draws in the inherited text colour, so a disabled row's icon greys with its name through one trigger
  on the row. **Every icon is a drawing** (2026-11-10, the user's choice, option A of two: an icon font Linux and macOS do
  not have against drawings for both heads): the 36 Segoe Fluent glyphs the WPF menus, the tree's folders, the Messages
  pane and the scene form had used (`MenuIcon.Glyph`, `IconView.Glyph`, the `IconGlyphText` style, 2026-10-22, all gone)
  are 40 drawings in the same style (New, Open, Recent, Examples, Save, SaveAs, Export, ExportLast, CloseProject, Exit,
  Undo, Redo, Cut, Copy, Paste, Delete, SelectAll, ToggleSelected, Duplicate, Remove, ToggleEnabled, MoveUp, MoveDown,
  Add, Run, Pause, Stop, Brightness, Panes, Theme, Zoom, Language, About, Manual, Clear) plus the port's five for the
  tree's folders (Scene, Simulation, Display, ColorSchemes, a picture frame, a stopwatch, a monitor, a palette, where the
  WPF tree had shown glyphs); a still export's entry shows the Scene drawing, as it had shared the folder's glyph. Seen in
  captures of the five menus of both heads and of both trees: the same drawings entry for entry. A lens row's icon is its own
  profile (2026-10-29, the user's design): nine `IconLens<Left><Right>` drawings, each surface convex, flat or concave by
  its sag at the rim (`LensPart.ShapeOf`: flat under half a pixel, 2026-10-29, the user's report: Example 1's mirror of radii
  14401 on 104 px showed as biconvex), chosen by `LensIconConverter` from R1, R2 and the diameter in a template scoped to
  the row's icon, so it follows an
  edit; the Part menu's `IconLens` is the lens-and-mirror pair the user drew, a biconvex lens beside a concave mirror with a
  flat back, which no row shows. The Mirror box renames the part, Lens to Mirror and back
  (`LensPart.MirrorName`, run by the box's command and never by the setter, so a loaded or undone name stays). Drawings are 16 px, 1 px strokes
  on half pixels, ink in rows and columns 0 to 14 (odd, so it centres like text); every tree row, toolbar button and
  menu entry is 23 px, the menu bar's headers through their padding. The pictures' handle glyphs stay in
  `PreviewPane.xaml`, odd-sized (21, 15, 13 px) with the point at the centre pixel (2026-10-26, Jeroen's report: at 20 px the
  window's layout rounding put the dot, its halo and the crosshair half a pixel apart).
- **Colour schemes are gradients** (2026-10-23, the user's design): the model holds the stops and no table; how many
  colours are taken is the renderer's (`ColorLut`): 255 at i/254 for 8-bit frames (the author's table, truncated as in
  his Python), and for 16-bit frames a table of the 65,536 half values per layer and brightness, the gradient at each
  value's position, rounded (the 255 entries had left visible patches two to five levels apart in dark gradients).
- **Two preview paths** (2026-10-23, the user's design): 8-bit samples are the fast preview, coloured at the store's
  size and scaled by WPF, as before. 16-bit samples are drawn at each picture's size on screen (`ScreenRenderer`, the
  pane reports the Image's rounded `RenderSize` in device pixels): resampled in value space, never colours (a box mean
  when shrinking, bilinear when enlarging, so every pixel shows a real value in a gradient colour), then the half
  table. The pictures are `CanvasImage`s, sized by the canvas and never by their bitmap, so a 16-bit bitmap is plain
  screen pixels drawn 1:1 and a new one never moves the layout (2026-10-23: a resolution set to make the bitmap measure
  the canvas came back from WPF in single precision, the reuse test failed on every frame, and the pane flickered).
  **Preview dithering** (View menu, user setting, off by default until the user has seen it; only with 16-bit samples):
  the colours at 16 bits per channel (`DeepHalfTable`) to 8 through a Bayer matrix of 2, 4, 8 or 16 cells a side for
  10, 12, 14 or 16 bits, fixed to the picture's pixels (on the screen's, by the window's layout rounding); preview only, never in an export (a video crushes it; an export
  above 8 bits per channel is a possible future goal). A better algorithm than Bayer may come later.
- **Colour schemes** are stop lists in `.wdscheme` XML, shipped ones embedded, user ones in
  `%LOCALAPPDATA%\WaveDudeNext\ColorSchemes`, project ones in the `.wdproj`; an in-house picker edits stops;
  renaming a project scheme repoints its layers. **The scene image** is embedded and follows its file on disk
  (**read when the file has been written and closed, at once, time having no say**, 2026-11-06, the user's rule: the file
  is opened refusing to share it with a writer, which fails while any program has it open for writing; then nothing
  happens and the writer's closing is the next event, measured at 0 ms after the close; a quiet period of 400 ms and a
  retry loop that slept up to 0.8 s on the UI thread had stood there; the window's activation looks too, but acts only on
  a file changed since it was last read, so an undone re-embedding stays undone), the
  path being where it came from (2026-10-26, Jeroen's report: the scene form shows the image rows for an image scene
  only, says "(not found)" beside a path whose file is gone, which changes nothing, and has three buttons beside the name,
  2026-10-29, the user's design: "…" picks a file, a save glyph writes the embedded bytes to a file, an export that
  changes nothing in the project, and the X drops the name, the path and the bytes and leaves a blank canvas of the
  image's size);
  the "glowing edges" outline overlays the pictures. **Import** of Jeroen's `configuration.json` is key for key; a source whose position was
  never picked (`has_position` false) stays enabled at its x/y, since his program simulated it there (Example 11's
  lattice came in disabled and ran black until 2026-10-16).
  **Languages**: four string tables, live switch; converter output needs the language as an extra binding input.
- **Export**: field, intensity or both, a still of the shown frame, from the current run when fresh and full,
  else a run is made first. **The Save dialog starts where the session's last export of its kind went** (2026-10-28,
  Jeroen's report, the user's rule: the first export after a project is opened starts in the project's folder under the
  project's name as before, every one after it in the last one's folder; a run's export proposes the last name with the
  layer suffix swapped, a still and probe data their own names; per kind and per project file, kept in the user settings
  as `ExportHint` entries, 20 projects, restored when the project is opened, the user's choice over the project file: a
  path belongs to the machine, not to a shared file, and an export must not dirty the project).
  **Export last** (Ctrl+E, File menu below Export, 2026-10-28, the user's request): the session's last run export again,
  the same layers, format and path, no dialog, an existing file or sequence folder replaced without a question; a still
  does not count; disabled until the first run export since the project was opened. Shortcuts (the user's choice, GIMP's
  pair): Ctrl+Shift+E, I, B the field, the intensity, both; Ctrl+Shift+F, G the two frames. In the dialog's order (2026-10-19) H.264 MP4 via Media Foundation (the only
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
  brightness, outline or boxes; a sink that `WantsHalves` gets the plane from the job unrendered. Two entries in the
  dialog (2026-10-23): the intensity as I (a run storing I is made first; a scripted `.exr`) or as √I (the preview's
  run as it is); the field the same in both. **Source markers**
  (2026-10-21, Display, off by default, the user's choices): the preview's marker, a composite source's dots and the
  name, enabled sources only, scaled like the probe labels, under the probe markers and boxes. Harness hooks:
  `WAVEDUDE_EXPORT_PATH`, `WAVEDUDE_DISCARD_EDITS=1`.
- **UI zoom** (View > UI zoom, 0.5x to 3x, `Settings.UiZoom`, default 1, live; 2026-11-01/02, the user's request, first
  as a start-up switch for figures at a higher pixel density without a DPI change, then as this setting; `--zoom=<factor>`,
  0.5 to 8, sets the setting at start): every window's content gets a layout scale (`WindowZoom`, from the theme's Window
  style, at Initialized and at each change of the setting) and Ideal text formatting (Display formatting rasterises on the
  100 % grid and WPF scales those glyph bitmaps, soft; seen pixel by pixel), window sizes are multiplied by the change
  (the main window's bound size carries the zoom from `ZoomConverter` when read and is set like the others after a change,
  never re-read: WPF writes both sizes back from the handle after the first resize), then the window is kept inside its
  monitor's work area (`KeepOnScreen`, at SourceInitialized and after a change; the user's rule, a zoomed window had
  wandered off). Whole pixels at any factor (2026-11-02, the user's rule: a line is there or not, 1 or 2 px, never cut; at
  1.5 the About window's bottom outline had been cut): WPF rounds layout to the DPI its tree believes in, so each tree's
  root is told the real DPI times the zoom through `VisualTreeHelper.SetRootDpi` (a system-DPI-aware process never resets
  it; `HwndTarget` does only when per-monitor aware), the root and its chrome outside the transform round to nothing, the
  content rounds. **The process is DPI aware per monitor** (`app.manifest`, PerMonitorV2; 2026-11-02, the user's choice
  after the About buttons' outlines at 1.5 had stayed on the monitor's grid): only in that mode does WPF give a new
  visual its parent's DPI (its `AddVisualChild`), which the templates' inner parts, created at a tree's first layout,
  need, and a Border builds its edge pens once, at its first drawing. WPF then also puts the monitor's DPI on a tree
  when a window's handle takes it as its root and when the window lands on a monitor of another scale, so the zoomed
  DPI is put back at the window's source change and in its DpiChanged event, by the handle's real DPI, never the
  event's (telling a tree its DPI raises DpiChanged itself; taking the event's value fed back without end, a silent
  crash). A DPI change also makes the preview pane tell the presenter its pictures' device sizes again (their layout
  size stays). Users with monitors of different scales get each window rendered at its monitor's scale, title bar
  included, instead of a stretched bitmap, and a window dragged across such a boundary resizes in pixels; untested
  here (one monitor), the user tests by hand. Popups are trees of their own and WPF raises no Loaded for their
  content: the theme's popups (menus, drop-downs, the colour button) carry `theming:WindowZoom.Popup="True"`, a
  tooltip or context menu `theming:WindowZoom.Standalone="True"` in its style, and each content is watched for the
  source it gets at an opening (`PresentationSource.AddSourceChangedHandler`, before any layout), where its whole tree
  is also invalidated: a popup opened before a zoom change keeps its entries' measure under the unchanged constraint,
  and each moved onto the new grid only when a hover invalidated it (the user's report, 1 px shifts); a tooltip or context
  menu sits in a parentless popup of WPF's own, which takes no transform (its `UpdateTransform`), so its style binds
  the scale and the formatting to the setting (`ZoomTransformConverter`, `ZoomTextFormattingConverter`). The log's
  "Display scale" line after the window shows names the monitor's scale and the zoom. Code
  working in device pixels (the 16-bit picture size, text metrics, the number box's drag) asks `DevicePixels.PerDip`: the
  composition target's real DPI (not `GetDpi`, which is the raised one) times the transforms to the root, so the 16-bit
  pictures are rendered at the picture's device size and drawn 1:1 at any zoom (the 8-bit ones are WPF-scaled bitmaps as
  always). The title bar stays at the monitor's scale. The app's own hairlines, the timeline's and the graph boxes',
  are whole at any scale (2026-11-02, the user's rule): a pen of `DevicePixels.Hairline`, the whole number of device
  pixels nearest to the scale, and a `GuidelineSet` at every line's and fill's edges, which WPF moves onto the device
  grid under the full transform; at 100 % the arithmetic is the old one, pixel for pixel. The handle glyphs (1.5 px
  strokes over a halo), the dashed probe lines and the marquee are antialiased by design and stay so. The window must
  fit the screen for a capture: the Desktop Window Manager composes nothing off it, so PrintWindow returns white there. `PreviewQualityCheck` and `LanguageCheck` count
  the View menu's entries past the new one. **The manual loads its document again when the zoom changes the text
  formatting mode** (2026-11-04, the user's report: the manual's text blurred at a live change and came back sharp
  reopened): WPF reads `TextOptions.TextFormattingMode` once, at a flow document's first formatting, into the text
  formatter it keeps for the document's life (`StructuralCache.EnsurePtsContext` in WPF's source), so the inherited
  change reached the document and re-formatted its lines with the Display-mode formatter, whose glyphs are drawn scaled,
  soft, and wrapped as at 100 %; a mode set on the document itself changes nothing either. `ManualWindow.ReloadDocument`:
  a new copy from BAML (130–180 ms), the headings re-pointed so the tree's rows stay, the place kept as a share of the
  height; the old text stays on screen, scaled, until the new copy is laid out, about half a second in all, which the
  user sees as the text sharpening a moment after the change (the log line names both times). The rule for the mode at
  a zoom is `WindowZoom.TextFormattingAt`. About is modal, so no zoom change reaches its license document. Proven by a
  probe that captures the manual at a live change and reopened: pixel for pixel the same. Ideal formatting for the
  manual at every zoom, which spares the reload, was built for the user to see and refused (2026-11-05, his decision: a
  zoom change with the manual open is rare and the delay short, and Ideal costs readability at 100 %).
- **Optical parts** (2026-10-18; **`OpticalParts.md` is the reference**: the kinds, the painting rules, the measurements
  and how to add a kind): subclasses of `OpticalPart` in `Project.Parts`, one class and one XML element per kind, the
  class being the kind (no enum, 2026-10-22): a lens or mirror, the shapes under `ShapePart` (polygon, rectangle, ellipse,
  triangle, outline), the kinds of Jeroen's sessions of 2026-11-07/08 (his requests and choices, integrated 2026-11-09):
  a diffuser (`DiffuserPart`, delivered as "Diffusor" and renamed before any release, the element's name being file
  format; a `ShapePart` whose front face is rough:
  a spread of ripples about a centre wavelength, the phases from the project's seed through `ISeeded`, which the project
  hands such a part on add, load, paste and reseed; the profile kept until a roughness value changes; its shape's depth the
  exact polygon distance by a pruned search; see `OpticalParts.md`), an interference filter (`InterferenceFilterPart`: a plate with a list of `FilterLayer`s on its front, outside it, the first outermost, painted as two
  shapes through `PartShape.PiecesOf` so a mirror plate keeps glass layers; the form reorders by buttons and by a drag), a simple
  2D grating (`SimpleGratingPart`, in a "Diffraction gratings" submenu among the optics: a
  lattice of round dots of the shapes' material, a mirror by default, the diameter at most half the smaller spacing, a
  dependent bound), a facetted grating (`FacettedGratingPart`, in that submenu: the
  diffuser's plate with one wavelength, `Pattern` sawtooth, square wave or sinusoidal every `Pitch` with `Amplitude`,
  the band on the back alone; both plates are `IProfiledPlate`s painted by `DiffuserShape`), a meta lens (`MetaLensPart`, in a "Meta surfaces"
  submenu: the filter's plate with a ridge per sub-wavelength cell in front, the fill from the lens phase at the design
  wavelength and focal length through the lamellar effective index, converging or diverging; two shapes through
  `PiecesOf`, the band on the ridges' tops and the back) and a meta prism
  (`MetaPrismPart`: a linear delay, `Deflection` degrees counter-clockwise) and a meta axicon
  (`MetaAxiconPart`: a delay in |y'|, `ConeAngle`, a line of focus of half the width over tan β); all three derive from
  the abstract `MetaSurfacePart`, which holds the plate, the pattern, the design frequency and the fill from a
  subclass's `Delay`; the shared strings are `MetaSurfacePart.*`), a Fresnel lens (`FresnelLensPart : LensPart`: the lens, unsealed, with
  its curved surfaces cut into `Zones` of equal width or equal depth (`ZoneDivision`, a lighthouse lens's elements), each
  arc restarted at the vertex plane; the lens reckons with `ZoneCount` and `ZoneDivision` through the deepest zone's sag
  for the fit, the edge, the handles; a 20 px plate 500 across with radii of ±1500 when new; no Concave box), a fibre on a Bézier path, a Bragg reflector, a GRIN lens, the XOR, AND and OR gates under
  `LogicGatePart` and a zone plate (both Jeroen's, 2026-10-21/22). Painted into the scene pixels, red the index (red 0 a
  mirror), blue for absorbers, green never; a part edit is a simulation edit. The menus of kinds share `PartMenuEntries`
  (`ProjectTree.xaml`), added by `ProjectTree.MenuEntries`, not as an `ItemsSource`. **Each kind has a description**
  (2026-10-29, the user's choice): the `Kind.<Type>` string in the four tables, the entry's tooltip in the menus and the
  text at the top of the part's form (`PartCommonView`, through `KindDescriptionConverter` with the language as an input),
  under a header whose chevron turns while it is open; open or closed is one user setting, `PartDescriptionExpanded`. **Coatings** (2026-10-29, the user's
  request, so Jeroen's drawn scenes become parts): a lens, a shape, the GRIN lens and the fibre are `CoatedPart`s, the AR
  band a `Layer` at √n or `Graded`, a raised cosine from the pixel's index to the body (measured against linear, smooth-step
  and quintic in `OpticalParts.md`), on the faces `FrontCoated`, `BackCoated`, `SidesCoated` choose, classed by the outward
  normal in the part frame (within 45° of −x' front, of +x' back, else a side; a fibre's start face, end face and tube); a
  cavity's inner faces can stay bare. A fibre's cladding can absorb (`Absorption`, `AbsorptionRamp`: a hump in its outer
  part, a mode stripper, Jeroen's strips along his fibres).
- **The project file carries its DTD** (2026-10-31, the user's request, so a text editor completes element and attribute
  names and enumeration values): `ProjectSchema` builds the DOCTYPE's internal subset once per process by reflection from
  the serializer attributes (one `ELEMENT` per name with its children in the written order, an `ATTLIST` line per attribute,
  bools and enums enumerated, the rest `CDATA`, no DTD defaults, `#REQUIRED` only where the serializer always writes), with a
  comment giving the setting's description, default and range before each declaration when the `ProjectSchema.Comments`
  constant is true, false since 2026-10-31, the user's choice after seeing them (56 KB a file with them, 15 KB without); `Save`
  writes it, `Load` skips it (`DtdProcessing.Ignore`; the reader's default refuses
  any DOCTYPE, so builds before this refuse new files); history snapshots and clipboard fragments carry none; `library_check`
  validates a one-of-every-kind project and every example as saved against it with .NET's DTD validator and proves a
  misspelt attribute, a wrong value and a misplaced element are reported. The examples and the harnesses' test projects are
  not re-saved with it yet.
- **Memory warning** (2026-11-03, the user's design): Simulation > Warn when a run does not fit in RAM
  (`Settings.WarnWhenRamInsufficient`, default on). A preview run whose frames exceed the budget asks first, F5, Play and
  the automatic re-run alike (the user's choice, so no spill is silent; an export's run is full quality by rule and never
  asks): `MemoryWarningWindow`, in the message window's look, names the need and the budget, offers four checkboxes: the
  RAM cache size to Auto (the user's request the same day; greyed when Auto gains less than a frame) and three that each
  lower the preview quality a notch (half the resolution, half the frames, 8-bit samples; a lever at the View menu's
  lowest is greyed), pre-ticked with the fewest ticks that fit and the mildest among as many (the largest need that
  fits, so the cache comes before any loss of quality), a live line with the ticks' result, and "Do not warn me again",
  which clears the setting. Run puts the choice into the Simulation and View menu settings
  (persisted, so the scratch frames match the run and later runs fit without asking; the settings' change schedules a
  scratch frame and a re-run as the View menu's does, and the run started right after leaves nothing stale for it);
  Cancel keeps the stale run and says so at Info. Asked every time, no memory of an answer. Play asks before the pane's
  clock starts and a cancelled warning turns playback off again (2026-11-03, the user's report: the modal window's message
  loop had ticked the playhead through the stale frames). The arithmetic is
  `RunSpec.StoreBytesAt`, proven by `library_check`.
- File types `.wdproj` / `.wdscheme` are registered per user at start-up; unsaved edits ask on close.
- **Examples** (2026-10-29, the user's request): File > Examples lists the project files of the repository's `Examples`
  folder, embedded in `WaveDudeNext.UI` (`EmbeddedResource` there, `Examples.cs`), in file order with the two-digit
  number leading each name; one opens as an unsaved project named after its file in the title (`SuggestedFileName`, which
  Save As proposes; an import is named the same way). The set is 33 files: 24
  curated from Jeroen's examples (the Classic ones through the importer, open edges with a rim, his drawn scenes rebuilt
  from parts: the ball lens, the spherical mirror, the test glass, the fibres), and nine he made for the kinds and the
  detectors of his sessions of 2026-11-07/08 (25 to 33, integrated 2026-11-10 on the user's word: the Fresnel lens, the
  diffusers, the interference filter, the blazed and the lattice grating, the meta prism, then the three with detectors,
  the meta lens, the imaging detector, the fibre split; none for the meta axicon). His files as saved, format version 4,
  less the document type definition, which no example carries, with the descriptions corrected (spelling, British
  "fibre", "diffuser") and the names made what `INamed` promises and the form does not enforce, unique within their
  list: his pairs of "Absorber", "Absorbing boundary" and "Source A" are numbered or lettered, a "Source 1 copy" is
  "Source 2", padded and lower-case names are trimmed and capitalised. A detector is seeded by its name, so the renamed
  "Multi Pixel Photon Counter" draws other detections of the same statistics. Each is byte for byte what the program
  saves from it, and runs (a one-off probe; `library_check` validates and paints every example). Example 30 needs
  19.7 GB at full quality, as Example 7 does. A tenth file of his was a bug report (see the smoothed square under
  Architecture). The UI harnesses count the File menu's entries, so a new entry moves their Down presses; the Examples
  submenu is walked from its first entry only. **An entry's tooltip is the project's description** (2026-11-10, the
  user's request; the WPF entries had none, the port's showed the file name): `ExampleEntry.Description`, the root's own
  `Description` element read from the embedded file with an `XmlReader` when the list is built (not the serializer, and not
  a nested scheme's element of that name), null for none, which both heads bind as the tip; every example has one. With
  it **the WPF theme's tooltips wrap at 400 px** (`MaxWidth` on the style, an implicit wrapping `TextBlock` style inside
  the template's presenter, the width the Avalonia head had wrapped at since the port): the descriptions run to 430
  characters, and the part kinds' tips had run past the screen on one line as well. Proven by a probe that opens the
  submenu by keys and reads the first entry's HelpText (both heads report a string tip there) and captures the tip WPF
  shows on keyboard focus, 391 × 53 px.
- **Manual** (2026-10-30, the user's request and choices): Help > Manual (F1, below About) opens MANUAL.md in a window
  owned by the main window (the user's choice, after a day unowned: not modal, but attached, so it is minimised and
  restored with the main window and stays above it; Windows does not maximise an owned window with its owner); F1 again
  brings it forward, Esc closes it, it closes with the main window. A contents tree of the headings by level beside the text, every branch open; the title (the one `#`) is no
  row, its chapters are the roots; the window opens with the text at its top and the first chapter's row taken, and a
  row taken goes to its heading. **MANUAL.md is written for its reader, never for us** (2026-10-30, the user's rule,
  said three times): what a control does for the user and how to use it, no internal names, no history, no engine
  talk; the chapters mirror the app (the main window and its panes, the menus, the project settings, sources, optical
  parts, probes, colour schemes), each figure's numbered list is its badges in the form `N. **Label**: text`, and
  settings that several forms share (a part's first six, the glass and mirror material) are described once, the other
  forms' lists starting at their own first number (`StartIndex`). A figure section regenerated by the harness is
  spliced in, keeping the wording. The build converts the Markdown like the license (the license's XAML stayed byte for
  byte the same) and compiles it to BAML (the Page `Help/Manual.xaml`): headings tagged `h{level}#{anchor}` with GitHub's
  anchors, so `[x](#anchor)` links work in the app; tight lists tight, as on GitHub. The figures' SVGs are drawn at build
  time: the embedded PNG becomes a WPF resource (`Manual/<name>.png`), each badge a round Border with its number and its
  `<title>` as the tooltip; an SVG element or attribute the converter does not draw stops the build. A screenshot that
  fits the column is drawn 1:1 at 100 % (whole-pixel line heights, whole-pixel scrolling; proven by a pixel search, only
  the badges' discs differing); a wider one is scaled down. Opening takes about 2 s the first time, 1 s after (the
  layout of the whole document; BAML and the light badges took 0.3 s off the first).

## Numbers (workstation: 10-core Xeon without AVX2, RTX 2080, 64 GB)

GPU step at 1080p 0.21 ms (9-point) to 0.38 ms (4th order, three sub-steps; 0.25–0.47 before the compact maps and the
deferred intensity blend of 2026-11-03), CPU 5.3 ms (9-point) to 8.7 (4th order) in lanes since 2026-11-04 (6.2 and 13.2 cell by cell); OpenCL build 0.3 s; a full-HD frame's capture (both planes as halves, 8.3 MB)
0.9 ms on top of its two steps since the page-locked snapshots (2026-11-03; 2.1 ms before, the stepping thread blocked in
the driver's copy; `bench 1920 1080`: 1.50 → 0.65 ms a step with captures every 2 steps at 4th order, the DMA on its own
queue), the DMA itself 0.63 ms at 13 GB/s. In the app the default project's run (2500 steps, 1250 frames, 16-bit, both
planes, 9.7 GiB) takes 2.7 s (2026-11-03; 3.2 before the transfer queue, about 4 before the page-locked snapshots): the card
needs about 1.3 ms a frame, the writer 1.6 ms for its copy of a frame into the store on one core (6.8 GB/s) and is the
clock, the stepping thread parked 1.8 ms a frame for a free snapshot. Measured on the way and settled: the capture's reads
are flushed at once (NVIDIA holds a small batch back until a blocking call; the writer's wait had been starting the DMA,
0.8 ms a frame); the store's producer touches a fresh block's pages on four cores (Windows zeroes pages at 5 GB/s for one
thread, 1.7 ms a full-HD block, which had been the writer's clock before the copy was; 0.6 on four, eight gain nothing);
every faster copy lost in a run though 0.35 ms on four cores in isolation (torn out, three variants measured in the app):
chunks on the thread pool queued behind the presenter's rendering rows; two or four dedicated threads, at normal or
above-normal priority, copied faster but the writer's wait for the card rose by more, the memory bus being shared with
the DMA, the producer's page zeroing and the presenter — the frame's way into memory is bandwidth-bound on this DDR3
Xeon, and the plain copy at 6.8 GB/s leaves the DMA its share. Renderer per 2880×1080 layer on ten cores (2026-10-23, a
table lookup per pixel): 16-bit 0.5 ms,
8-bit 0.6; a half table built in 0.4 ms; the screen path from 1920×1080 to 914×514 1.4 ms, dithered 1.5. Presenter for
Example 7 (2880×1080 in a 458×172 picture, both layers, playing): 16-bit 7.3–8.5 ms a frame, dithered 7.7–9.1, 8-bit
10–11 (its bitmaps are canvas-sized). Presenter about 7 ms a frame quiet, 20 during a run. **The handle overlay draws in
three child layers, the sources' one a bitmap cache** (`SceneOverlay.DrawingLayer`, 2026-11-04, the user's report:
Example 7, a lattice of thousands of emitter dots, stuttered in playback with every frame in RAM): the pictures change
every frame, so WPF's render thread rasterised the overlay's retained drawing again every frame, every dot with its halo,
and the presenter's lock of the bitmap at Render priority waited behind it, 51 ms of a 59 ms frame. A cache on the whole
overlay gave 10–14 ms a frame but rendered the labels greyscale, since a cache has no opaque background for ClearType,
and moved every antialiased edge by a few levels; the user's rule: ClearType in the app. So the drawing is split in its
order, the line probes, then the sources (axes and dots, cached), then the parts, the marquee and the labels: the figures
with and without the cache differ on the dot rows alone, at most 53 levels at an antialiased edge, and the frame is
14–21 ms with 6–8 at the bitmap. The dots' halo pen is frozen, since an unfrozen pen is cloned by every draw call. The
probe graph overlay stays uncached: its drawing changes with the frame by design. **The sources' cache is renewed at
every source edit** (`RefreshSourcesCache`, 2026-11-08, Jeroen's report from his machine: after a mouse drag of Example 3's
composite source its dots stayed put until another edit; the log showed the model moved and the scratch frame followed,
and twenty keyboard moves could not repeat it, so the one thing the dots have that nothing else has, the bitmap cache,
gets a new object per edit). **A precaution, its cause unknown** (2026-11-09, at the integration): on the workstation
eighteen mouse drags of that source through WPF's own input pipeline (the in-app drag probe, see Harnesses), paced and
in bursts, on both pictures, rendered by the card and in software, left no dot behind on a build without the renewal:
the window as composed after each drag against the same after a cache replaced by hand, no pixel differing. His focus
mechanism for a pick from a menu of kinds (`MainWindow.Added` waiting for the menu's closing, 2026-11-08) was not
carried over: on the models the reveal request puts the caret into the new item's name after a pick from either menu
(a keyboard probe, the window inactive as in his). The measurement is the presenter's log
line during playback after a completed run, with Loop on, F5 and Space posted; a stale Debug build had once "proven" the
whole-overlay cache harmless, so the figure script's build is checked against the source now. **A child element drawn
from its parent's ArrangeOverride must draw by its own size** (2026-11-04, the user's report: a resize left the dots, the
lines and the labels at the previous size while the handles moved): WPF renders an element at the end of its own Arrange
call, so a layer arranged from inside the overlay's ArrangeOverride draws before the overlay's own RenderSize is updated;
the band methods take the layer's size, which its Arrange set a moment before. Proven by a probe that captures the
picture, resizes the window by SetWindowPos, captures again and resizes back: the drawing follows the picture, and the
first and third captures agree. JPEG 1080p at 4:4:4: WIC 26 ms a frame
single-threaded (9 ms a frame in a sequence job on half the cores), in-house 9.8 ms on ten cores (15 in a job);
MP4 job 16 ms a frame. Part painter on a Full HD canvas (ten cores, the bench's figures less its 3 ms canvas, 2026-11-04):
a 400 × 330 outline 0.8 ms (30 on 2026-10-19, 3.6 before the runs), a 400 px lens 0.6, a hexagon 0.7 (was 2.3), an S-bent
fibre across the canvas 9.6 (51 on 2026-10-22, 19 before the runs and the one chord pass; 156 chords on the cell grid, 1.3 s
without it); the glowing edges 8.8 ms on noise and 4.5 on Example 16's scene (12.7 and 6.2 cell by cell; 2.1 of each the
result's allocation); the preview's scene pictures about 100 ms more, so the scene rebuild is coalesced at background priority.
**The composition runs on a worker since 2026-11-03** (the user's choice, after the log of a drag in Example 16: every
coalesced update repainted all four rectangles, one the whole canvas, "composed in 115–270 ms" on the UI thread, then
20–30 ms of pictures, and the handle jumped): `SceneComposer` (`Update`, in `WaveDudeNext.UI` since 2026-11-06; `MainWindow.ComposeScene` then) clones the parts (`OpticalPart.Clone` copies the
point and node lists) and paints on the thread pool, one composition in flight, the newest request after it, the UI
thread taking only the finished pixels (20–24 ms a time, measured on a burst of ten nudges: three compositions of 395,
212 and 128 ms on the worker); `SceneComposer.Load` (a run or scratch request) joins an in-flight composition of the same
inputs rather than painting again. **The painter's per-pixel path is out of JIT tiering** (2026-11-03, the user's choice,
first by `AggressiveOptimization` on the shape methods a pixel reaches, the painter's row lambdas and the glowing edges',
since that evening by quick JIT off for all of this code, see Measure properly, the attributes gone): alone
it took 31 ms for those four parts, in the app 115–460 ms, because the runtime left its methods at
tier-0 code in the busy process (pinned there, the probe took 212 ms); optimised at first call,
a composition is 26–35 ms a background and 10–17 ms a box in the app,
74 ms for the four during a burst of nudges (was 395–460), the pictures 17–32 ms on the UI thread. Each part logs its
paint time, and a composition logs the thread pool's state when it starts. **A pixel is sampled 16 times only where an
edge is within reach** — the outline, the coating band's, or an inner edge the shape reports (`InnerEdgeDistance`: a
fibre's core boundary, a Bragg stack's layer boundaries; the logic gate still says 0, every inside pixel sampled) — where
every pixel of a fibre had been (2026-11-03, after the user's drag of a fibre node: Example 23's three fibres 138/163/398 →
43/50/96 ms, its composition 731 → 218 ms; a graded band's pixels are the centre sample now, as every other coated shape's,
within 0.01 of index where the band curves inside a pixel). **The painter settles runs** (2026-11-04, the user's
request, "all of them to the best of abilities"; the rules and the figures are in `OpticalParts.md` under Rasterisation):
where a shape's depths are distances (`PartShape.DepthIsDistance`), one pixel centre settles the next
floor(e − 0.6 − allowance) pixels of its row, e the nearest edge's distance, as outside or as body (`BodyIndexAt`), so a
box costs a handful of depth evaluations a row; the lens and the gate are not distances and stay pixel by pixel; a fibre
sample runs its chords once for depth, index and face (`Evaluate`), its wedge jump at a bent flat end covered by a computed
`SkipAllowance`. Proven byte for byte against the pixel-by-pixel painting over every example and every kind at four
angles (`library_check`, "painter runs"); the proof of any later painter change is that check. **Lanes** (`Vector<T>`, 4
floats on this Xeon, 8 with AVX2): the glowing edges (the Sobel sums as 16-bit integers, the root in double, the bytes the
scalar's, proven on noise and a painted scene) and the CPU engine's stencil (each lane the scalar arithmetic in its
order, the field and intensity bit for bit the committed engine's after 100 steps of a painted scene with two sources:
6.2 → 5.3 ms a step at 9-point, 13.2 → 8.7 at 4th order on 1920 × 1080 with a 32 px rim, the rest the memory bus). The
screen path keeps its loops: a two-to-three-term dot product a pixel and a table gather, which lanes do not help. Not an
incremental painter, since a coating's band rises from the index beneath it. Laptop (2 cores, HD 520, HDD): iGPU 4.3 ms a step against CPU 14.7; the ball
lens run 105 s full quality (disk-bound), 61 s at half resolution with every second frame, in RAM. Settled by these
numbers: no GPU JPEG, no OpenCL colouring at presentation, no explicit `Vector256` paths (`Vector<T>` takes the width the
machine has), no Direct3D display path.

## Harnesses

`library_check` (100+ checks: engines, store, renderer, half tables, screen path, schemes, probes, scratch, reduced preview, importer, export, parts, group move, the smoothed square's envelope, open edges, work groups on a prime grid, ranges, XOR gate, zone plate, gate port follower, history, clipboard, the project file's DTD, the first start's tiers, the engine's page-locked snapshots, scene replacement, the memory warning's store bytes, the painter's runs and the glowing edges' lanes byte for byte against pixel by pixel, the diffuser, the interference filter, the simple and the facetted grating, the Fresnel lens, the meta surfaces' fills and the detectors' statistics).
UI, all on `Common.ps1`, with their check counts: `PreviewCheck` 43, `RunCheck` 71, `LanguageCheck` 11,
`ProbeGraphCheck` 39, `ImportCheck` 10, `ExportCheck` 44, `PreviewQualityCheck` 35, `SceneImageCheck` 8,
`ColorPickerCheck` 13, `KeyboardCheck` 13, `AboutCheck` 18, `SelectionCheck` 10. Test projects `tiny*.wdproj` sit beside them. The
Wave Dude 2D projects the import checks read (`library_check`, `ImportCheck`, `SceneImageCheck`) sit in
`_tools/legacy_examples` (2026-10-31, the user's wish: the root folder `Wave_Dude_Examples` with Jeroen's original
examples is gone from the repository, in history up to 02d0e2d; `library_check` finds the repository root by the
fixtures folder). A harness
sets the persisted settings it expects (split orientation, preview quality). **A one-off probe may move the real pointer
when the user says so** (2026-11-04, his instruction for a mouse-related report: `SetCursorPos` and `SendInput` from a
probe under `%TEMP%`, no clicks, the pointer put back; posted WM_MOUSEMOVE reproduced nothing, and only `SendInput`
raises the input Windows queues like a device's). **A mouse flow in the real app without the real pointer: the in-app
drag probe** (2026-11-09, for Jeroen's emitter-dot report): a .NET startup hook (`DOTNET_STARTUP_HOOKS`, a class
`StartupHook` with `Initialize()` in a library built under `%TEMP%` against the Debug build's assemblies) starts a
thread inside the app, which opens an example through `MainWindow.Document`, finds a handle in the visual tree and has
the UI thread send its own window the mouse messages (`SetCapture` first, the button as the thread's key state, the
window `Topmost` for the moment, as the in-process probe of 2026-11-06 found), then captures the window with
`PrintWindow` from that thread: the app's whole path from the press to the composed picture, the process path the
Debug exe's, so the file types stay registered to it. The first run's press was not taken (the window had just
started); a diagnostic line per condition (capture, `WindowFromPoint`, `Mouse.DirectlyOver`, the button state) tells
which is missing. **The figure runs can leave a runaway thumbnail cache** (2026-11-04:
a `dllhost.exe` hosting `thumbcache.dll` had used 81,000 s of CPU, 1.3 cores for seventeen hours, after the night's
rewrites of the 38 files under Assets; a sampling of every process's CPU while the user moved the mouse found it; the
user ended it, Windows starts it again when needed). **Run only the harnesses listed above**: `ProjectTreeCheck`,
`MessagesCheck`, `StatusBarCheck`, `StatusBarDoubleClickCheck` and `ComboListShot` beside them are not on `Common.ps1`
and predate the keyboard-only rule; `ProjectTreeCheck` takes the foreground, drags with the real pointer and sends
Ctrl+D and Delete through `SendKeys` (run once by mistake on 2026-11-06 while the user worked; its keys landed in the
app). **A probe walks a menu by the focused entry's name, never by counting Down presses**: a disabled entry is
skipped, so the count shifts with the app's state (2026-11-06: after an undo to the first step Undo was disabled, a
counted walk meant for Redo pressed Cut, which removed the source and replaced the user's clipboard). And WPF reads
the real modifier keys: a nudge posted while the user holds Shift is ten pixels, not one. A tree row's AutomationId
keeps the name the row was made with: after a rename find the row by its Name. The tree is one tab stop, at its
selected row, behind its toolbar; another row is reached with the arrows from there, never by more Tabs (Tab from a
handle on the preview passes the other handles, and each selects its own item, so `FocusOn "Tree:<name>"` only works
while that item is the selection). Space on + Part opens the menu of kinds with no entry taken: Down first. `Type` is PowerShell's alias of
`Get-Content` and wins over a function of that name. **`ProbeGraphCheck` had failed one check of 39 since the live rules lost their timers** (found 2026-11-10): it ticks
Probe 2's Active box with Space and waits for the automatic re-run with the focus still on the box, and a74c04a, which
changed `RunCheck` and `PreviewQualityCheck` for the rule that the re-run comes when the edit has finished, had left
this script: the re-run came 21 s later, when the script moved the focus on. The user's choice of the two ways out:
the rule changed, a box ticked with a key is an edit that has finished (see Undo and redo). With it the re-run starts
at the tick and the status bar's "edited since" lasts milliseconds, so the check before it reads the log for "the run
is stale" as the others read it for "Re-run started"; 39 of 39, and `PreviewCheck`, which ticks the intensity toggle
with Space, 43 of 43. Known flake: a second app start in one
script sometimes comes up active; only the foreground guard fails then. UI Automation files an owned dialog (the message
window, the memory warning) under its owner, not among the root's children: find its content below the main window and
walk up to the Window element for its handle (2026-11-03, half an hour lost to a root search).
**Manual figures** (2026-10-27): `Screenshots.ps1` on `Common.ps1` writes `Assets/*.svg` (the PNG inlined, numbered
badges at UI Automation rectangles), `Assets/Legend.md` (what each number marks) and `Assets/main-window.png` (the
window without badges, the README's picture, 2026-10-30): the window, the menus, the tree,
the preview and the timeline from `Examples/Example16_Wave_in_2_boxes.wdproj` (a relative path, 2026-11-02) at a scratch frame, the
properties form of every object kind from `figures.wdproj` beside the harnesses (one of each kind, made with the
library's adders), plus two crops of the rectangle's form for the manual's sections on the settings several forms share
(`Properties-Figure` takes a row range, a section heading and the first badge's number, so the crops keep the whole
form's numbers; 2026-10-30, the user's request); dark theme, English, **greyscale text** (the app's `--greyscale-text` switch, the script's `-TextSwitch`; 2026-11-04,
the user's rule: a figure never lands on the reader's subpixel grid, so its text carries no ClearType fringes, while the
app itself keeps ClearType; `WindowZoom.TextRendering`, set before any window and read by the standalone popup styles
too), **UI zoom 1.25** (2026-11-02, the user's choice,
so the figures are sharp on denser screens; the window 1642 × 1408 px on the monitor at 100 %), the user's 1314 × 1126
arrangement, his settings put back afterwards. A pointer resting on the window is parked just outside it for a capture and put back at
the end (2026-10-30: it lay on a probe's graph box, whose hover border changed the main window's picture between two
runs); the harnesses' one mouse action, clicking and activating nothing. **Only a pointer that lies on a window of the
app itself is moved** (`Shot.ProcessAt`, 2026-11-09, the user's report, in anger: the test had been the app window's
rectangle alone, so the pointer was taken from under his hand at every capture while he worked in a window covering
the app's). **A figure shows the window inactive**: an active window's frame is drawn in the accent colour, one pixel
column of every form's crop (2026-11-09: a second session came up active, the known flake, and 18 forms carried an
orange line), so each session's start is made again while the window holds the foreground (`Start-Inactive`, three
times at most) and a figure taken with the window active is a PROBLEM line. **A figure run needs the machine**: about
six minutes, no other WaveDude start (it takes the harness window's keyboard focus: "tree row not reached; focus is at
(none)") and no click on its window; with the user at the machine it is announced and waits for his word, and a
failed run is reported, not started again (2026-11-09: three runs in a row collided with his work). A figure must come out byte for byte the same
from an unchanged app. `-Zoom <factor>` (2026-11-01; 1.25 is the default and the figures' zoom, 1 for figures at the
monitor's own pixels) puts the UI zoom
setting into the figures' settings, scales the script's pixel constants, takes 16-bit samples (the 8-bit picture is a
bitmap WPF scales) and writes the SVG with its size in CSS pixels over a view box in the figure's pixels, which the
manual's converter (`SvgReader`, a uniform view-box scale) and browsers draw at the 100 % size; the PNGs declare their
density. The window must fit the monitor (nothing off it is composed): at 2 the 1314 × 1126 window would be 2252 px
tall, more than the 4K monitor; 1.25 and 1.5 fit, the properties session's 1400 too.

## Port (macOS and Linux through Avalonia)

The user's plan (2026-11-06): the WPF app stays canonical. The preparation is **`WaveDudeNext.UI`**, a plain `net10.0`
class library for the UI-related code that no framework anchors (no WPF, no Avalonia reference; code bound to one
operating system carries `SupportedOSPlatform`, as in the library), which both heads use; it holds `Examples` with the
embedded example projects, `Rendering.ColorSchemeCatalog`, `Theming.AppTheme`, `FileAssociation`, `Settings` and the
saved dock layout (`Docking.DockLayout` and its node classes) so far. Things move
there without breaking the WPF app, each move proven by the build and the one harness that covers it (the first by
`ExportCheck`, which walks the Examples menu). **`Settings` is framework-free by two choices of the user** (2026-11-06):
the window state and the directions are enums of the library, `WindowMode` and `LayoutDirection`, whose members carry
the names WPF's and Avalonia's enums have and the file has always held, bound in WPF through `EnumByNameConverter`
(the window's state, the preview's orientation) and converted in `DockHost` where it saves and restores a split; and
the layout classes carry no content property, an attribute of one framework or the other, so the default layout in
`MainWindow.xaml` names `SplitLayout.Children` and `GroupLayout.Items` as property elements. The settings file is
unchanged: the old and the new class rewrote the user's file and every value of the two enums to the same text, each
reading the other's (a probe against a kept copy of the old build); a start without a saved layout came up in the
declared arrangement, 1 : 3 across and 1 : 2 down; `PreviewCheck` flips the orientation through the converted binding.
`Settings` computes its own path and never through another class's statics: its first use loads the file and logs, which
must come after the logging is set up. The first step after the preparation is an Avalonia app **on Windows** with feature
parity, tested against the WPF app, before anything goes to macOS or Linux. He has a Mac Mini M1 and can boot a bare-metal
Linux. Not pursued (his decision the same day): running `library_check` on the Mac first, and a capture path of its own for
GPUs sharing the host's memory, estimated at 9 % of a run at best, the step kernel being bound by the memory bus there.
Whether both heads share one settings file or keep one each is open until the Avalonia app exists.
**How the window's logic moves** (2026-11-06, the user's rules): in small steps, each one a cleanup the WPF app would
want anyway, never at the cost of its WPF quality. The application's state and rules go into model classes of
`WaveDudeNext.UI` that the XAML binds (plain notifying properties, no dependency properties for application state);
a model tells the app what happened through events and returns an operation's error for the app to show; what only a
framework can do (dialogs, the UI thread's timers and priorities, image decoding, the clipboard) will sit behind
interfaces each app implements. **Bindings stay in XAML, command bindings included**: the routed commands, their
`CommandBinding`s and the handlers stay in the WPF app, the handlers shrinking to calls into the model; a shared
command catalogue with the routing set up in code was proposed and refused as an anti-pattern. The converters stay
per app (each framework has its own interface type; about 320 lines of real code in 34 of them). The first step:
**`ProjectDocument`** holds the open project, its file name, the dirty flag and the suggested name, and does New,
Open (by kind: project, Wave Dude 2D import, colour scheme), the examples, Save and the recent list; the window keeps
`Document` as a property, binds `Document.Project` and the title's three inputs there, shows the dialogs, and reacts
to `ProjectReplacing` (the run goes first), `ProjectReplaced` (raised before the property's change notification, as
the dependency property's callback came before its bindings), `Opened` (tree selection, export paths, scene image
sync) and `Saved` (the history's saved step, the image watch). The jump list follows `Settings.RecentFiles`;
`Settings.TrySave` replaced the window's own. Proven by a keyboard probe (open, edit, save, close, example, new: title,
file, log and recent list), `ImportCheck` and `SceneImageCheck`. The second step: **`Selection`** holds the current
item and the selected list with their rules (a current item outside the list is a plain selection, null empties it;
`Target`, `Group`, `Set`, `SelectAll`, `Toggle`); the tree, the preview and the properties pane bind
`Selection.Current` and `Selection.Items` in the XAML. For that `ProjectTree.SelectedItem` became an ordinary
dependency property that binds two-way and mirrors a value set from outside into its `TreeView` without taking the
focus, which the window had done by hand through an event and `Select(item, focus: false)`; the tree writes it with
`SetCurrentValue`. Where a command wants more than the selection, the row in front of the user with the keyboard
focus on it (add, remove, move, open, an undo that leaves nothing selected), it calls `Selection.Reveal(item)`,
`Reveal(item, edit: true)` for a new item whose name is typed next, or `RevealFolder(list)`; each makes a
**`RevealRequest`**, an object equal to no other, which the tree and the properties pane bind in the XAML
(`RevealRequest="{Binding Selection.RevealRequest}"`) and act on in their property's change: the tree selects and
focuses the row, the properties pane puts the caret into the first editor. So the window names neither pane in its
code any more (2026-11-06, the user's point: a step is finished when what was said of it is done; the six tree calls
had first been left for a later step). `Reveal` sets the current item at once; a folder's node is the tree's own, so
there the current item follows from the tree. A file that uses the model's static helpers inside the window needs
`using WaveDudeNext.UI`, or the property `Selection` hides the type. Proven by `SelectionCheck`, `KeyboardCheck` and
two probes (a handle or graph box taking the focus selects its row and keeps the focus; open, add, removing the last
source and an undo reveal the root, the name editor, the Sources folder and the root). The window reached into the
preview pane (playing, scene pixels, colours, the shown frame, the canvas size) until the fifth and the ninth step. The third step: **`UndoHistory`** keeps the `ProjectHistory` and the rules around it (the step in progress,
which `EndStep` ends; Undo and Redo end it first; a step written back is no edit; the selection is carried over by
name; the document's dirty flag follows the saved step) and announces `CanUndo` and `CanRedo`; the window creates it,
tells it `Start(project)`, `EditMade()` and `MarkSaved()` from where it did those things itself, and its four command
handlers are one line each. It needs nothing from a framework: the first version carried the old quiet period over
behind two interfaces (a UI-thread timer, "is a mouse button held"), which went again the same day with the rule
(see Undo and redo); when an edit has finished is the app's to say, WPF's way being `Platform/EditBoundaries`, and an
Avalonia app will need its own. (No interface each app implements existed then; the UI thread's timers came with the
tenth step, as `IUiTimers`, for the pacing of the scratch frames and the re-run, and went again the same day with the
timers.) `ProjectItems.ListOf` maps an item to
its list for the history and the window's item commands alike. Proven by three one-off probes: the watcher under a
real press, drag and release sent to a probe's own window (11 checks: a press says "finished" before anything is
edited, nothing is said while the button is held, the release says it after a thumb completed its drag and after a
control that handles the preview release ended its scrub); the keyboard flows in the app (17 checks: three nudges and
2.5 s of waiting record nothing, the focus leaving ends the step, arrow keys in a number box wait for Enter, a typed
number and a typed name are one step when the box is left, a command is a step at once, an undo with nudges in
progress takes them back); and the first version's rules without a window (22 checks on plain `net10.0`). The fourth step: **`ProjectItems`** carries out the item commands (add a source, a part of a kind, a
probe, a colour scheme, or to a folder's list; duplicate, remove, move, enable), each on its parameter or the
selection and each leaving the user where they go on (`Selection.Reveal`); the window's handlers are one line each
and keep what needs the window: where on the shown canvas a new item lands (`CanvasCentre`, still read from the
preview pane), the menu of part kinds, a text box's own Delete, and that a folder's node is a `NamedCollection`.
**A folder's plus now puts a source or a probe at the centre of the shown canvas too** (a fix on the way: it had
called the model's parameterless adders, which take the scene's own size, which an image scene does not have; the
toolbar's buttons had passed the canvas's centre since 2026-10-26). Proven without a window (20 checks on plain
`net10.0`), by `KeyboardCheck` and `SelectionCheck`, and by a probe in the app for what those do not press (a probe, a
part through the menu of kinds, Space on a row, Duplicate from the row's context menu: each one undo step at once).
The fifth step: the scene image. **`SceneImageFile`** embeds a picked file, drops the image, writes the embedded
bytes out (the dialogs stay the window's: it asks the model what a save proposes) and keeps the embedded image in step
with its file, the watch rebuilt without timers (see The scene image); `IsFileMissing` is bound by the scene form,
`Reembedded` tells the window to end the undo step. **`SceneComposer`** holds the decode and composition caches,
`Pixels` for the preview (bound in the XAML, where the window had set the pane's property) and `Load` for runs, scratch
frames, exports and the fit. With them came **the first interface each app implements, `IImageDecoder`** (WPF's is
`Platform/WpfImageDecoder`, the former `SceneImageLoader`; a file that is no image raises `NotSupportedException`, a
damaged one a `FormatException`, which WPF's `FileFormatException` is). The UI thread reaches the models as plain
`SynchronizationContext`s the window makes, as the library's runs take theirs: one at normal priority for the file
watcher's events, one at Background priority for the composer's coalesced update; no interface was needed for that.
**An error names its text by key**: `RunSpecException` (library) carries an optional `Key`, the two the composer raises
have an English message for the log and the key of the string tables' text, which `ShowError` shows when there is
one, so the model needs no strings and the log is no longer localised there. `ProjectDocument.Name` is the name a
file made from the project takes. Proven without a window (26 checks on plain `net10.0` with a real
`FileSystemWatcher` and a writer holding the file: nothing is embedded while it is open, the embed follows the close;
a check queued by a watch that ended is dropped, which the probe found missing at first), by `library_check`,
`SceneImageCheck` (9) and `RunCheck` (71).
The sixth step: **`ObjectClipboard`** carries out Cut, Copy and Paste of project objects (what is taken, the
clip parsed once per change of the clipboard, Paste selecting what it put in, Cut removing through `ProjectItems`
only what was copied) on the second interface each app implements, **`IClipboard`**: a change count, text put under
the application's format and as plain text, and one read that gives the text under that format or else the plain
text, telling "holds nothing" from "cannot be read now", which is not kept (WPF's is `Platform/WpfClipboard`, with
Windows' sequence number). The window's three handler pairs are one line each; a focused text box still takes the
keys first, by WPF's routing. Drags keep their own `ObjectsData` in the panes. Proven with a stand-in clipboard and no
window (19 checks) and on the real clipboard through the Edit menu (15 checks: copy, paste, cut, paste, the same XML
as plain text alone, plain words not offered). **A probe that uses the real clipboard saves what it holds first and
puts it back** (every readable format copied into a `System.Windows.Forms.DataObject`, set again with copy at the end;
`powershell -STA`): the user's seven formats from a browser came back.
The seventh step: **`ExportPaths`** is where exports go: what each kind's dialog starts with (`StartOfRun`,
`StartOfStill`, `StartOfProbe`), the paths kept per project file in the settings (`Restore`, and `RunExported`,
`StillExported`, `ProbeExported` once something was chosen), the format chosen last, the folder a dialog chose, and
`LastRun` for Export last. The export flow itself (dialogs, the run, the job) stays in the window and asks it. Proven
without a window (21 checks) and by `ExportCheck` (44, Export last among them).
**A probe of a model that saves the settings writes the user's real settings file**, which lives at one fixed path
(2026-11-06: a console probe saved temp projects through `ProjectDocument`, whose recent list is capped at ten, and
pushed four of the user's recent projects out; they were put back from an earlier listing). The user's word the same
day: a probe touching his settings file is fine, that way he sees what it opened; so it needs no copy and restore.
Around a harness run the recent files and export hints are kept as blocks and put back, since the harnesses' temp
projects go into both lists too.
The eighth step: **`BusyState`** is what the application is busy with, for the status bar's indicator, the taskbar
button and the preview's dimming: three inputs (`Export`, the job it follows through the job's own notifications to
its end; `IsWaitingForRun`; `ComputingFrame`, -1 for none), the `Kind` by precedence (an export, then its wait, then
the frame, which shows again when the export is over), `IsBusy`, `IsIndeterminate`, `IsComputingFrame`, the frames
done and their count and `Share` (0 to 1). No words in the model: `BusyTextConverter` builds the text from the kind,
the numbers and the language, as the run status does, and `TaskbarProgressConverter` the taskbar's state, so the
`TaskbarItemInfo` is bound in the XAML like everything else (a Freezable set on the window resolves the window's
DataContext). Gone with it: the three busy dependency properties, `IsExportWaiting` and `IsComputingFrame` as
dependency properties nothing bound (the first a private forwarder to the model now), `MainWindow.Busy.cs`,
`MainWindow.Taskbar.cs`, the `UpdateBusy` call of the language switch. Proven by a WPF probe without a shown window
(21 checks: the rules, a real export job of 20 frames followed on the way and to its end, the app's own markup for the
taskbar and the text parsed and bound; a binding whose source is not there at parse time attaches at DataBind
priority, so such a probe pumps the dispatcher once after setting the DataContext) and by `ExportCheck` (41 of 44,
the three the foreground flake of a second start; every export check passed).
The ninth step (2026-11-07): **`PreviewState`** is the preview as the application sees it: `Position` and `IsPlaying`
(two-way: the pane's scrubber, keys and clock move them, the application stops playback and follows the position with
its scratch requests), `ShownFrame` (a record of the frames and the index shown, told by the pane, read by the
brightness fit) and `CanvasWidth`/`CanvasHeight` (told by the pane, read for where a new item lands), and
`ColorsVersion`, a count the pane binds and rebuilds its palettes on when `RefreshColors()` raises it (a number, not an
event, so the pane binds it like the rest). The pane's `ShownFrame`, `CanvasWidth` and `CanvasHeight` became ordinary
dependency properties it writes with `SetCurrentValue` (as the tree's `SelectedItem`), bound `OneWayToSource`; the XAML
compiler needs a public setter to bind even that way (MC3080), so they have one. Gone: the window's `PreviewPosition`
and `IsPreviewPlaying` dependency properties, the pane's public `RefreshColors`, and the window's colour refresh at a
project replacement (from before the rebrand): the pane's layer settings follow the project it binds, which moves after
`ProjectReplaced`, and each applies its palette then, so the refresh did the same work a moment earlier with the old
project. The window reaches into the pane for nothing but `Focus()` now. Proven by a keyboard probe on `tiny_scheme.wdproj`
(14 checks: the project scheme's colour at a quiet pixel after the start, a stop edit recolouring at once and Escape
putting it back, a scrub asking for its frame, Auto brightness fitting the shown 16-bit frame without computing it, a
new source at the canvas centre, an example replacing the project with its own colours; a probe's expected colour goes
through the layer's black level, −0.05 there, c · 1.05 − 0.05 a channel) and by `PreviewCheck` (42 of 43: the playback
check's 2-second budget failed under a Rust build of the user's that held every core, the same run taking 11.6 s
against 2.7; machine load, not the app). **A check with a time budget needs a quiet machine**: look at the CPU before
judging one (`PreviewCheck`'s first playback is the one such check). **The harnesses run against the user's settings
file** (2026-11-07, the whole set after the twelfth step: 10 of 12 clean, two failed on a file at half resolution and 8-bit,
the memory warning's levers from his session): `PreviewQualityCheck` chose "Half" on a setting that was Half already, so
nothing went stale, and it now puts the resolution and the frames to Full first, as it did the samples; `ProbeGraphCheck`
looked for "Running" in the status bar after Play, which a reduced run of 100 steps had left in 0.2 s, and now reads the
log from a byte mark for "Re-run started", the measurement the app makes itself. Both passed once changed; the other ten
had passed: `library_check` 662 ok.
The tenth step (2026-11-07): the runs and the live-editing rules, on two more interfaces each app implements: first
**`IUiTimers`** (`Create(interval, tick)` → `IUiTimer`; WPF's `DispatcherTimers`), the user's choice among the options
offered for the two pacing timers, which went again the same evening when the timers did (see Live editing: the
requests at every change, the re-run at the edit's end; the harnesses that awaited a re-run after an arrow-key nudge
now leave the handle first, as a user does, and `PreviewQualityCheck` reads "Re-run started" in the log, since the
stale status lasts milliseconds); and **`IMemoryWarning`** (`Ask(spec, budget, autoBudget,
quality)` → `MemoryWarningChoice?`, the record now in `WaveDudeNext.UI`; WPF's `Platform/WpfMemoryWarning` shows
`MemoryWarningWindow`). **`RunHost`** holds the current run and its store: `Start(automatic, trackIntensity, fullQuality,
intensityRoot)` (the scene composed, the RAM budget, the warning, `FrameCache.CheckSpace`, the store, the worker with the
progress context the application gives it, Loaded priority in WPF), `Discard`, `Shutdown`, `Stop`, `TogglePause`,
`IsActive`, the flags `IsRunStale` and `IsProbesStale`, the events `RunStateChanged` and `RunFinished` (the current run's
only), and the static `PreviewQualitySetting`. **`LivePreview`** is the rules: `Edited(change)` (a probe edit stales the
recordings and asks for the frame; an active probe the run does not record, or a brightness edit the 8-bit samples bake
in, is a simulation edit; a simulation edit stales the run, stops playback and any run in progress, repaints the scene
after a part edit, paces the scratch frame and the re-run), `PreviewQualityChanged`, `EnsureRun` (Play's rule: not during
an export, a run refused in the warning turns playback off), `RunFinished` (also called by the export flow when its export
ends), `ScratchFrame` (bound by the pane), the scratch runner with its pause of the run and the busy indicator, the re-run
postponed behind a pending frame or an export (`BusyState.IsExporting`), and the first project's warm-up, which the probe
found missing (the window had scheduled it by a direct call the document's event does not cover). **`ProjectDocument`
owns the `ProjectWatcher` and the `GatePortFollower`** per project: every change makes it dirty, is logged at Trace with
old → new, and is raised as `Changed`, the one event the window's edit hub (`MainWindow.Live.cs`, 62 lines) hangs the
models on: the undo history, the scene image's watch, the preview's colours, `Live.Edited`. `MainWindow.Simulation.cs` is
the command handlers, one line each, and `DiscardRun`, which ends an export reading the store before `RunHost.Discard`.
Gone: the window's `Run`, `IsRunStale`, `IsProbesStale` and `ScratchFrame` dependency properties (the XAML binds
`RunHost.Run`, `RunHost.IsRunStale`, `RunHost.IsProbesStale`, `Live.ScratchFrame`), `StartRun`, `OnProjectChanged`'s rules,
the timers. Proven without a window (32 checks, the timers fired by hand, a warning stand-in, a pumped UI thread, real
runs of a 64 × 48 project on the engine of the settings: the warm-up, a run followed to its end, an edit's staleness and
the stop of playback, the re-run postponed behind the frame and started after it, an edit stopping a run, a probe added
against a probe moved, a scheme edit meaning nothing, a scrub past the run's frames, Play's three cases, the export
holding the re-run, the quality change, the warning asked for 19 GB against a 6.4 GB budget and refused; the warning's
choice path, which saves the settings, was not run with the user's app open, and is the moved code) and by `RunCheck`
(71 of 71). Settled on the way: a probe without the window must stand in for the window's edit hub
(`document.Changed += (_, c) => live.Edited(c)`), or no edit reaches the rules.
The eleventh step (2026-11-07): **`Exporter`** carries the exports out (`ExportAsync(field, intensity, still)`,
`ExportLastAsync`, `ExportProbeAsync(probe)`, `Cancel`, `Shutdown`; `Job`, `IsWriting`, `IsWaiting`, `IsExporting`,
`CanExport`, `CanExportLast`, `CanExportProbe`; the events `Failed(ExportFailure(key, error))`, which the window shows
by the key's text, "ExportError" or "RunError", with the error's words when there is one, and `Finished`, which the
window hands to `Live.RunFinished`), on **two more interfaces each app implements**: **`IExportDialogs`**
(`AskWhereToSave(subject, folder, name, formats, formatIndex)` → `SavePick(path, formatIndex)?`, the `ExportSubject`
naming what the dialog is for, so the application picks the title and the filters, the formats in its filter order,
0-based; `AskToReplaceFolder(folder)`; WPF's `Platform/WpfExportDialogs`, the common Save dialog and the message
window) and **`IFrameDecorators`** (`Create(project, run)` → `IFrameDecorator.For(layer)`, the painter of what the
application draws over a layer's frames; WPF's `Platform/WpfFrameDecorators` wraps `ProbeGraphPainter` and
`SourceMarkerPainter`, which rasterise with `RenderTargetBitmap` and `FormattedText` and so stay in WPF). The model reads
`WAVEDUDE_EXPORT_PATH`, the harness hook, itself; it feeds `BusyState.Export` and `IsWaitingForRun`. `MainWindow.Export.cs`
(74 lines, was 546) is the handlers, one line each, and `OnExportFailed`; the `Export` dependency property is gone
(`Exporter.IsWriting`, `Exporter.Cancel()` for Stop and the discard). Proven without a window (19 checks, a dialog stand-in
recording its inputs and answering as told, no decorations, real runs and files: the dialog's subject, name, formats and
first format, Cancel, a PNG sequence with the run made first and the wait shown, Export last without the question, the
folder question answered No, a still named with the padded frame among the stills' formats and no "last export", a
scripted path with the format by its extension, a probe's CSV after the live rules staled the run, a failure on a drive
that is not there with `Failed` and `Finished` both raised) and by `ExportCheck` (44 of 44). A probe of the exporter
needs the live rules too, or a probe added never stales the run and the export rightly fails on the old one.
The twelfth step (2026-11-07): **`AutoBrightness`** is the pictures' Auto brightness: `Fit(layer)` fits the shown 16-bit
frame at once, else computes the frame in 16-bit on a scratch runner of its own with any run paused meanwhile and
applies the fit when it arrives, raising `Fitted`, which the window answers with `_history.EndStep()` (an edit complete
in itself, after the command that asked for it); `IsFitting`, `CanFit` (no fit computing, no export, steps to run).
`MainWindow.Display.cs` is the two handlers (31 lines, was 132). Proven without a window (11 checks: the computed path
with `Fitted` once and the brightness fitted, the shown-frame path at once without an event for both layers, a run in
progress paused and resumed around the computed frame, an empty frame fitting nothing) and by a keyboard probe in the
app on the 8-bit preview (the frame computed in 16-bit, the fit, the undo step recorded after it in that order, the
scratch frame re-requested as for any brightness edit in 8-bit). **The window's logic is in models now**: the window's
partials are 1,195 lines, command handlers of a line each, the edit hub, the dialogs, the constructor's wiring and the
things only WPF has (the dock, the jump list, the zoom, the menu bar's F10, the focus); it reaches into a pane for
`Focus()` and `FocusList()` alone. Five interfaces each app implements: `IImageDecoder`, `IClipboard`,
`IMemoryWarning`, `IExportDialogs`, `IFrameDecorators` (`IUiTimers` came and went with the live rules' timers).
What still anchors the rest: the live dock model (`DockItem` with WPF's `ContentProperty`, `DockSplitNode` with WPF's
`Orientation`); `RelayCommand` requeries through WPF's `CommandManager`; the converters implement
`IValueConverter`; the 59 commands are routed commands; the scene image is decoded by WPF's `BitmapDecoder`
(`SceneImageLoader`; the library writes PNG but reads none); the manual and license are FlowDocuments, which Avalonia has
only in its paid tier.
**A contributed Avalonia port** (2026-11-09, read that day): `C:\Work\WaveDudeNext-PRs\wavedude-next-huygens` (Forgejo
`hibryda/wavedude-next-huygens`, author Hibryda, written with Claude Code over 2026-11-07 to 09, 157 commits, Avalonia
12.1.5), based on be0023f (0.1.3), the commit before `WaveDudeNext.UI`, which it carries as `upstream/`. Its controls,
docking and presenter are close translations of the WPF files (87 % of `DockHost`'s lines carried over, 79 % of
`SceneOverlay`'s); its window is 3,800 lines of 0.1.3 window logic wired in code (no binding in `MainWindow.axaml` or
`PreviewPane.axaml`, the old live-rule timers), and it has its own `Settings`, `ColorSchemeCatalog`, `Examples`,
`AppTheme` and `DockLayout`; 160 headless tests (xunit.v3, `Avalonia.Headless`) port the twelve `ui_check` scripts; every
file's header names JJM Vleggaar (Huygens Optics) as the copyright holder. The user built and ran it on Windows himself.
Recommended: keep its controls, panes, forms, themes, docking, manual renderer, Skia painters and tests, and translate the
window from master's WPF window on the models rather than adapt its own; open for his word: the git form (one commit or the
history, which carries a second copy of the 0.1.3 tree), the credit, what else to take (the CLI, the Linux packaging, the
ffmpeg MP4 pipe, the OpenCL loader name and `/proc/meminfo` into the library), the look on Windows (Inter instead of Segoe
UI, settled the same evening: see below; no menu glyphs, dark default, antialiased lines at a fractional zoom, no jump list, taskbar progress or title-bar
click-through), `Avalonia.Controls.ColorPicker` (referenced, apparently unused). **The string tables stay as they are**
(2026-11-09, the user's choice: each is 902 `s:String` entries and nothing else, `WaveDudeNext.UI` reads no string, and the
port's conversion was three text substitutions): WPF loads them natively as now, and the Avalonia project converts the same
files at build (an inline task like `Documents.targets`, output in `obj`), so both heads load compiled dictionaries;
reading them as XML at run time in Avalonia is the fallback should its compiler not take generated files. Refused: both
heads reading XML at run time (the WPF designer would show no text), a new format (resx or our own).
The thirteenth step (2026-11-09, before the Avalonia head): **the models' dialogs answer as tasks**, since Avalonia's
windows and file pickers answer only later (the user's choice over a nested dispatcher frame in the Avalonia
implementation): `IMemoryWarning.AskAsync`, `IExportDialogs.AskWhereToSaveAsync` and `AskToReplaceFolderAsync`,
`RunHost.StartAsync` (`Task<bool>`), `LivePreview.EnsureRunAsync`. WPF's implementations return finished tasks after their
blocking windows, so every await goes on within the call and the order of events is the old one (`OnRun` and `OnEnsureRun`
are `async void` handlers; the re-run inside `LivePreview` is a private `async void`, so an unexpected error reaches the UI
thread's handler as it did). **`RunHost.IsStarting`** is true from the question to the start or the refusal: no other
start is taken meanwhile (`StartAsync` answers false, `TryRerun` holds), which closes a gap the blocking window had too: the
application is live under a modal window's message loop, and a scratch frame arriving there began the automatic re-run,
which asked a second time over the open question (shown on that afternoon's build with a stand-in warning that pumps the UI
thread while open: asked twice, nested; never seen in the app). The project is snapshotted again after the answer,
whatever the choice, so an edit that came without input meanwhile is in the run that is marked fresh. **`PreviewState.IsPlaybackHeld`**
holds the pane's clock while Play's run is decided: set and released by `EnsureRunAsync`, released after a refusal has
turned playback off, never before (a hold on `IsStarting` itself would be released first and start the clock for a
refused run); the pane's clock runs while `IsPlaying && !IsPlaybackHeld` (`UpdateClock`). The window's own dialogs (Open,
Save As, the save question, errors, About, the scene image's) are called by no model and stay as they are. Proven without
a window (28 checks: a start that asks nothing finished at return; the automatic re-run's question open while a scratch
frame arrives, an edit finishes and a second start is refused, each unasked; Cancel; F5's question with an edit under it in
the run; Play refused later with playback off before the hold's release, Play accepted; the blocking answers decided at
return in the same order; a project that cannot run; the export's Save dialog and folder question answered later and at
once), in the app by a keyboard probe with the real warning window on a 1920 × 1080 project of 0.8 GB against a RAM cache
at 1 % (17 checks: Play refused with the position held at 0 under the question and playback off after Esc, F5 refused,
Play accepted with the re-run starting and playback moving; the settings file backed up and put back), `ExportCheck` 44
of 44, `RunCheck` 69 of 71 and then 70 of 71 (the first run: four keys at the end of the first session had no effect,
L, Space, Space, F5, with the foreground moving from the harness's window to another's in that span; the same sequence
passed seven rounds in a probe of its own; the second run: every check but the foreground guard, the window having been
activated during the first session). On the way `RunCheck`'s three trailing Left keys went (they had moved the source
back while its handle held the focus; since a74c04a leaves the handle first they hit whatever had the focus after the
re-run's start, and on the first run switched the pictures to side by side, saved into the user's settings and put back
by hand). For the Avalonia head: its export dialogs must keep a second export from beginning while one is open where the
platform's picker does not block the window (`Exporter` guards a second export only once it waits or writes), and the
warning's choice changes the quality settings inside the start, which `PreviewQualityChanged` treats as an edit whose end
a command brings, so that head's edit boundaries must end it when the dialog closes.
**The Avalonia head is `WaveDudeNext.App`** (2026-11-09, the user's choice of name over `WaveDudeNext.AvaloniaUI` and
`WaveDudeNext.Avalonia`, whose namespace would shadow the framework's; the port's own name, so its 157 files kept their
namespace). Its first step: the port's leaves imported as they are, the window a frame. Taken: `Controls`, `Converters`,
`Docking` (its `DockLayout.cs` dropped for the UI library's layout classes, `DockHost` converting `Orientation` ↔
`LayoutDirection` at save and load), `Help` (the run-time Markdown renderer and the SVG figures), `Dialogs`, `Markup`,
`Localization`, `Logging`, `Panes`, `Rendering`, `Theming` (its `AppTheme` dropped for the UI library's), `Themes`, `Views`,
`Commands` (`AppCommands`, `RelayCommand`), `NamedCollection`, `App.axaml`, `Program`, `app.manifest`, the icon; the
platform companion's painters as `Platform/` with **`SkiaFrameDecorators : IFrameDecorators`** and its decoder as
**`SkiaImageDecoder : IImageDecoder`**. Not taken: the port's window (3,800 lines of 0.1.3 logic), its `Settings`,
`ColorSchemeCatalog`, `Examples`, string tables, tests, CLI, packaging, the ffmpeg MP4 sink, the OpenCL loader name and
`/proc/meminfo` (the last two belong in the library when Linux comes), `Avalonia.Controls.ColorPicker` (referenced, unused).
Packages: Avalonia 12.1.5 with Desktop and Fluent, SkiaSharp 3.119.4, NLog. **The text is the platform's default font**
(2026-11-09, the user's rule: the same font as the WPF app on Windows): no bundled font, `FontFamily.Default` everywhere,
which Avalonia resolves to Segoe UI on Windows and to the system's UI font elsewhere; the port's `Avalonia.Fonts.Inter` and
its `WithInterFont()` are gone (Inter's wider glyphs against Segoe UI's, seen in two captures of the menu bar). **The text is
rendered as the WPF head renders it** (the same evening, the user's report that the antialiasing looked poor): subpixel
antialiasing, strong hinting and the baseline on the pixel grid, WPF's Display mode, set by `Theming/TextRendering.Apply`
on every window `WindowZoom.Attach` gets, from where the inherited value reaches the window's popups; Avalonia 12 exposes
the three as `TextOptions.Set…` methods, not as properties a style's setter can name (the XAML compiler refused them).
Seen in captures of the app's own windows: crisper strokes with the subpixel fringes, in the menus too. Settled with it:
**the menu bar's dropdowns are placed against the header itself** (`PlacementTarget="{Binding $parent[MenuItem]}"`; the
popup's parent is the panel inside the header's padding, which had put the menu 7 px right and 4 px up into the header:
measured 610,359 under a header at 603..635 × 339..363, now 603,363), **the "+ Part" flyout drops left-aligned under its
button** (`PlacementMode.BottomEdgeAlignedLeft`; Avalonia centres a flyout by default: 699 under a button at 746, now
746), and **a flyout's presenter and a context menu have `FontSize` 12** (a flyout's presenter is a root of its own and
took Fluent's 14 px; the user's report on the Part menu). The window handles F10 as the port's did (`menu.Open()`).
**Tooltips let the pointer through** (`Controls/ToolTipPassThrough`, 2026-11-10, the user's report on the Examples entries:
the tooltip "steals the hover under it"): Avalonia places a tooltip 20 px below the pointer in a popup window that takes
the mouse, and its `ToolTipService` keeps a tooltip open while the pointer is over that window, so moving down a menu
parked the pointer on the tooltip of the entry just left and the entry beneath got no hover; `PopupRoot.IsHitTestVisible`
forwards to `IPopupImpl.SetHitTestVisible`, documented to make the native window input-transparent (on Win32 its
`WM_NCHITTEST` answers `HTTRANSPARENT`), which the tooltip never sets, so a class handler on `ToolTip`'s `Loaded` sets it on
every tooltip's popup root (a new root per opening), and the pointer reaches the window beneath as under WPF's tooltips.
Proven by a startup-hook probe that opened a tooltip programmatically and asked its popup window `WM_NCHITTEST` at its
centre: −1; Avalonia's default is `HTCLIENT` by its source (read at tag 12.1.5, the service's `Update` returning early
"while the pointer is over a tooltip"). The hover itself the user tests by hand (tooltips show on the pointer alone).
**The menu of part kinds is the WPF head's** (the same evening, the user's report that the eight kinds of 2026-11-07/08
were missing): `ProjectTree`'s kind tables carry them in `PartMenuEntries`' order with the Diffraction gratings and Meta
surfaces submenus among the optics (`Submenu`, which the Optical logic submenu uses too), and `Themes/Icons.axaml` had
the twelve geometries the WPF theme had gained (the eight kinds, the two submenus, the two detectors), copied as they
are, since the path markup is the same in both frameworks; the tree's row icon is the class name without "Part", so
the rows of the new kinds find theirs. The entries' commands come with the models. **The icons and the colour tables are
built from the WPF files as the string tables are** (2026-11-10, the user's choice, option A of two for each of his two
questions, the menu glyphs and the duplicated geometries): `Build/SharedXaml.targets` with the inline task `SharedXaml.cs`
(the former `StringTables.targets`, generalised) converts the `SharedXaml` items of the project file, each with a `Folder`
metadata, into `obj/.../Shared/<Folder>/<name>.axaml`, added as `AvaloniaXaml` with the Link `<Folder>/<name>.axaml`:
`WaveDudeNext.WPF/Strings/Strings.*.xaml` under `Strings`, `WaveDudeNext.WPF/Themes/Icons.xaml`, `Dark.xaml` and
`Light.xaml` under `Themes`, so `avares://WaveDudeNext.App/Themes/Icons.axaml` (loaded by `IconView`) and the two theme
dictionaries of `App.axaml` are the WPF files; the App's copies of the three are gone (the port's 92-key tables were the
WPF ones key for key, its icons 48 of the WPF 53 plus its five folder drawings, now in the WPF file). The conversion is
the same three substitutions, of which the theme files need the namespace alone. `WaveDudeNext.WPF/Themes/Icons.xaml`
holds every `StreamGeometry` the WPF `Controls.xaml` had (the chrome's arrows and chevrons too, which the App's
`Controls.axaml` still declares for itself), merged into `Controls.xaml` at its top; `IconPlusBadgeStyle` stays in
`Controls.xaml`. The App's `Controls/MenuIcon` is the attached `Drawing` property the WPF head has, on its menus in the
markup and on the code-built context menus (the tree's rows, the pictures, the graph boxes, the Messages pane); the
scene form's save button is the shared Save drawing in both. Not shared: the control-local geometries each head's panes
declare for their own chrome (the timeline's buttons, the tree's plus and minus, the markers), the port's translations of
the WPF ones. **A probe captures the app's own
windows with PrintWindow, never the screen** (2026-11-09: a capture through
`CopyFromScreen` at the app's rectangle caught another window the user had over it, deleted unread; a popup is a window
of its own, found by enumerating the process's visible windows, since UI Automation files none of them under the root).
**Every file of the project
carries the repository's header with a third "Based on" line, "Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port"**
(the user's choice, as the author asked to be named): an `.editorconfig` section `[WaveDudeNext.App/**.cs]` holds that
template, so IDE0073 enforces it there as the root template elsewhere; the markup files carry it as the WPF ones do;
**the About window says the same** (2026-11-10, the user's rule): the copyright line is his, and the header's third line
is the third credit under "Based on the work of" (`AboutInfo.CreditPort`), where the port's own copyright line stood.
**The string tables are converted at build**: `Build/StringTables.targets` with the inline task `StringTables.cs` turns
`WaveDudeNext.WPF/Strings/Strings.*.xaml` into `obj/.../Strings/Strings.*.axaml` (three substitutions: the default
namespace, the System namespace, `s:String` → `x:String`), added as `AvaloniaXaml` with the Link `Strings/…` before
Avalonia gathers its resources, so `avares://WaveDudeNext.App/Strings/Strings.EN.axaml` is compiled in and the language
switch swaps it; the output folder is named inside the targets, since a property of the imported file is evaluated
before the SDK sets `IntermediateOutputPath` (the first build wrote the tables into the project folder). The window
(`MainWindow.axaml`): the port's menus (texts through `DynamicResource`, no commands yet), the dock host with its three
items, the preview document and the default arrangement in the markup as the WPF window has them (`x:Name` on a
`DockItem` is refused by Avalonia's compiler, the items are found by `Id`), the status bar; its code restores and saves
the dock layout and the placement through the UI library's `Settings` (`WindowMode`) and attaches the zoom. The panes
are unbound and the menus do nothing yet; a project file argument is logged, not opened. Proven: the solution builds with
0 warnings; a start on the user's settings file came up with his dock layout, the three panes, the pictures and
"Ready" from the string table, closed on WM_CLOSE and saved the settings, the log clean; the capture is under `%TEMP%`.
UI Automation found no menu items in the Avalonia window (Avalonia's peers differ from WPF's); how this head is driven by
a harness is open (the port's tests use `Avalonia.Headless`). Next: the models wired as the WPF window wires them and the
panes bound to them, the menus bound to `AppCommands` in the markup, the dialogs behind the five interfaces plus an
Avalonia `EditBoundaries`; then the new part forms and the detectors' rows; then the tests.
**The second step, the window on the models** (2026-11-09, the same evening): `MainWindow.axaml.cs` constructs and wires
the models as the WPF window does (the same constructor, the same hub `MainWindow.Live.cs`, `OnProjectReplaced`,
`OnDocumentOpened`, `DiscardRun`), on the Avalonia implementations of the five interfaces under `Platform/`:
`SkiaImageDecoder`, `SkiaFrameDecorators`, **`AvaloniaMemoryWarning`** and **`AvaloniaExportDialogs`** (the port's
`MemoryWarningWindow` and the storage provider's Save dialog with the string table's filters; both answer later, which
the dialog step made possible; the Save dialog does not say which filter was chosen, so the format is read from the
path's extension), **`AvaloniaClipboard`** (reads asynchronously and answers "nothing known" until the read has landed,
as the interface foresees; Windows' clipboard sequence number as the change count) and **`EditBoundaries`** (pointer
pressed on the tunnelling pass, released, focus lost, Enter in a single-line text box, a toggle button's click, a drop,
all heard at the window
with the handled events too; a command before and after through `Command(action)`, since Avalonia has no routed commands
to hear). The window is its own data context (`x:DataType`): the panes bind `Document.Project`, `Selection.Current`
(two-way) and `Items`, `RunHost.Run` and its flags, `Live.ScratchFrame`, `SceneComposer.Pixels`, `Busy.IsComputingFrame`,
`Preview.Position` and `IsPlaying` (two-way), `IsPlaybackHeld`, `ColorsVersion`, the settings' orientation through
`EnumByNameConverter` and dither bits; the title and the run's status are multi-bindings through `TitleConverter` and
`RunStatusConverter` with the language as an input, as in WPF. **The menus bind `Commands.X` in the markup with their
`HotKey`s**; `AppCommands` gained the window's own (New, Open, Save, SaveAs, CloseProject, Exit, PlayPause, About,
Manual), each wired in `MainWindow.Commands.cs` as a line into a model, with the WPF window's CanExecute rules; every
command is an edit boundary before and after. A `HotKey` on an entry inside the menu bar registers nothing before its
menu has opened once (the port's author had found the same), so `RegisterHotKeys` walks the menu and puts every gesture
the markup names on the window's `KeyBindings`; a text box's own Ctrl+C/V/X/A and Delete stay its own, since a handled
key never reaches the window's bindings. The menus whose entries come from data (recent files, examples, panes, theme,
zoom, language, the preview quality, the RAM cache) are still filled in code from the port, their check marks pushed
(`_checkedMenus`; 2026-11-10: the theme entries name their texts by `Enum.AppTheme.<Member>`, the keys the WPF menu's
`EnumNameConverter` uses, where the port had asked for `MenuViewTheme<Member>`, keys no table has, so the entries were
blank; every other fixed key the App's code names was checked against the table and the themes); ItemsSource bindings are a later cleanup, as are the Pause/Resume and Play/Pause headers that follow
the state. **The preview pane places its own timeline bar and scene overlays** (the port's window had put them into the
pane's hosts from outside; 2026-11-10: the bar binds its own `Background` and `Foreground`, which a pane method's
bare `BackgroundProperty` had missed for the pane's, so the bar had come out dark with a black playhead; seen against the
WPF bar's capture in the same state; both draw the same ticks, minor every frame and major with a label every 5, in
`ControlBorder` #3F3F46 on the bar's #3E3E42, four levels apart and as good as invisible, counted pixel for pixel in
both, so the ticks the user saw in the Avalonia bar were those marks on the missing fill; **the ticks are visible but
subtle in both heads since 2026-11-10**, the user's request: a theme brush `TimelineTick` of their own, #78787F dark and
#A6A8B2 light, about 60 levels from the fill (30 was "a bit too subtle"), bound to `TickBrush`, while the bar's outline and hover line stay on `ControlBorder`
through a new `OutlineBrush` of both bars; measured 9 and 10 tick pixels of #5C5C64 in the same strip of each), gained `IsPlaybackHeld` with the WPF pane's
`UpdateClock` and `ColorsVersion`, and its `MemoryWarningChoice` record went for the UI library's. The status bar's message comes from the log target as in the
port (`MainWindow.Status.cs`). Proven by a keyboard probe on `tiny.wdproj` by argument (13 of 13): the title, the open
and the first scratch frame in the log, nine tree rows, "0 / 99", F5's run completed and followed to 99, Space playing
and pausing, a nudge staling the run and asking for its frame, the re-run when the handle is left, the nudge one undo
step; the capture showed both pictures with the source's marker and label, the form of Source 1, the red edge line and
the run's status. Not yet: `RevealRequest` on the tree and the properties pane (a new item's row and caret),
`ShownFrame` for the brightness fit of the shown 16-bit frame (the fit computes the frame instead), the new part forms
and the detectors' rows, the export's busy text and taskbar, the tests. UI Automation gives the Avalonia tree's rows no
names.

## People, name, license

Alexander Gräf (the user), author of WaveDude Next: his name has the umlaut in documentation shown to the public (the
README; 2026-10-31, his rule), while the ASCII "Graef" stays in the license headers and the git author, his choice to
avoid Unicode there. Jeroen Vleggaar, Huygens Optics, author of Wave Dude 2D,
Copyright (C) 2026. 0x23 (Diffraction Limited), author of WaveSimulator2D, which Jeroen's numerics followed; named by his handle only, at his wish (2026-10-19).
Private addresses stay out of files. The name was agreed with Jeroen (he proposed "Pro"); the GitHub repository
`graealex/WaveDude` still has the old name. The header text is the editorconfig template, identical in every file,
never reworded per file.

## Open items

README and a documentation file (he writes them); an integer
upscale in exports; HEVC;
dropping the 9-point Laplacian (undecided);
**optical parts**: the open points at the end of `OpticalParts.md` (a `PartCheck.ps1` harness, example projects,
Bragg thicknesses in pixels) wait for the user; **after Jeroen's drop of 2026-11-08**: an example project for the
meta axicon (the other new kinds have theirs since 2026-11-10, Examples 25 to 33), and by hand, since no harness presses the mouse: the caret in a new item's name after a pick from "+ Part" and
"+ Probe" with the window active, a filter layer dragged by its grip as one undo step, and the emitter dots after a
drag on Jeroen's machine with and without the cache renewal. The figures were retaken that afternoon with the user's word (51, 14 of them new; of the 37 there before, 22 came out
byte for byte as committed and 15 differ where the change shows: the "+ Probe" arrow in the toolbar, in the tree, the
main window and behind the menus, the Add part menu's entries, format version 4, the outline ticked in the Display
form, the probes' three new rows, the zone plate's defaults). The script's restart of a session that comes
up active and the pointer's move when it does lie on the app's window had no occasion in those runs. **Retaken again
on 2026-11-10** with the user's word (51 figures, no problem, under six minutes, the app's log clean): 33 byte for byte
as committed, 18 and `main-window.png` changed, each compared pixel by pixel with the committed picture: the Examples
submenu with its 33 entries, the counting detector's form with Step axis and Auto-scale steps enabled, and sixteen that
show what three commits of that day had changed without a figure run, the icons drawn instead of glyphs (the menus'
icon columns, the tree's folder icons in the tree, the window and behind the Add menus, the scene form's save button)
and the timeline's ticks. **A figure run shows every look change since the last one**: before saying which figures
will change, read the log of commits since the last one that touched `Assets` (that evening the prediction named two
figures and eighteen changed).

**Unsolved: clicks on the intensity picture selected nothing** (2026-10-22, seen once, not reproduced). In
Jeroen's `logical_elements_example.wdproj` (git history up to 02d0e2d; Example 18 is its curated form), after several
undo and redo cycles (gates moved, undone, redone),
a click on Probe 1's or Rectangle 1 copy's handle in the intensity picture selected nothing; nothing lay over them, the
user checked. Reopening the project (a fresh start) cured it. The session is in the Release build's log
(`WaveDudeNext.WPF\bin\Release\net10.0-windows\WaveDudeNext.WPF.log`, 21:58:52 to 22:00:42): no warning or error, and
the log records no clicks or selection changes. Looked at without a finding: `SceneOverlay` and `ProbeGraphOverlay`
rebuild their handles on every list change (an undo keeps the list objects, refills them), the selection bindings
overlay > pane > window are two-way, the marquee releases its mouse capture, the busy dimming is not hit-testable, the
graph boxes lay far from both handles. A keyboard-driven rerun (handles moved, undone, redone) found every handle once,
in place. Not yet looked at: the click path itself (`MarkerThumb` drag start, mouse capture after an undo during or
right after a drag, focus) with logging of what a press hits.
