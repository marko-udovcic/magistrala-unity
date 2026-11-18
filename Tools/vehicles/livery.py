# Livery decal sheets for the Tripo traffic cars (Assets/Editor/TripoCarImporter.cs, shader Magistrala/TripoCar).
# One 2048 x 1024 RGBA sheet per livery, straight alpha, colour bled under transparent texels (no dark mip fringes):
#   bottom half  (Unity v 0 .. 0.5)      side band, u = front -> rear of the car (projected on both sides the same way)
#   top-left     (u 0 .. 0.5, v .75 .. 1) the wordmark, transparent background (sides, bonnet, rear; mirrored by the shader if asked)
#   top-right    (u .5 .. 1,  v .75 .. 1) the wordmark on white (the light bar's centre panel)
#   v .5 .. .75                            unused
# Font: Inter (OFL, Assets/Resources/Fonts). Rendered at 2x and downsampled.
# Usage: PYTHONPATH=<pillow> python3 Tools/vehicles/livery.py   (writes Assets/Textures/Vehicles/Livery/<name>.png)
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FONT = os.path.join(ROOT, "Assets/Resources/Fonts/Inter-SemiBold.ttf")
OUT = os.path.join(ROOT, "Assets/Textures/Vehicles/Livery")
W, H, S = 2048, 1024, 2

LIVERIES = {
    # Croatian police: white car, dark-blue band with a thin yellow line, blue wordmark
    "policija": dict(text="POLICIJA", ink=(22, 52, 140), band=(22, 52, 140), line=(244, 200, 0), line_w=.13, line_y=.62, taper=.07),
    # ambulance: yellow car, red band, red wordmark (the bonnet one mirrored by the shader: ANTIH)
    "hitna": dict(text="HITNA", ink=(200, 24, 28), band=(200, 24, 28), line=None, line_w=0, line_y=0, taper=.04),
    # taxi: no band, dark wordmark
    "taksi": dict(text="TAXI", ink=(25, 25, 28), band=None, line=None, line_w=0, line_y=0, taper=0),
}


def wordmark(img, box, text, ink, bg=None, skew=.14, stroke=3):
    """text centred in box (x0, y0, x1, y1), slanted like the concept's italic, filling ~86 % of the box width"""
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0, y1 - y0
    layer = Image.new("RGBA", (bw, bh), (0, 0, 0, 0) if bg is None else bg + (255,))
    size = int(bh * .78)
    while True:
        f = ImageFont.truetype(FONT, size)
        l, t, r, b = f.getbbox(text, stroke_width=stroke * S)
        if r - l <= bw * .86 or size < 10: break
        size -= 4
    d = ImageDraw.Draw(layer)
    tw, th = r - l, b - t
    d.text(((bw - tw) / 2 - l + skew * th / 2, (bh - th) / 2 - t), text, font=f, fill=ink + (255,), stroke_width=stroke * S, stroke_fill=ink + (255,))
    layer = layer.transform(layer.size, Image.AFFINE, (1, skew, -skew * bh / 2, 0, 1, 0), resample=Image.BICUBIC)
    img.alpha_composite(layer, (x0, y0))


def build(name, L):
    w, h = W * S, H * S
    # transparent texels carry the colour of their region (ink / band): mips and the resize don't fringe dark
    img = Image.new("RGBA", (w, h), L["ink"] + (0,))
    d = ImageDraw.Draw(img)
    d.rectangle((0, h // 2, w, h), fill=(L["band"] or L["ink"]) + (0,))
    # side band (bottom half: PIL rows h/2 .. h), front at the left; a slanted cut at the front end
    if L["band"]:
        y0, y1 = h // 2, h
        t = int(L["taper"] * w)
        d.polygon([(t, y0), (w, y0), (w, y1), (0, y1)], fill=L["band"] + (255,))
        if L["line"]:
            ly = y0 + int((1 - L["line_y"]) * (y1 - y0)); lw = int(L["line_w"] * (y1 - y0))
            # the line follows the slanted front cut
            fx = lambda yy: t * (y1 - yy) / (y1 - y0)
            d.polygon([(fx(ly - lw / 2), ly - lw / 2), (w, ly - lw / 2), (w, ly + lw / 2), (fx(ly + lw / 2), ly + lw / 2)], fill=L["line"] + (255,))
    wordmark(img, (0, 0, w // 2, h // 4), L["text"], L["ink"])
    wordmark(img, (w // 2, 0, w, h // 4), L["text"], L["ink"], bg=(246, 246, 244), skew=0)
    img = img.resize((W, H), Image.LANCZOS)
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name + ".png"))
    print("livery", name)


if __name__ == "__main__":
    for n, L in LIVERIES.items(): build(n, L)
