# Headless core smoke tests

GPL-3.0-or-later, like WaveDude Next. The source header preserves the upstream notices.

Run from the repository root with the official .NET 10 SDK:

```sh
dotnet run --project tests/WaveDudeNext.SmokeTests -c Release
```

This console runner has no test-framework package dependency and runs on macOS, Linux, and Windows. Exit 0 means success; an assertion or exception exits 1; the 90-second watchdog exits 2. Individual worker waits have 15-second limits. Only tiny 48 × 32 simulations are run; full-size upstream examples are loaded and serialized, never simulated.

Assertions:

- Both CPU Laplacian modes produce finite, nonzero field and nonnegative, nonzero intensity. Captured snapshots have the expected geometry. Reset reproduces the same half-precision field exactly within the same process.
- GPU-preferred factory creation exercises OpenCL loading and initialization, logs the chosen engine and fallback reason, and checks a finite/nonzero captured field and intensity. A compatible available GPU is used; otherwise the CPU fallback must work. The explicit CPU-only baseline is retained.
- A fixed seed reproduces randomized source emitters; changing the seed changes them. This is not a cross-architecture floating-point bit-identity guarantee.
- All bundled native `.wdproj` examples load, save, reload, and serialize identically after normalization, preserving their source/part counts and seed.
- A rectangle paints its refractive index against the background; a mirror changes red to zero without modifying the input image. Red/blue material and green source channels decode correctly, including disabling image sources.
- The core PNG writer emits a valid signature and dimensions; IDAT decompresses to the exact expected RGB pixel rows. Spaces and Unicode in temporary filenames and relative scene paths work; cache paths are absolute and native.
- Pause requested before worker start is acknowledged and holds the step count steady; resume finishes all steps. Stored field/intensity frames can be retrieved from both RAM and disk. Cancellation wakes a paused worker and ends as Cancelled. Disposal follows completion and preserves caller-owned stores.
- Cancellation after active stepping is tested with RAM and disk storage, capture every one or four steps, and immediate or queued UI callbacks. The final event must see all point samples and line profiles, and CSV row counts must match the completed steps or captured frames. The baseline implementation failed this regression by losing the final unflushed probe batch.
- In an **empty app cache only**, an active disk store survives `FrameCache.Sweep()` and remains readable, disposal removes its temporary file, and sweep removes a deliberately created orphan. This test reports SKIP if any files already exist in the cache, to avoid deleting a user's cache. Use a dedicated clean CI account/home for guaranteed coverage.

Temporary fixtures are created under the OS temporary directory and cleaned up. The cache regression writes only its own random-named cache file. No GUI, audio device, Windows media API, or native UI automation is used. OpenCL is probed in the GPU-preferred factory test; a GPU is not required. These tests establish core portability, not native macOS GUI behavior, code signing, notarization, GPU support, or successful app launch.

For sandboxed Linux CI, point `HOME`, `DOTNET_CLI_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME`, `XDG_RUNTIME_DIR`, `NUGET_PACKAGES`, `NUGET_HTTP_CACHE_PATH`, and `NUGET_SCRATCH` to writable isolated directories. Create `XDG_RUNTIME_DIR` with mode 700. Normal macOS CI should not need these overrides; use a clean runner account for the cache test.
