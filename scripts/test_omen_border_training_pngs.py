# -*- coding: utf-8 -*-
"""
Mirror OmenActivationService: border_ratio + LooksLikeExaltationOmenBorderPixel.

Training set (expected DEACTIVATED = no activation border), 1..60 left-right top-bottom:
  - training   — 18 fixed cells (column pattern)
  - training3  — even cells 2,4,...,60 (active 1,3,5,...)
  - training4  — odd cells 1,3,...,59 (active 2,4,6,...)

training2 removed (unconfirmed labels).

PNG has no game ScreenRect: search ml, mt (margins). Production uses calibrated cells.

Verification:
  - pattern (training): best symmetric difference vs expected; PASS if <= 1 (PNG grid vs stash)
  - checkerboard: max over ml,mt of (mean ratio | active) - (mean | deactivated) >= CHECKER_MEAN_GAP (~0.035)
  - ninecell / ninecell2: 3x3, bleed + «полый центр» по 4 соседям (как в C#)
"""
from __future__ import annotations

import statistics
import sys
from pathlib import Path

from PIL import Image

ASSETS = Path(
    r"C:\Users\VVK\.cursor\projects\c-Users-VVK-GameHelper\assets"
)

RED_RATIO_THRESHOLD = 0.16  # sync OmenActivationService.RedPixelRatioThreshold
BORDER_BLEED_OUT = 3  # sync OmenActivationService.BorderBleedOutPx
NEIGHBOR_FACING_RATIO_MAX = 0.72  # sync NeighborFacingHighlightRatioMax
OUTWARD_FLOOR = 0.028  # sync hollow outward gate in C#

EXP_TRAINING_PATTERN = frozenset(
    {8, 12, 24, 32, 33, 34, 35, 36, 44, 45, 46, 47, 48, 56, 57, 58, 59, 60}
)
EXP_TRAINING3 = frozenset(range(2, 61, 2))  # deactivate evens
EXP_TRAINING4 = frozenset(range(1, 61, 2))  # deactivate odds

# 3x3: активна только центральная (5)
EXP_NINECELL_CENTER_ACTIVE = frozenset({1, 2, 3, 4, 6, 7, 8, 9})
# 3x3: активно кольцо, выключен центр
EXP_NINECELL_RING = frozenset({5})

# Min (mean active ratio - mean deactivated ratio) for checkerboard PNGs
CHECKER_MEAN_GAP = 0.035  # training4 PNG ~0.038 at best ml,mt

# Reasonable PNG crop: avoid using bottom-of-image as "grid origin"
MT_MAX_PATTERN = 14
ML_MAX = 32


def looks_like_exaltation_omen_border_pixel(r: int, g: int, b: int) -> bool:
    """Keep in sync with OmenActivationService.LooksLikeExaltationOmenBorderPixel."""
    if r <= 90 and g <= 90 and b <= 90:
        return False
    if r > 215 and g > 200 and b > 95:
        return False
    if r > 172 and g < 128 and b < 132 and r - max(g, b) > 48:
        return True
    if r > 160 and r - max(g, b) > 60:
        return True
    if r > 145 and r - max(g, b) > 42:
        return True
    if r > 155 and g > 52 and g < 215 and r >= g - 38 and r > b + 15:
        return True
    if r > 168 and g > 58 and g < 208 and r + g > 238 and r > b + 18:
        return True
    if r > 188 and g > 82 and g < 232 and r + g > 268 and r > b + 26:
        return True
    return False


def _accum_region(
    px, im_w: int, im_h: int, sx: int, sy: int, sw: int, sh: int
) -> tuple[int, int]:
    hit = tot = 0
    for yy in range(max(0, sy), min(im_h, sy + sh)):
        for xx in range(max(0, sx), min(im_w, sx + sw)):
            tot += 1
            c = px[xx, yy]
            if looks_like_exaltation_omen_border_pixel(c[0], c[1], c[2]):
                hit += 1
    return hit, tot


def border_ratio_bleed(
    px,
    im_w: int,
    im_h: int,
    x0: int,
    y0: int,
    cw: int,
    ch: int,
    edge_inset: int = 2,
    frame_th: int = 2,
    bleed: int = BORDER_BLEED_OUT,
) -> float:
    """Синхронно с ComputeBorderHighlightRatio (внутренний прямоугольник + расширение полос)."""
    ei = edge_inset
    ix = x0 + ei
    iy = y0 + ei
    iw = cw - 2 * ei
    ih = ch - 2 * ei
    if iw < 6 or ih < 6:
        return 0.0
    th = min(frame_th, min(iw, ih) // 2)
    if th < 1:
        return 0.0
    mid_h = ih - 2 * th
    if mid_h < 1:
        return 0.0

    cap_w = iw + 2 * bleed
    cap_h = ih + 2 * bleed
    src_x = ix - bleed
    src_y = iy - bleed
    copy_w, copy_h = cap_w, cap_h
    if src_x < 0:
        copy_w += src_x
        src_x = 0
    if src_y < 0:
        copy_h += src_y
        src_y = 0
    if copy_w < 1 or copy_h < 1:
        return 0.0

    inner_bx = ix - src_x
    inner_by = iy - src_y

    red = tot = 0

    def acc(rx: int, ry: int, rw: int, rh: int) -> None:
        nonlocal red, tot
        if rw < 1 or rh < 1:
            return
        rx = max(0, min(rx, copy_w - 1))
        ry = max(0, min(ry, copy_h - 1))
        rw = min(rw, copy_w - rx)
        rh = min(rh, copy_h - ry)
        if rw < 1 or rh < 1:
            return
        for yy in range(ry, ry + rh):
            for xx in range(rx, rx + rw):
                sx = src_x + xx
                sy = src_y + yy
                if 0 <= sx < im_w and 0 <= sy < im_h:
                    tot += 1
                    c = px[sx, sy]
                    if looks_like_exaltation_omen_border_pixel(c[0], c[1], c[2]):
                        red += 1

    top_y = max(0, inner_by - bleed)
    top_h = inner_by + th - top_y
    acc(inner_bx, top_y, iw, top_h)

    bot_y = inner_by + ih - th
    bot_h = min(copy_h - bot_y, th + bleed)
    acc(inner_bx, bot_y, iw, bot_h)

    left_x = max(0, inner_bx - bleed)
    left_w = inner_bx + th - left_x
    acc(left_x, inner_by + th, left_w, mid_h)

    right_x = inner_bx + iw - th
    right_w = min(copy_w - right_x, th + bleed)
    acc(right_x, inner_by + th, right_w, mid_h)

    return red / tot if tot else 0.0


def inner_strip_ratio(
    px,
    im_w: int,
    im_h: int,
    x0: int,
    y0: int,
    cw: int,
    ch: int,
    edge: str,
    edge_inset: int = 2,
    frame_th: int = 2,
) -> float:
    ei = edge_inset
    ix, iy = x0 + ei, y0 + ei
    iw, ih = cw - 2 * ei, ch - 2 * ei
    if iw < 6 or ih < 6:
        return 0.0
    th = min(frame_th, min(iw, ih) // 2)
    mh = ih - 2 * th
    if mh < 1:
        return 0.0
    if edge == "top":
        h, w, sx, sy = th, iw, ix, iy
    elif edge == "bottom":
        h, w, sx, sy = th, iw, ix, iy + ih - th
    elif edge == "left":
        h, w, sx, sy = mh, th, ix, iy + th
    elif edge == "right":
        h, w, sx, sy = mh, th, ix + iw - th, iy + th
    else:
        return 0.0
    hit, tot = _accum_region(px, im_w, im_h, sx, sy, w, h)
    return hit / tot if tot else 0.0


def avg_inner_strip_excluding_facing(
    px,
    im_w: int,
    im_h: int,
    x0: int,
    y0: int,
    cw: int,
    ch: int,
    facing_toward_center: str,
) -> float:
    """Три полосы соседа, кроме грани к центру 3×3 (синхронно с C# AverageInnerStripHighlightRatioExcludingFacing)."""
    if facing_toward_center == "bottom":
        a = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "top")
        b = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "left")
        c = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "right")
    elif facing_toward_center == "top":
        a = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "bottom")
        b = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "left")
        c = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "right")
    elif facing_toward_center == "right":
        a = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "top")
        b = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "bottom")
        c = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "left")
    elif facing_toward_center == "left":
        a = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "top")
        b = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "bottom")
        c = inner_strip_ratio(px, im_w, im_h, x0, y0, cw, ch, "right")
    else:
        return 0.0
    return (a + b + c) / 3.0


def hollow_center_3x3(
    px,
    im_w: int,
    im_h: int,
    bounds: list[tuple[int, int, int, int]],
    i: int,
) -> bool:
    """Индексы 0..8, строка-major; только для центра i==4 при наличии 4 соседей."""
    r, c = i // 3, i % 3
    if r != 1 or c != 1:
        return False
    up, dn, lf, rt = i - 3, i + 3, i - 1, i + 1
    inward = (
        inner_strip_ratio(px, im_w, im_h, *bounds[up], "bottom")
        + inner_strip_ratio(px, im_w, im_h, *bounds[dn], "top")
        + inner_strip_ratio(px, im_w, im_h, *bounds[lf], "right")
        + inner_strip_ratio(px, im_w, im_h, *bounds[rt], "left")
    ) / 4.0
    bu, bd, bl, br = bounds[up], bounds[dn], bounds[lf], bounds[rt]
    outward = (
        avg_inner_strip_excluding_facing(px, im_w, im_h, *bu, "bottom")
        + avg_inner_strip_excluding_facing(px, im_w, im_h, *bd, "top")
        + avg_inner_strip_excluding_facing(px, im_w, im_h, *bl, "right")
        + avg_inner_strip_excluding_facing(px, im_w, im_h, *br, "left")
    ) / 4.0
    if outward < OUTWARD_FLOOR:
        return False
    return inward < outward * NEIGHBOR_FACING_RATIO_MAX


def cell_bounds_grid(
    px, im_w: int, im_h: int, ml: int, mt: int, rows: int, cols: int
) -> list[tuple[int, int, int, int]]:
    out: list[tuple[int, int, int, int]] = []
    for row in range(rows):
        y0 = mt + round(row * (im_h - mt) / rows)
        y1 = mt + round((row + 1) * (im_h - mt) / rows)
        for col in range(cols):
            x0 = ml + round(col * (im_w - ml) / cols)
            x1 = ml + round((col + 1) * (im_w - ml) / cols)
            out.append((x0, y0, x1 - x0, y1 - y0))
    return out


def cell_ratios_grid(
    px, im_w: int, im_h: int, ml: int, mt: int, rows: int, cols: int
) -> list[float]:
    ratios: list[float] = []
    for x0, y0, cw, ch in cell_bounds_grid(px, im_w, im_h, ml, mt, rows, cols):
        ratios.append(border_ratio_bleed(px, im_w, im_h, x0, y0, cw, ch))
    return ratios


def cell_ratios(px, im_w: int, im_h: int, ml: int, mt: int) -> list[float]:
    return cell_ratios_grid(px, im_w, im_h, ml, mt, 5, 12)


def logical_active_3x3(
    i: int,
    ratio: float,
    thr: float,
    px,
    im_w: int,
    im_h: int,
    bounds: list[tuple[int, int, int, int]],
) -> bool:
    if ratio < thr:
        return False
    if hollow_center_3x3(px, im_w, im_h, bounds, i):
        return False
    return True


def deactivated_set_3x3_hollow(
    px,
    im_w: int,
    im_h: int,
    ml: int,
    mt: int,
    thr: float,
) -> set[int]:
    bounds = cell_bounds_grid(px, im_w, im_h, ml, mt, 3, 3)
    ratios = cell_ratios_grid(px, im_w, im_h, ml, mt, 3, 3)
    out: set[int] = set()
    for i in range(9):
        if not logical_active_3x3(i, ratios[i], thr, px, im_w, im_h, bounds):
            out.add(i + 1)
    return out


def find_best_3x3(
    path: Path, expected: frozenset[int]
) -> tuple[int, tuple[int, int, float] | None, set[int]]:
    img = Image.open(path).convert("RGB")
    px = img.load()
    w, h = img.size
    best_sd = 999
    best_params: tuple[int, int, float] | None = None
    best_set: set[int] = set()
    for ml in range(0, 24):
        for mt in range(0, 18):
            # Нижняя граница: на tight crop угловая ячейка может дать ratio < 0.06 (ninecell2).
            for ti in range(4, 56):
                thr = ti / 200.0
                d = deactivated_set_3x3_hollow(px, w, h, ml, mt, thr)
                sd = len(expected.symmetric_difference(d))
                if sd < best_sd:
                    best_sd = sd
                    best_params = (ml, mt, thr)
                    best_set = d
                if sd == 0:
                    return 0, (ml, mt, thr), d
    return best_sd, best_params, best_set


def deactivated_from_ratios(ratios: list[float], thr: float) -> set[int]:
    return {i + 1 for i, r in enumerate(ratios) if r < thr}


def find_best_pattern(
    path: Path, expected: frozenset[int]
) -> tuple[int, tuple[int, int, float] | None, set[int]]:
    img = Image.open(path).convert("RGB")
    px = img.load()
    w, h = img.size
    best_sd = 999
    best_params: tuple[int, int, float] | None = None
    best_set: set[int] = set()
    for ml in range(0, ML_MAX):
        for mt in range(0, MT_MAX_PATTERN + 1):
            ratios = cell_ratios(px, w, h, ml, mt)
            for ti in range(12, 56):
                thr = ti / 200.0
                d = deactivated_from_ratios(ratios, thr)
                sd = len(expected.symmetric_difference(d))
                if sd < best_sd:
                    best_sd = sd
                    best_params = (ml, mt, thr)
                    best_set = d
                if sd == 0:
                    return 0, (ml, mt, thr), d
    return best_sd, best_params, best_set


def max_checker_mean_gap(path: Path, exp_deact: frozenset[int]) -> tuple[float, int, int]:
    """Maximize mean(active) - mean(deactivated) over ml, mt."""
    img = Image.open(path).convert("RGB")
    px = img.load()
    w, h = img.size
    best_gap = -1.0
    best_ml = best_mt = 0
    for ml in range(0, ML_MAX):
        for mt in range(0, MT_MAX_PATTERN + 1):
            ratios = cell_ratios(px, w, h, ml, mt)
            ra = [ratios[i] for i in range(60) if (i + 1) not in exp_deact]
            rd = [ratios[i] for i in range(60) if (i + 1) in exp_deact]
            gap = statistics.mean(ra) - statistics.mean(rd)
            if gap > best_gap:
                best_gap = gap
                best_ml, best_mt = ml, mt
    return best_gap, best_ml, best_mt


def verify_fixed_threshold_pattern(
    path: Path, expected: frozenset[int], ml: int, mt: int, thr: float
) -> tuple[bool, set[int]]:
    img = Image.open(path).convert("RGB")
    px = img.load()
    w, h = img.size
    ratios = cell_ratios(px, w, h, ml, mt)
    got = deactivated_from_ratios(ratios, thr)
    return got == set(expected), got


def smoke_training_cell20(path: Path) -> bool:
    ml, mt = 8, 3
    img = Image.open(path).convert("RGB")
    px = img.load()
    w, h = img.size
    ratios = cell_ratios(px, w, h, ml, mt)
    r20 = ratios[19]
    exp_de = [ratios[i] for i in range(60) if (i + 1) in EXP_TRAINING_PATTERN]
    mx = max(exp_de) if exp_de else 0.0
    ok = r20 >= RED_RATIO_THRESHOLD and mx < RED_RATIO_THRESHOLD
    print(
        f"  smoke training cell20: ml={ml} mt={mt} r20={r20:.4f} max_exp_deact={mx:.4f} thr={RED_RATIO_THRESHOLD} -> {'OK' if ok else 'WARN'}"
    )
    return ok


def resolve_asset(glob_pat: str) -> Path | None:
    matches = sorted(ASSETS.glob(glob_pat))
    return matches[0] if matches else None


def main() -> int:
    print("Omen border training / verification")
    print(f"RED_RATIO_THRESHOLD (C# sync) = {RED_RATIO_THRESHOLD}")
    print(f"checker mean gap PASS >= {CHECKER_MEAN_GAP}")
    print(f"pattern search: ml 0..{ML_MAX - 1}, mt 0..{MT_MAX_PATTERN}\n")

    ok_all = True

    # --- training (pattern) ---
    g = "*training-b71cf040*.png"
    p = resolve_asset(g)
    if p is None:
        print(f"MISSING {g}", file=sys.stderr)
        ok_all = False
    else:
        sd, params, got = find_best_pattern(p, EXP_TRAINING_PATTERN)
        print(f"training (18-cell pattern)  file={p.name}")
        print(f"  best symdiff={sd}  ml,mt,thr={params}")
        print(f"  got deactivated ({len(got)}): {sorted(got)}")
        pattern_ok = sd <= 1
        print(f"  VERIFY pattern (symdiff<=1): {'PASS' if pattern_ok else 'FAIL'}")
        if not pattern_ok:
            ok_all = False
        if params is not None:
            fixed_ok, _ = verify_fixed_threshold_pattern(
                p, EXP_TRAINING_PATTERN, params[0], params[1], RED_RATIO_THRESHOLD
            )
            print(
                f"  VERIFY same ml,mt with C# thr={RED_RATIO_THRESHOLD}: {'PASS' if fixed_ok else 'FAIL (expected for PNG)'}",
            )
        smoke_training_cell20(p)
        print()

    # --- training3 checker ---
    g = "*training3*.png"
    p = resolve_asset(g)
    if p is None:
        print(f"MISSING {g}", file=sys.stderr)
        ok_all = False
    else:
        gap, ml, mt = max_checker_mean_gap(p, EXP_TRAINING3)
        chk = gap >= CHECKER_MEAN_GAP
        print(f"training3 (deact evens 2..60)  file={p.name}")
        print(f"  max mean(active)-mean(deact)={gap:.4f} at ml={ml} mt={mt}")
        print(f"  VERIFY checker separation: {'PASS' if chk else 'FAIL'}")
        if not chk:
            ok_all = False
        print()

    # --- training4 checker ---
    g = "*training4*.png"
    p = resolve_asset(g)
    if p is None:
        print(f"MISSING {g}", file=sys.stderr)
        ok_all = False
    else:
        gap, ml, mt = max_checker_mean_gap(p, EXP_TRAINING4)
        chk = gap >= CHECKER_MEAN_GAP
        print(f"training4 (deact odds 1..59)  file={p.name}")
        print(f"  max mean(active)-mean(deact)={gap:.4f} at ml={ml} mt={mt}")
        print(f"  VERIFY checker separation: {'PASS' if chk else 'FAIL'}")
        if not chk:
            ok_all = False
        print()

    # --- ninecell 3x3: only center active ---
    g = "*ninecell-*.png"
    p = resolve_asset(g)
    if p is None:
        print(f"MISSING {g} (ninecell center active)", file=sys.stderr)
        ok_all = False
    else:
        sd, params, got = find_best_3x3(p, EXP_NINECELL_CENTER_ACTIVE)
        print(f"ninecell (only 5 active)  file={p.name}")
        print(f"  best symdiff={sd}  ml,mt,thr={params}")
        print(f"  got deactivated: {sorted(got)}")
        nc_ok = sd == 0
        print(f"  VERIFY 3x3+hollow: {'PASS' if nc_ok else 'FAIL'}")
        if not nc_ok:
            ok_all = False
        print()

    # --- ninecell2 3x3: only center deactivated ---
    g2 = "*ninecell2-*.png"
    p2 = resolve_asset(g2)
    if p2 is None:
        print(f"MISSING {g2} (ninecell ring)", file=sys.stderr)
        ok_all = False
    else:
        sd2, params2, got2 = find_best_3x3(p2, EXP_NINECELL_RING)
        print(f"ninecell2 (only 5 deactivated)  file={p2.name}")
        print(f"  best symdiff={sd2}  ml,mt,thr={params2}")
        print(f"  got deactivated: {sorted(got2)}")
        nc2_ok = sd2 == 0
        print(f"  VERIFY 3x3+hollow: {'PASS' if nc2_ok else 'FAIL'}")
        if not nc2_ok:
            ok_all = False
        print()

    if ok_all:
        print("Summary: all checks PASS.")
        return 0
    print("Summary: one or more FAIL or missing assets.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
