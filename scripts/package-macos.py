#!/usr/bin/env python3
"""Bundle an already published desktop app. GPL-3.0-or-later."""
import argparse
import json
from pathlib import Path
import plistlib
import re
import shutil
import struct
import zipfile
from third_party_notices import supplement

parser = argparse.ArgumentParser()
parser.add_argument('--rid', required=True, choices=('osx-arm64', 'osx-x64'))
parser.add_argument('--version', required=True)
args = parser.parse_args()
if not re.fullmatch(r'\d+\.\d+\.\d+', args.version):
    parser.error('--version must be a three-part numeric bundle version')
root = Path(__file__).resolve().parent.parent
publish = root / 'artifacts' / 'publish' / args.rid
app = root / 'artifacts' / args.rid / 'WaveDude Next.app'
if not (publish / 'WaveDudeNext.Desktop').is_file():
    parser.error('The expected published WaveDudeNext.Desktop apphost is missing')
if app.exists():
    shutil.rmtree(app)
macos = app / 'Contents' / 'MacOS'
resources = app / 'Contents' / 'Resources'
resources.mkdir(parents=True)
shutil.copytree(publish, macos)
# ic07 stores a 128x128 PNG directly; preserve the upstream artwork unchanged.
png = (root / 'WaveDudeNext.WPF' / 'App.png').read_bytes()
if png[:8] != b'\x89PNG\r\n\x1a\n' or png[12:16] != b'IHDR' or struct.unpack('>II', png[16:24]) != (128, 128):
    parser.error('App.png must be a 128x128 PNG for the ic07 icon representation')
icon_chunk = b'ic07' + struct.pack('>I', len(png) + 8) + png
(resources / 'App.icns').write_bytes(b'icns' + struct.pack('>I', len(icon_chunk) + 8) + icon_chunk)
(macos / 'WaveDudeNext.Desktop').chmod(0o755)
info = {
    'CFBundleName': 'WaveDude Next',
    'CFBundleDisplayName': 'WaveDude Next',
    'CFBundleIdentifier': 'org.wavedudenext.community.desktop',
    'CFBundleExecutable': 'WaveDudeNext.Desktop',
    'CFBundlePackageType': 'APPL',
    'CFBundleIconFile': 'App.icns',
    'CFBundleShortVersionString': args.version,
    'CFBundleVersion': args.version,
    'NSHighResolutionCapable': True,
    'LSMinimumSystemVersion': '14.0',
    'NSHumanReadableCopyright': 'Copyright (C) 2026 Alexander Gräf and contributors. GPL-3.0-or-later.',
}
with (app / 'Contents' / 'Info.plist').open('wb') as stream:
    plistlib.dump(info, stream)
(app / 'Contents' / 'PkgInfo').write_bytes(b'APPL????')
for name in ('LICENSE.md', 'README.md', 'README.macOS.md', 'MANUAL.md'):
    shutil.copy2(root / name, resources / name)
shutil.copytree(root / 'Examples', resources / 'Examples')
# Retain license/notice material shipped by restored third-party NuGet packages.
assets_path = root / 'WaveDudeNext.Desktop' / 'obj' / 'project.assets.json'
assets = json.loads(assets_path.read_text())
license_dir = resources / 'ThirdPartyNotices'
license_dir.mkdir()
manifest = []
for key, data in sorted(assets.get('libraries', {}).items()):
    if data.get('type') != 'package':
        continue
    package_path = data.get('path', key.lower())
    copied = []
    for folder in assets.get('packageFolders', {}):
        package = Path(folder) / package_path
        if not package.is_dir():
            continue
        for file in package.rglob('*'):
            if file.is_file() and (any(term in file.name.lower() for term in ('license', 'notice', 'copying', 'copyright')) or file.suffix == '.nuspec'):
                relative = file.relative_to(package)
                target = license_dir / package_path / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(file, target)
                copied.append(str(relative))
        break
    manifest.append({'package': key, 'sha512': data.get('sha512'), 'notice_files': copied})
# The installed SDK also carries .NET runtime attribution and license notices.
dotnet = shutil.which('dotnet')
if dotnet:
    dotnet_root = Path(dotnet).resolve().parent
    for name in ('LICENSE.txt', 'ThirdPartyNotices.txt'):
        source = dotnet_root / name
        if source.is_file():
            target = license_dir / 'dotnet' / name
            target.parent.mkdir(exist_ok=True)
            shutil.copy2(source, target)
# Full, version-pinned licenses supplement packages that ship only SPDX metadata.
supplement(manifest, license_dir, root / 'third-party-notices')
(license_dir / 'packages.json').write_text(json.dumps(manifest, indent=2) + '\n')
archive = root / 'artifacts' / f'WaveDudeNext-macOS-{args.version}-{args.rid}.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as output:
    for file in sorted(app.rglob('*')):
        output.write(file, file.relative_to(app.parent))
print(archive)
