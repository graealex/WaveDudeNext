#!/usr/bin/env python3
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

"""Extract the universal Vulkan libraries from the pinned LunarG macOS SDK, without running its installer."""

import hashlib
import mmap
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import zipfile

SDK_SHA256 = "163431e0203c8215286a17cf393643ce6b951f77d1b4f74ab30821a1f0beb09b"
INSTALLER_DATA = "vulkansdk-macOS-1.4.363.0.app/Contents/Resources/installer.dat"
LIBRARIES = {
    "macOS/lib/libvulkan.1.4.363.dylib": "libvulkan.1.dylib",
    "macOS/lib/libMoltenVK.dylib": "libMoltenVK.dylib",
}


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit(f"Usage: {sys.argv[0]} VULKAN_SDK.zip NEW_OUTPUT_DIR")
    sdk, output = map(Path, sys.argv[1:])
    if output.exists():
        raise SystemExit(f"Output already exists: {output}")
    with sdk.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != SDK_SHA256:
            raise SystemExit("The Vulkan SDK checksum does not match the pinned version.")
    temp_root = Path(tempfile.gettempdir()) / "WaveDudeNext"
    temp_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="vulkan-sdk-", dir=temp_root) as temp:
        work = Path(temp)
        installer = work / "installer.dat"
        with zipfile.ZipFile(sdk) as archive, archive.open(INSTALLER_DATA) as source, installer.open("wb") as destination:
            shutil.copyfileobj(source, destination)
        # Qt's offline installer concatenates 7z archives. The largest archive contains the SDK payload.
        with installer.open("rb") as source, mmap.mmap(source.fileno(), 0, access=mmap.ACCESS_READ) as data:
            signature = b"7z\xbc\xaf\x27\x1c"
            candidates = []
            position = 0
            while (start := data.find(signature, position)) >= 0:
                position = start + len(signature)
                if start + 32 > len(data):
                    continue
                offset, size = struct.unpack_from("<QQ", data, start + 12)
                length = 32 + offset + size
                if 32 <= length <= len(data) - start:
                    candidates.append((length, start))
            if not candidates:
                raise RuntimeError("The pinned SDK contains no readable payload archive.")
            length, start = max(candidates)
            archive_path = work / "payload.7z"
            with archive_path.open("wb") as archive:
                for chunk in range(start, start + length, 4 * 1024 * 1024):
                    archive.write(data[chunk:min(chunk + 4 * 1024 * 1024, start + length)])
        extracted = work / "extracted"
        subprocess.run(["7z", "x", "-y", f"-o{extracted}", str(archive_path), *LIBRARIES], check=True)
        for source in LIBRARIES:
            if not (extracted / source).is_file():
                raise RuntimeError(f"The SDK library is missing: {source}")
        output.mkdir(parents=True, exist_ok=False)
        for source, name in LIBRARIES.items():
            shutil.copy2(extracted / source, output / name)
    print(f"Extracted universal Vulkan libraries to {output}")


if __name__ == "__main__":
    main()
