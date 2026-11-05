#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Alexander Graef <alex@graef.cc>
#
# This file is part of WaveDude Next, a 2D wave simulator.
#
# Based on the work of
#   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
#     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
#   0x23 (Diffraction Limited), author of WaveSimulator2D
#     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
#
# WaveDude Next is free software: you can redistribute it and/or modify it under
# the terms of the GNU General Public License as published by the Free Software
# Foundation, either version 3 of the License, or (at your option) any later
# version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
# <https://www.gnu.org/licenses/>.

# Contributors: Vinicius Miguel

set -euo pipefail

if [[ $# != 5 ]]; then
	printf 'Usage: %s OUTPUT.app ARM64_SDK_LIB_DIR X64_SDK_LIB_DIR VULKAN_LOADER_LICENSE MOLTENVK_LICENSE\n' "$0" >&2
	exit 2
fi
if [[ $(uname -s) != Darwin ]]; then
	printf 'A macOS host with Xcode command line tools is required.\n' >&2
	exit 1
fi
root=$(cd "$(dirname "$0")/.." && pwd)
output_dir=$(cd "$(dirname "$1")" && pwd)
output="$output_dir/$(basename "$1")"
arm_lib=$(cd "$2" && pwd)
x64_lib=$(cd "$3" && pwd)
loader_license=$4
molten_license=$5
if [[ "$output" != *.app || -e "$output" ]]; then
	printf 'Output must be a new .app path: %s\n' "$output" >&2
	exit 1
fi
for license in "$loader_license" "$molten_license"; do
	if [[ ! -f "$license" ]]; then printf 'License file missing: %s\n' "$license" >&2; exit 1; fi
done
for library in libvulkan.1.dylib libMoltenVK.dylib; do
	lipo -verify_arch arm64 "$arm_lib/$library"
	lipo -verify_arch x86_64 "$x64_lib/$library"
done

version=$(dotnet msbuild "$root/WaveDudeNext.Avalonia/WaveDudeNext.Avalonia.csproj" -nologo -getProperty:Version)
if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+){1,3}$ ]]; then
	printf 'Unsupported bundle version: %s\n' "$version" >&2
	exit 1
fi
temp_root="${TMPDIR:-/tmp}/WaveDudeNext"
mkdir -p "$temp_root"
work=$(mktemp -d "$temp_root/macos.XXXXXXXX")
trap 'rm -rf -- "$work"' EXIT

for rid in osx-arm64 osx-x64; do
	dotnet publish "$root/WaveDudeNext.Avalonia/WaveDudeNext.Avalonia.csproj" -c Release -r "$rid" --self-contained true \
		-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -o "$work/$rid"
done

app="$work/WaveDude Next.app"
contents="$app/Contents"
mkdir -p "$contents/MacOS" "$contents/Frameworks" "$contents/Resources/vulkan/icd.d" "$contents/Resources/licenses" "$work/icons.iconset"
lipo -create "$work/osx-arm64/WaveDudeNext.Avalonia" "$work/osx-x64/WaveDudeNext.Avalonia" -output "$contents/MacOS/WaveDudeNext"
copy_slice() {
	local source=$1 arch=$2 destination=$3 architectures
	architectures=$(lipo -archs "$source")
	if [[ "$architectures" == *' '* ]]; then
		lipo "$source" -thin "$arch" -output "$destination"
	else
		cp "$source" "$destination"
	fi
}
for library in libvulkan.1.dylib libMoltenVK.dylib; do
	copy_slice "$arm_lib/$library" arm64 "$work/arm64-$library"
	copy_slice "$x64_lib/$library" x86_64 "$work/x64-$library"
	if [[ "$library" == libvulkan.1.dylib ]]; then target=libvulkan.dylib; else target=$library; fi
	lipo -create "$work/arm64-$library" "$work/x64-$library" -output "$contents/Frameworks/$target"
	lipo -verify_arch arm64 x86_64 "$contents/Frameworks/$target"
done
lipo -verify_arch arm64 x86_64 "$contents/MacOS/WaveDudeNext"
# Silk.NET searches AppContext.BaseDirectory first; the link resolves to the signed loader in Frameworks.
ln -s ../Frameworks/libvulkan.dylib "$contents/MacOS/libvulkan.dylib"
cat > "$contents/Resources/vulkan/icd.d/MoltenVK_icd.json" <<'JSON'
{
  "file_format_version": "1.0.0",
  "ICD": {
    "library_path": "../../../Frameworks/libMoltenVK.dylib",
    "api_version": "1.3.0"
  }
}
JSON
cp "$root/LICENSE.md" "$contents/Resources/licenses/WaveDudeNext.md"
cp "$loader_license" "$contents/Resources/licenses/Vulkan-Loader.txt"
cp "$molten_license" "$contents/Resources/licenses/MoltenVK.txt"
for size in 16 32 128 256 512; do
	sips -z "$size" "$size" "$root/WaveDudeNext.WPF/App.png" --out "$work/icons.iconset/icon_${size}x${size}.png" >/dev/null
	double=$((size * 2))
	sips -z "$double" "$double" "$root/WaveDudeNext.WPF/App.png" --out "$work/icons.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$work/icons.iconset" -o "$contents/Resources/App.icns"
cat > "$contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>WaveDude Next</string>
  <key>CFBundleDisplayName</key><string>WaveDude Next</string>
  <key>CFBundleIdentifier</key><string>io.github.graealex.WaveDudeNext</string>
  <key>CFBundleExecutable</key><string>WaveDudeNext</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>App</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
plutil -lint "$contents/Info.plist"
# Ad-hoc signing makes the merged executable runnable for development; it is not a distribution signature.
codesign --force --sign - --timestamp=none "$contents/Frameworks/libvulkan.dylib"
codesign --force --sign - --timestamp=none "$contents/Frameworks/libMoltenVK.dylib"
codesign --force --sign - --timestamp=none "$contents/MacOS/WaveDudeNext"
codesign --force --sign - --timestamp=none "$app"
codesign --verify --deep --strict "$app"
mv "$app" "$output"
printf 'Built %s (ad-hoc signed; not notarised)\n' "$output"
