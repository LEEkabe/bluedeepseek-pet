"""Cut the white background out of the character art.

The background must be removed by flood-filling inward from the borders, NOT by
deleting white pixels globally: this character is full of white (apron, ruffles,
socks, collar, hair highlights) and a global white cut would punch holes in her.

Usage: make_sprite.py <src.jpg> <out.png> <out.ico> <preview.png>
"""
import sys
import numpy as np
from PIL import Image

# A pixel counts as "background candidate" when it is near-white and near-neutral.
NEAR_WHITE = 234      # min(R,G,B) at or above this is candidate background
MAX_CHROMA = 16       # max(R,G,B) - min(R,G,B) must be at most this
DARK_NEIGHBOUR = 205  # a real (non-white) object is this dark or darker


def dilate(mask):
    """4-connected binary dilation by one pixel."""
    n = mask.copy()
    n[1:, :] |= mask[:-1, :]
    n[:-1, :] |= mask[1:, :]
    n[:, 1:] |= mask[:, :-1]
    n[:, :-1] |= mask[:, 1:]
    return n


def border_connected(cand):
    """Grow the border seeds through `cand` until fixpoint = connected component."""
    reach = np.zeros_like(cand)
    reach[0, :] = cand[0, :]
    reach[-1, :] = cand[-1, :]
    reach[:, 0] |= cand[:, 0]
    reach[:, -1] |= cand[:, -1]
    reach &= cand
    it = 0
    while True:
        nxt = dilate(reach) & cand
        if np.array_equal(nxt, reach):   # short-circuits on first mismatch
            return reach, it
        reach = nxt
        it += 1
        if it > 20000:
            raise RuntimeError("flood fill did not converge")


def main():
    src, out_png, out_ico, out_prev = sys.argv[1:5]

    im = Image.open(src).convert("RGB")
    W, H = im.size
    print("source: %s  %dx%d" % (src, W, H))

    a = np.asarray(im)
    mn = a.min(axis=2).astype(np.int16)
    mx = a.max(axis=2).astype(np.int16)

    cand = (mn >= NEAR_WHITE) & ((mx - mn) <= MAX_CHROMA)
    print("background candidates: %d / %d" % (int(cand.sum()), W * H))

    bg, iters = border_connected(cand)
    print("border-connected background: %d px (converged in %d passes)"
          % (int(bg.sum()), iters))

    alpha = np.where(bg, 0, 255).astype(np.uint8)

    # Soften the 1px seam, but ONLY where a genuinely dark object sits behind it.
    # Where the seam is white-on-white (socks, hem frills) we keep it opaque,
    # otherwise those white garment edges would fade out.
    ring = dilate(bg) & ~bg
    dark = (~bg) & (mn < DARK_NEIGHBOUR)
    near_dark = dilate(dilate(dark))
    soft = ring & near_dark
    ramp = np.clip((255.0 - mn) / (255.0 - NEAR_WHITE), 0.0, 1.0)
    alpha[soft] = (ramp[soft] * 255.0).astype(np.uint8)
    print("softened seam pixels: %d" % int(soft.sum()))

    rgba = np.dstack([a, alpha])
    img = Image.fromarray(rgba, "RGBA")

    ys, xs = np.nonzero(alpha > 0)
    if len(xs) == 0:
        raise RuntimeError("everything was classified as background")
    pad = 2
    x0 = max(0, int(xs.min()) - pad)
    y0 = max(0, int(ys.min()) - pad)
    x1 = min(W, int(xs.max()) + 1 + pad)
    y1 = min(H, int(ys.max()) + 1 + pad)
    img = img.crop((x0, y0, x1, y1))
    print("trimmed: %dx%d  (bbox %d,%d - %d,%d)" % (img.width, img.height, x0, y0, x1, y1))
    img.save(out_png, "PNG", optimize=True)

    # ---- icon (square, character centred on transparency) ----
    ico = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    thumb = img.copy()
    thumb.thumbnail((240, 240), Image.LANCZOS)
    ico.paste(thumb, ((256 - thumb.width) // 2, (256 - thumb.height) // 2), thumb)
    ico.save(out_ico, sizes=[(256, 256), (64, 64), (48, 48), (32, 32), (16, 16)])

    # ---- preview: magenta on the left, near-black on the right, so any
    #      leftover white halo or hole is obvious at a glance ----
    pv = Image.new("RGB", (img.width * 2 + 8, img.height), (255, 0, 255))
    pv.paste((18, 20, 28), (img.width + 8, 0, img.width * 2 + 8, img.height))
    pv.paste(img, (0, 0), img)
    pv.paste(img, (img.width + 8, 0), img)
    pv.thumbnail((900, 900), Image.LANCZOS)
    pv.save(out_prev, "PNG")
    print("wrote %s , %s , %s" % (out_png, out_ico, out_prev))


if __name__ == "__main__":
    main()
