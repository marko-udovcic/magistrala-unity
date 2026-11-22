"""The facade plaster variants (Magistrala/Facade _Plaster / _PlasterN arrays, BuildingTextures.P*) and the near micro grain from the
nine Poly Haven CC0 2K plasters in BalkanDriver/assets-src/polyhaven-zbuke (README there): each albedo made grey at mean .86
(sRGB, as the facade's plaster layer: the building's tint sets the colour) with its detail and 25 % of its own chroma variation,
downscaled to 1024; normal xy + roughness in blue; flipbook strips (first layer = top row); a tenth layer is the yard hedge
(zivica.py). Run:
  PYLIB=<folder with numpy, Pillow> python3 -I Tools/zbuke/zbuke.py"""
import sys, os
if os.environ.get('PYLIB'): sys.path.insert(0, os.environ['PYLIB'])
import numpy as np
from PIL import Image
SRC = '/Users/macair/Documents/BalkanDriver/assets-src/polyhaven-zbuke/'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'Textures', 'PolyHaven', 'zbuke') + '/'
os.makedirs(OUT, exist_ok=True)
# order = layer index in the _Plaster array (Magistrala/Facade, BuildingTextures.Plasters); contrast of the grey detail
IDS = [('concrete_wall_003', .7), ('white_rough_plaster', .55), ('cracked_concrete_wall', .6), ('preconcrete_wall_001', .8),
       ('beige_wall_002', .8), ('painted_plaster_wall', .85), ('yellow_plaster', .85), ('red_plaster_weathered', .8), ('grey_plaster_02', .65)]
S = 1024
alb, nor = [], []
for i, (n, con) in enumerate(IDS):
    d = Image.open(f'{SRC}{n}/{n}_diff_2k.jpg').convert('RGB').resize((S, S), Image.LANCZOS)
    a = np.asarray(d).astype(np.float32) / 255
    m = a.reshape(-1, 3).mean(0)
    lum = (.3 * a[..., 0] / m[0] + .59 * a[..., 1] / m[1] + .11 * a[..., 2] / m[2])
    # the photos' big stains repeat visibly every tile on a long wall: 65 % of the luminance high-passed (structure over ~0.6 m
    # removed; Magistrala/Facade adds its own non-repeating blotches), 35 % as photographed
    from PIL import ImageFilter
    lb = np.asarray(Image.fromarray(np.clip(lum * 100, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(S / 10))).astype(np.float32) / 100
    lum = .35 * lum + .65 * lum / np.maximum(lb, .05)
    # large-scale luminance flattened a little (the shader adds its own blotches), detail kept at `con`
    v = .86 * (1 + (lum - 1) * con)
    # a little of the photo's own chroma (patches of an older coat), 25 %
    an = a / m; ch = an / np.maximum(an.mean(-1, keepdims=True), 1e-3)
    rgb = np.clip(v[..., None] * (1 + (ch - 1) * .25), 0, 1)
    alb.append((rgb * 255 + .5).astype(np.uint8))
    nn = np.asarray(Image.open(f'{SRC}{n}/{n}_nor_gl_2k.jpg').convert('RGB').resize((S, S), Image.LANCZOS)).copy()
    ro = np.asarray(Image.open(f'{SRC}{n}/{n}_rough_2k.jpg').convert('L').resize((S, S), Image.LANCZOS))
    nn[..., 2] = ro
    nor.append(nn)
    print(n, m.round(3), 'lum sd', lum.std().round(3))
# layer 9 (Look variant 10): the trimmed yard hedge (zivica.py: Poly Haven Periwinkle Plant leaves, colour kept)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from zivica import hedge
ha, hn = hedge(S); alb.append(ha); nor.append(hn)
# flipbook strip: first layer = top row
Image.fromarray(np.concatenate(alb, 0)).save(OUT + 'zbuke_albedo.jpg', quality=90)
Image.fromarray(np.concatenate(nor, 0)).save(OUT + 'zbuke_normal.jpg', quality=92)
# micro grain (near detail): grey_plaster_02 high-pass, 512 px, tiles at 0.45 m; r = luminance detail (.5 = none), g / b = normal xy
g = np.asarray(Image.open(SRC + 'grey_plaster_02/grey_plaster_02_diff_2k.jpg').convert('L')).astype(np.float32) / 255
g = np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((512, 512), Image.LANCZOS)).astype(np.float32) / 255
from PIL import ImageFilter
blur = np.asarray(Image.fromarray((g * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(12))).astype(np.float32) / 255
hp = g - blur; hp = hp / (np.abs(hp).mean() * 4 + 1e-6)
r = np.clip(.5 + hp * .5, 0, 1)
gn = np.asarray(Image.open(SRC + 'grey_plaster_02/grey_plaster_02_nor_gl_2k.jpg').convert('RGB').resize((512, 512), Image.LANCZOS))
mic = np.stack([(r * 255).astype(np.uint8), gn[..., 0], gn[..., 1]], -1)
Image.fromarray(mic).save(OUT + 'mikro.png')
for f in os.listdir(OUT): print(f, os.path.getsize(OUT + f) // 1024, 'KB')
