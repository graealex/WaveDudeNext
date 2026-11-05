"""Version-pinned supplemental dependency notices. GPL-3.0-or-later."""
import hashlib
import json
from pathlib import PurePosixPath
import shutil


def load_sources(source):
    entries = json.loads((source / 'sources.json').read_text())
    packages = {}
    for entry in entries:
        name = entry['file']
        if PurePosixPath(name).name != name or '\\' in name:
            raise ValueError('Unsafe supplemental notice filename')
        data = (source / name).read_bytes()
        if hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise ValueError(f'Supplemental notice hash mismatch: {name}')
        blob = b'blob ' + str(len(data)).encode() + b'\0' + data
        if hashlib.sha1(blob).hexdigest() != entry['git_blob_sha']:
            raise ValueError(f'Upstream Git blob mismatch: {name}')
        for package in entry['packages']:
            if package in packages:
                raise ValueError(f'Duplicate supplemental package: {package}')
            packages[package] = 'curated/' + name
    return packages


def supplement(manifest, destination, source):
    packages = load_sources(source)
    shutil.copytree(source, destination / 'curated')
    for item in manifest:
        item['supplemental_notice_files'] = [packages[item['package']]] if item['package'] in packages else []
    verify(manifest, destination, source)


def verify(manifest, destination, source):
    packages = load_sources(source)
    for original in source.iterdir():
        if original.is_file() and (destination / 'curated' / original.name).read_bytes() != original.read_bytes():
            raise ValueError(f'Missing/stale supplemental notice: {original.name}')
    for item in manifest:
        package = item['package']
        supplied = item.get('supplemental_notice_files', [])
        expected = [packages[package]] if package in packages else []
        if supplied != expected:
            raise ValueError(f'Supplemental notice mapping mismatch: {package}')
        # A nuspec or third-party attribution file is not the package's license.
        full_license = any(PurePosixPath(name).name.lower().startswith(('license', 'licence', 'copying'))
                           for name in item.get('notice_files', []))
        if not full_license and not supplied:
            raise ValueError(f'Package needs a full license notice: {package}; update third-party-notices for this exact version')
