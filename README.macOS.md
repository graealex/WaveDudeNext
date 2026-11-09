# WaveDude Next: experimental macOS desktop port

This is a community port of [WaveDude Next](https://github.com/graealex/WaveDudeNext), based on upstream commit `08f9e63a0137c04debe891c76421fbc382c881b7` (version 0.1.3). The upstream release is a Windows/WPF application. A separate Avalonia desktop frontend reuses its .NET simulation library; it does not make WPF run on macOS.

## Scope and limitations

The initial frontend is intended to support opening/saving `.wdproj` files and bundled examples, field/intensity views, running/pausing/stopping, timeline playback and scrubbing, scalar property editing, adding/deleting sources and optical parts, image scene decoding, PNG export, and recorded-probe CSV export. It is English-only. Availability in source is not proof these paths work correctly on macOS; validate on the target Mac before relying on results.

This is not feature parity with the upstream WPF interface. Do not assume docking, graphical shape handles, rich probe graphs, undo/redo, localisation, automatic image-file watching, every importer/exporter, video export, or the complete upstream editing workflow are implemented. The bundled upstream manual describes the original Windows application and is reference material, not a macOS feature guarantee. Windows Media Foundation export is unavailable on macOS. GPU/OpenCL support and performance are not promised; the CPU path is the baseline.

## Requirements

- macOS 15 or later is recommended under the current [.NET 10 supported-OS lifecycle table](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) (checked 2026-11-05). The bundle declares macOS 14 as a compatibility minimum, but that version is unverified and Microsoft lists it out of support since September 2026. Only the native M5 configuration described below has been validated; other configurations remain unverified.
- Apple Silicon (`osx-arm64`) or Intel (`osx-x64`), using the matching download. No universal binary is produced.
- Building requires the **.NET SDK 10.0.403** (or a newer 10.0.4xx servicing patch selected by `global.json`), Python 3, Bash, and network access to NuGet for dependency restore. Running the self-contained application does not require a separately installed .NET runtime.

## Build from the supplied source

Use the modified source archive delivered beside the binaries, not a fresh upstream checkout: upstream does not contain this frontend.

```sh
tar -xzf WaveDudeNext-macOS-source.tar.gz
cd WaveDudeNext-macOS-source
bash scripts/build-macos.sh osx-arm64  # Apple Silicon
# or: bash scripts/build-macos.sh osx-x64  # Intel
# or: bash scripts/build-macos.sh         # both architectures
```

Optional `VERSION=0.1.3` and `CONFIGURATION=Release` environment variables control the bundle version and build configuration. The version must have three numeric components. The script publishes the desktop project only; building the original Windows solution is not the macOS build procedure. `WaveDudeNext.CrossPlatform.sln` groups the core, desktop frontend and smoke tests for development builds.

Each architecture produces:

- `artifacts/<rid>/WaveDude Next.app`, with `Contents/Info.plist`, an executable in `Contents/MacOS`, self-contained runtime/dependencies, examples, notices, and the original upstream icon (128px PNG stored in an ICNS `ic07` representation).
- `artifacts/WaveDudeNext-macOS-<version>-<rid>.zip`, preserving the app executable permission.
- `artifacts/WaveDudeNext-macOS-source.tar.gz`, containing the modified application/library source, original notices, examples and build scripts, excluding Git metadata and build outputs.

The build script automatically runs `scripts/verify-macos-bundle.py` after packaging each architecture. This standard-library verifier checks plist metadata, the apphost and native-library Mach-O architectures, ICNS structure, license/notices, safe ZIP paths, matching file contents and executable permissions. To recheck an existing bundle without invoking .NET, run `python3 scripts/verify-macos-bundle.py --rid osx-arm64` (or `osx-x64`). These checks are static, not macOS execution.

The archive contains all modified and untracked source files present at packaging time. Review it before redistribution. Build after completing source changes so the source and binaries correspond. Keep the source archive available alongside any binary redistribution and comply with GPLv3's corresponding-source requirements. NuGet and .NET dependencies retain their own licenses; bundled `Resources/ThirdPartyNotices` contains package notices found during restore, package identities/hashes, and available .NET license/notices. Version-pinned full licenses in `third-party-notices/` supplement packages that ship only SPDX metadata. Their source URLs, version evidence and hashes are recorded in `sources.json`; packaging and verification fail if a restored package lacks base license text. See `third-party-notices/README.md` for the focused audit scope.

The commands reproduce the build procedure, not a claim of bit-for-bit reproducible binaries. Exact SDK patch, restored package graph, operating system and ZIP timestamps can affect output. For auditability retain `dotnet --info`, the restored `obj/project.assets.json`, the binary checksums and CI logs with any release.

## Install and trust

Extract the matching ZIP and copy **WaveDude Next.app** to Applications. These development bundles are **not Developer ID signed or notarized**. An SDK-produced ad-hoc signature, if present, is not a verified developer signature. macOS may refuse to open a downloaded bundle. This project does not disable Gatekeeper, remove quarantine flags or change security settings. Review the source and build locally, or obtain a properly Developer ID signed/notarized release from a maintainer you trust. Signing and notarization need the distributor's Apple Developer credentials and are deliberately outside these scripts.

## Validation

A clean Release build of the cross-platform solution completed with **zero warnings and zero errors** on Debian 13 x64 using SDK 10.0.403 on 5 November 2026. The CPU smoke suite passed both solver stencils, deterministic resets/seeds, all 24 upstream example round trips, image/material painting, PNG pixels, run lifecycle, and RAM/disk cache checks. GPU-preferred selection correctly fell back to CPU on this host, which had no OpenCL GPU. The desktop smoke mode passed simulation/rendering/PNG checks, and **45 headless desktop assertions** passed editing, validation, cancellation, run controls, timeline, playback, repeated clicks, cleanup, and the real project/PNG/CSV handlers with a recording file-picker provider. Cancellation regressions cover eight combinations of frame storage, capture cadence and UI delivery; each verifies that the final CSV contains all completed samples and frame profiles. Save-panel regressions verify suffix-free suggested names while preserving the exact chosen destination. Readable field/intensity screenshots were visually checked with native Skia rendering in the headless test environment.

The first Apple Silicon snapshot was separately run on an Apple M5 Mac. Its packaged smoke mode and interactive examples, run/pause/resume/stop, scrubbing/playback, resize, scalar edits, project save/reopen, PNG and probe CSV passed. That check found two issues corrected in preview 2: a cancelled run could omit the last probe batch, and native Save As could propose a duplicate `.wdproj` suffix. Preview 2 passed native M5 regressions: cancelled line-probe CSV had 81 rows for 81 frames at interval 100 and 112 rows for 112 frames at interval 4; a point probe had 447 rows for 447 completed steps at interval 4. Save As, PNG and CSV each produced a single extension, with valid project and PNG output. A GUI CPU run completed 600 steps / 60 frames in 0.6 seconds on that testbed; this is a small-scene observation, not a general performance claim. The reported purple monitor/person control replacing the traffic lights is macOS’s native screen-capture/window-sharing menu, identified from screenshots and Apple’s ScreenCaptureKit notes. No application titlebar workaround is applied. The reported intermittent flicker itself was not retested after native preview 2 validation; a capture-idle observation is still needed to distinguish intermittent capture from persistent OS behavior. Download quarantine behavior and Intel hardware execution have not been tested.

A Linux cloud cross-build can establish that restore, compilation and bundle assembly succeed. It **does not execute or validate macOS**, window creation, native graphics loading, input, packaging trust, hardware acceleration, or performance.

The final notices-complete bundles contain byte-identical executable/runtime files to the native-tested preview 2 ZIPs. Only resource documentation and license notices were refreshed after that test.

The supplied `.github/workflows/macos.yml` defines native macOS jobs for each architecture, a headless simulation smoke suite and the packaged executable’s headless smoke mode. The latter runs before Avalonia startup; it does not create a window or test native GUI libraries. It must be run in a repository containing this port before its results are evidence of success. Merely supplying the workflow is not a passing CI run. Headless smoke tests also do not establish desktop feature parity.

On a target Mac after a successful build:

```sh
dotnet run --project tests/WaveDudeNext.SmokeTests -c Release
"artifacts/osx-arm64/WaveDude Next.app/Contents/MacOS/WaveDudeNext.Desktop" --smoke-test
```

Substitute `osx-x64` on Intel. Also check the actual GUI: launch from Finder, open multiple supplied examples, run/pause/stop, scrub frames, edit a scene, save/reopen it, and export/reopen a PNG. Check both views, resize the window, verify menus/file dialogs and test a modest simulation on CPU. Record the OS version, architecture and any failure. A startup/simulation smoke mode is not a replacement for these interactive checks.

## License and attribution

The upstream application is Copyright (C) 2026 Alexander Gräf, under **GPL-3.0-or-later**. Port additions are also GPL-3.0-or-later. The original license is retained in `LICENSE.md` and the app's Resources directory. Credits to Jeroen Vleggaar (Huygens Optics) and 0x23 (Diffraction Limited) are retained in the upstream README included with the app. Avalonia, .NET and other dependencies are separately licensed; see their accompanying notices and the recorded dependency manifest.
