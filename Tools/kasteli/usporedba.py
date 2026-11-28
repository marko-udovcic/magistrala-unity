# usporedba.py <izlaz.png> <ref.jpg|-> <render.png> [...]: referenca lijevo, renderi desno, visina 480
import sys
from PIL import Image
out, *ims = sys.argv[1:]
H = 480; row = []
for p in ims:
    if p == '-': continue
    im = Image.open(p).convert('RGB'); row.append(im.resize((int(im.width * H / im.height), H)))
W = sum(i.width for i in row); S = Image.new('RGB', (W, H))
x = 0
for i in row: S.paste(i, (x, 0)); x += i.width
S.save(out)
