#!/usr/bin/env python3
"""Strict, same-pixel comparison for UI-separation baseline/candidate captures."""

import argparse
import hashlib
import json
from pathlib import Path
from typing import Iterable

from PIL import Image, ImageChops


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_nonuniform(image: Image.Image, name: str) -> None:
    extrema = image.getextrema()
    if all(low == high for low, high in extrema):
        raise AssertionError(f"{name} is uniform after cropping; capture is not valid evidence")


def bbox_for(mask: Iterable[bool], width: int, crop_top: int) -> list[int] | None:
    xs: list[int] = []
    ys: list[int] = []
    for index, changed in enumerate(mask):
        if changed:
            xs.append(index % width)
            ys.append(index // width + crop_top)
    if not xs:
        return None
    return [min(xs), min(ys), max(xs) + 1, max(ys) + 1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--crop-top", type=int, default=0, metavar="N", help="Ignore exactly N rows at the top.")
    parser.add_argument("--tolerance", type=int, default=0, help="Per-channel delta required to count as changed.")
    args = parser.parse_args()

    if args.crop_top < 0:
        raise ValueError("--crop-top must be zero or positive")
    if not 0 <= args.tolerance <= 255:
        raise ValueError("--tolerance must be in 0..255")

    baseline = Image.open(args.baseline).convert("RGBA")
    candidate = Image.open(args.candidate).convert("RGBA")
    if baseline.size != candidate.size:
        raise AssertionError(f"capture dimensions differ: {baseline.size} != {candidate.size}")
    if args.crop_top >= baseline.height:
        raise AssertionError("--crop-top removes every image row")

    comparison_box = (0, args.crop_top, baseline.width, baseline.height)
    baseline_crop = baseline.crop(comparison_box)
    candidate_crop = candidate.crop(comparison_box)
    require_nonuniform(baseline_crop, "baseline")
    require_nonuniform(candidate_crop, "candidate")

    deltas = ImageChops.difference(baseline_crop, candidate_crop)
    delta_pixels = list(deltas.getdata())
    exact_mask = [any(channel != 0 for channel in pixel) for pixel in delta_pixels]
    tolerant_mask = [any(channel > args.tolerance for channel in pixel) for pixel in delta_pixels]
    row_counts = [sum(tolerant_mask[row * baseline.width:(row + 1) * baseline.width]) for row in range(baseline_crop.height)]
    ordered_rows = sorted(enumerate(row_counts, start=args.crop_top), key=lambda item: (-item[1], item[0]))

    diff = Image.new("RGBA", baseline.size, (0, 0, 0, 0))
    diff.paste(deltas, (0, args.crop_top))
    diff_path = args.candidate.with_name(f"{args.candidate.stem}.diff.png")
    diff.save(diff_path)

    channel_count = baseline_crop.width * baseline_crop.height * 4
    channel_sum = sum(sum(pixel) for pixel in delta_pixels)
    result = {
        "baseline": {"path": str(args.baseline), "sha256": sha256(args.baseline)},
        "candidate": {"path": str(args.candidate), "sha256": sha256(args.candidate)},
        "size": {"width": baseline.width, "height": baseline.height},
        "crop_top": args.crop_top,
        "tolerance": args.tolerance,
        "exact_changed_pixels": sum(exact_mask),
        "over_tolerance_changed_pixels": sum(tolerant_mask),
        "max_channel_delta": max(max(pixel) for pixel in delta_pixels),
        "mean_channel_delta": channel_sum / channel_count,
        "bounding_rect": bbox_for(tolerant_mask, baseline.width, args.crop_top),
        "exact_bounding_rect": bbox_for(exact_mask, baseline.width, args.crop_top),
        "top_horizontal_rows": [{"y": y, "changed_pixels": count} for y, count in ordered_rows[:20] if count],
        "diff_png": str(diff_path),
    }
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
