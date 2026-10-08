#!/usr/bin/env python3
"""US-39-23 / DO-39-04: p95 публичного каталога домов (GET /api/stays/public/catalog) на ~500 опубликованных домах.

Только чтение. НЕ запускать на боевой машине: гоняйте на изолированном стенде (docker compose cycle-39 с отдельной БД),
где уже есть >= 500 опубликованных домов (см. README.md). Зависимости: pip install requests.

  bench_catalog.py --base http://127.0.0.1:5039 [--n 200] [--warmup 20] [--budget-ms 500]
Сценарии: без фильтров, с датами+гостями, с maxPricePerNight, страница 5. Код возврата 1, если p95 любого сценария > budget.
"""
import argparse, datetime as dt, statistics, sys, time
import requests

ap = argparse.ArgumentParser()
ap.add_argument("--base", required=True)
ap.add_argument("--n", type=int, default=200)
ap.add_argument("--warmup", type=int, default=20)
ap.add_argument("--budget-ms", type=float, default=500)
a = ap.parse_args()

d0 = dt.date.today() + dt.timedelta(days=14)
cases = {
    "no-filter": {},
    "dates+guests": {"checkIn": d0.isoformat(), "checkOut": (d0 + dt.timedelta(days=3)).isoformat(), "guests": 4},
    "max-price": {"maxPricePerNight": 8000},
    "page-5": {"page": 5, "pageSize": 20},
}
s = requests.Session()
probe = s.get(f"{a.base}/api/stays/public/catalog", params={"pageSize": 1}, timeout=30)
probe.raise_for_status()
print("probe:", {k: v for k, v in probe.json().items() if k != "items"})
bad = False
for name, params in cases.items():
    for _ in range(a.warmup):
        s.get(f"{a.base}/api/stays/public/catalog", params=params, timeout=30)
    t = []
    for _ in range(a.n):
        t0 = time.perf_counter()
        r = s.get(f"{a.base}/api/stays/public/catalog", params=params, timeout=30)
        r.raise_for_status()
        t.append((time.perf_counter() - t0) * 1000)
    t.sort()
    p95 = t[int(len(t) * 0.95) - 1]
    print(f"{name:14} p50={statistics.median(t):7.1f} p95={p95:7.1f} max={t[-1]:7.1f} ms")
    bad |= p95 > a.budget_ms
sys.exit(1 if bad else 0)
