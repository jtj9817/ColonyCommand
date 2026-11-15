#!/usr/bin/env python3
"""Check Source/<MOD_NAME>/Look.cs's colours for colour-blind safety and contrast.

Reads every `public static readonly Color Name = Hex(0xRRGGBB[, alpha]);` line of Look.cs, composites the panel's alpha over
a few backdrops, and checks (numbers printed, exit 1 on any failure):

  * text on every button state: WCAG contrast >= 4.5 (AA), also under protan / deutan / tritan simulation;
  * a selected button against a resting one (and a hovered one): WCAG contrast >= 3 (it reads in grayscale too), and a CIELAB
    distance >= 40 under normal vision and under each simulation (it reads without telling hues apart);
  * the warning text on the panel: contrast >= 4.5;
  * each border against what it sits on: contrast >= 3;
  * the panel against sample backdrops (soil, grass, snow, rock, night): its fill or its border contrasts >= 3, so it stands out.

Simulation: Machado, Oliveira and Fernandes (2009), severity 1.0, applied in linear RGB. Contrast is computed on the simulated
colours too. Python 3 standard library only (run as `python3 -I tools/palette-check.py`).
"""
import math
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SIMULATIONS = {
    "normal": None,
    "protan": ((0.152286, 1.052583, -0.204868), (0.114503, 0.786281, 0.099216), (-0.003882, -0.048116, 1.051998)),
    "deutan": ((0.367322, 0.860646, -0.227968), (0.280085, 0.672501, 0.047413), (-0.011820, 0.042940, 0.968881)),
    "tritan": ((1.255528, -0.076749, -0.178779), (-0.078411, 0.930809, 0.147602), (0.004733, 0.691367, 0.303900)),
}
BACKDROPS = {"soil": 0x5D4630, "grass": 0x52632F, "snow": 0xF2F5F7, "rock": 0x808080, "night": 0x05070A}


def load(path):
    colors = {}
    pattern = re.compile(r"public static readonly Color (\w+) = Hex\(0x([0-9A-Fa-f]{6})(?:,\s*([0-9.]+)f)?\)")
    for line in path.read_text().splitlines():
        m = pattern.search(line)
        if m:
            rgb = int(m.group(2), 16)
            colors[m.group(1)] = (((rgb >> 16) & 255) / 255, ((rgb >> 8) & 255) / 255, (rgb & 255) / 255, float(m.group(3) or 1))
    return colors


def lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def gam(c):
    c = min(1.0, max(0.0, c))
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def simulate(rgb, matrix):
    if matrix is None:
        return rgb
    l = [lin(c) for c in rgb]
    return tuple(gam(sum(matrix[i][j] * l[j] for j in range(3))) for i in range(3))


def luminance(rgb):
    r, g, b = (lin(c) for c in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a, b):
    la, lb = luminance(a), luminance(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def lab(rgb):
    r, g, b = (lin(c) for c in rgb)
    x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 216 / 24389 else (24389 / 27 * t + 16) / 116
    fx, fy, fz = f(x), f(y), f(z)
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def delta_e(a, b):
    return math.dist(lab(a), lab(b))


def over(rgba, backdrop):
    """The colour as drawn: its alpha composited over a backdrop."""
    r, g, b, a = rgba
    return tuple(a * c + (1 - a) * bd for c, bd in zip((r, g, b), backdrop))


def main():
    look = ROOT / "Source" / "ColonyCommand" / "Look.cs"
    c = load(look)
    need = ["PanelFill", "PanelBorder", "ButtonFill", "ButtonBorder", "ButtonHoverFill", "ButtonHoverBorder", "SelectedFill",
            "SelectedBorder", "SelectedHoverFill", "TextOnButton", "TextOnSelected", "Warning"]
    missing = [n for n in need if n not in c]
    if missing:
        sys.exit(f"palette-check: {look} lacks {missing}")
    rgb = {k: v[:3] for k, v in c.items()}
    failures = []
    rows = []

    def check(label, value, minimum, unit=""):
        ok = value >= minimum
        rows.append((ok, label, value, minimum, unit))
        if not ok:
            failures.append(label)

    for sim, matrix in SIMULATIONS.items():
        s = {k: simulate(v, matrix) for k, v in rgb.items()}
        s_panel = simulate(over(c["PanelFill"], (0.0, 0.0, 0.0)), matrix)   # an opaque-enough panel: worst case is a dark backdrop for light text
        for text, fills in (("TextOnButton", ("ButtonFill", "ButtonHoverFill")), ("TextOnSelected", ("SelectedFill", "SelectedHoverFill"))):
            for fill in fills:
                check(f"[{sim}] {text} on {fill}", contrast(s[text], s[fill]), 4.5, ":1")
        check(f"[{sim}] Warning on PanelFill", contrast(s["Warning"], s_panel), 4.5, ":1")
        check(f"[{sim}] TextOnButton on PanelFill", contrast(s["TextOnButton"], s_panel), 4.5, ":1")
        for rest in ("ButtonFill", "ButtonHoverFill"):
            for sel in ("SelectedFill", "SelectedHoverFill"):
                check(f"[{sim}] {sel} vs {rest}: brightness", contrast(s[sel], s[rest]), 3.0, ":1")
                check(f"[{sim}] {sel} vs {rest}: colour distance", delta_e(s[sel], s[rest]), 40.0, " dE")
        check(f"[{sim}] PanelBorder vs PanelFill", contrast(s["PanelBorder"], s_panel), 3.0, ":1")
        check(f"[{sim}] ButtonBorder vs ButtonFill", contrast(s["ButtonBorder"], s["ButtonFill"]), 3.0, ":1")
        check(f"[{sim}] ButtonBorder vs PanelFill", contrast(s["ButtonBorder"], s_panel), 3.0, ":1")
        check(f"[{sim}] ButtonHoverBorder vs ButtonHoverFill", contrast(s["ButtonHoverBorder"], s["ButtonHoverFill"]), 3.0, ":1")
        check(f"[{sim}] SelectedBorder vs PanelFill", contrast(s["SelectedBorder"], s_panel), 3.0, ":1")
        for name, hexv in BACKDROPS.items():
            bd = tuple(((hexv >> sh) & 255) / 255 for sh in (16, 8, 0))
            fill = simulate(over(c["PanelFill"], bd), matrix)
            edge = s["PanelBorder"]
            sbd = simulate(bd, matrix)
            check(f"[{sim}] panel over {name}: fill or edge stands out", max(contrast(fill, sbd), contrast(edge, sbd)), 3.0, ":1")

    width = max(len(r[1]) for r in rows)
    for ok, label, value, minimum, unit in rows:
        print(f"{'ok  ' if ok else 'FAIL'} {label:<{width}}  {value:7.2f}{unit}  (>= {minimum:g})")
    print(f"\npalette-check: {len(rows) - len(failures)}/{len(rows)} checks pass")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
