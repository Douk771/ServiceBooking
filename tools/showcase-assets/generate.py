#!/usr/bin/env python3
"""Generates the neutral flat illustrations of the showcase (cycle 28, CT-1) and of the demo shops of «Заказы» (cycle 35, T-35-15).

No photographs and no people: every picture is drawn here from shapes, so there is nothing to license.
Run:  python3 tools/showcase-assets/generate.py [--only all|salons|shops]      (needs Pillow; default: all)
Writes ServiceBooking.API/ShowcaseAssets/{logos,photos,services,products}/*.jpg, manifest.json and the journal table of LICENSES.md.
Output is deterministic (fixed seeds). `--only shops` draws only the shop pictures (logo.shop.*, photo.shop.*, product.*) and keeps every entry of
the salons in the manifest as it is: the salon files are not redrawn, so their bytes do not depend on the Pillow version of the machine that runs this.
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


# ====================================================================================================================
# Cycle 35 (ARCHITECTURE_CYCLE35.md §35.11, T-35-15): the five demo shops of «Заказы» — logos, gallery photos, product pictures.
# Keys: logo.shop.<cat>, photo.shop.<cat>.<n>, product.<cat>.<name>. <cat> is coffee | bakery | canteen | flowers | farm.
# ====================================================================================================================

SHOP_PALETTES = {  # wall, floor, accent, accent2, dark
    "coffee": ("#f1e6d8", "#b08968", "#7f4f24", "#d6a77a", "#3b2a1e"),
    "bakery": ("#f8ecd3", "#d1a566", "#c77d2e", "#f0c987", "#4a3219"),
    "canteen": ("#eaf0e3", "#b7c4a3", "#5e8c3a", "#e3a93b", "#2f3d24"),
    "flowers": ("#f7e6ee", "#c9b3a0", "#c4527a", "#9bc78f", "#3a2a35"),
    "farm": ("#e8efdd", "#b39b75", "#6a8f3a", "#d9853b", "#3a3522"),
}
SHOP_CATEGORIES = list(SHOP_PALETTES)
SHOP_LOGO_PRODUCT = {"coffee": "cappuccino", "bakery": "croissant", "canteen": "soup-borsch", "flowers": "bouquet-mixed", "farm": "tomatoes"}
SHOP_PRODUCTS = {  # the image names the catalogs use (ShowcaseShopSpecs.cs) -> how each is drawn
    "coffee": ["espresso", "americano", "cappuccino", "latte", "flat-white", "raf", "tea-black", "tea-green", "tea-herbal", "cocoa", "croissant", "muffin",
               "cookie", "cheesecake", "sandwich", "toast-avocado", "granola"],
    "bakery": ["loaf", "baguette", "rye-bread", "bun", "cinnamon-roll", "pie-apple", "pie-cabbage", "pie-meat", "cake-honey", "cake-chocolate", "eclair", "cookies"],
    "canteen": ["soup-borsch", "soup-chicken", "cutlet", "goulash", "fish", "buckwheat", "rice", "mashed-potato", "pasta", "salad-vinaigrette", "salad-fresh",
                "compote", "tea", "pancakes"],
    "flowers": ["bouquet-roses", "bouquet-mixed", "bouquet-tulips", "bouquet-peonies", "arrangement-box", "orchid", "succulent", "plant-pot", "card"],
    "farm": ["tomatoes", "cucumbers", "potatoes", "carrots", "apples", "berries", "milk", "cheese", "meat-beef", "honey", "eggs", "bread-farm"],
}


def cup(c, x, y, k, color, steam=True, saucer="#ffffff", foam=None):
    c.ell((x - 70 * k, y + 40 * k, x + 70 * k, y + 62 * k), saucer)
    c.rect((x - 46 * k, y - 34 * k, x + 46 * k, y + 50 * k), "#ffffff", 20 * k)
    c.ell((x - 46 * k, y - 46 * k, x + 46 * k, y - 22 * k), color)
    if foam:
        c.ell((x - 26 * k, y - 42 * k, x + 26 * k, y - 28 * k), foam)
    c.rect((x + 40 * k, y - 14 * k, x + 68 * k, y + 22 * k), "#ffffff", 12 * k)
    c.rect((x + 50 * k, y - 4 * k, x + 60 * k, y + 14 * k), mix("#ffffff", "#c8c8c8", .5), 4 * k)
    if steam:
        for dx in (-16, 0, 16):
            c.line([(x + dx * k, y - 56 * k), (x + (dx + 8) * k, y - 76 * k), (x + dx * k, y - 96 * k)], mix("#ffffff", "#c8c8c8", .4), 4 * k)


def glass(c, x, y, k, color, layer=None):
    c.poly([(x - 34 * k, y - 66 * k), (x + 34 * k, y - 66 * k), (x + 28 * k, y + 60 * k), (x - 28 * k, y + 60 * k)], mix("#ffffff", "#dde8ee", .6))
    c.poly([(x - 31 * k, y - 34 * k), (x + 31 * k, y - 34 * k), (x + 27 * k, y + 56 * k), (x - 27 * k, y + 56 * k)], color)
    if layer:
        c.poly([(x - 31 * k, y - 34 * k), (x + 31 * k, y - 34 * k), (x + 29 * k, y), (x - 30 * k, y)], layer)


def bowl(c, x, y, k, fill, rim="#ffffff", spots=()):
    c.ell((x - 70 * k, y - 10 * k, x + 70 * k, y + 16 * k), rim)
    c.poly([(x - 70 * k, y), (x + 70 * k, y), (x + 46 * k, y + 56 * k), (x - 46 * k, y + 56 * k)], rim)
    c.ell((x - 62 * k, y - 8 * k, x + 62 * k, y + 12 * k), fill)
    for dx, dy, r, col in spots:
        c.ell((x + (dx - r) * k, y + (dy - r) * k, x + (dx + r) * k, y + (dy + r) * k), col)


def plate(c, x, y, k):
    c.ell((x - 90 * k, y - 18 * k, x + 90 * k, y + 50 * k), "#ffffff")
    c.ell((x - 70 * k, y - 8 * k, x + 70 * k, y + 38 * k), mix("#ffffff", "#dfe5e8", .6))


def croissant(c, x, y, k, color):
    c.poly([(x - 72 * k, y + 24 * k), (x - 44 * k, y - 26 * k), (x + 44 * k, y - 26 * k), (x + 72 * k, y + 24 * k), (x + 40 * k, y + 34 * k), (x - 40 * k, y + 34 * k)], color)
    for i, dx in enumerate((-46, -22, 2, 26)):
        c.line([(x + dx * k, y - 22 * k), (x + (dx + 14) * k, y + 30 * k)], mix(color, "#6b3a10", .35), 5 * k)


def loaf(c, x, y, k, color, length=1.0, slashes=3):
    w = 84 * k * length
    c.ell((x - w, y - 38 * k, x + w, y + 46 * k), color)
    c.ell((x - w * .9, y - 38 * k, x + w * .9, y + 20 * k), mix(color, "#ffffff", .15))
    for i in range(slashes):
        dx = (i - (slashes - 1) / 2) * 34 * k * length
        c.line([(x + dx - 10 * k, y - 16 * k), (x + dx + 10 * k, y + 12 * k)], mix(color, "#6b3a10", .45), 5 * k)


def round_loaf(c, x, y, k, color, dark):
    c.ell((x - 74 * k, y - 50 * k, x + 74 * k, y + 52 * k), color)
    c.ell((x - 56 * k, y - 38 * k, x + 56 * k, y + 14 * k), mix(color, "#ffffff", .16))
    for dx in (-30, 0, 30):
        c.line([(x + dx * k, y - 26 * k), (x + dx * k, y + 18 * k)], dark, 5 * k)


def pie(c, x, y, k, crust, filling):
    c.ell((x - 80 * k, y - 34 * k, x + 80 * k, y + 54 * k), crust)
    c.ell((x - 62 * k, y - 26 * k, x + 62 * k, y + 38 * k), filling)
    for i in range(-3, 4):
        c.line([(x + i * 18 * k - 40 * k, y - 24 * k), (x + i * 18 * k + 40 * k, y + 36 * k)], crust, 6 * k)
        c.line([(x + i * 18 * k + 40 * k, y - 24 * k), (x + i * 18 * k - 40 * k, y + 36 * k)], crust, 6 * k)


def cake(c, x, y, k, sponge, cream, top):
    for i in range(3):
        c.rect((x - 66 * k, y + (-44 + i * 34) * k, x + 66 * k, y + (-14 + i * 34) * k), sponge if i % 2 == 0 else cream, 8 * k)
    c.ell((x - 66 * k, y - 58 * k, x + 66 * k, y - 28 * k), top)
    c.ell((x - 10 * k, y - 66 * k, x + 10 * k, y - 46 * k), "#c2453a")


def cake_slice(c, x, y, k, sponge, cream, top):
    c.poly([(x - 70 * k, y + 40 * k), (x + 70 * k, y + 40 * k), (x + 70 * k, y - 10 * k), (x - 10 * k, y - 50 * k), (x - 70 * k, y - 10 * k)], sponge)
    c.rect((x - 70 * k, y + 2 * k, x + 70 * k, y + 16 * k), cream)
    c.poly([(x - 70 * k, y - 10 * k), (x - 10 * k, y - 50 * k), (x + 70 * k, y - 10 * k), (x - 2 * k, y - 6 * k)], top)


def muffin(c, x, y, k, top, paper):
    c.poly([(x - 50 * k, y - 4 * k), (x + 50 * k, y - 4 * k), (x + 36 * k, y + 58 * k), (x - 36 * k, y + 58 * k)], paper)
    c.ell((x - 62 * k, y - 62 * k, x + 62 * k, y + 14 * k), top)
    for dx in (-24, 0, 24):
        c.line([(x + dx * k, y - 2 * k), (x + dx * k * .8, y + 56 * k)], mix(paper, "#ffffff", .4), 4 * k)


def cookie_disc(c, x, y, k, color, chips):
    c.ell((x - 62 * k, y - 50 * k, x + 62 * k, y + 54 * k), color)
    for dx, dy in [(-24, -16), (14, -28), (30, 4), (-10, 14), (-34, 26), (20, 28)]:
        c.ell((x + (dx - 8) * k, y + (dy - 8) * k, x + (dx + 8) * k, y + (dy + 8) * k), chips)


def sandwich(c, x, y, k, bread, fill1, fill2):
    c.rect((x - 80 * k, y + 14 * k, x + 80 * k, y + 50 * k), bread, 14 * k)
    c.rect((x - 84 * k, y - 4 * k, x + 84 * k, y + 14 * k), fill1, 6 * k)
    c.rect((x - 76 * k, y - 16 * k, x + 76 * k, y - 2 * k), fill2, 6 * k)
    c.rect((x - 80 * k, y - 50 * k, x + 80 * k, y - 14 * k), bread, 18 * k)


def eclair(c, x, y, k, dough, icing):
    c.rect((x - 88 * k, y - 26 * k, x + 88 * k, y + 34 * k), dough, 28 * k)
    c.rect((x - 84 * k, y - 34 * k, x + 84 * k, y + 4 * k), icing, 22 * k)


def stack(c, x, y, k, color, n=4, w=80):
    for i in range(n):
        c.ell((x - w * k, y + (34 - i * 18) * k, x + w * k, y + (58 - i * 18) * k), mix(color, "#6b3a10", .08 * (i % 2)))


def leaf(c, x, y, k, color, ang=0, size=40):
    c.poly([(x, y), (x + size * k * math.cos(ang - .5), y + size * k * math.sin(ang - .5)), (x + size * 1.6 * k * math.cos(ang), y + size * 1.6 * k * math.sin(ang)),
            (x + size * k * math.cos(ang + .5), y + size * k * math.sin(ang + .5))], color)


def flower(c, x, y, k, petal, center="#f3c64b", r=22, n=6):
    for i in range(n):
        a = i * 2 * math.pi / n
        c.ell((x + (math.cos(a) * r - r * .8) * k, y + (math.sin(a) * r - r * .8) * k, x + (math.cos(a) * r + r * .8) * k, y + (math.sin(a) * r + r * .8) * k), petal)
    c.ell((x - r * .55 * k, y - r * .55 * k, x + r * .55 * k, y + r * .55 * k), center)


def bouquet(c, x, y, k, petals, wrap="#e9dcc6", leafc="#5d8a5a"):
    for i, dx in enumerate((-46, -16, 14, 44, 0)):
        c.line([(x, y + 60 * k), (x + dx * k, y - 20 * k)], leafc, 6 * k)
    for ang in (-2.6, -0.5, -1.6):
        leaf(c, x, y + 10 * k, k, leafc, ang, 38)
    for i, (dx, dy) in enumerate([(-46, -24), (-16, -60), (14, -26), (44, -56), (0, -20)]):
        flower(c, x + dx * k, y + dy * k, k, petals[i % len(petals)])
    c.poly([(x - 56 * k, y + 8 * k), (x + 56 * k, y + 8 * k), (x + 16 * k, y + 96 * k), (x - 16 * k, y + 96 * k)], wrap)
    c.poly([(x - 56 * k, y + 8 * k), (x + 56 * k, y + 8 * k), (x + 40 * k, y + 34 * k), (x - 40 * k, y + 34 * k)], mix(wrap, "#ffffff", .4))


def potted(c, x, y, k, pot, kind):
    if kind == "orchid":
        c.line([(x, y + 10 * k), (x - 6 * k, y - 70 * k), (x + 40 * k, y - 96 * k)], "#5d8a5a", 5 * k)
        for dx, dy in [(-18, -70), (6, -82), (30, -92)]:
            flower(c, x + dx * k, y + dy * k, k, "#f3d7e6", "#c4527a", 18, 5)
        for ang in (-2.4, -0.7):
            leaf(c, x, y + 8 * k, k, "#4f8a52", ang, 44)
    elif kind == "succulent":
        for i, ang in enumerate([-3.4, -2.9, -2.4, -0.7, -0.2, 0.3, -1.57]):
            leaf(c, x, y + 8 * k, k, mix("#7fb08a", "#3f7a55", i / 7), ang, 34)
    elif kind == "plant":
        for ang in (-2.9, -2.2, -1.57, -0.9, -0.2):
            leaf(c, x, y + 8 * k, k, "#5d8a5a", ang, 42)
        flower(c, x - 16 * k, y - 30 * k, k, "#9a6fc8", "#f3c64b", 14, 5)
    c.poly([(x - 48 * k, y + 6 * k), (x + 48 * k, y + 6 * k), (x + 34 * k, y + 70 * k), (x - 34 * k, y + 70 * k)], pot)
    c.rect((x - 52 * k, y - 4 * k, x + 52 * k, y + 14 * k), mix(pot, "#ffffff", .15), 4 * k)


def arrangement_box(c, x, y, k, box, petals):
    c.rect((x - 80 * k, y + 4 * k, x + 80 * k, y + 70 * k), box, 10 * k)
    c.rect((x - 84 * k, y - 6 * k, x + 84 * k, y + 14 * k), mix(box, "#ffffff", .2), 6 * k)
    for i, (dx, dy) in enumerate([(-52, -14), (-18, -36), (18, -20), (52, -34), (0, -4), (-30, 0), (34, 2)]):
        flower(c, x + dx * k, y + dy * k, k, petals[i % len(petals)], r=24)


def card_item(c, x, y, k, paper, accent):
    c.rect((x - 70 * k, y - 50 * k, x + 70 * k, y + 50 * k), paper, 8 * k)
    c.rect((x - 70 * k, y - 50 * k, x, y + 50 * k), mix(paper, "#000000", .05), 8 * k)
    flower(c, x + 34 * k, y - 4 * k, k, accent, r=22)
    c.line([(x + 34 * k, y + 14 * k), (x + 34 * k, y + 44 * k)], "#5d8a5a", 4 * k)


def veg_pile(c, x, y, k, color, hi, shape="round", n=5):
    spots = [(-42, 20), (8, 26), (48, 16), (-18, -14), (30, -10), (2, -44)][:n]
    for dx, dy in spots:
        if shape == "round":
            c.ell((x + (dx - 30) * k, y + (dy - 28) * k, x + (dx + 30) * k, y + (dy + 28) * k), color)
            c.ell((x + (dx - 14) * k, y + (dy - 18) * k, x + (dx - 2) * k, y + (dy - 6) * k), hi)
        elif shape == "long":
            c.rect((x + (dx - 56) * k, y + (dy - 14) * k, x + (dx + 56) * k, y + (dy + 14) * k), color, 14 * k)
        elif shape == "oval":
            c.ell((x + (dx - 38) * k, y + (dy - 22) * k, x + (dx + 38) * k, y + (dy + 22) * k), color)
        elif shape == "small":
            c.ell((x + (dx - 16) * k, y + (dy - 16) * k, x + (dx + 16) * k, y + (dy + 16) * k), color)


def carrots(c, x, y, k):
    for i, dx in enumerate((-50, -16, 18, 52)):
        c.poly([(x + (dx - 18) * k, y - 40 * k), (x + (dx + 18) * k, y - 40 * k), (x + (dx + 2) * k, y + 72 * k)], "#e5822e")
        for a in (-2.2, -1.57, -0.9):
            leaf(c, x + dx * k, y - 40 * k, k, "#5d8a3a", a, 22)


def bottle_item(c, x, y, k, liquid, cap, label="#ffffff"):
    c.rect((x - 36 * k, y - 20 * k, x + 36 * k, y + 84 * k), mix("#ffffff", "#dde8ee", .55), 16 * k)
    c.rect((x - 32 * k, y + 8 * k, x + 32 * k, y + 80 * k), liquid, 12 * k)
    c.rect((x - 16 * k, y - 62 * k, x + 16 * k, y - 18 * k), mix("#ffffff", "#dde8ee", .55), 6 * k)
    c.rect((x - 20 * k, y - 76 * k, x + 20 * k, y - 58 * k), cap, 4 * k)
    c.rect((x - 26 * k, y + 28 * k, x + 26 * k, y + 52 * k), label, 4 * k)


def jar_item(c, x, y, k, fill, lid):
    c.rect((x - 54 * k, y - 34 * k, x + 54 * k, y + 70 * k), mix("#ffffff", "#e7e0cf", .6), 18 * k)
    c.rect((x - 48 * k, y - 10 * k, x + 48 * k, y + 64 * k), fill, 14 * k)
    c.rect((x - 58 * k, y - 56 * k, x + 58 * k, y - 32 * k), lid, 6 * k)
    c.rect((x - 34 * k, y + 8 * k, x + 34 * k, y + 40 * k), "#fff6df", 6 * k)


def cheese(c, x, y, k, color):
    c.poly([(x - 80 * k, y + 40 * k), (x + 80 * k, y + 40 * k), (x + 80 * k, y - 4 * k), (x - 80 * k, y - 50 * k)], color)
    c.poly([(x - 80 * k, y - 50 * k), (x + 80 * k, y - 4 * k), (x + 46 * k, y - 14 * k), (x - 50 * k, y - 60 * k)], mix(color, "#ffffff", .3))
    for dx, dy, r in [(-30, 4, 10), (20, 18, 8), (46, 4, 6)]:
        c.ell((x + (dx - r) * k, y + (dy - r) * k, x + (dx + r) * k, y + (dy + r) * k), mix(color, "#b8860b", .35))


def meat(c, x, y, k):
    c.ell((x - 86 * k, y - 40 * k, x + 86 * k, y + 50 * k), "#b23a3a")
    c.ell((x - 62 * k, y - 26 * k, x + 40 * k, y + 20 * k), "#cf5a55")
    c.ell((x + 20 * k, y - 10 * k, x + 70 * k, y + 28 * k), "#f0d6cf")


def eggs(c, x, y, k):
    c.rect((x - 96 * k, y + 10 * k, x + 96 * k, y + 70 * k), "#cdb997", 10 * k)
    for dx in (-60, -20, 20, 60):
        c.ell((x + (dx - 24) * k, y - 30 * k, x + (dx + 24) * k, y + 40 * k), "#f6ecd9")
    for dx in (-60, -20, 20, 60):
        c.ell((x + (dx - 10) * k, y - 18 * k, x + (dx - 2) * k, y - 4 * k), "#ffffff")


def fish_item(c, x, y, k):
    c.ell((x - 80 * k, y - 34 * k, x + 50 * k, y + 40 * k), "#d8e3ea")
    c.poly([(x + 40 * k, y + 3 * k), (x + 92 * k, y - 34 * k), (x + 92 * k, y + 40 * k)], "#b6c8d4")
    c.ell((x - 62 * k, y - 10 * k, x - 48 * k, y + 4 * k), "#3b2a1e")
    c.ell((x - 44 * k, y - 34 * k, x + 20 * k, y + 40 * k), "#eaf1f5")


def product_picture(cat, name, pal):
    """One product: a round soft background and the drawn item. The look of every picture is fixed by (category, name)."""
    wall, floor, acc, acc2, dark = pal
    rnd = random.Random(sum(ord(ch) * (i + 1) for i, ch in enumerate(cat + name)))
    t = rnd.uniform(0.0, 0.25)
    c = Canvas(800, 800)
    c.vgrad((0, 0, 800, 800), mix(acc2, "#ffffff", .55 + t / 2), mix(acc, "#ffffff", .25 + t))
    for _ in range(8):
        r = rnd.randint(18, 52)
        x, y = rnd.randint(40, 760), rnd.randint(40, 760)
        c.ell((x - r, y - r, x + r, y + r), mix(acc2, "#ffffff", rnd.uniform(.55, .85)))
    c.ell((120, 120, 680, 680), mix("#ffffff", acc2, .12))
    x, y, k = 400, 400, 2.3
    coffee_brown, milk, green, red = "#6f4426", "#f4ead9", "#6a9a4e", "#c2453a"
    d = {
        # coffee
        "espresso": lambda: cup(c, x, y, 2.0, "#3a2214"),
        "americano": lambda: cup(c, x, y, k, "#4a2c19"),
        "cappuccino": lambda: cup(c, x, y, k, "#9a6a45", foam=milk),
        "latte": lambda: glass(c, x, y, k, "#c79b73", "#f1e3d0"),
        "flat-white": lambda: cup(c, x, y, k, "#b07e57", foam=milk),
        "raf": lambda: cup(c, x, y, k, "#dcc29c", foam="#fff6e6"),
        "tea-black": lambda: cup(c, x, y, k, "#8a4a1e"),
        "tea-green": lambda: cup(c, x, y, k, "#b6c96a"),
        "tea-herbal": lambda: cup(c, x, y, k, "#d8c36a"),
        "cocoa": lambda: cup(c, x, y, k, "#5b3522", foam="#f1e3d0"),
        "croissant": lambda: croissant(c, x, y, 2.4, "#d99a4a"),
        "muffin": lambda: muffin(c, x, y, k, "#7a4a2a", "#e9c7a0"),
        "cookie": lambda: cookie_disc(c, x, y, k, "#d8a864", "#5b3522"),
        "cheesecake": lambda: cake_slice(c, x, y, k, "#f3dfb0", "#fff3d0", "#e8b4a0"),
        "sandwich": lambda: sandwich(c, x, y, k, "#d9a866", green, "#e8a46a"),
        "toast-avocado": lambda: (c.rect((x - 96 * k * .8, y - 50 * k, x + 96 * k * .8, y + 50 * k), "#d9a866", 16 * k), c.rect((x - 84 * k * .8, y - 38 * k, x + 84 * k * .8, y + 38 * k), "#9cc070", 12 * k), c.ell((x - 30 * k, y - 18 * k, x + 30 * k, y + 28 * k), "#f6ecd9"), c.ell((x - 12 * k, y - 4 * k, x + 12 * k, y + 16 * k), "#f0b43a")),
        "granola": lambda: bowl(c, x, y, k, "#d8b070", spots=[(-30, 0, 8, "#7a4a2a"), (10, 4, 8, "#b05a3a"), (34, -2, 7, "#c2453a"), (-6, -4, 6, "#f4ead9")]),
        # bakery
        "loaf": lambda: loaf(c, x, y, 2.3, "#d9a05a"),
        "baguette": lambda: (loaf(c, x, y - 40 * 2.3, 2.3, "#d9a05a", 1.25, 4), loaf(c, x, y + 30 * 2.3, 2.3, "#cf954f", 1.25, 4)),
        "rye-bread": lambda: loaf(c, x, y, 2.3, "#8a5a36", 1.0, 2),
        "bun": lambda: round_loaf(c, x, y, 2.2, "#d99a4a", "#b27430"),
        "cinnamon-roll": lambda: (c.ell((x - 82 * k, y - 50 * k, x + 82 * k, y + 54 * k), "#d99a4a"), c.ell((x - 62 * k, y - 38 * k, x + 62 * k, y + 38 * k), "#e9b676"), c.ell((x - 40 * k, y - 26 * k, x + 40 * k, y + 26 * k), "#a9622d"), c.ell((x - 20 * k, y - 14 * k, x + 20 * k, y + 14 * k), "#e9b676")),
        "pie-apple": lambda: pie(c, x, y, k, "#d99a4a", "#c9a15a"),
        "pie-cabbage": lambda: pie(c, x, y, k, "#cf954f", "#b9c97a"),
        "pie-meat": lambda: pie(c, x, y, k, "#c98a4a", "#a8603a"),
        "cake-honey": lambda: cake(c, x, y, k, "#d9a05a", "#fff0cf", "#e9b676"),
        "cake-chocolate": lambda: cake(c, x, y, k, "#5b3522", "#f1dfc8", "#3a2214"),
        "eclair": lambda: eclair(c, x, y, k, "#d99a4a", "#6b3a22"),
        "cookies": lambda: (cookie_disc(c, x - 50 * k, y + 10 * k, 1.4, "#e0b77a", "#e0b77a"), cookie_disc(c, x + 40 * k, y - 10 * k, 1.4, "#d9a865", "#d9a865"), cookie_disc(c, x, y + 50 * k, 1.4, "#e7c48c", "#e7c48c")),
        # canteen
        "soup-borsch": lambda: bowl(c, x, y, k, "#b03a2e", spots=[(-20, 0, 12, "#fff6ea"), (24, 4, 7, "#5d8a3a")]),
        "soup-chicken": lambda: bowl(c, x, y, k, "#e8c872", spots=[(-26, 0, 7, "#f6ecd9"), (10, 4, 7, "#f6ecd9"), (34, -2, 6, "#5d8a3a")]),
        "cutlet": lambda: (plate(c, x, y, k), c.ell((x - 50 * k, y - 10 * k, x + 10 * k, y + 34 * k), "#b5763a"), c.ell((x + 4 * k, y - 4 * k, x + 54 * k, y + 32 * k), "#f3e6c8"), c.ell((x - 20 * k, y - 34 * k, x + 4 * k, y - 18 * k), "#5d8a3a")),
        "goulash": lambda: (plate(c, x, y, k), c.ell((x - 60 * k, y - 12 * k, x + 50 * k, y + 34 * k), "#9a4a2a"), c.ell((x - 30 * k, y - 4 * k, x + 20 * k, y + 22 * k), "#b5653a")),
        "fish": lambda: (plate(c, x, y, k), fish_item(c, x - 6 * k, y + 8 * k, k * .6)),
        "buckwheat": lambda: (plate(c, x, y, k), c.ell((x - 52 * k, y - 18 * k, x + 52 * k, y + 34 * k), "#7a4a2a"), c.ell((x - 34 * k, y - 26 * k, x + 34 * k, y + 14 * k), "#8a5a36")),
        "rice": lambda: (plate(c, x, y, k), c.ell((x - 52 * k, y - 18 * k, x + 52 * k, y + 34 * k), "#f6f0e0"), c.ell((x - 34 * k, y - 28 * k, x + 34 * k, y + 12 * k), "#fffaf0")),
        "mashed-potato": lambda: (plate(c, x, y, k), c.ell((x - 52 * k, y - 18 * k, x + 52 * k, y + 34 * k), "#f3e3a8"), c.ell((x - 34 * k, y - 30 * k, x + 34 * k, y + 10 * k), "#f8ecc0"), c.ell((x - 10 * k, y - 34 * k, x + 10 * k, y - 22 * k), "#f0b43a")),
        "pasta": lambda: (plate(c, x, y, k), c.ell((x - 52 * k, y - 18 * k, x + 52 * k, y + 34 * k), "#ecc66a"), c.ell((x - 34 * k, y - 28 * k, x + 34 * k, y + 12 * k), "#f3d27e"), c.ell((x - 10 * k, y - 28 * k, x + 18 * k, y - 10 * k), red)),
        "salad-vinaigrette": lambda: bowl(c, x, y, k, "#8a2a4a", spots=[(-26, 0, 10, "#a8385a"), (14, 4, 9, "#d9a05a"), (36, -4, 7, "#5d8a3a")]),
        "salad-fresh": lambda: bowl(c, x, y, k, "#7cb254", spots=[(-26, 0, 10, red), (14, 4, 9, "#f3e36a"), (36, -4, 7, "#fffaf0")]),
        "compote": lambda: glass(c, x, y, k, "#c2453a", "#e9705a"),
        "tea": lambda: cup(c, x, y, k, "#a0561e"),
        "pancakes": lambda: (stack(c, x, y, k, "#e0a85a", 4), c.ell((x - 30 * k, y - 34 * k, x + 30 * k, y - 10 * k), "#fff6e0"), c.ell((x - 8 * k, y - 38 * k, x + 18 * k, y - 22 * k), "#c2453a")),
        # flowers
        "bouquet-roses": lambda: bouquet(c, x, y - 20, 2.0, ["#e4708f", "#f3a7bd", "#c4527a"]),
        "bouquet-mixed": lambda: bouquet(c, x, y - 20, 2.0, ["#f3c64b", "#c4527a", "#ffffff", "#9a6fc8"]),
        "bouquet-tulips": lambda: bouquet(c, x, y - 20, 2.0, ["#e85a6a", "#f3c64b", "#f3a7bd"], wrap="#dfe9d2"),
        "bouquet-peonies": lambda: bouquet(c, x, y - 20, 2.0, ["#f0a6bd", "#f7d0dc", "#e4708f"]),
        "arrangement-box": lambda: arrangement_box(c, x, y, 2.0, "#c9a27a", ["#e4708f", "#f3c64b", "#ffffff", "#9a6fc8"]),
        "orchid": lambda: potted(c, x, y - 10, 2.2, "#d9d2c2", "orchid"),
        "succulent": lambda: potted(c, x, y - 10, 2.2, "#c2785a", "succulent"),
        "plant-pot": lambda: potted(c, x, y - 10, 2.2, "#7a8fb5", "plant"),
        "card": lambda: card_item(c, x, y, 2.2, "#fff6e6", "#e4708f"),
        # farm
        "tomatoes": lambda: veg_pile(c, x, y, 2.0, "#d9442e", "#f2907f"),
        "cucumbers": lambda: veg_pile(c, x, y, 1.9, "#4f8a3a", "#7ab05a", "long", 4),
        "potatoes": lambda: veg_pile(c, x, y, 2.0, "#c7a26a", "#dec08a", "oval", 5),
        "carrots": lambda: carrots(c, x, y, 2.1),
        "apples": lambda: veg_pile(c, x, y, 2.0, "#c2453a", "#e8806f"),
        "berries": lambda: veg_pile(c, x, y, 2.2, "#7a2a5a", "#b5649a", "small", 6),
        "milk": lambda: bottle_item(c, x, y - 10, 2.2, "#fbf6ec", "#3f7ab0"),
        "cheese": lambda: cheese(c, x, y, 2.0, "#f0c25a"),
        "meat-beef": lambda: meat(c, x, y, 2.0),
        "honey": lambda: jar_item(c, x, y - 10, 2.0, "#e8a020", "#8a5a36"),
        "eggs": lambda: eggs(c, x, y, 2.0),
        "bread-farm": lambda: round_loaf(c, x, y, 2.2, "#c98a4a", "#8a5a36"),
    }
    d[name]()
    return c


def shop_logo(cat):
    pal = SHOP_PALETTES[cat]
    wall, floor, acc, acc2, dark = pal
    c = Canvas(600, 600)
    c.vgrad((0, 0, 600, 600), mix(acc, "#ffffff", .1), mix(acc, dark, .35))
    c.ell((60, 60, 540, 540), mix("#ffffff", acc2, .12))
    inner = product_picture(cat, SHOP_LOGO_PRODUCT[cat], pal).im.resize((300 * SS, 300 * SS), Image.LANCZOS)
    c.im.paste(inner, (150 * SS, 150 * SS))
    return c


def shop_scene(cat, variant, seed):
    rnd = random.Random(seed)
    pal = SHOP_PALETTES[cat]
    wall, floor, acc, acc2, dark = pal
    W, H = 1200, 800
    c = Canvas(W, H)
    c.vgrad((0, 0, W, 560), mix(wall, "#ffffff", .3), wall)
    c.vgrad((0, 560, W, H), floor, mix(floor, dark, .25))
    items = SHOP_PRODUCTS[cat]

    def item(i, x, y, k):
        inner = product_picture(cat, items[i % len(items)], pal).im
        size = int(300 * k * SS)
        c.im.paste(inner.resize((size, size), Image.LANCZOS), (int(x * SS - size / 2), int(y * SS - size / 2)))

    if variant == 1:  # counter with three showpieces under a window
        c.rect((80, 80, 420, 400), "#ffffff", 6); c.vgrad((92, 92, 408, 388), "#cfe6f5", "#f6fbff")
        c.line([(250, 92), (250, 388)], "#ffffff", 8)
        c.rect((60, 470, 1140, 560), dark, 12); c.rect((40, 450, 1160, 480), acc, 8)
        for i, x in enumerate((360, 640, 920)):
            item(rnd.randint(0, 40) + i, x, 340, 0.95)
    elif variant == 2:  # shelves with goods
        for row, y in enumerate((250, 520)):
            c.rect((70, y + 120, 1130, y + 142), dark, 6)
            for i in range(4):
                item(row * 4 + i * 2 + variant, 210 + i * 260, y + 10, 0.72)
    elif variant == 3:  # a table and a plant
        c.rect((120, 420, 1080, 470), dark, 14); c.rect((180, 470, 210, 700), dark); c.rect((990, 470, 1020, 700), dark)
        item(3, 420, 330, 0.9); item(7, 780, 340, 0.85)
        prop(c, "plant", 1110, 330, 1.5, pal)
    elif variant == 4:  # the sign and the door
        c.rect((380, 60, 820, 170), acc, 14); c.rect((400, 76, 800, 154), mix(acc2, "#ffffff", .5), 10)
        inner = product_picture(cat, SHOP_LOGO_PRODUCT[cat], pal).im.resize((100 * SS, 100 * SS), Image.LANCZOS)
        c.im.paste(inner, (550 * SS, 66 * SS))
        c.rect((460, 230, 740, 640), mix(dark, "#ffffff", .15), 10); c.rect((480, 250, 720, 620), mix(acc2, "#ffffff", .6), 6)
        c.ell((690, 430, 710, 450), dark)
    elif variant == 5:  # flat-lay of goods on a tinted surface
        c.vgrad((0, 0, W, H), mix(acc2, "#ffffff", .55), mix(acc2, "#ffffff", .25))
        for i in range(6):
            item(i * 3 + 1, 200 + (i % 3) * 400, 230 + (i // 3) * 330, 0.85)
    else:  # waiting corner
        c.rect((230, 400, 870, 620), acc, 50); c.rect((200, 330, 900, 450), mix(acc, "#ffffff", .15), 50)
        c.rect((280, 620, 310, 680), dark); c.rect((790, 620, 820, 680), dark)
        c.line([(1000, 120), (1000, 560)], dark, 8); c.poly([(930, 200), (1070, 200), (1040, 120), (960, 120)], "#f7d27a")
        item(2, 560, 280, 0.6)
    return c


def build_shops(add):
    """Draws logos, gallery photos and products of the five demo shops through `add` (the same writer as for the salons)."""
    for i, cat in enumerate(SHOP_CATEGORIES):
        add(f"logo.shop.{cat}", "logo", f"logos/shop-{cat}.jpg", 600, 600, 600, shop_logo(cat))
        for v in range(1, 7):
            add(f"photo.shop.{cat}.{v}", "photo", f"photos/shop-{cat}-{v}.jpg", 1200, 1200, 800, shop_scene(cat, v, 500 + i * 10 + v), q=72)
        for name in SHOP_PRODUCTS[cat]:
            cvs = product_picture(cat, name, SHOP_PALETTES[cat])
            add(f"product.{cat}.{name}", "product", f"products/{cat}/{name}.jpg", 800, 800, 800, cvs, q=70,
                thumb=(f"products/{cat}/{name}.thumb.jpg", 320))


ROW = "| `{key}` | `{rel}` | Нарисовано программно `tools/showcase-assets/generate.py` (автор — проект) | Без третьих лиц, авторских прав нет | 2026-10-01 |"


def is_shop_key(key):
    return key.startswith("product.") or ".shop." in key


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", choices=["all", "salons", "shops"], default="all")
    mode = parser.parse_args().only

    manifest_path = ROOT / "manifest.json"
    existing = json.loads(manifest_path.read_text(encoding="utf-8"))["assets"] if manifest_path.exists() else []
    salon_assets, shop_assets, total = [], [], 0

    def writer(target):
        def add(key, role, rel, cw, w, h, cvs, q=80, thumb=None):
            nonlocal total
            size = cvs.save(ROOT / rel, w, h, q)
            total += size
            entry = {"key": key, "role": role, "file": rel, "thumbnail": None, "width": w, "height": h}
            assert size <= 250 * 1024, (rel, size)
            if thumb is not None:
                thumb_rel, thumb_size = thumb
                total += cvs.save(ROOT / thumb_rel, thumb_size, thumb_size, q)
                entry["thumbnail"] = thumb_rel
            target.append(entry)
        return add

    if mode in ("all", "salons"):
        add = writer(salon_assets)
        for i, cat in enumerate(CATEGORIES):
            add(f"logo.{cat}", "logo", f"logos/{cat}.jpg", 600, 600, 600, logo(cat))
            for v in range(1, 7):
                add(f"photo.{cat}.{v}", "photo", f"photos/{cat}-{v}.jpg", 1200, 1200, 800, scene(cat, v, i * 10 + v))
        for key in SERVICE_CATEGORY:
            add(f"service.{key}", "service", f"services/{key}.jpg", 800, 800, 600, service(key))
    else:
        salon_assets = [a for a in existing if not is_shop_key(a["key"])]

    if mode in ("all", "shops"):
        build_shops(writer(shop_assets))
        missing = [n for cat, names in SHOP_PRODUCTS.items() for n in names]
        assert len(shop_assets) == 5 * 7 + len(missing), len(shop_assets)
    else:
        shop_assets = [a for a in existing if is_shop_key(a["key"])]

    assets = salon_assets + shop_assets
    total = sum((ROOT / a["file"]).stat().st_size + ((ROOT / a["thumbnail"]).stat().st_size if a["thumbnail"] else 0) for a in assets)
    shops_total = sum((ROOT / a["file"]).stat().st_size + ((ROOT / a["thumbnail"]).stat().st_size if a["thumbnail"] else 0) for a in shop_assets)
    assert total <= 8 * 1024 * 1024, total
    # Cycle 35 (§35.11): the shops' pictures add at most 3 MB to the set.
    assert shops_total <= 3 * 1024 * 1024, shops_total
    manifest_path.write_text(json.dumps({"version": 1, "assets": assets}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    rows = []
    for a in assets:
        rows.append(ROW.format(key=a["key"], rel=a["file"]))
        if a["thumbnail"]:
            rows.append(ROW.format(key=a["key"] + " (миниатюра)", rel=a["thumbnail"]))
    lic = (ROOT / "LICENSES.md").read_text(encoding="utf-8")
    rules = lic.split("## Журнал файлов")[0]
    files = len(assets) + sum(1 for a in assets if a["thumbnail"])
    rules = rules.replace(rules[rules.index("**Состояние:"):rules.index("## Правила набора")],
                          f"**Состояние: {files} файлов, {total // 1024} КБ** (из них картинки магазинов «Заказов» — {shops_total // 1024} КБ). "
                          "Плоские иллюстрации нарисованы программно, фотографий и людей нет.\n\n")
    (ROOT / "LICENSES.md").write_text(rules + "## Журнал файлов\n\n| Ключ | Файл | Источник | Лицензия | Дата проверки |\n|---|---|---|---|---|\n" + "\n".join(rows) + "\n", encoding="utf-8")
    print(f"{len(assets)} manifest entries, {files} files, {total // 1024} KB (shops: {shops_total // 1024} KB)")


if __name__ == "__main__":
    main()
