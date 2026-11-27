#!/usr/bin/env python3
"""Copy read-only source models and their report; finish only project copies. Run from any directory."""
from pathlib import Path
import sys
sys.dont_write_bytecode = True

import hashlib
import shutil
import uuid
from finish_glb import ROOT, finish

MODELS = [('sprinter-firma', 'SprinterFirma', 'sprinter'), ('mali-kombi', 'MaliKombi', 'mali-kombi'), ('galeb-l3h2', 'GalebL3H2', 'galeb')]

if __name__ == '__main__':
    base = ROOT / 'Assets/Resources/Vehicles'
    template = (base / 'KombiL4H1/kombi-l4h1.glb.meta').read_text()
    import re
    for source, folder, stem in MODELS:
        src = Path.home() / 'Documents/BalkanDriver/assets-src' / source
        dest = base / folder; dest.mkdir(exist_ok=True)
        fm = base / (folder + '.meta')
        if not fm.exists(): fm.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
        shutil.copy2(src / 'izvjestaj.md', dest / 'izvjestaj.md')
        for suffix in ('', '_lod1', '_lod2'):
            name = stem + suffix + '.glb'; original = src / name
            before = hashlib.sha256(original.read_bytes()).digest()
            target = dest / name; shutil.copy2(original, target); finish(target)
            assert hashlib.sha256(original.read_bytes()).digest() == before
            meta = target.with_suffix('.glb.meta')
            if not meta.exists(): meta.write_text(re.sub(r'guid: [a-f0-9]+', 'guid: '+uuid.uuid4().hex, template, count=1))
        report_meta = dest / 'izvjestaj.md.meta'
        if not report_meta.exists(): report_meta.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
