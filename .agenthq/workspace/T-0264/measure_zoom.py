# Measure how many PHYSICAL pixels one simulated pixel occupies, straight out of a screen capture.
#
# The preview's own canvas-edge frame border is the ruler: it is one line drawn exactly on the canvas rect, so
# the longest uninterrupted horizontal run of border-coloured pixels IS the picture's width in device pixels.
# Divide by the canvas's own width in simulated pixels and you have the answer with no help from the code
# under test.
import sys
from PIL import Image


def runs(row, pred):
    best = 0; best_x = -1; cur = 0; start = 0
    for x, p in enumerate(row):
        if pred(p):
            if cur == 0: start = x
            cur += 1
            if cur > best: best, best_x = cur, start
        else:
            cur = 0
    return best, best_x


def border_pixel(p):
    r, g, b = p[0], p[1], p[2]
    return min(r, g, b) > 120 and (max(r, g, b) - min(r, g, b)) < 60


def measure(path, canvas_w, canvas_h, x0=0, y0=0, x1=None, y1=None, min_run=60):
    im = Image.open(path).convert("RGB")
    W, H = im.size
    x1 = x1 or W; y1 = y1 or H
    px = im.load()
    rows = []
    for y in range(y0, y1):
        row = [px[x, y] for x in range(x0, x1)]
        n, sx = runs(row, border_pixel)
        if n >= min_run:
            rows.append((y, n, sx + x0))
    if not rows:
        return None
    # The two longest runs are the top and bottom edges of the canvas frame.
    rows.sort(key=lambda r: -r[1])
    top = rows[0]
    same = [r for r in rows if abs(r[1] - top[1]) <= 2 and abs(r[2] - top[2]) <= 2]
    ys = sorted(r[0] for r in same)
    width_px = top[1]
    height_px = (ys[-1] - ys[0] + 1) if len(ys) >= 2 else None
    return {
        "image": path, "image_size": (W, H),
        "border_top_y": ys[0], "border_bottom_y": ys[-1], "border_x": top[2],
        "width_px": width_px, "height_px": height_px,
        "canvas": (canvas_w, canvas_h),
        "px_per_simulated_x": width_px / canvas_w,
        "px_per_simulated_y": (height_px / canvas_h) if height_px else None,
    }


if __name__ == "__main__":
    path = sys.argv[1]; cw = int(sys.argv[2]); ch = int(sys.argv[3])
    box = [int(v) for v in sys.argv[4:8]] if len(sys.argv) >= 8 else [0, 0, None, None]
    r = measure(path, cw, ch, box[0], box[1], box[2], box[3])
    print(r)
