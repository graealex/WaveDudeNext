#!/usr/bin/env python3
"""Archive this modified source tree, including uncommitted port files. GPL-3.0-or-later."""
from pathlib import Path
import tarfile

root = Path(__file__).resolve().parent.parent
artifacts = root / 'artifacts'
artifacts.mkdir(exist_ok=True)
archive = artifacts / 'WaveDudeNext-macOS-source.tar.gz'
excluded = {'.git', 'bin', 'obj', 'artifacts', '__pycache__', '.DS_Store', '.vs', '.idea'}
with tarfile.open(archive, 'w:gz') as output:
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if any(part in excluded for part in relative.parts):
            continue
        # Do not follow symlinks outside the source checkout.
        if path.is_file() and not path.is_symlink():
            output.add(path, arcname=str(Path('WaveDudeNext-macOS-source') / relative), recursive=False)
print(archive)
