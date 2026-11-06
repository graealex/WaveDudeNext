# Supplemental third-party license notices

These files supplement restored NuGet packages that provide an SPDX expression in their `.nuspec` but omit the full license/copyright text. They are copied into the app's `Contents/Resources/ThirdPartyNotices/curated` directory and included in the corresponding source archive. Original package `.nuspec` files, hashes and any shipped license/third-party notice files are also retained separately.

`sources.json` maps exact package versions to verbatim license texts, official source URLs, upstream Git blob hashes and SHA-256 hashes. Files were retrieved read-only on 2026-11-05. Five source groups are pinned to the repository commits recorded by the restored NuGet metadata. MicroCom.Runtime 0.11.0 omits its build commit; its pinned historical `Directory.Build.props` declares version 0.11.0 and the copyright matches the NuGet package. This establishes matching version/license evidence, not proof of its exact build commit.

- Avalonia 11.3.22 and its same-version packages: MIT, AvaloniaUI OÜ.
- Avalonia.BuildServices 11.3.2: MIT, AvaloniaUI OU. It is a development dependency; retaining its notice also documents the build inputs.
- MicroCom.Runtime 0.11.0: MIT, Nikita Tsukanov.
- NLog 6.2.1: BSD-3-Clause, the named NLog contributors. The original license's 2004–2024 copyright is preserved unchanged; the package's newer copyright statement remains in its `.nuspec`.
- Tmds.DBus.Protocol 0.21.3: MIT, Alp Toker, other contributors and Tom Deseyn.
- System.Numerics.Tensors 10.0.12: MIT, .NET Foundation and Contributors. The license at its exact package source commit (`dotnet/dotnet`, `src/runtime/LICENSE.TXT`) is byte-identical to the .NET `LICENSE.txt` already bundled with this port. The existing notice therefore contains those base license terms and copyright; this extra copy makes the package mapping explicit. Its package-provided `THIRD-PARTY-NOTICES.TXT` remains separately retained.

Packaging and static verification require complete license text for every restored package. A package with only `.nuspec` metadata or third-party attribution must have an exact-version supplemental mapping; upgrades fail this check until reviewed. This focused notice audit does not certify every possible redistribution obligation or replace review of changed dependencies. Packages present in the restore graph but unused on macOS are conservatively retained in the notice inventory.
