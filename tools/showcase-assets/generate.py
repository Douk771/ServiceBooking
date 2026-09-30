#!/usr/bin/env python3
"""Generates the neutral flat illustrations of the showcase (cycle 28, CT-1).

No photographs and no people: every picture is drawn here from shapes, so there is nothing to license.
Run:  python3 tools/showcase-assets/generate.py      (needs Pillow)
Writes ServiceBooking.API/ShowcaseAssets/{logos,photos,services}/*.jpg, manifest.json and the journal table of LICENSES.md.
Output is deterministic (fixed seeds).
"""
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2] / "ServiceBooking.API" / "ShowcaseAssets"
SS = 2  # supersampling factor

PALETTES = {  # wall, floor, accent, accent2, dark
    "beauty": ("#f3e3dc", "#c9a48f", "#c2607a", "#e8b4bc", "#4a2f3a"),
    "barber": ("#dfe3e6", "#6b5a4d", "#b23a3a", "#2f4a6d", "#1f2933"),
    "nails": ("#f6e6ee", "#d7b9c6", "#d45a8a", "#7a5cc2", "#3d2a4a"),
    "massage": ("#eee7da", "#b9a27f", "#6f8f72", "#c9b27c", "#3a3a2c"),
    "cosmetology": ("#e4f0ef", "#b7cfcc", "#3f9a96", "#9fd3cf", "#23444a"),
    "brows": ("#f1e6df", "#c8ab98", "#8a5a44", "#d9a679", "#3b2a22"),
    "home": ("#e7eef5", "#b5c4d3", "#3d6fb0", "#e0a43a", "#243548"),
}
CATEGORIES = list(PALETTES)
SERVICE_CATEGORY = {
    "haircut": "beauty", "coloring": "beauty", "styling": "beauty", "hair-care": "beauty", "makeup": "beauty",
    "manicure": "nails", "pedicure": "nails", "nails-design": "nails",
    "beard": "barber", "shave": "barber",
    "brows": "brows", "lashes": "brows",
    "massage": "massage", "spa": "massage", "wrap": "massage",
    "facial": "cosmetology", "peeling": "cosmetology", "mesotherapy": "cosmetology", "consult": "home",
}
SERVICE_PROP = {
    "haircut": "scissors", "coloring": "bottle", "styling": "dryer", "hair-care": "jar", "makeup": "brush",
    "manicure": "polish", "pedicure": "polish", "nails-design": "polish",
    "beard": "razor", "shave": "razor", "brows": "tweezers", "lashes": "brush",
    "massage": "stones", "spa": "candle", "wrap": "towels",
    "facial": "jar", "peeling": "drop", "mesotherapy": "drop", "consult": "calendar",
}


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    a, b = rgb(a) if isinstance(a, str) else a, rgb(b) if isinstance(b, str) else b
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w * SS, h * SS
        self.im = Image.new("RGB", (self.w, self.h), "white")
        self.d = ImageDraw.Draw(self.im)

    def s(self, v):
        return int(v * SS)

    def vgrad(self, box, c1, c2):
        x0, y0, x1, y1 = [self.s(v) for v in box]
        for y in range(y0, y1):
            t = (y - y0) / max(1, y1 - y0 - 1)
            self.d.line([(x0, y), (x1, y)], fill=mix(c1, c2, t))

    def rect(self, box, fill, r=0):
        b = [self.s(v) for v in box]
        if r:
            self.d.rounded_rectangle(b, radius=self.s(r), fill=fill)
        else:
            self.d.rectangle(b, fill=fill)

    def ell(self, box, fill):
        self.d.ellipse([self.s(v) for v in box], fill=fill)

    def poly(self, pts, fill):
        self.d.polygon([(self.s(x), self.s(y)) for x, y in pts], fill=fill)

    def line(self, pts, fill, w=2):
        self.d.line([(self.s(x), self.s(y)) for x, y in pts], fill=fill, width=self.s(w))

    def save(self, path, w, h, q=80):
        im = self.im.resize((w, h), Image.LANCZOS)
        path.parent.mkdir(parents=True, exist_ok=True)
        im.save(path, "JPEG", quality=q, optimize=True, progressive=True)
        return path.stat().st_size


# ---------- props (all drawn around a point, scale k) ----------

def prop(c, name, x, y, k, pal):
    wall, floor, acc, acc2, dark = pal
    if name == "scissors":
        c.line([(x - 30 * k, y - 40 * k), (x + 30 * k, y + 40 * k)], "#b8bec6", 7 * k)
        c.line([(x + 30 * k, y - 40 * k), (x - 30 * k, y + 40 * k)], "#9aa2ab", 7 * k)
        for dx in (-34, 34):
            c.ell((x + (dx - 18) * k, y + 34 * k, x + (dx + 18) * k, y + 70 * k), acc)
            c.ell((x + (dx - 10) * k, y + 42 * k, x + (dx + 10) * k, y + 62 * k), wall)
    elif name == "bottle":
        c.rect((x - 22 * k, y - 10 * k, x + 22 * k, y + 60 * k), acc, 8 * k)
        c.rect((x - 10 * k, y - 38 * k, x + 10 * k, y - 8 * k), dark, 3 * k)
        c.rect((x - 22 * k, y + 10 * k, x + 22 * k, y + 34 * k), wall, 2 * k)
    elif name == "dryer":
        c.rect((x - 50 * k, y - 30 * k, x + 20 * k, y + 10 * k), dark, 18 * k)
        c.rect((x + 20 * k, y - 22 * k, x + 38 * k, y + 2 * k), acc2, 3 * k)
        c.rect((x - 20 * k, y + 6 * k, x - 4 * k, y + 60 * k), acc, 6 * k)
    elif name == "jar":
        c.rect((x - 28 * k, y - 4 * k, x + 28 * k, y + 40 * k), wall, 8 * k)
        c.rect((x - 32 * k, y - 22 * k, x + 32 * k, y - 2 * k), acc, 5 * k)
        c.rect((x - 18 * k, y + 8 * k, x + 18 * k, y + 28 * k), acc2, 4 * k)
    elif name == "brush":
        c.line([(x - 40 * k, y + 40 * k), (x + 36 * k, y - 36 * k)], dark, 8 * k)
        c.poly([(x + 28 * k, y - 28 * k), (x + 52 * k, y - 52 * k), (x + 60 * k, y - 44 * k), (x + 36 * k, y - 20 * k)], acc)
    elif name == "polish":
        c.rect((x - 22 * k, y - 4 * k, x + 22 * k, y + 50 * k), acc, 9 * k)
        c.rect((x - 9 * k, y - 50 * k, x + 9 * k, y - 4 * k), dark, 3 * k)
        c.rect((x - 16 * k, y + 10 * k, x + 16 * k, y + 20 * k), "#ffffff", 3 * k)
    elif name == "razor":
        c.rect((x - 60 * k, y - 8 * k, x + 10 * k, y + 10 * k), dark, 6 * k)
        c.rect((x + 4 * k, y - 22 * k, x + 54 * k, y + 14 * k), "#c4cad1", 6 * k)
        c.rect((x + 8 * k, y - 16 * k, x + 50 * k, y - 8 * k), "#8c949c", 2 * k)
    elif name == "tweezers":
        c.poly([(x - 50 * k, y + 40 * k), (x + 40 * k, y - 34 * k), (x + 46 * k, y - 28 * k), (x - 44 * k, y + 46 * k)], "#b8bec6")
        c.poly([(x - 30 * k, y + 44 * k), (x + 44 * k, y - 18 * k), (x + 46 * k, y - 28 * k), (x - 44 * k, y + 46 * k)], "#9aa2ab")
    elif name == "stones":
        for i, (dx, dy, r) in enumerate([(-30, 14, 26), (8, -6, 22), (34, 20, 18)]):
            c.ell((x + (dx - r) * k, y + (dy - r * .7) * k, x + (dx + r) * k, y + (dy + r * .7) * k), mix(dark, acc2, .35 + .15 * i))
    elif name == "candle":
        c.rect((x - 20 * k, y - 6 * k, x + 20 * k, y + 50 * k), wall, 8 * k)
        c.poly([(x, y - 44 * k), (x + 10 * k, y - 18 * k), (x, y - 8 * k), (x - 10 * k, y - 18 * k)], "#f3b23c")
    elif name == "towels":
        for i in range(4):
            c.rect((x - 40 * k, y + (18 - i * 14) * k, x + 40 * k, y + (30 - i * 14) * k), mix(wall, acc2, .35 + .12 * i), 5 * k)
    elif name == "drop":
        c.poly([(x, y - 46 * k), (x + 26 * k, y + 6 * k), (x - 26 * k, y + 6 * k)], acc2)
        c.ell((x - 26 * k, y - 8 * k, x + 26 * k, y + 40 * k), acc2)
        c.ell((x - 10 * k, y + 2 * k, x, y + 14 * k), "#ffffff")
    elif name == "calendar":
        c.rect((x - 46 * k, y - 38 * k, x + 46 * k, y + 46 * k), "#ffffff", 9 * k)
        c.rect((x - 46 * k, y - 38 * k, x + 46 * k, y - 14 * k), acc, 9 * k)
        for r in range(3):
            for col in range(4):
                c.rect((x + (-36 + col * 20) * k, y + (-6 + r * 16) * k, x + (-26 + col * 20) * k, y + (2 + r * 16) * k), mix(wall, acc2, .5), 2 * k)
    elif name == "plant":
        c.rect((x - 18 * k, y + 20 * k, x + 18 * k, y + 60 * k), acc, 5 * k)
        for a in range(-2, 3):
            c.poly([(x, y + 22 * k), (x + a * 26 * k - 10 * k, y - 50 * k + abs(a) * 14 * k), (x + a * 26 * k + 10 * k, y - 50 * k + abs(a) * 14 * k)], mix("#5d8a5a", "#9cc49a", abs(a) / 3))


# ---------- scenes ----------

def scene(cat, variant, seed):
    rnd = random.Random(seed)
    pal = PALETTES[cat]
    wall, floor, acc, acc2, dark = pal
    W, H = 1200, 800
    c = Canvas(W, H)
    c.vgrad((0, 0, W, 560), mix(wall, "#ffffff", .3), wall)
    c.vgrad((0, 560, W, H), floor, mix(floor, dark, .25))
    if variant == 1:  # wide interior with window, mirror, chair
        c.rect((80, 90, 380, 420), "#ffffff", 6); c.vgrad((92, 102, 368, 408), "#cfe6f5", "#f6fbff")
        c.line([(230, 102), (230, 408)], "#ffffff", 8); c.line([(92, 255), (368, 255)], "#ffffff", 8)
        c.rect((520, 110, 760, 470), mix(dark, "#ffffff", .15), 14); c.rect((536, 126, 744, 454), mix(acc2, "#ffffff", .7), 8)
        c.rect((860, 380, 1020, 560), acc, 30); c.rect((840, 560, 1040, 600), dark, 10); c.rect((930, 600, 950, 690), dark)
        prop(c, "plant", 1110, 540, 1.3, pal)
    elif variant == 2:  # table with tools
        c.rect((0, 470, W, 640), mix(floor, "#ffffff", .15)); c.rect((0, 640, W, H), mix(floor, dark, .3))
        names = {"beauty": ["scissors", "dryer", "bottle"], "barber": ["razor", "bottle", "scissors"], "nails": ["polish", "polish", "brush"],
                 "massage": ["stones", "candle", "towels"], "cosmetology": ["jar", "drop", "jar"], "brows": ["tweezers", "brush", "jar"], "home": ["calendar", "bottle", "brush"]}[cat]
        for i, n in enumerate(names):
            prop(c, n, 280 + i * 320, 420 + rnd.randint(-10, 10), 2.0, pal)
    elif variant == 3:  # reception desk
        c.rect((180, 380, 1020, 600), dark, 18); c.rect((160, 360, 1040, 400), acc, 10)
        prop(c, "plant", 300, 250, 1.6, pal); prop(c, "calendar", 780, 280, 1.4, pal)
        c.rect((520, 120, 680, 200), "#ffffff", 12); c.rect((536, 136, 664, 184), acc2, 6)
    elif variant == 4:  # window light with shelves
        c.rect((380, 70, 820, 480), "#ffffff", 8); c.vgrad((396, 86, 804, 464), "#bcdcf2", "#f4faff")
        c.ell((540, 130, 640, 230), "#fff6cf"); c.rect((80, 250, 300, 266), dark, 4); c.rect((900, 250, 1120, 266), dark, 4)
        prop(c, "jar", 140, 210, 1.1, pal); prop(c, "bottle", 240, 205, 1.1, pal); prop(c, "plant", 1010, 170, 1.0, pal)
        c.poly([(400, 480), (820, 480), (1000, 800), (220, 800)], mix(floor, "#fff6cf", .18))
    elif variant == 5:  # flat-lay of tools on a tinted surface
        c.vgrad((0, 0, W, H), mix(acc2, "#ffffff", .55), mix(acc2, "#ffffff", .25))
        names = {"beauty": ["scissors", "brush", "dryer", "bottle"], "barber": ["razor", "scissors", "bottle", "brush"], "nails": ["polish", "polish", "polish", "brush"],
                 "massage": ["stones", "candle", "towels", "drop"], "cosmetology": ["jar", "drop", "jar", "brush"], "brows": ["tweezers", "brush", "tweezers", "jar"], "home": ["calendar", "bottle", "brush", "jar"]}[cat]
        for i, n in enumerate(names):
            prop(c, n, 210 + (i % 2) * 380 + (i // 2) * 160, 230 + (i // 2) * 290, 2.2, pal)
    else:  # waiting area: sofa and lamp
        c.rect((230, 400, 870, 620), acc, 50); c.rect((200, 330, 900, 450), mix(acc, "#ffffff", .15), 50)
        c.rect((280, 620, 310, 680), dark); c.rect((790, 620, 820, 680), dark)
        c.line([(1000, 120), (1000, 560)], dark, 8); c.poly([(930, 200), (1070, 200), (1040, 120), (960, 120)], "#f7d27a")
        prop(c, "plant", 100, 500, 1.4, pal)
    return c


def logo(cat):
    pal = PALETTES[cat]
    wall, floor, acc, acc2, dark = pal
    c = Canvas(600, 600)
    c.vgrad((0, 0, 600, 600), mix(acc, "#ffffff", .1), mix(acc, dark, .35))
    c.ell((60, 60, 540, 540), mix("#ffffff", acc2, .12))
    prop_name = {"beauty": "scissors", "barber": "razor", "nails": "polish", "massage": "stones", "cosmetology": "drop", "brows": "tweezers", "home": "calendar"}[cat]
    prop(c, prop_name, 300, 290, 2.2, pal)
    return c


def service(key):
    cat = SERVICE_CATEGORY[key]
    pal = PALETTES[cat]
    wall, floor, acc, acc2, dark = pal
    rnd = random.Random(sum(ord(ch) * (i + 1) for i, ch in enumerate(key)))  # a distinct, reproducible picture per service
    t = rnd.uniform(0.0, 0.35)
    c = Canvas(800, 600)
    c.vgrad((0, 0, 800, 600), mix(acc2, "#ffffff", .45 + t / 2), mix(acc, dark, t))
    for _ in range(9):
        r = rnd.randint(14, 46)
        x, y = rnd.randint(30, 770), rnd.randint(30, 570)
        c.ell((x - r, y - r, x + r, y + r), mix(acc2, "#ffffff", rnd.uniform(.5, .8)))
    c.ell((220, 120, 580, 480), mix("#ffffff", acc2, .15))
    prop(c, SERVICE_PROP[key], 400, 300, 2.4, pal)
    return c


def main():
    assets, rows, total = [], [], 0

    def add(key, role, rel, cw, w, h, cvs, q=80):
        nonlocal total
        size = cvs.save(ROOT / rel, w, h, q)
        total += size
        assets.append({"key": key, "role": role, "file": rel, "thumbnail": None, "width": w, "height": h})
        rows.append(f"| `{key}` | `{rel}` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |")
        assert size <= 250 * 1024, (rel, size)

    for i, cat in enumerate(CATEGORIES):
        add(f"logo.{cat}", "logo", f"logos/{cat}.jpg", 600, 600, 600, logo(cat))
        for v in range(1, 7):
            add(f"photo.{cat}.{v}", "photo", f"photos/{cat}-{v}.jpg", 1200, 1200, 800, scene(cat, v, i * 10 + v))
    for key in SERVICE_CATEGORY:
        add(f"service.{key}", "service", f"services/{key}.jpg", 800, 800, 600, service(key))
    assert total <= 8 * 1024 * 1024, total
    (ROOT / "manifest.json").write_text(json.dumps({"version": 1, "assets": assets}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    lic = (ROOT / "LICENSES.md").read_text(encoding="utf-8")
    head = lic.split("**Состояние:")[0]
    body = lic.split("## Журнал файлов")[0].split("\n", 1)
    rules = "## Журнал файлов".join(lic.split("## Журнал файлов")[:1])
    rules = rules.replace(rules[rules.index("**Состояние:"):rules.index("## Правила набора")],
                          f"**Состояние: {len(assets)} файлов, {total // 1024} КБ.** Плоские иллюстрации нарисованы программно, фотографий и людей нет.\n\n")
    (ROOT / "LICENSES.md").write_text(rules + "## Журнал файлов\n\n| Ключ | Файл | Источник | Лицензия | Дата проверки |\n|---|---|---|---|---|\n" + "\n".join(rows) + "\n", encoding="utf-8")
    print(f"{len(assets)} files, {total // 1024} KB")


if __name__ == "__main__":
    main()
