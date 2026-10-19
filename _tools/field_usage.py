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

"""List every private field/const of the classes in the given .cs files with usage counts, grouped by method."""
import re, sys

STRING = re.compile(r'"(?:[^"\\]|\\.)*"')
FIELD = re.compile(r'^\t\tprivate (?:readonly |static |const )*([A-Za-z0-9_<>\[\],]+) (_[A-Za-z0-9_]+|[A-Z][A-Za-z0-9_]*)(?: =|;)', re.M)
METHOD = re.compile(r'^\t\t(?:public|private|internal|protected)[^\r\n=;]*? ([A-Za-z0-9_]+)\([^)]*\)\s*(?:=>|\r?\n\t\t\{)', re.M)

for f in sys.argv[1:]:
    src = open(f, encoding='utf-8').read()
    code = '\n'.join(l for l in src.split('\n') if not l.strip().startswith('///'))
    code = STRING.sub('""', code)
    fields = FIELD.findall(code)
    meths = [(m.group(1), m.start()) for m in METHOD.finditer(code)]
    print('=== %s: %d private fields/consts' % (f, len(fields)))
    for typ, name in fields:
        uses = []
        for m in re.finditer(r'(?<![A-Za-z0-9_.])' + re.escape(name) + r'\b', code):
            line = code[code.rfind('\n', 0, m.start()) + 1:m.end()]
            if not line.startswith('\t\tprivate'):
                uses.append(m.start())
        where = {}
        for u in uses:
            owner = [n for n, s in meths if s <= u]
            key = owner[-1] if owner else '<decl>'
            where[key] = where.get(key, 0) + 1
        flag = '<-- ' if len(uses) <= 2 or len(where) <= 1 else '    '
        print('%s%-26s %-20s uses=%-3d %s' % (flag, name, typ, len(uses), ', '.join('%s(%d)' % kv for kv in sorted(where.items()))))
