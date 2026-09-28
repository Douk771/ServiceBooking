#!/usr/bin/env python3
"""Builds the markdown tables for REPORT.md from results/*.json.
Latency = average of the two passes (run1: BEFORE measured first, run2: AFTER measured first)."""
import json

R = "/tmp/bench/results"
cnt = json.load(open(f"{R}/count.json"))
l1 = json.load(open(f"{R}/latency-run1.json"))
l2 = json.load(open(f"{R}/latency-run2-reversed.json"))


def pct(b, a):
    return f"{(a - b) / b * 100:+.0f}%" if b else "—"


def kb(x):
    return f"{x / 1024:,.0f}" if x >= 1024 else f"{x / 1024:.1f}"


rows = ["| Эндпоинт | SQL/запрос до → после | Строк из БД до → после | КБ из БД до → после | p50 мс до → после | Δ p50 | p95 мс до → после | Δ p95 |",
        "|---|---|---|---|---|---|---|---|"]
for name in cnt:
    b, a = cnt[name]["before"], cnt[name]["after"]
    lb = {k: (l1[name]["before"][k] + l2[name]["before"][k]) / 2 for k in ("p50", "p95")}
    la = {k: (l1[name]["after"][k] + l2[name]["after"][k]) / 2 for k in ("p50", "p95")}
    rows.append(f"| {name} | {int(b['stmts'])} → {int(a['stmts'])} | {int(b['rows']):,} → {int(a['rows']):,} | "
                f"{kb(b['bytes_from_db'])} → {kb(a['bytes_from_db'])} | {lb['p50']:.1f} → {la['p50']:.1f} | {pct(lb['p50'], la['p50'])} | "
                f"{lb['p95']:.1f} → {la['p95']:.1f} | {pct(lb['p95'], la['p95'])} |")
print("\n".join(rows))
print()
print("Прогоны по отдельности (p50, мс): run1 до/после | run2 до/после")
for name in l1:
    print(f"- {name}: {l1[name]['before']['p50']:.1f}/{l1[name]['after']['p50']:.1f} | {l2[name]['before']['p50']:.1f}/{l2[name]['after']['p50']:.1f}")
c = json.load(open(f"{R}/create.json"))["result"]
print()
print(f"create: before {c['before']}, after {c['after']}")
