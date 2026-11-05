#!/usr/bin/env python3
"""Static bundle/ZIP checks only; never executes a binary. GPL-3.0-or-later."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import plistlib
import re
import stat
import struct
import zipfile
from third_party_notices import verify as verify_notices


def require(condition, message):
    if not condition:
        raise ValueError(message)


def architectures(data):
    """Return Mach-O CPU types, validating fat table bounds and slice headers."""
    magic = data[:4]
    thin = {b'\xcf\xfa\xed\xfe': '<', b'\xfe\xed\xfa\xcf': '>',
            b'\xce\xfa\xed\xfe': '<', b'\xfe\xed\xfa\xce': '>'}
    if magic in thin:
        require(len(data) >= 28, 'Truncated Mach-O header')
        return {struct.unpack_from(thin[magic] + 'I', data, 4)[0]}
    fat = {b'\xca\xfe\xba\xbe': ('>', False), b'\xbe\xba\xfe\xca': ('<', False),
           b'\xca\xfe\xba\xbf': ('>', True), b'\xbf\xba\xfe\xca': ('<', True)}
    require(magic in fat and len(data) >= 8, 'Not a recognized Mach-O file')
    endian, wide = fat[magic]
    count = struct.unpack_from(endian + 'I', data, 4)[0]
    entry_size = 32 if wide else 20
    require(0 < count <= 64 and 8 + count * entry_size <= len(data), 'Invalid universal architecture table')
    result = set()
    for index in range(count):
        position = 8 + index * entry_size
        cpu = struct.unpack_from(endian + 'I', data, position)[0]
        offset, size = struct.unpack_from(endian + ('QQ' if wide else 'II'), data, position + 8)
        require(offset >= 8 + count * entry_size and size >= 28 and offset + size <= len(data), 'Invalid Mach-O slice bounds')
        payload = data[offset:offset + size]
        require(payload[:4] in thin, 'Universal slice must be a thin Mach-O image')
        require(architectures(payload) == {cpu}, 'Universal table and slice CPU disagree')
        result.add(cpu)
    return result


def verify(rid, version):
    require(re.fullmatch(r'\d+\.\d+\.\d+', version), 'Version must contain three numeric components')
    root = Path(__file__).resolve().parent.parent
    app = root / 'artifacts' / rid / 'WaveDude Next.app'
    resources = app / 'Contents' / 'Resources'
    require(app.is_dir() and not app.is_symlink(), 'Missing or symlinked app bundle')
    for path in app.rglob('*'):
        require(not path.is_symlink(), f'Unexpected symlink in bundle: {path}')
    info = plistlib.loads((app / 'Contents' / 'Info.plist').read_bytes())
    require(info.get('CFBundleExecutable') == 'WaveDudeNext.Desktop', 'Unexpected executable name')
    require(info.get('CFBundlePackageType') == 'APPL', 'Bundle type is not APPL')
    require(info.get('CFBundleIdentifier') == 'org.wavedudenext.community.desktop', 'Unexpected bundle identifier')
    require(info.get('CFBundleVersion') == version and info.get('CFBundleShortVersionString') == version, 'Bundle version mismatch')
    require(info.get('CFBundleIconFile') == 'App.icns', 'Missing icon declaration')
    executable = app / 'Contents' / 'MacOS' / info['CFBundleExecutable']
    require(executable.is_file() and executable.stat().st_mode & 0o111, 'Apphost missing or not executable')
    expected = {'osx-arm64': 0x0100000c, 'osx-x64': 0x01000007}[rid]
    require(architectures(executable.read_bytes()) == {expected}, 'Apphost architecture does not match runtime')
    native_files = list(app.rglob('*.dylib'))
    require(native_files, 'No native runtime libraries found')
    for native in native_files:
        require(expected in architectures(native.read_bytes()), f'Native library lacks expected architecture: {native.name}')
    icon = (resources / 'App.icns').read_bytes()
    require(len(icon) >= 40 and icon[:4] == b'icns' and icon[8:12] == b'ic07', 'Invalid ICNS/icon type')
    require(struct.unpack_from('>I', icon, 4)[0] == len(icon), 'ICNS file length mismatch')
    require(struct.unpack_from('>I', icon, 12)[0] == len(icon) - 8, 'ICNS chunk length mismatch')
    require(icon[16:24] == b'\x89PNG\r\n\x1a\n' and icon[28:32] == b'IHDR', 'ICNS does not contain PNG')
    require(struct.unpack_from('>II', icon, 32) == (128, 128), 'Icon is not 128x128')
    require(icon[16:] == (root / 'WaveDudeNext.WPF' / 'App.png').read_bytes(), 'Upstream icon artwork differs')
    for name in ('LICENSE.md', 'README.md', 'README.macOS.md', 'MANUAL.md'):
        require((resources / name).read_bytes() == (root / name).read_bytes(), f'Missing/stale source notice: {name}')
    require(any((resources / 'Examples').iterdir()), 'Bundled examples missing')
    notices = resources / 'ThirdPartyNotices'
    manifest = json.loads((notices / 'packages.json').read_text())
    require(isinstance(manifest, list) and manifest, 'Missing dependency manifest')
    verify_notices(manifest, notices, root / 'third-party-notices')
    for package in manifest:
        require(isinstance(package.get('package'), str) and '/' in package['package'], 'Invalid package identity')
        for notice in package.get('notice_files', []):
            relative = PurePosixPath(package['package'].lower()) / PurePosixPath(notice)
            require(not relative.is_absolute() and '..' not in relative.parts, 'Unsafe dependency notice path')
            require((notices / relative).is_file(), f'Dependency notice missing: {relative}')
    for name in ('LICENSE.txt', 'ThirdPartyNotices.txt'):
        require((notices / 'dotnet' / name).is_file(), f'.NET notice missing: {name}')
    archive = root / 'artifacts' / f'WaveDudeNext-macOS-{version}-{rid}.zip'
    with zipfile.ZipFile(archive) as zipped:
        names = set()
        for member in zipped.infolist():
            path = PurePosixPath(member.filename)
            require(not path.is_absolute() and '..' not in path.parts and '\\' not in member.filename,
                    f'Unsafe ZIP path: {member.filename}')
            require(path.parts and path.parts[0] == app.name, 'ZIP entry outside application root')
            require(member.filename not in names, 'Duplicate ZIP entry')
            names.add(member.filename)
            mode = member.external_attr >> 16
            require(not stat.S_ISLNK(mode), 'Unexpected ZIP symlink')
            if member.is_dir():
                continue
            local = app.parent / path
            require(local.is_file(), f'ZIP contains unexpected file: {path}')
            require(hashlib.sha256(zipped.read(member)).digest() == hashlib.sha256(local.read_bytes()).digest(),
                    f'ZIP and bundle differ: {path}')
            if local == executable:
                require(mode & 0o111, 'ZIP lost executable permission')
        expected_names = {str(PurePosixPath(app.name) / file.relative_to(app)) for file in app.rglob('*') if file.is_file()}
        require(expected_names <= names, 'ZIP is missing bundle files')
    print(f'PASS {rid}: plist, apphost, {len(native_files)} native libraries, icon, licenses/notices, ZIP paths/content/mode. Static validation only; no macOS execution.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', required=True, choices=('osx-arm64', 'osx-x64'))
    parser.add_argument('--version', default='0.1.3')
    args = parser.parse_args()
    try:
        verify(args.rid, args.version)
    except (ValueError, OSError, KeyError, struct.error, zipfile.BadZipFile) as error:
        parser.exit(1, f'STATIC VALIDATION FAILED: {error}\n')
