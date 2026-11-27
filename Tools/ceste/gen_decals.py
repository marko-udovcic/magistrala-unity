"""Procedural CC0 decals for Magistrala roads: pukotine, ulje, prasina, sahta, resetka.
Usage: python gen_decals.py <outdir>
"""
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1]
rng = np.random.default_rng(1998)


def fft_noise(n, beta, seed):
    """Tileable gaussian random field with 1/f^beta spectrum, normalised to 0..1."""
    r = np.random.default_rng(seed)
    f = np.fft.fftfreq(n)
    fx, fy = np.meshgrid(f, f)
    k = np.sqrt(fx * fx + fy * fy)
    k[0, 0] = 1
    amp = 1.0 / k ** beta
    amp[0, 0] = 0
    ph = r.normal(size=(n, n)) + 1j * r.normal(size=(n, n))
    x = np.real(np.fft.ifft2(amp * ph))
    x = (x - x.min()) / (x.max() - x.min())
    return x


def blur(a, rad):
    """Periodic gaussian blur via FFT."""
    n0, n1 = a.shape
    fy = np.fft.fftfreq(n0)[:, None]
    fx = np.fft.fftfreq(n1)[None, :]
    g = np.exp(-2 * (np.pi * rad) ** 2 * (fx * fx + fy * fy))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def normal_from_height(h, strength):
    """OpenGL (Y+) tangent-space normal map from height (row 0 = top of image)."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx = -dx * strength
    ny = dy * strength  # v points up, rows point down
    nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8), 'RGB')


def save_rgba(rgb, a, name):
    arr = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)[..., None]])
    Image.fromarray((arr * 255).astype(np.uint8), 'RGBA').save(os.path.join(out, name), optimize=True)


# ---------- pukotine: tileable crack-sealing tar lines, 2048 px = 8 m ----------
N = 2048
lines = np.zeros((N, N))
for seed, w, thr in [(11, 1.0, 0.016), (12, 0.8, 0.013)]:
    nz = fft_noise(N, 2.25, seed)
    ridge = np.abs(nz - 0.5)  # zero crossing = line
    # width in noise units -> soft line
    lines = np.maximum(lines, w * np.clip(1 - ridge / thr, 0, 1))
# sparsity: only keep lines where a low-frequency mask allows
mask = fft_noise(N, 2.6, 21)
mask = np.clip((mask - 0.45) / 0.15, 0, 1)
width_var = 0.6 + 0.4 * fft_noise(N, 1.2, 22)
tar = np.clip(lines * mask * width_var * 1.6, 0, 1)
tar = blur(tar, 1.6)
tar = np.clip(tar * 1.3, 0, 1)
grain = fft_noise(N, 0.6, 23)
rgb = np.zeros((N, N, 3)) + np.array([0.055, 0.055, 0.06])
rgb += (grain[..., None] - 0.5) * 0.04
save_rgba(rgb, tar * 0.95, 'pukotine_d.png')

# ---------- ulje: oil stain, 1024 px = 1.5 m ----------
N = 1024
yy, xx = np.mgrid[0:N, 0:N] / N - 0.5
r = np.sqrt(xx * xx + yy * yy)
shape = fft_noise(N, 2.2, 31) - 0.5
blob = np.clip((0.33 + shape * 0.35 - r) / 0.12, 0, 1)
drops = np.zeros((N, N))
for i in range(18):
    cx, cy = rng.normal(0, 0.16, 2)
    rr = rng.uniform(0.004, 0.014)
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    drops = np.maximum(drops, np.clip((rr - d) / 0.006, 0, 1) * rng.uniform(0.5, 1))
core = np.clip((0.18 + shape * 0.3 - r) / 0.15, 0, 1)
detail = fft_noise(N, 1.0, 32)
a = np.clip(blob * (0.35 + 0.25 * detail) + core * 0.3 + drops * 0.45, 0, 0.8)
edge = blur(np.clip(blob, 0, 1), 3)
a = a * (1 - 0.5 * np.exp(-((r - 0.0) ** 2) / 0.4))  # keep
a = np.where(r > 0.49, 0, a)
rgb = np.zeros((N, N, 3)) + np.array([0.06, 0.05, 0.04])
rgb += (detail[..., None] - 0.5) * np.array([0.05, 0.04, 0.06])  # faint iridescent variation
save_rgba(rgb, a, 'ulje_d.png')

# ---------- prasina: tileable dust/sand, 2048 px = 4 m ----------
N = 2048
low = fft_noise(N, 2.4, 41)
mid = fft_noise(N, 1.5, 42)
fine = fft_noise(N, 0.3, 43)
a = np.clip((low * 0.7 + mid * 0.3 - 0.42) / 0.3, 0, 1)
a = a * (0.75 + 0.25 * fine)
col = np.array([0.74, 0.68, 0.57])
rgb = col + (mid[..., None] - 0.5) * 0.10 + (fine[..., None] - 0.5) * 0.08
save_rgba(rgb, a * 0.85, 'prasina_d.png')

# ---------- sahta: round manhole cover, 1024 px = 0.80 m (frame dia 0.80, lid 0.62) ----------
N = 1024
yy, xx = (np.mgrid[0:N, 0:N] + 0.5) / N - 0.5
r = np.sqrt(xx * xx + yy * yy)  # 0.5 = 0.40 m
m_per = 0.8
R_frame, R_lid, R_gap = 0.5 * 0.98, 0.31 / m_per, 0.006 / m_per
h = np.zeros((N, N))
frame = (r < R_frame) & (r > R_lid + R_gap)
lid = r < R_lid
h[frame] = 0.55
# frame: radial ribs ring
ang = np.arctan2(yy, xx)
rib = (np.cos(ang * 48) > 0.3) & (r > R_lid + 0.03) & (r < R_frame - 0.02)
h[rib & frame] = 0.75
# lid: diamond (rotated grid) raised pattern, no text/logos
u = (xx + yy) / np.sqrt(2) / (0.032 / m_per)
v = (xx - yy) / np.sqrt(2) / (0.032 / m_per)
fu = np.abs(u - np.round(u))
fv = np.abs(v - np.round(v))
diamond = (fu < 0.18) | (fv < 0.18)
h[lid] = 0.5
h[lid & diamond] = 0.8
# lid rim and centre plain disc
h[lid & (r > R_lid - 0.03)] = 0.75
h[lid & (r < 0.07)] = 0.7
# two generic pick holes
for sx in (-1, 1):
    d = np.sqrt((xx - sx * 0.22) ** 2 + yy ** 2)
    hole = d < 0.018
    h[hole] = 0.1
h[(r >= R_lid) & (r <= R_lid + R_gap)] = 0.05  # gap groove
h = blur(h, 1.2)
noise = fft_noise(N, 1.3, 51)
rust = fft_noise(N, 2.0, 52)
h += (noise - 0.5) * 0.04
wear = np.clip((h - 0.6) / 0.2, 0, 1)  # raised parts polished by tyres
iron = np.array([0.20, 0.19, 0.18])
polished = np.array([0.36, 0.35, 0.34])
rusty = np.array([0.30, 0.18, 0.10])
rgb = iron + (polished - iron) * wear[..., None]
rgb = rgb + (rusty - rgb) * (np.clip((rust - 0.55) / 0.3, 0, 1) * (1 - wear) * 0.6)[..., None]
rgb *= (0.85 + 0.3 * noise)[..., None]
rgb[h < 0.2] *= 0.35
alpha = np.clip((R_frame - r) * N / 2.0, 0, 1)
save_rgba(rgb, alpha, 'sahta_d.png')
normal_from_height(h, 14).save(os.path.join(out, 'sahta_n.png'), optimize=True)

# ---------- resetka: square road gully grate, 1024 px = 0.50 m (frame 0.50, grate 0.40) ----------
N = 1024
yy, xx = (np.mgrid[0:N, 0:N] + 0.5) / N - 0.5
m_per = 0.5
ax, ay = np.abs(xx), np.abs(yy)
h = np.full((N, N), 0.55)
inner = (ax < 0.20 / m_per) & (ay < 0.20 / m_per)
h[(ax < 0.205 / m_per) & (ay < 0.205 / m_per) & ~inner] = 0.1  # gap
# grate: bars along x with cross members (generic, no lettering)
slot_w, pitch = 0.022 / m_per, 0.036 / m_per
s = (xx + 0.2 / m_per) / pitch
slot = (np.abs(s - np.floor(s) - 0.5) < (slot_w / pitch) / 2)
cross = np.zeros_like(slot)
for cy in (-0.07, 0.07):
    cross |= np.abs(yy - cy / m_per) < 0.012 / m_per
border = (ax > 0.185 / m_per) | (ay > 0.185 / m_per)
hole = inner & slot & ~cross & ~border
h[inner] = 0.7
h[hole] = 0.0
h = blur(h, 1.0)
noise = fft_noise(N, 1.3, 61)
rust = fft_noise(N, 2.0, 62)
h += (noise - 0.5) * 0.03
wear = np.clip((h - 0.62) / 0.12, 0, 1)
rgb = iron + (polished - iron) * wear[..., None]
rgb = rgb + (rusty - rgb) * (np.clip((rust - 0.5) / 0.3, 0, 1) * 0.7)[..., None]
rgb *= (0.85 + 0.3 * noise)[..., None]
rgb[h < 0.15] = np.array([0.03, 0.03, 0.03])
edge = np.maximum(ax, ay)
alpha = np.clip((0.497 - edge) * N, 0, 1)
save_rgba(rgb, alpha, 'resetka_d.png')
normal_from_height(h, 12).save(os.path.join(out, 'resetka_n.png'), optimize=True)
print('done')
