"""The trimmed yard hedge (živica: laurel / pittosporum / box) as one more layer of the facade plaster array (Magistrala/Facade
_Plaster layer 9, Look variant 10, HouseBuilder Slot.Hedge): the green leaves of Poly Haven's CC0 Periwinkle Plant
(BalkanDriver/assets-src/polyhaven-zbuke/periwinkle_plant, 1k diff + opacity) stamped thousands of times, in random
directions, layer over layer (deeper = darker) into a 1024 px tile of 1 m; normal from the stacked leaves' heights, roughness
in blue (glossy leaves, rough gaps). Colour is kept (the hedge's vertex tint only varies it a little). Used by zbuke.py."""
import sys, os
if os.environ.get("PYLIB"): sys.path.insert(0, os.environ["PYLIB"])
import numpy as np
from PIL import Image

SRC = '/Users/macair/Documents/BalkanDriver/assets-src/polyhaven-zbuke/periwinkle_plant/'


def leaves():
    d = np.asarray(Image.open(SRC + 'periwinkle_plant_diff_1k.png').convert('RGB')).astype(np.float32) / 255
    a = np.asarray(Image.open(SRC + 'periwinkle_plant_opacity_1k.png').convert('L')).astype(np.float32) / 255
    H, W = a.shape
    lab = np.zeros((H, W), np.int32); cur = 0
    m = a > .5
    # connected components (4-neighbour flood fill, the atlas is small)
    for y in range(H):
        for x in range(W):
            if not m[y, x] or lab[y, x]: continue
            cur += 1; st = [(y, x)]; lab[y, x] = cur
            while st:
                cy, cx = st.pop()
                for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                    if 0 <= ny < H and 0 <= nx < W and m[ny, nx] and not lab[ny, nx]: lab[ny, nx] = cur; st.append((ny, nx))
    out = []
    for k in range(1, cur + 1):
        ys, xs = np.nonzero(lab == k)
        if len(ys) < 1500: continue
        y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        if x0 < 60: continue                                   # the bark strip on the atlas' left edge
        c = d[ys, xs].mean(0)
        if not (c[1] > c[0] * 1.08 and c[1] > c[2] * 1.25): continue  # green leaves only (no flowers, yellow leaves, stems)
        fill = len(ys) / ((y1 - y0) * (x1 - x0))
        if fill < .25: continue                                # thin stems
        rgba = np.concatenate([d[y0:y1, x0:x1], (a[y0:y1, x0:x1] * (lab[y0:y1, x0:x1] == k))[..., None]], -1)
        out.append(Image.fromarray((rgba * 255).astype(np.uint8), 'RGBA'))
    return out


def hedge(S=1024, seed=7):
    rng = np.random.default_rng(seed)
    src = leaves()
    print('hedge leaves:', len(src), [l.size for l in src])
    alb = np.zeros((S, S, 3), np.float32); alb[:] = (.035, .05, .025)   # the dark inside of the hedge
    hgt = np.zeros((S, S), np.float32)
    rough = np.full((S, S), .9, np.float32)
    passes = 7
    for p in range(passes):
        depth = p / (passes - 1)                                # 0 deep inside .. 1 the outer skin
        n = int(650 + 350 * depth)
        shade = .32 + .68 * depth ** 1.3
        for _ in range(n):
            l = src[rng.integers(len(src))]
            L = 45 + 45 * rng.random()                         # leaf length 5.5-10 cm at 1 m per tile
            sc = L / max(l.size)
            im = l.resize((max(4, int(l.size[0] * sc)), max(4, int(l.size[1] * sc))), Image.BILINEAR)
            im = im.rotate(rng.random() * 360, Image.BILINEAR, expand=True)
            q = np.asarray(im).astype(np.float32) / 255
            al = q[..., 3:4]
            if al.max() < .5: continue
            h, w = al.shape[:2]
            y, x = rng.integers(S), rng.integers(S)
            ys = (y + np.arange(h)) % S; xs = (x + np.arange(w)) % S
            ix = np.ix_(ys, xs)
            # per-leaf tone: some lighter young growth, some dull / dusty; a slight hue drift
            t = shade * (.8 + .4 * rng.random())
            hue = np.array([.92 + .16 * rng.random(), 1, .9 + .2 * rng.random()], np.float32)
            col = q[..., :3] * t * hue
            dome = al[..., 0] * (.4 + .6 * np.sqrt(np.clip(al[..., 0], 0, 1)))
            hh = depth + .15 * dome
            mask = (al[..., 0] > .5) & (hh > hgt[ix])
            a3 = mask[..., None]
            alb[ix] = np.where(a3, col, alb[ix])
            hgt[ix] = np.where(mask, hh, hgt[ix])
            rough[ix] = np.where(mask, .45 + .2 * rng.random(), rough[ix])
    # sRGB-ish leaf colours stay as they are; the whole layer a touch darker and less yellow (sun-lit Mediterranean hedges)
    alb = np.clip(alb * np.array([.6, .72, .56], np.float32), 0, 1)
    # normal from the height (wrap-around gradient), OpenGL y+
    gx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 4
    gy = (np.roll(hgt, 1, 0) - np.roll(hgt, -1, 0)) * 4
    nz = 1 / np.sqrt(1 + gx * gx + gy * gy)
    nor = np.stack([(-gx * nz * .5 + .5), (-gy * nz * .5 + .5), rough], -1)
    return (alb * 255 + .5).astype(np.uint8), (np.clip(nor, 0, 1) * 255 + .5).astype(np.uint8)


if __name__ == '__main__':
    a, n = hedge()
    import sys; o = sys.argv[1]; Image.fromarray(a).save(o + '/zivica_alb.png'); Image.fromarray(n).save(o + '/zivica_nor.png')
