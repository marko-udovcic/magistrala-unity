"""The railing / fence atlas of the house chunks' cutout submesh (HouseBuilder.RailMaterial, House_Rail.mat): one texture, so the
balcony railings, the yard walls' railings and the yards' wire fences (žičana ograda) share one material and add no draw call.
Texture 512 x 1024, 1 m per repeat along u (the material repeats u, clamps v); bands along v (rows from the bottom):
  v 0     .. .125   railing bars: vertical bars (an eighth of the width) in the old dark painted steel
  v .125  .. .1406  solid patches for posts / top rails: u 0 .. .5 green, .5 .. 1 galvanised
  v .1484 .. .5547  green plastic-coated chain-link (Poly Haven CC0 Modular Chainlink Fence wire alpha), 1.2 m high, 5 cm mesh
  v .5859 .. .9922  the same in galvanised steel
plus ograda_metal.png (URP Lit metallic r / smoothness a, the same bands). HouseBuilder.FenceV maps a fence to its band. Run:
  PYLIB=<folder with numpy, Pillow> python3 -I Tools/zbuke/ograda.py"""
import sys, os
if os.environ.get('PYLIB'): sys.path.insert(0, os.environ['PYLIB'])
import numpy as np
from PIL import Image

SRC = '/Users/macair/Documents/BalkanDriver/assets-src/polyhaven-zbuke/modular_chainlink_fence/modular_chainlink_fence_wire_alpha_1k.png'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Textures', 'Buildings') + '/'
W, H = 512, 1024
BARS, PATCH, G0, G1, Z0, Z1 = (0, 128), (128, 144), (152, 568), (600, 1016), None, None
BAR = (38, 38, 41); GREEN = (52, 82, 56); GALV = (150, 155, 152); GPOST = (44, 68, 48); ZPOST = (122, 126, 124)

img = np.zeros((H, W, 4), np.uint8)          # row 0 = v 0 (flipped when saved)
img[BARS[0]:BARS[1], :W // 8] = BAR + (255,)
img[BARS[0]:BARS[1], W // 8:] = BAR + (0,)
img[PATCH[0]:PATCH[1], :W // 2] = GPOST + (255,)
img[PATCH[0]:PATCH[1], W // 2:] = ZPOST + (255,)
# chain-link: 20 diamonds across 1 m (the source has 20 across its 1024 px) and 24 up the 1.2 m band
a = np.asarray(Image.open(SRC).convert('L')).astype(np.float32) / 255
rows = G0[1] - G0[0]
src = np.tile(a, (2, 1))[:int(1024 * 1.2)]
band = np.asarray(Image.fromarray((src * 255).astype(np.uint8)).resize((W, rows), Image.LANCZOS)).astype(np.float32) / 255
band = np.clip(band * 1.7, 0, 1)
band[:4] = 1; band[-4:] = 1                  # tension wires at the bottom and the top
alpha = (band * 255 + .5).astype(np.uint8)
rng = np.random.default_rng(3)
for (r0, r1), col in ((G0, GREEN), (G1, GALV)):
    tone = (.9 + .2 * rng.random((rows, W, 1))) * np.array(col, np.float32)
    img[r0:r1, :, :3] = np.clip(tone, 0, 255).astype(np.uint8)
    img[r0:r1, :, 3] = alpha
# the gaps between bands: the neighbouring band's colour, transparent (mip bleed stays the right colour)
img[PATCH[1]:G0[0], :, :3] = GREEN; img[G0[1]:G1[0], :, :3] = GALV; img[G1[1]:, :, :3] = GALV
Image.fromarray(img[::-1], 'RGBA').save(OUT + 'ograda_atlas.png', optimize=True)
# metallic (r) / smoothness (a), 8 x 128: one texel row per 8 atlas rows
m = np.zeros((128, 8, 4), np.uint8)
def band_rows(r0, r1, met, sm): m[r0 // 8:(r1 + 7) // 8, :, 0] = int(met * 255); m[r0 // 8:(r1 + 7) // 8, :, 3] = int(sm * 255)
band_rows(0, 128, .6, .4); band_rows(128, 144, .3, .35)
band_rows(144, 584, 0, .35); band_rows(584, 1024, .75, .45)
m[PATCH[0] // 8:PATCH[1] // 8, 4:, 0] = int(.75 * 255)
Image.fromarray(m[::-1], 'RGBA').save(OUT + 'ograda_metal.png')
for f in ('ograda_atlas.png', 'ograda_metal.png'): print(f, os.path.getsize(OUT + f) // 1024, 'KB')
