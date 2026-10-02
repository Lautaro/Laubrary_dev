# The strongest of the two measurements: don't trust any chrome, measure the PICTURE.
#
# At zoom N every simulated pixel must be exactly N x targetScale physical pixels, all identical. So along a
# scanline through the picture, every run of one constant colour is a whole number of blocks — and the GCD of
# all those run lengths is the block size itself. If any simulated pixel came out one physical pixel wider
# than its neighbour (the whole bug this task exists to remove), the GCD collapses to 1.
import sys
from math import gcd
from PIL import Image


def block_size(path, box, rows=None, min_runs=8):
    im = Image.open(path).convert("RGB")
    px = im.load()
    x0, y0, x1, y1 = box
    rows = rows or list(range(y0, y1, 7))
    lengths = []
    for y in rows:
        if not (y0 <= y < y1):
            continue
        run = 1
        prev = px[x0, y]
        first = True
        for x in range(x0 + 1, x1):
            p = px[x, y]
            if p == prev:
                run += 1
            else:
                if not first:            # drop the first run: it starts at the crop edge, not a block edge
                    lengths.append(run)
                first = False
                run = 1
                prev = p
        # the final run also ends at the crop edge — dropped
    if len(lengths) < min_runs:
        return {"runs": len(lengths), "block": None, "lengths": lengths[:20]}
    g = 0
    for L in lengths:
        g = gcd(g, L)
    from collections import Counter
    return {"runs": len(lengths), "gcd_block_px": g, "min_run": min(lengths),
            "histogram": Counter(lengths).most_common(6)}


if __name__ == "__main__":
    path = sys.argv[1]
    box = tuple(int(v) for v in sys.argv[2:6])
    print(path, block_size(path, box))
