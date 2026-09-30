"""QA цикл 25, бенч §515: p95 маршрутов истории, сводки, листа сборки, карточки покупателя и каталога на 200 000 заказов.
Порядок: 1) поднять API (Testing, Release) на БД с данными seed.py + seed_orders.sql + seed_shops.sql,
2) BENCH_BASE=http://localhost:5125 python3 bench25.py (читает seed25.json: {tok, shop}). Одиночные запросы подряд, по 60 после 5 прогревочных;
для каталога кеш выключен (Orders__CatalogCacheSeconds=0) — худший случай. Бюджеты — SPEC цикла 25 §6.
"""
import json, os, time, urllib.request, statistics, sys, datetime as dt
B = os.environ.get("BENCH_BASE", "http://localhost:5125")
seed = json.load(open("seed25.json")); TOK = seed["tok"]; SHOP = seed["shop"]
def req(m, p, body=None, auth=True):
    h = {"Content-Type": "application/json"}
    if auth: h["Authorization"] = "Bearer " + TOK
    r = urllib.request.Request(B + p, data=json.dumps(body).encode() if body is not None else None, headers=h, method=m)
    t = time.perf_counter()
    try:
        x = urllib.request.urlopen(r); d = x.read(); st = x.status
    except urllib.error.HTTPError as e:
        d = e.read(); st = e.code
    return time.perf_counter() - t, st, d
def bench(name, m, p, body=None, auth=True, n=60, budget=None):
    for _ in range(5): req(m, p, body, auth)
    ts = []
    sts = set()
    for _ in range(n):
        t, st, d = req(m, p, body, auth); ts.append(t * 1000); sts.add(st)
    ts.sort()
    p95 = ts[int(len(ts) * 0.95) - 1]
    print(f"{name:52s} status={sorted(sts)} p50={statistics.median(ts):7.1f} p95={p95:7.1f} max={ts[-1]:7.1f} ms  budget={budget}  {'OK' if budget and p95 < budget else 'FAIL' if budget else ''}")
    return p95
today = dt.date.fromisoformat(json.loads(req("GET", "/api/storefront/qa-shop-25", auth=False)[2])["date"])
h = f"/api/shops/{SHOP}"
bench("history default (7 days, no filters)", "POST", h + "/order-history", {}, budget=500)
bench("history 366 days, no filters", "POST", h + "/order-history", {"period": "Custom", "from": str(today - dt.timedelta(days=365)), "to": str(today)}, budget=500)
bench("history 366 d + 4 digits phone", "POST", h + "/order-history", {"period": "Custom", "from": str(today - dt.timedelta(days=365)), "to": str(today), "customer": "0123"}, budget=500)
bench("history 366 d + name substring", "POST", h + "/order-history", {"period": "Custom", "from": str(today - dt.timedelta(days=365)), "to": str(today), "customer": "иванова"}, budget=500)
bench("history 366 d + all filters", "POST", h + "/order-history", {"period": "Custom", "from": str(today - dt.timedelta(days=365)), "to": str(today), "statuses": ["Issued", "NotPickedUp", "Rejected"], "customer": "анна", "amountFrom": 400, "amountTo": 900, "sort": "PickupAsc"}, budget=500)
bench("history 366 d + number", "POST", h + "/order-history", {"period": "Custom", "from": str(today - dt.timedelta(days=365)), "to": str(today), "number": 1200}, budget=500)
bench("history 366 d, page 300 (deep)", "POST", h + "/order-history", {"period": "Custom", "from": str(today - dt.timedelta(days=365)), "to": str(today), "page": 300}, budget=500)
bench("summary today", "GET", h + "/summary", budget=1000)
bench("summary 31 days (Last30Days)", "GET", h + "/summary?period=Last30Days", budget=1000)
bench("summary 31 days + compare", "GET", h + "/summary?period=Last30Days&compare=true", budget=1000)
bench("summary 366 days", "GET", h + f"/summary?period=Custom&from={today - dt.timedelta(days=365)}&to={today}", budget=3000)
bench("summary 366 days + compare + top=Quantity", "GET", h + f"/summary?period=Custom&from={today - dt.timedelta(days=365)}&to={today}&compare=true&top=Quantity", budget=3000)
bench("picklist today (~547 orders)", "GET", h + "/picklist", budget=300)
bench("picklist today, includeNew=false", "GET", h + "/picklist?includeNew=false", budget=300)
# customer card: an order of a phone with ~67 orders
t, st, d = req("POST", h + "/order-history", {"customer": "0000012"})
row = json.loads(d)["items"][0]
bench("customer card (~67 orders)", "GET", h + f"/customers/{row['orderId']}", budget=300)
bench("customer note GET", "GET", h + f"/customers/{row['orderId']}/note", budget=300)
bench("catalog, city with 201 shops, anonymous (no cache)", "GET", "/api/goods/catalog", auth=False, budget=300)
bench("catalog, page 5", "GET", "/api/goods/catalog?page=5", auth=False, budget=300)
bench("catalog, search+openNow", "GET", "/api/goods/catalog?search=%D0%BC%D0%B0%D0%B3%D0%B0%D0%B7%D0%B8%D0%BD&openNow=true", auth=False, budget=300)
