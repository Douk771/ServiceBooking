#!/usr/bin/env python3
"""Сверка покрытия маршрутов эталона до и после ревизии (цикл 36, BE-36-02).

API_CONTRACT_CYCLE36.md §36.24, §36.28.3. Только стандартная библиотека.

    route_coverage.py --golden ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt \
        --before <routes.jsonl>... --after <routes.jsonl>...

Маршрут эталона (<METHODS> <RawText>) покрыт, если в журнале есть строка с тем же route и method из METHODS
(для ANY - любой метод). Коды выхода: 0 нет потерь, 1 аргументы, 4 есть потерянные маршруты.
"""
import argparse
import json
import sys


def parse_golden(path):
    routes = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            head = line.split(" | ", 1)[0].strip()
            if not head:
                continue
            tokens = head.split()
            if len(tokens) < 2:
                raise ValueError("не разобрана строка эталона: " + line[:80])
            routes.append((tokens[0], tokens[1]))
    return routes


def load_hits(paths):
    hits = set()
    for path in paths:
        with open(path, encoding="utf-8") as f:
            for n, line in enumerate(f, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                    hits.add((row["method"], row["route"]))
                except (ValueError, KeyError) as exc:
                    raise ValueError("%s:%d: %s" % (path, n, exc))
    return hits


def covered(route, hits):
    methods, raw = route
    if methods == "ANY":
        return any(r == raw for _, r in hits)
    return any((m, raw) in hits for m in methods.split(","))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--golden", required=True)
    p.add_argument("--before", required=True, nargs="+")
    p.add_argument("--after", required=True, nargs="+")
    args = p.parse_args()
    try:
        golden = parse_golden(args.golden)
        before, after = load_hits(args.before), load_hits(args.after)
    except (OSError, ValueError) as exc:
        print("Ошибка чтения: %s" % exc, file=sys.stderr)
        return 1

    cov_before = [r for r in golden if covered(r, before)]
    cov_after = [r for r in golden if covered(r, after)]
    lost = [r for r in cov_before if r not in set(cov_after)]
    never = [r for r in golden if r not in set(cov_before) and r not in set(cov_after)]
    print("Маршрутов в эталоне: %d" % len(golden))
    print("Покрыто до: %d" % len(cov_before))
    print("Покрыто после: %d" % len(cov_after))
    print("Потеряно: %d" % len(lost))
    for m, r in lost:
        print("  - %s %s" % (m, r))
    print("Никогда не покрыто (информация, в долг цикла): %d" % len(never))
    for m, r in never:
        print("  . %s %s" % (m, r))
    return 4 if lost else 0


if __name__ == "__main__":
    sys.exit(main())
