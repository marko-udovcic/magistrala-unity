"""Marko's approved Poly Haven CC0 materials (BalkanDriver/assets-src/polyhaven-materijali, README + kontakt-list.jpg, 25. 11. 2026):
2K sources made into what the game reads, all 1024 px (seen at street level; 1k already gives ~2-4 mm per texel at their sizes):
  1. 1k copies {id}_diff_1k.jpg / _nor_gl_1k.jpg / _rough_1k.jpg of the facade array's layers in OUT1K (argument 1, a scratch
     folder) for BuildingTextures.Approved(OUT1K): white_sandstone_blocks_02 (stone), large_red_bricks (brick), concrete_wall_007
     (concrete), painted_metal_shutter (shutter louvres), stone_wall (dry-stone yard walls), clay_roof_tiles_02 (roof)
  2. Assets/Textures/PolyHaven/: the sidewalk slabs concrete_tiles_02 (StreetWalk), the podzid / parapet concrete concrete_wall_007
     (the road models' Concrete), both albedos made grey at the luminance mean of concrete_grey_d.jpg (the materials keep their
     tints); rock_boulder_dry as a URP Lit detail map on the limestone cliffs (grey around sRGB .5 = x1, luminance only)
The plaster array's two new layers (worn_plaster_wall, corrugated_iron_02) are made by zbuke.py. Run:
  PYLIB=<folder with numpy, Pillow> python3 -I Tools/zbuke/materijali.py <scratch folder for the 1k copies>"""
import sys, os
if os.environ.get('PYLIB'): sys.path.insert(0, os.environ['PYLIB'])
import numpy as np
from PIL import Image, ImageFilter

SRC = '/Users/macair/Documents/BalkanDriver/assets-src/polyhaven-materijali/'
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')
OUT = os.path.join(ROOT, 'Assets', 'Textures', 'PolyHaven') + '/'
S = 1024


def load(n, kind, mode='RGB'):
    return Image.open(f'{SRC}{n}/{n}_{kind}_2k.jpg').convert(mode).resize((S, S), Image.LANCZOS)


def grey(n, mean, contrast=1.0, flatten=.5):
    """luminance of the albedo, large-scale stains partly high-passed (they repeat visibly on long walls), at sRGB `mean`"""
    a = np.asarray(load(n, 'diff')).astype(np.float32) / 255
    m = a.reshape(-1, 3).mean(0)
    lum = .3 * a[..., 0] / m[0] + .59 * a[..., 1] / m[1] + .11 * a[..., 2] / m[2]
    lb = np.asarray(Image.fromarray(np.clip(lum * 100, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(S / 10))).astype(np.float32) / 100
    lum = (1 - flatten) * lum + flatten * lum / np.maximum(lb, .05)
    v = mean * (1 + (lum - 1) * contrast)
    return (np.clip(v, 0, 1) * 255 + .5).astype(np.uint8)


if len(sys.argv) > 1:
    o1 = sys.argv[1]; os.makedirs(o1, exist_ok=True)
    for n in ('white_sandstone_blocks_02', 'large_red_bricks', 'concrete_wall_007', 'painted_metal_shutter', 'stone_wall', 'clay_roof_tiles_02'):
        load(n, 'diff').save(f'{o1}/{n}_diff_1k.jpg', quality=95)
        load(n, 'nor_gl').save(f'{o1}/{n}_nor_gl_1k.jpg', quality=95)
        load(n, 'rough', 'L').convert('RGB').save(f'{o1}/{n}_rough_1k.jpg', quality=95)
    print('1k sources ->', o1)

# the grey concrete the street / road materials were tuned on (its sRGB luminance mean)
cg = np.asarray(Image.open(os.path.join(ROOT, 'Assets', 'Textures', 'concrete_grey_d.jpg')).convert('L')).astype(np.float32) / 255
cmean = float(cg.mean()); print('concrete_grey_d mean', round(cmean, 3))
for n, con in (('concrete_tiles_02', 1.3), ('concrete_wall_007', .8)):
    Image.fromarray(grey(n, cmean, con)).convert('RGB').save(f'{OUT}{n}_d.jpg', quality=90)
    load(n, 'nor_gl').save(f'{OUT}{n}_n.jpg', quality=92)
# detail albedo for URP Lit (_DETAIL_MULX2: albedo x detail x 2 in sRGB terms, .5 = unchanged), contrast kept moderate: the cliff's
# own texture carries the colour and the large forms
Image.fromarray(grey('rock_boulder_dry', .5, .75, .7)).convert('RGB').save(f'{OUT}rock_boulder_dry_detail.jpg', quality=90)
load('rock_boulder_dry', 'nor_gl').save(f'{OUT}rock_boulder_dry_n.jpg', quality=92)
for f in sorted(os.listdir(OUT)):
    if f.startswith(('concrete_tiles_02', 'concrete_wall_007', 'rock_boulder_dry')): print(f, os.path.getsize(OUT + f) // 1024, 'KB')
