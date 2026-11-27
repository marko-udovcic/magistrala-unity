# Priprema tekstura za kaštele: Poly Haven CC0 (assets-src/polyhaven-materijali) -> 1K, tonirano u dalmatinski vapnenac.
import sys
from PIL import Image, ImageEnhance, ImageOps
SRC = '/Users/macair/Documents/BalkanDriver/assets-src/polyhaven-materijali/'
OUT = '/Users/macair/Documents/BalkanDriver/assets-src/kasteli/_teksture/'
def prep(name, out, sat, bright, contrast, tint=None, size=1024):
    d = Image.open(f'{SRC}{name}/{name}_diff_2k.jpg').convert('RGB').resize((size, size), Image.LANCZOS)
    d = ImageEnhance.Color(d).enhance(sat); d = ImageEnhance.Brightness(d).enhance(bright); d = ImageEnhance.Contrast(d).enhance(contrast)
    if tint:
        r, g, b = d.split(); r = r.point(lambda v: min(255, v * tint[0])); g = g.point(lambda v: min(255, v * tint[1])); b = b.point(lambda v: min(255, v * tint[2])); d = Image.merge('RGB', (r, g, b))
    d.save(OUT + out + '_d.jpg', quality=88)
    n = Image.open(f'{SRC}{name}/{name}_nor_gl_2k.jpg').convert('RGB').resize((size, size), Image.LANCZOS)
    n.save(OUT + out + '_n.jpg', quality=92)
prep('sandstone_blocks_05', 'kamen', .45, 1.08, 1.05, (1.0, 1.0, 1.02))      # klesani kvadri (kule, palače)
prep('stone_wall', 'lomljeni', .45, 1.05, 1.0, (1.0, 1.0, 1.03))             # lomljeni kamen (kuće, obodni zidovi)
prep('clay_roof_tiles_02', 'crijep', .95, 1.0, 1.0)                           # kupa kanalica
prep('worn_plaster_wall', 'zbuka', .6, 1.05, 1.0)                              # stara žbuka
print('ok')
