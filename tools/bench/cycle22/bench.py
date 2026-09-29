#!/usr/bin/env python3
"""Benchmark driver for cycle 22 BEFORE/AFTER.

  bench.py count    -> APIs must talk to Postgres through sqlproxy.py (run-api.sh <v> <port> <proxyport>);
                       per case: 2 warm-up requests, then 5 requests, each with the proxy counters
                       read before/after -> results/count.json, SQL texts -> sql/<version>/<case>.sql
  bench.py latency  -> APIs talk to Postgres directly; per case and version: 20 warm-up + 200 measured
                       sequential requests (versions alternate per case) -> results/latency.json
  bench.py compare  -> fetches masters/clients + stats (+ reports, lists) from both, replaces API-minted
                       ids (users/company/services) by stable keys, compares JSON -> results/compare.json
  bench.py create   -> ~20 booking creations per version on identical free slots, SQL counted via proxy
                       (APIs through proxy) -> results/create.json

Only one request is in flight at a time (sequential) — the SQL counters are process-wide, so this is
what makes "delta between two reads" = "SQL of that request".
"""
import json
import os
import statistics
import subprocess
import sys
import time

import requests

VERSIONS = {"before": {"port": 5601, "ctl": 7601}, "after": {"port": 5602, "ctl": 7602}}
PASSWORD = "Password123!"
OUT = "/tmp/bench/results"
os.makedirs(OUT, exist_ok=True)


def psql(db, sql):
    r = subprocess.run(["psql", "-h", "localhost", "-U", "postgres", "-d", db, "-At", "-c", sql],
                       capture_output=True, text=True, env={"PGPASSWORD": "postgres", "PATH": "/usr/bin:/bin"})
    if r.returncode:
        raise SystemExit(r.stderr)
    return r.stdout.strip()


def context(v):
    """Per-version ids and tokens."""
    db = f"bench_{v}"
    base = f"http://127.0.0.1:{VERSIONS[v]['port']}"
    s = requests.Session()

    def login(phone):
        r = s.post(f"{base}/api/auth/login", json={"phone": phone, "password": PASSWORD})
        r.raise_for_status()
        return r.json()

    ctx = {"base": base, "db": db, "s": s}
    ctx["company"] = psql(db, "select \"Id\" from \"Companies\" where \"Slug\"='bench-salon'")
    m = login("+79000000101")
    ctx["master"], ctx["master_id"] = m["token"], m["userId"]
    ctx["owner"] = login("+79000000001")["token"]
    ctx["client"] = login("+79000000201")["token"]
    ctx["admin"] = requests.post(f"{base}/api/auth/login",
                                 json={"phone": "+70000000009", "password": "SuperAdmin123!"}).json()["token"]
    ctx["service"] = psql(db, f"select \"Id\" from \"Services\" where \"CompanyId\"='{ctx['company']}' and \"Name\"='Стрижка женская'")
    ctx["booking"] = psql(db, f"""select b."Id" from "Bookings" b where b."MasterId"='{ctx['master_id']}'
        and b."Date"='2026-09-29' order by b."StartTime" limit 1""")
    # id -> stable key map for JSON comparison
    idmap = {}
    for line in psql(db, "select \"Id\", 'user:'||\"PhoneNumber\" from \"AspNetUsers\" where \"Id\" not like 'bench-%'").splitlines():
        k, val = line.split("|")
        idmap[k] = val
    for line in psql(db, f"select \"Id\", 'svc:'||\"Name\" from \"Services\" where \"CompanyId\"='{ctx['company']}'").splitlines():
        k, val = line.split("|")
        idmap[k] = val
    idmap[ctx["company"]] = "company:bench-salon"
    ctx["idmap"] = idmap
    return ctx


def cases(ctx):
    c, mid, svc = ctx["company"], ctx["master_id"], ctx["service"]
    mc = f"/api/masters/clients?companyId={c}&pageSize=20"
    return [
        # name, role, path
        ("masters/clients p1", "master", f"{mc}&page=1"),
        ("masters/clients p60 (middle)", "master", f"{mc}&page=60"),
        ("masters/clients search phone", "master", f"{mc}&page=1&search=%2B7%20911%20000-00-05"),
        ("masters/clients search name", "master", f"{mc}&page=1&search=%D0%98%D0%B2%D0%B0%D0%BD"),
        ("company stats 1 month", "owner", f"/api/companies/{c}/stats?from=2026-08-01T00:00:00&to=2026-08-31T23:59:59"),
        ("company stats 1 year", "owner", f"/api/companies/{c}/stats?from=2025-09-01T00:00:00&to=2026-08-31T23:59:59"),
        ("reports/masters 1 month", "owner", f"/api/reports/masters?companyId={c}&from=2026-08-01&to=2026-08-31"),
        ("reports/masters 1 year", "owner", f"/api/reports/masters?companyId={c}&from=2025-09-01&to=2026-08-31"),
        ("bookings/master 1 day", "master", "/api/bookings/master?date=2026-09-29&to=2026-09-29"),
        ("bookings/master 1 month", "master", "/api/bookings/master?date=2026-09-01&to=2026-09-30"),
        ("bookings/client (~150)", "client", "/api/bookings/client"),
        ("bookings/{id} (master)", "master", f"/api/bookings/{ctx['booking']}"),
        ("bookings/availability 30d (anon)", None,
         f"/api/bookings/availability?companyId={c}&masterId={mid}&serviceId={svc}&from=2026-09-28&to=2026-10-27"),
        ("bookings/slots 1 day (anon)", None,
         f"/api/bookings/slots?companyId={c}&masterId={mid}&serviceId={svc}&date=2026-09-30"),
        ("bookings/slots 1 day (client JWT)", "client",
         f"/api/bookings/slots?companyId={c}&masterId={mid}&serviceId={svc}&date=2026-09-30"),
        ("admin/companies p1", "admin", "/api/admin/companies?page=1&pageSize=20"),
        ("admin/bookings (no filter, top 500)", "admin", "/api/admin/bookings"),
        ("admin/bookings company+month", "admin", f"/api/admin/bookings?companyId={c}&from=2026-08-01&to=2026-08-31"),
        ("admin/notification-channels p1", "admin", "/api/admin/notification-channels?page=1&pageSize=20"),
        ("profile (trivial auth, F20)", "master", "/api/profile"),
        ("legal/documents (anon baseline)", None, "/api/legal/documents"),
    ]


def get(ctx, role, path):
    h = {"Authorization": f"Bearer {ctx[role]}"} if role else {}
    return ctx["s"].get(ctx["base"] + path, headers=h)


def ctl(v, what):
    return requests.get(f"http://127.0.0.1:{VERSIONS[v]['ctl']}/{what}").json()


def cmd_count():
    res = {}
    for v in VERSIONS:
        ctx = context(v)
        os.makedirs(f"/tmp/bench/sql/{v}", exist_ok=True)
        for name, role, path in cases(ctx):
            for _ in range(2):
                get(ctx, role, path)
            samples = []
            for _ in range(5):
                ctl(v, "reset")
                r = get(ctx, role, path)
                st = ctl(v, "stats")
                samples.append({"status": r.status_code, "stmts": st["stmts"] + st["queries"], "roundtrips": st["syncs"],
                                "rows": st["rows"], "bytes_from_db": st["bytes_from_db"], "resp_bytes": len(r.content),
                                "resets": st["resets"]})
                texts = st["texts"]
            fn = name.replace("/", "_").replace(" ", "_")
            with open(f"/tmp/bench/sql/{v}/{fn}.sql", "w") as f:
                f.write("\n\n-- ---\n".join(texts))
            med = {k: statistics.median(s_[k] for s_ in samples) for k in samples[0]}
            res.setdefault(name, {})[v] = med
            print(f"{v:6} {name:40} {med}", flush=True)
    json.dump(res, open(f"{OUT}/count.json", "w"), ensure_ascii=False, indent=1)


def pct(xs, p):
    xs = sorted(xs)
    k = (len(xs) - 1) * p / 100
    f = int(k)
    return xs[f] + (xs[min(f + 1, len(xs) - 1)] - xs[f]) * (k - f)


def cmd_latency(n=200, warm=20, only=None):
    ctxs = {v: context(v) for v in VERSIONS}
    res = {}
    names = [c[0] for c in cases(ctxs["before"])]
    for i, name in enumerate(names):
        if only and only not in name:
            continue
        order = list(VERSIONS)[::-1] if os.environ.get("REVERSE") else list(VERSIONS)
        for v in order:
            ctx = ctxs[v]
            _, role, path = cases(ctx)[i]
            for _ in range(warm):
                get(ctx, role, path)
            ts = []
            for _ in range(n):
                t0 = time.perf_counter()
                r = get(ctx, role, path)
                ts.append((time.perf_counter() - t0) * 1000)
                assert r.status_code == 200, (v, name, r.status_code, r.text[:300])
            res.setdefault(name, {})[v] = {"n": n, "p50": pct(ts, 50), "p95": pct(ts, 95), "mean": statistics.mean(ts),
                                           "min": min(ts), "max": max(ts)}
            print(f"{v:6} {name:40} p50={pct(ts, 50):8.2f} p95={pct(ts, 95):8.2f} mean={statistics.mean(ts):8.2f}", flush=True)
    fn = f"{OUT}/latency{'-' + only.replace('/', '_').replace(' ', '_') if only else ''}.json"
    json.dump(res, open(fn, "w"), ensure_ascii=False, indent=1)


def normalize(obj, idmap):
    s = json.dumps(obj, ensure_ascii=False, sort_keys=True)
    for k, val in idmap.items():
        s = s.replace(k, val)
    return json.loads(s)


def canon(x):
    """Order-insensitive canonical form (for lists the old code built without explicit ORDER BY)."""
    if isinstance(x, dict):
        return {k: canon(v) for k, v in x.items()}
    if isinstance(x, list):
        return sorted((canon(v) for v in x), key=lambda v: json.dumps(v, ensure_ascii=False, sort_keys=True))
    return x


def cmd_compare():
    ctxs = {v: context(v) for v in VERSIONS}
    c_b, c_a = cases(ctxs["before"]), cases(ctxs["after"])
    extra = [("masters/clients p%d" % p, "master", f"/api/masters/clients?companyId={{c}}&pageSize=20&page={p}")
             for p in (2, 30, 120, 121)]
    extra += [("masters/clients search partial phone", "master",
               "/api/masters/clients?companyId={c}&pageSize=100&page=1&search=91100000"),
              ("masters/clients search guest phone", "master",
               "/api/masters/clients?companyId={c}&pageSize=20&page=1&search=8%20922%20000-00-07"),
              ("company stats 2y", "owner", "/api/companies/{c}/stats?from=2024-01-01T00:00:00&to=2026-12-31T00:00:00")]
    out = {}
    pairs = list(zip(c_b, c_a)) + [((n, r, p.format(c=ctxs["before"]["company"])), (n, r, p.format(c=ctxs["after"]["company"])))
                                   for n, r, p in extra]
    for (name, role, pb), (_, _, pa) in pairs:
        rb, ra = get(ctxs["before"], role, pb), get(ctxs["after"], role, pa)
        try:
            jb = normalize(rb.json(), ctxs["before"]["idmap"])
            ja = normalize(ra.json(), ctxs["after"]["idmap"])
        except ValueError:
            jb, ja = rb.text, ra.text
        strict = jb == ja
        loose = strict or canon(jb) == canon(ja)
        out[name] = {"status": [rb.status_code, ra.status_code], "equal_strict": strict, "equal_ignoring_order": loose}
        if not loose:
            with open(f"{OUT}/diff-{name.replace('/', '_').replace(' ', '_')}.json", "w") as f:
                json.dump({"before": jb, "after": ja}, f, ensure_ascii=False, indent=1)
        print(f"{name:42} status={rb.status_code}/{ra.status_code} strict={strict} ignoring_order={loose}", flush=True)
    json.dump(out, open(f"{OUT}/compare.json", "w"), ensure_ascii=False, indent=1)


def cmd_create(n=20):
    """Creates n bookings per version as the bench client, on slots free in BOTH DBs (identical data)."""
    ctxs = {v: context(v) for v in VERSIONS}
    # pick free slots from BEFORE (data is identical), masters 2..8, days T+10..T+30
    cb = ctxs["before"]
    masters_b = psql(cb["db"], "select \"PhoneNumber\"||'|'||\"Id\" from \"AspNetUsers\" where \"PhoneNumber\" like '790000001%' order by 1").splitlines()
    picks = []
    for line in masters_b[1:]:
        phone, mid = line.split("|")
        for day in range(10, 31, 3):
            date = f"2026-10-{day - 2:02d}" if day - 2 <= 31 else None
            r = get(cb, None, f"/api/bookings/slots?companyId={cb['company']}&masterId={mid}&serviceId={cb['service']}&date={date}")
            free = [sl for sl in r.json() if sl.get("isAvailable", sl.get("available", True))]
            if free:
                picks.append((phone, date, free[0]["startTime"] if "startTime" in free[0] else free[0]["start"]))
            if len(picks) >= n:
                break
        if len(picks) >= n:
            break
    res = {}
    for v in VERSIONS:
        ctx = ctxs[v]
        mids = dict(l.split("|") for l in psql(ctx["db"], "select \"PhoneNumber\"||'|'||\"Id\" from \"AspNetUsers\" where \"PhoneNumber\" like '790000001%'").splitlines())
        samples = []
        for phone, date, start in picks:
            body = {"companyId": ctx["company"], "serviceId": ctx["service"], "masterId": mids[phone], "date": date,
                    "startTime": start, "notes": None, "guestName": None, "guestPhone": None, "guestEmail": None,
                    "captchaToken": None}
            ctl(v, "reset")
            t0 = time.perf_counter()
            r = ctx["s"].post(ctx["base"] + "/api/bookings", json=body, headers={"Authorization": f"Bearer {ctx['client']}"})
            ms = (time.perf_counter() - t0) * 1000
            st = ctl(v, "stats")
            if r.status_code not in (200, 201):
                print(v, r.status_code, r.text[:300])
                continue
            samples.append({"stmts": st["stmts"] + st["queries"], "roundtrips": st["syncs"], "rows": st["rows"], "ms": ms})
            texts = st["texts"]
        os.makedirs(f"/tmp/bench/sql/{v}", exist_ok=True)
        with open(f"/tmp/bench/sql/{v}/booking_create.sql", "w") as f:
            f.write("\n\n-- ---\n".join(texts))
        res[v] = {"n": len(samples), "stmts_median": statistics.median(s_["stmts"] for s_ in samples),
                  "stmts_all": [s_["stmts"] for s_ in samples],
                  "roundtrips_median": statistics.median(s_["roundtrips"] for s_ in samples),
                  "ms_median_via_proxy": statistics.median(s_["ms"] for s_ in samples)}
        print(v, res[v], flush=True)
    json.dump({"slots": picks, "result": res}, open(f"{OUT}/create.json", "w"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "count":
        cmd_count()
    elif cmd == "latency":
        cmd_latency(only=sys.argv[2] if len(sys.argv) > 2 else None)
    elif cmd == "compare":
        cmd_compare()
    elif cmd == "create":
        cmd_create()
