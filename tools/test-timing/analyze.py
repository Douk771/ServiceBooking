#!/usr/bin/env python3
"""DO-36-01: анализ замера времени регресса (ARCHITECTURE_CYCLE36.md §36.4.2, API_CONTRACT_CYCLE36.md §36.28.2).

    analyze.py <measure-dir> --out report.json [--md report.md]
    analyze.py --compare <a.json> <b.json> [--md]
    analyze.py --emit-durations <report.json> --out ServiceBooking.Tests/test-durations.json

Только стандартная библиотека. --ci-summary (DO-36-04) печатает Markdown и ::warning:: для CI.
"""
import argparse
import csv
import glob
import json
import math
import os
import re
import statistics
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
TOP = 20


def r3(x):
    return round(float(x), 3)


def parse_duration(text):
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def parse_iso(text):
    # 2026-10-01T12:00:00.1234567+03:00 — дробная часть бывает 7 знаков
    m = re.match(r"(.*?\.\d{6})\d*(.*)", text)
    if m:
        text = m.group(1) + m.group(2)
    return datetime.fromisoformat(text).timestamp()


def percentile(values, p):
    if not values:
        return 0.0
    v = sorted(values)
    k = max(0, math.ceil(p / 100 * len(v)) - 1)
    return v[k]


def read_jsonl(path):
    out = []
    try:
        with open(path, encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if line:
                    try:
                        out.append(json.loads(line))
                    except ValueError:
                        pass
    except OSError:
        pass
    return out


# ---------------------------------------------------------------- TRX

def parse_trx(path):
    root = ET.parse(path).getroot()
    defs = {}
    for ut in root.iterfind(".//t:TestDefinitions/t:UnitTest", NS):
        tm = ut.find("t:TestMethod", NS)
        defs[ut.get("id")] = tm.get("className") if tm is not None else "?"
    results = []
    for r in root.iterfind(".//t:Results/t:UnitTestResult", NS):
        try:
            start = parse_iso(r.get("startTime"))
            end = parse_iso(r.get("endTime"))
            dur = parse_duration(r.get("duration"))
        except (TypeError, ValueError):
            continue
        results.append({"cls": defs.get(r.get("testId"), "?"), "name": r.get("testName"), "id": r.get("testId"),
                        "start": start, "end": end, "dur": dur, "outcome": r.get("outcome")})
    c = root.find(".//t:ResultSummary/t:Counters", NS)
    counters = {"passed": 0, "failed": 0, "skipped": 0}
    if c is not None:
        counters["passed"] = int(c.get("passed", 0))
        counters["failed"] = int(c.get("failed", 0)) + int(c.get("error", 0)) + int(c.get("timeout", 0))
        counters["skipped"] = int(c.get("notExecuted", 0))
    return results, counters


def class_stats(results):
    agg = {}
    for r in results:
        a = agg.setdefault(r["cls"], {"seconds": 0.0, "tests": 0, "start": r["start"], "end": r["end"]})
        a["seconds"] += r["dur"]
        a["tests"] += 1
        a["start"] = min(a["start"], r["start"])
        a["end"] = max(a["end"], r["end"])
    return agg


def tail_seconds(intervals, p):
    """Длина завершающего отрезка прогона, где активных классов меньше p."""
    if not intervals:
        return 0.0
    events = sorted([(s, 1) for s, _ in intervals] + [(e, -1) for _, e in intervals], key=lambda x: (x[0], x[1]))
    end = max(e for _, e in intervals)
    active = 0
    last_full = None  # момент, когда число активных последний раз упало ниже p после >= p
    for t, d in events:
        before = active
        active += d
        if before >= p > active:
            last_full = t
        elif active >= p:
            last_full = None
    return max(0.0, end - last_full) if last_full is not None else 0.0


def top_lists(results, agg):
    classes = sorted(agg.items(), key=lambda kv: -kv[1]["seconds"])[:TOP]
    top_classes = [{"name": k.rsplit(".", 1)[-1], "seconds": r3(v["seconds"]), "tests": v["tests"],
                    "wallSeconds": r3(v["end"] - v["start"]), "className": k} for k, v in classes]
    tests = sorted(results, key=lambda r: -r["dur"])[:TOP]
    top_tests = [{"name": r["name"] or "?", "seconds": r3(r["dur"]), "className": r["cls"], "testCaseId": r["id"] or ""} for r in tests]
    return top_classes, top_tests


# ---------------------------------------------------------------- метрики хоста

def read_metrics(d, files):
    events = []
    for f in files:
        events.extend(read_jsonl(os.path.join(d, f)))
    return events


def functional_block(d, results, agg, run, env, metrics):
    p = max(1, int(env.get("parallelism", 1)))
    classes_wall = sum(v["end"] - v["start"] for v in agg.values())
    run_wall = run["wallSeconds"] or 1.0
    idle = min(1.0, max(0.0, 1 - classes_wall / (p * run_wall)))
    longest = max(agg.items(), key=lambda kv: kv[1]["end"] - kv[1]["start"], default=(None, None))
    tail = tail_seconds([(v["start"], v["end"]) for v in agg.values()], p)

    slot_class = {}
    for e in metrics:
        if e.get("event") == "class-recorded":
            slot_class[(e["runKey"], e["slot"])] = e["testClass"]

    boots = [e for e in metrics if e.get("event") == "host-booted"]
    ms = [e["ms"] for e in boots]
    by_factory = {}
    by_class = {}
    for e in boots:
        k = (e["factoryType"], e["factoryTag"])
        f = by_factory.setdefault(k, {"factoryType": k[0], "factoryTag": k[1], "count": 0, "totalSeconds": 0.0})
        f["count"] += 1
        f["totalSeconds"] += e["ms"] / 1000
        cls = slot_class.get((e["runKey"], e["slot"]), "(unknown)")
        c = by_class.setdefault(cls, {"name": cls, "tests": 0, "seconds": 0.0})
        c["tests"] += 1
        c["seconds"] += e["ms"] / 1000

    def total(event):
        return sum(e["ms"] for e in metrics if e.get("event") == event) / 1000

    created = [e for e in metrics if e.get("event") == "class-db-created"]
    block = {
        "classes": len(agg),
        "hostBoots": {
            "count": len(boots), "totalSeconds": r3(sum(ms) / 1000),
            "medianMs": round(statistics.median(ms), 1) if ms else 0, "p95Ms": round(percentile(ms, 95), 1),
            "byFactory": [dict(v, totalSeconds=r3(v["totalSeconds"])) for v in sorted(by_factory.values(), key=lambda x: -x["totalSeconds"])],
            "byClass": [dict(v, seconds=r3(v["seconds"])) for v in sorted(by_class.values(), key=lambda x: (-x["tests"], -x["seconds"]))[:TOP]],
        },
        "databases": {
            "serverSeconds": r3(total("server-ready")), "templateSeconds": r3(total("template-ready")),
            "cloneCount": len(created), "cloneTotalSeconds": r3(total("class-db-created")),
            "dropTotalSeconds": r3(total("class-db-dropped")),
        },
        "scheduling": {
            "threadIdleFraction": round(idle, 4), "longestClassSeconds": r3(longest[1]["end"] - longest[1]["start"]) if longest[0] else 0,
            "longestClass": longest[0] or "", "tailSeconds": r3(tail),
        },
    }
    cpu = postgres_cpu(d, run["index"], env)
    block["postgresCpu"] = cpu
    block["efManyServiceProvidersWarning"] = grep_file(os.path.join(d, "functional-%d.stdout.log" % run["index"]), "ManyServiceProvidersCreatedWarning")
    return block


def postgres_cpu(d, idx, env):
    path = os.path.join(d, "docker-stats-functional-%d.csv" % idx)
    vals = []
    try:
        with open(path, encoding="utf-8") as fh:
            for row in csv.DictReader(fh):
                try:
                    vals.append(float(row["cpu_percent"]))
                except (KeyError, ValueError):
                    pass
    except OSError:
        return None
    if not vals:
        return None
    out = {"maxPercent": round(max(vals), 1), "meanPercent": round(statistics.mean(vals), 1)}
    colima = env.get("colima")
    if colima:
        out["cpuLimit"] = int(colima["cpu"])
    return out


def grep_file(path, needle):
    try:
        with open(path, encoding="utf-8", errors="replace") as fh:
            return any(needle in line for line in fh)
    except OSError:
        return False


# ---------------------------------------------------------------- vitest

def to_seconds(num, unit):
    v = float(num)
    return v / 1000 if unit == "ms" else v


def vitest_phases(stdout_path):
    try:
        text = open(stdout_path, encoding="utf-8", errors="replace").read()
    except OSError:
        return None
    text = re.sub(r"\x1b\[[0-9;]*m", "", text)
    m = re.search(r"Duration\s+.*?\((.*?)\)", text)
    if not m:
        return None
    out = {}
    for key in ("transform", "setup", "collect", "tests", "environment", "prepare"):
        mm = re.search(r"%s\s+([\d.]+)(ms|s)\b" % key, m.group(1))
        out[key + "Seconds"] = r3(to_seconds(mm.group(1), mm.group(2))) if mm else 0.0
    return out


def vitest_env(root, rel):
    try:
        head = open(os.path.join(root, "frontend", rel), encoding="utf-8", errors="replace").read(2000)
    except OSError:
        return "jsdom"
    m = re.search(r"@vitest-environment\s+(\w+)", head)
    return "node" if m and m.group(1) == "node" else "jsdom"


def vitest_block(d, idx, root):
    try:
        data = json.load(open(os.path.join(d, "vitest-%d.json" % idx), encoding="utf-8"))
    except (OSError, ValueError):
        return None, {"passed": 0, "failed": 0, "skipped": 0}
    frontend = os.path.join(root, "frontend") + os.sep
    files = []
    for tr in data.get("testResults", []):
        name = tr.get("name", "")
        rel = name[len(frontend):] if name.startswith(frontend) else name
        sec = max(0.0, (tr.get("endTime", 0) - tr.get("startTime", 0)) / 1000)
        tests = sum((a.get("duration") or 0) for a in tr.get("assertionResults", [])) / 1000
        files.append({"file": rel, "seconds": r3(sec), "testsSeconds": r3(min(tests, sec) if sec else tests),
                      "overheadSeconds": r3(max(0.0, sec - tests)), "environment": vitest_env(root, rel)})
    files.sort(key=lambda x: -x["seconds"])
    pool = "forks"
    try:
        m = re.search(r"pool:\s*['\"](\w+)['\"]", open(os.path.join(root, "frontend", "vitest.config.ts"), encoding="utf-8").read())
        if m and m.group(1) in ("forks", "threads", "vmThreads", "vmForks"):
            pool = m.group(1)
    except OSError:
        pass
    phases = vitest_phases(os.path.join(d, "vitest-%d.stdout.log" % idx)) or {
        k + "Seconds": 0.0 for k in ("transform", "setup", "collect", "tests", "environment", "prepare")}
    block = {"files": len(files), "pool": pool, "phases": phases, "topFiles": files[:TOP]}
    counters = {"passed": data.get("numPassedTests", 0), "failed": data.get("numFailedTests", 0), "skipped": data.get("numPendingTests", 0)}
    return block, counters


# ---------------------------------------------------------------- отчёт

def median_run(runs):
    ordered = sorted(runs, key=lambda r: r["wallSeconds"])
    return ordered[(len(ordered) - 1) // 2]


def build_report(d):
    d = os.path.abspath(d)
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    env_all = json.load(open(os.path.join(d, "env.json"), encoding="utf-8"))
    env = env_all["environment"]
    seed = env_all.get("seed")
    all_runs = read_jsonl(os.path.join(d, "runs.jsonl"))
    suites = []
    for name in ("unit", "functional", "vitest"):
        runs = [r for r in all_runs if r["suite"] == name]
        if not runs:
            continue
        run_entries = []
        parsed = {}
        for r in sorted(runs, key=lambda x: x["index"]):
            counters = {"passed": 0, "failed": 0, "skipped": 0}
            results = None
            if name in ("unit", "functional"):
                trx = os.path.join(d, "%s-%d.trx" % (name, r["index"]))
                if os.path.exists(trx):
                    results, counters = parse_trx(trx)
            else:
                vb, counters = vitest_block(d, r["index"], root)
                parsed[r["index"]] = vb
            parsed.setdefault(r["index"], results)
            entry = {"index": r["index"], "wallSeconds": r3(r["wallSeconds"]), **counters}
            if name == "functional":
                entry["orderSeed"] = seed
                keys = [re.match(r"sb-test-metrics-([0-9a-f]{8})\.jsonl", f) for f in r["metricsFiles"]]
                keys = [k.group(1) for k in keys if k]
                if keys:
                    entry["runKey"] = keys[0]
            if entry.get("orderSeed") is None:
                entry.pop("orderSeed", None)
            run_entries.append(entry)
        med = median_run(runs)
        suite = {"suite": name, "runs": run_entries, "medianWallSeconds": r3(statistics.median([r["wallSeconds"] for r in runs]))}
        if name in ("unit", "functional"):
            results = parsed.get(med["index"])
            if results:
                agg = class_stats(results)
                suite["topClasses"], suite["topTests"] = top_lists(results, agg)
                if name == "functional":
                    metrics = read_metrics(d, med["metricsFiles"])
                    suite["functional"] = functional_block(d, results, agg, med, env, metrics)
        else:
            if parsed.get(med["index"]):
                suite["vitest"] = parsed[med["index"]]
        suites.append(suite)

    if env.get("concurrentSuites"):
        by_idx = {}
        for r in all_runs:
            a = by_idx.setdefault(r["index"], [r["startEpoch"], r["endEpoch"]])
            a[0] = min(a[0], r["startEpoch"]); a[1] = max(a[1], r["endEpoch"])
        total = statistics.median([b - a for a, b in by_idx.values()]) if by_idx else 0.0
    else:
        total = sum(s["medianWallSeconds"] for s in suites)

    return {
        "schemaVersion": 1, "label": env_all["label"], "commit": env_all["commit"], "dirty": bool(env_all.get("dirty")),
        "createdAtUtc": env_all["createdAtUtc"], "environment": env, "suites": suites, "totalMedianSeconds": r3(total),
    }, env_all


def fmt(sec):
    sec = float(sec)
    return "%d:%04.1f" % (int(sec // 60), sec % 60) if sec >= 60 else "%.1f s" % sec


def render_md(rep, env_all):
    L = []
    e = rep["environment"]
    L.append("# Замер времени регресса: %s" % rep["label"])
    L.append("")
    L.append("Коммит `%s`%s, %s. Хост %s (%s), %s ядер, %s ГБ, Docker: %s, режим БД: %s, P=%s, сборка: %s%s." % (
        rep["commit"], " (dirty)" if rep.get("dirty") else "", rep["createdAtUtc"], e["host"], e["os"],
        e["cpuCores"], e["memoryGb"], e["dockerRuntime"], e["mode"], e["parallelism"], e["build"],
        ", чужие прогоны: ДА (отчёт не годен для сравнения)" if e.get("foreignRunsDetected") else ""))
    if e.get("notes"):
        L.append("")
        L.append("Примечание: %s" % e["notes"])
    L.append("")
    L.append("Итого медиана: **%s**" % fmt(rep["totalMedianSeconds"]))
    L.append("")
    L.append("## Наборы")
    L.append("")
    L.append("| Набор | Прогонов | Медиана wall | Тестов (passed/failed/skipped) |")
    L.append("|---|---|---|---|")
    for s in rep["suites"]:
        last = s["runs"][-1]
        L.append("| %s | %d | %s | %d / %d / %d |" % (s["suite"], len(s["runs"]), fmt(s["medianWallSeconds"]), last["passed"], last["failed"], last["skipped"]))
    for s in rep["suites"]:
        f = s.get("functional")
        if f:
            db = f["databases"]
            hb = f["hostBoots"]
            wall = s["medianWallSeconds"]
            L.append("")
            L.append("## Функциональный набор: разбивка по фазам")
            L.append("")
            L.append("Суммы по всем параллельным потокам — это не доли wall; wall набора %s." % fmt(wall))
            L.append("")
            L.append("| Фаза | Время | Примечание |")
            L.append("|---|---|---|")
            bs = env_all.get("buildSeconds")
            L.append("| Сборка (`dotnet build`, вне замера) | %s | %s |" % (fmt(bs) if bs is not None else "н/д", "каждый dotnet test дополнительно собирает" if e["build"] == "with-build" else "прогоны с --no-build"))
            L.append("| Старт контейнера / подключение к серверу | %s | server-ready |" % fmt(db["serverSeconds"]))
            L.append("| Шаблон БД (миграции) | %s | template-ready |" % fmt(db["templateSeconds"]))
            L.append("| Создание баз классов | %s суммарно, %d шт. | class-db-created |" % (fmt(db["cloneTotalSeconds"]), db["cloneCount"]))
            L.append("| Удаление баз классов | %s суммарно | class-db-dropped |" % fmt(db["dropTotalSeconds"]))
            L.append("| Старты хоста | %s суммарно, %d шт. | медиана %.0f мс, p95 %.0f мс |" % (fmt(hb["totalSeconds"]), hb["count"], hb["medianMs"], hb["p95Ms"]))
            L.append("| Тесты (wall набора) | %s | |" % fmt(wall))
            L.append("")
            L.append("Планирование: простой потоков %.1f %%, самый длинный класс %s (%s), хвост %s." % (
                f["scheduling"]["threadIdleFraction"] * 100, f["scheduling"]["longestClass"] or "н/д", fmt(f["scheduling"]["longestClassSeconds"]), fmt(f["scheduling"]["tailSeconds"])))
            if f.get("postgresCpu"):
                c = f["postgresCpu"]
                L.append("")
                L.append("CPU контейнера Postgres: макс %.0f %%, среднее %.0f %%%s." % (c["maxPercent"], c["meanPercent"], (", лимит %d ядер" % c["cpuLimit"]) if c.get("cpuLimit") else ""))
            L.append("")
            L.append("EF `ManyServiceProvidersCreatedWarning` в выводе: %s." % ("да" if f["efManyServiceProvidersWarning"] else "нет"))
            if hb["byFactory"]:
                L.append("")
                L.append("### Старты хоста по фабрикам")
                L.append("")
                L.append("| Фабрика | Тег | Стартов | Сумма |")
                L.append("|---|---|---|---|")
                for x in hb["byFactory"][:TOP]:
                    L.append("| %s | %s | %d | %s |" % (x["factoryType"], x.get("factoryTag", ""), x["count"], fmt(x["totalSeconds"])))
            if hb.get("byClass"):
                L.append("")
                L.append("### Классы по числу стартов хоста")
                L.append("")
                L.append("| Класс | Стартов | Сумма |")
                L.append("|---|---|---|")
                for x in hb["byClass"][:TOP]:
                    L.append("| %s | %d | %s |" % (x["name"], x["tests"], fmt(x["seconds"])))
    for s in rep["suites"]:
        if s.get("topClasses"):
            L.append("")
            L.append("## Топ медленных классов: %s" % s["suite"])
            L.append("")
            L.append("| Класс | Сумма тестов | Wall класса | Тестов |")
            L.append("|---|---|---|---|")
            for c in s["topClasses"]:
                L.append("| %s | %s | %s | %d |" % (c["className"], fmt(c["seconds"]), fmt(c["wallSeconds"]), c["tests"]))
        v = s.get("vitest")
        if v:
            ph = v["phases"]
            L.append("")
            L.append("## vitest (%d файлов, pool %s)" % (v["files"], v.get("pool", "?")))
            L.append("")
            L.append("Фазы (суммы по воркерам): transform %s, setup %s, collect %s, tests %s, environment %s, prepare %s." % tuple(
                fmt(ph[k + "Seconds"]) for k in ("transform", "setup", "collect", "tests", "environment", "prepare")))
            L.append("")
            L.append("| Файл | Время | Тесты | Накладные | Среда |")
            L.append("|---|---|---|---|---|")
            for x in v["topFiles"]:
                L.append("| %s | %s | %s | %s | %s |" % (x["file"], fmt(x["seconds"]), fmt(x["testsSeconds"]), fmt(x["overheadSeconds"]), x["environment"]))
    L.append("")
    return "\n".join(L)


# ---------------------------------------------------------------- CI-сводка (DO-36-04)

def _vitest_results(path):
    """vitest --reporter=json -> (список «результатов» как у TRX, wall в секундах)."""
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    results = []
    starts, ends = [], []
    for f in data.get("testResults", []):
        st = f.get("startTime")
        en = f.get("endTime")
        if st and en:
            starts.append(st / 1000.0)
            ends.append(en / 1000.0)
        for a in f.get("assertionResults", []):
            d = (a.get("duration") or 0) / 1000.0
            results.append({"cls": f.get("name", "?"), "name": a.get("fullName") or a.get("title") or "?",
                            "id": "", "start": (st or 0) / 1000.0, "end": (en or 0) / 1000.0, "dur": d,
                            "outcome": a.get("status")})
    wall = (max(ends) - min(starts)) if starts and ends else 0.0
    return results, wall


def ci_summary(files, metrics_glob, thresholds_path):
    th = {}
    if thresholds_path:
        with open(thresholds_path, encoding="utf-8") as fh:
            th = json.load(fh)

    def limit(key):
        v = th.get(key, 0) or 0
        return v if v > 0 else None

    out = ["## Медленные тесты", ""]
    warnings = []
    for path in files:
        label = os.path.basename(path)
        if path.endswith(".json"):
            results, wall = _vitest_results(path)
            kind, wall_key = "vitest", "vitestWallSecondsWarn"
        else:
            results, _ = parse_trx(path)
            wall = (max(r["end"] for r in results) - min(r["start"] for r in results)) if results else 0.0
            name = label.lower()
            kind = "unit" if "unit" in name else "functional"
            wall_key = "unitWallSecondsWarn" if kind == "unit" else "functionalWallSecondsWarn"
        agg = class_stats(results)
        out.append("### %s: %d тестов, %d классов, wall %.0f с" % (label, len(results), len(agg), wall))
        out.append("")
        out.append("| Класс | Сумма, с | Wall, с | Тестов |")
        out.append("|---|---|---|---|")
        for k, v in sorted(agg.items(), key=lambda kv: -kv[1]["seconds"])[:10]:
            out.append("| %s | %.1f | %.1f | %d |" % (k.rsplit(".", 1)[-1] if kind != "vitest" else k.rsplit("/", 1)[-1],
                                                      v["seconds"], v["end"] - v["start"], v["tests"]))
        out.append("")
        out.append("| Тест | с |")
        out.append("|---|---|")
        for r in sorted(results, key=lambda r: -r["dur"])[:10]:
            out.append("| %s | %.1f |" % ((r["name"] or "?").replace("|", "\\|")[:110], r["dur"]))
        out.append("")
        lim = limit(wall_key)
        if lim and wall > lim:
            warnings.append("%s: wall %.0f с выше порога %.0f с (%s)" % (label, wall, lim, wall_key))
        lim = limit("classWallSecondsWarn")
        for k, v in agg.items():
            if lim and (v["end"] - v["start"]) > lim and kind == "functional":
                warnings.append("%s: класс %s шёл %.0f с (порог %.0f с)" % (label, k.rsplit(".", 1)[-1], v["end"] - v["start"], lim))
        lim = limit("testSecondsWarn")
        for r in results:
            if lim and r["dur"] > lim:
                warnings.append("%s: тест %s шёл %.1f с (порог %.0f с)" % (label, (r["name"] or "?")[:100], r["dur"], lim))

    events = []
    for f in sorted(glob.glob(metrics_glob)) if metrics_glob else []:
        events.extend(read_jsonl(f))
    boots = [e for e in events if e.get("event") == "host-booted"]
    if boots:
        ms = [e["ms"] for e in boots]
        out.append("### Старты хоста")
        out.append("")
        out.append("Стартов: %d, суммарно %.0f с, медиана %.0f мс, p95 %.0f мс." % (
            len(boots), sum(ms) / 1000, statistics.median(ms), percentile(ms, 95)))
        out.append("")
        slot_class = {(e["runKey"], e["slot"]): e["testClass"] for e in events if e.get("event") == "class-recorded"}
        per = {}
        for e in boots:
            c = slot_class.get((e["runKey"], e["slot"]), "(unknown)")
            per[c] = per.get(c, 0) + 1
        lim = limit("hostBootsPerClassWarn")
        for c, n in sorted(per.items(), key=lambda kv: -kv[1]):
            if lim and n > lim:
                warnings.append("класс %s поднимал хост %d раз (порог %d)" % (c.rsplit(".", 1)[-1], n, lim))
    else:
        out.append("Метрик хоста нет (SERVICEBOOKING_TEST_METRICS=0 или файл не найден).")
        out.append("")
    print("\n".join(out))
    for w in warnings[:50]:
        print("::warning title=Slow tests::%s" % w)
    return 0


def compare(a_path, b_path):
    a = json.load(open(a_path, encoding="utf-8"))
    b = json.load(open(b_path, encoding="utf-8"))
    lines = ["| Набор | %s | %s | Разница |" % (a["label"], b["label"]), "|---|---|---|---|"]
    am = {s["suite"]: s["medianWallSeconds"] for s in a["suites"]}
    bm = {s["suite"]: s["medianWallSeconds"] for s in b["suites"]}
    for k in ("unit", "functional", "vitest"):
        if k in am or k in bm:
            x, y = am.get(k), bm.get(k)
            diff = "" if x is None or y is None else "%+.1f %%" % ((y - x) / x * 100 if x else 0)
            lines.append("| %s | %s | %s | %s |" % (k, fmt(x) if x is not None else "-", fmt(y) if y is not None else "-", diff))
    x, y = a["totalMedianSeconds"], b["totalMedianSeconds"]
    lines.append("| итого | %s | %s | %+.1f %% |" % (fmt(x), fmt(y), (y - x) / x * 100 if x else 0))
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dir", nargs="?")
    ap.add_argument("--out")
    ap.add_argument("--md", nargs="?", const="-")
    ap.add_argument("--compare", nargs=2, metavar=("A", "B"))
    ap.add_argument("--emit-durations", metavar="REPORT")
    ap.add_argument("--ci-summary", nargs="+")
    ap.add_argument("--metrics")
    ap.add_argument("--thresholds")
    args = ap.parse_args()

    if args.ci_summary:
        try:
            return ci_summary(args.ci_summary, args.metrics, args.thresholds)
        except (OSError, KeyError, ValueError, ET.ParseError) as ex:
            print("ci-summary failed: %s" % ex, file=sys.stderr)
            return 1
    if args.compare:
        print(compare(*args.compare))
        return 0
    if args.emit_durations:
        rep = json.load(open(args.emit_durations, encoding="utf-8"))
        classes = {}
        for s in rep["suites"]:
            if s["suite"] == "functional":
                for c in s.get("topClasses", []):
                    classes[c["className"]] = c["seconds"]
        if not args.out:
            print("--out is required", file=sys.stderr)
            return 1
        json.dump({"schemaVersion": 1, "classes": classes}, open(args.out, "w", encoding="utf-8"), indent=2, ensure_ascii=False)
        print("note: only the top %d classes are present in the report" % TOP, file=sys.stderr)
        return 0
    if not args.dir or not args.out:
        ap.print_usage(sys.stderr)
        return 1
    try:
        rep, env_all = build_report(args.dir)
    except (OSError, KeyError, ValueError) as ex:
        print("analysis failed: %s" % ex, file=sys.stderr)
        return 1
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(rep, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    if args.md:
        md = render_md(rep, env_all)
        if args.md == "-":
            print(md)
        else:
            with open(args.md, "w", encoding="utf-8") as fh:
                fh.write(md)
    return 0


if __name__ == "__main__":
    sys.exit(main())
