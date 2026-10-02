# Measure physical pixels per simulated pixel from a screen capture, using the preview's own canvas-edge
# frame border as the ruler. Horizontal runs give the width; vertical runs give the height, so a picture whose
# top and bottom are clipped (a zoom bigger than the pane) can still be measured across.
import sys
from PIL import Image


def load(path, box):
    im = Image.open(path).convert("RGB")
    W, H = im.size
    x0, y0, x1, y1 = box
    return im.load(), max(0, x0), max(0, y0), min(W, x1), min(H, y1), (W, H)


def long_runs(px, x0, y0, x1, y1, isb, minrun, vertical=False):
    out = []
    outer = range(x0, x1) if vertical else range(y0, y1)
    inner = range(y0, y1) if vertical else range(x0, x1)
    for a in outer:
        best = cur = bs = st = 0
        for b in inner:
            p = px[a, b] if vertical else px[b, a]
            if isb(p):
                if cur == 0:
                    st = b
                cur += 1
                if cur > best:
                    best, bs = cur, st
            else:
                cur = 0
        if best >= minrun:
            out.append((a, best, bs))
    return out


def measure(path, cw, ch, box, target, tol=4, minrun=100):
    px, x0, y0, x1, y1, size = load(path, box)
    isb = lambda p: all(abs(p[i] - target[i]) <= tol for i in range(3))
    rows = long_runs(px, x0, y0, x1, y1, isb, minrun, vertical=False)
    cols = long_runs(px, x0, y0, x1, y1, isb, minrun, vertical=True)
    res = {"image": path, "size": size, "canvas": (cw, ch)}
    if rows:
        top = max(rows, key=lambda r: r[1])
        same = [r for r in rows if abs(r[1] - top[1]) <= 2 and abs(r[2] - top[2]) <= 2]
        ys = sorted(r[0] for r in same)
        res["width_px"] = top[1]
        res["px_per_sim_x"] = top[1] / cw
        if len(ys) >= 2:
            res["height_px_from_rows"] = ys[-1] - ys[0] + 1
            res["px_per_sim_y"] = (ys[-1] - ys[0] + 1) / ch
    if cols:
        left = max(cols, key=lambda c: c[1])
        same = [c for c in cols if abs(c[1] - left[1]) <= 2 and abs(c[2] - left[2]) <= 2]
        xs = sorted(c[0] for c in same)
        res["height_px"] = left[1]
        if len(xs) >= 2:
            res["width_px_from_cols"] = xs[-1] - xs[0] + 1
            res["px_per_sim_x_from_cols"] = (xs[-1] - xs[0] + 1) / cw
    return res


if __name__ == "__main__":
    path, cw, ch = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    box = tuple(int(v) for v in sys.argv[4:8])
    target = tuple(int(v) for v in sys.argv[8:11])
    tol = int(sys.argv[11]) if len(sys.argv) > 11 else 4
    minrun = int(sys.argv[12]) if len(sys.argv) > 12 else 100
    print(measure(path, cw, ch, box, target, tol, minrun))
