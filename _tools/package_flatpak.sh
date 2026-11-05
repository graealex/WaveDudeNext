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

if [[ $# != 1 ]]; then
	printf 'Usage: %s OUTPUT.flatpak\n' "$0" >&2
	exit 2
fi

root=$(cd "$(dirname "$0")/.." && pwd)
output_dir=$(cd "$(dirname "$1")" && pwd)
output="$output_dir/$(basename "$1")"
if [[ -e "$output" ]]; then
	printf 'Output already exists: %s\n' "$output" >&2
	exit 1
fi

case "$(uname -m)" in
	x86_64) arch=x86_64; rid=linux-x64 ;;
	aarch64|arm64) arch=aarch64; rid=linux-arm64 ;;
	*) printf 'Unsupported architecture: %s\n' "$(uname -m)" >&2; exit 1 ;;
esac

app_id=io.github.graealex.WaveDudeNext
runtime=${WAVEDUDE_FLATPAK_RUNTIME:-25.08}
temp_root="${TMPDIR:-/tmp}/WaveDudeNext"
mkdir -p "$temp_root"
work=$(mktemp -d "$temp_root/flatpak.XXXXXXXX")
trap 'rm -rf -- "$work"' EXIT

flatpak build-init --arch="$arch" "$work/build" "$app_id" org.freedesktop.Sdk org.freedesktop.Platform "$runtime"
dotnet publish "$root/WaveDudeNext.Avalonia/WaveDudeNext.Avalonia.csproj" -c Release -r "$rid" --self-contained true \
	-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -o "$work/build/files/bin"
install -Dm644 "$root/_tools/packaging/$app_id.desktop" "$work/build/files/share/applications/$app_id.desktop"
install -Dm644 "$root/WaveDudeNext.WPF/App.png" "$work/build/files/share/icons/hicolor/128x128/apps/$app_id.png"
# Projects can reference sibling scene images and exports can write sequences beside a chosen path.
# Until those workflows use directory portal grants, they need access to the user's chosen host paths.
flatpak build-finish --command=WaveDudeNext.Avalonia --share=ipc --socket=wayland --socket=fallback-x11 --device=dri --filesystem=host "$work/build"
flatpak build-export "$work/repo" "$work/build" stable
flatpak build-bundle --arch="$arch" --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo "$work/repo" "$output" "$app_id" stable
printf 'Built %s\n' "$output"
