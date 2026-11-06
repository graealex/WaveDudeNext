# Desktop control-flow verification

This dependency-light console test starts the real Avalonia `App` and `MainWindow` on Linux using `Avalonia.Headless` and native Skia, without an X server. It raises real button Click events and changes real list, property-editor, view and timeline controls. Reflection observes private state; it does not replace the application model or event handlers. An injected recording `IStorageProvider` captures actual picker options and returns local test destinations; all save, reopen, PNG, and CSV work still runs through the real window handlers. The run-progress timer handler is explicitly advanced after completion to make timeline tests deterministic in the headless dispatcher. Bundled Inter fonts provide readable headless captures without reliance on the host font configuration.

Run from the repository root with the .NET 10 SDK:

```sh
dotnet build tests/WaveDudeNext.DesktopTests -m:1 --disable-build-servers
dotnet run --no-build --project tests/WaveDudeNext.DesktopTests -- /path/to/qa-output
```

The optional output directory defaults to a subdirectory of the OS temporary directory. It receives a text assertion report and four PNG captures: initial scene/editor, unsaved-changes dialog, completed field/timeline, and intensity view. The final two use an emitter moved through the actual property editors into a resized 128×96 scene; tests require non-uniform wave pixels.

Coverage: initial layout and object population; scene/simulation/source editing; add/delete sources, probes and parts; invalid numeric input and blocked selection changes; unsaved replacement and window-close cancellation; run/pause/resume/stop/restart; completed frame navigation and field/intensity/scene switching; playback toggle; repeated restart/stop/pause clicks; confirmed replacement cleanup and clean close. Save-picker regressions check suffix-free default suggestions, cancelled Save As, exact preservation of a user-selected doubled-suffix filename, project save/reopen, Save without another picker, and PNG/CSV output at the exact chosen paths.

This is **Linux headless integration verification, not macOS execution**. It does not verify macOS native file pickers, Finder launch, Gatekeeper, code signing/notarization, Retina/multiple monitors, keyboard/pointer hit-testing, or native macOS fonts/chrome. The recording provider verifies the application’s save/open/export picker contract and file output, not native dialog rendering or the platform’s final extension behavior. Native file dialogs and image import still require platform acceptance tests. Screenshots show the actual Avalonia control tree, not native macOS window decorations.
