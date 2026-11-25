"""The facade plaster variants (Magistrala/Facade _Plaster / _PlasterN arrays, BuildingTextures.P*) and the near micro grain from the
nine Poly Haven CC0 2K plasters in BalkanDriver/assets-src/polyhaven-zbuke (README there): each albedo made grey at mean .86
(sRGB, as the facade's plaster layer: the building's tint sets the colour) with its detail and 25 % of its own chroma variation,
downscaled to 1024; normal xy + roughness in blue; flipbook strips (first layer = top row); a tenth layer is the yard hedge
(zivica.py); layers 10 / 11 (Look variant 11 / 12) are two of Marko's approved materials (BalkanDriver/assets-src/polyhaven-materijali,
25. 11. 2026): worn_plaster_wall (old peeling render) and corrugated_iron_02 (hall roofs, sRGB mean .80 = the concrete layer's linear
.605, so HouseBuilder.ConcreteTint of the orthophoto's roof colour stays right). Run:
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
# layers 10, 11: the approved materials (2K from polyhaven-materijali), grey like the plasters; (id, contrast, sRGB mean, chroma kept)
SRC2 = '/Users/macair/Documents/BalkanDriver/assets-src/polyhaven-materijali/'
for n, con, mean, chroma in (('worn_plaster_wall', .8, .86, .3), ('corrugated_iron_02', .9, .80, .1)):
    d = Image.open(f'{SRC2}{n}/{n}_diff_2k.jpg').convert('RGB').resize((S, S), Image.LANCZOS)
    a = np.asarray(d).astype(np.float32) / 255
    m = a.reshape(-1, 3).mean(0)
    lum = (.3 * a[..., 0] / m[0] + .59 * a[..., 1] / m[1] + .11 * a[..., 2] / m[2])
    from PIL import ImageFilter
    lb = np.asarray(Image.fromarray(np.clip(lum * 100, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(S / 10))).astype(np.float32) / 100
    # (the worn plaster's peeled patches ARE the large structure: only half of it flattened)
    lum = .5 * lum + .5 * lum / np.maximum(lb, .05)
    v = mean * (1 + (lum - 1) * con)
    an = a / m; ch = an / np.maximum(an.mean(-1, keepdims=True), 1e-3)
    rgb = np.clip(v[..., None] * (1 + (ch - 1) * chroma), 0, 1)
    alb.append((rgb * 255 + .5).astype(np.uint8))
    nn = np.asarray(Image.open(f'{SRC2}{n}/{n}_nor_gl_2k.jpg').convert('RGB').resize((S, S), Image.LANCZOS)).copy()
    nn[..., 2] = np.asarray(Image.open(f'{SRC2}{n}/{n}_rough_2k.jpg').convert('L').resize((S, S), Image.LANCZOS))
    nor.append(nn)
    print(n, m.round(3), 'lum sd', lum.std().round(3))
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
