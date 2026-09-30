#!/usr/bin/env python3
"""Сверка «после = до - реестр» (цикл 36, BE-36-02). API_CONTRACT_CYCLE36.md §36.28.3, §36.29.

    diff_inventory.py --suite <s> --before <before.json> --after <after.json> --registry TEST_CATALOG.md

Коды выхода: 0 сошлось, 1 аргументы/неверный файл, 4 нарушение сверки.
"""
import argparse
import json
import re
import sys

ROW_ID_RE = re.compile(r"^R36-[BF]\d{3}$")
SUITES = {"unit", "functional", "vitest"}
CATEGORIES = set("АБВГДЕ")
ACTIONS = {"удалён", "перенесён в юнит", "объединён", "ускорен", "разделён", "переименован", "флейк-долг",
           "добавлен (замена)", "изменение продуктового кода"}
GONE_ACTIONS = {"удалён", "перенесён в юнит", "объединён", "переименован", "разделён"}
NEW_ACTIONS = {"добавлен (замена)", "переименован", "разделён"}
REGISTRY_HEADING = "## Цикл 36 — ревизия"


def clean(cell):
    return cell.strip().strip("`").strip()


def parse_registry(path):
    """Строки реестра: таблицы раздела «Цикл 36 — ревизия», первая ячейка R36-[BF]nnn."""
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()
    start = next((i for i, l in enumerate(lines) if l.startswith(REGISTRY_HEADING)), None)
    if start is None:
        return [], ["в %s нет раздела «%s»" % (path, REGISTRY_HEADING[3:])]
    rows, errors = [], []
    for line in lines[start:]:
        if not line.startswith("|"):
            continue
        cells = [c for c in line.strip().strip("|").split("|")]
        # экранированные «\|» внутри ячеек не поддерживаются: реестр их не использует
        cells = [clean(c) for c in cells]
        if not cells or not ROW_ID_RE.match(cells[0]):
            continue
        if len(cells) != 8:
            errors.append("%s: ожидалось 8 колонок, найдено %d" % (cells[0], len(cells)))
            continue
        num, suite, tid, test, cat, action, reason, repl = cells
        if suite not in SUITES:
            errors.append("%s: набор «%s» не из %s" % (num, suite, sorted(SUITES)))
        if cat not in CATEGORIES:
            errors.append("%s: категория «%s» не из А-Е" % (num, cat))
        if action not in ACTIONS:
            errors.append("%s: действие «%s» не из контракта" % (num, action))
        if action == "удалён" and cat not in ("А", "Б"):
            errors.append("%s: «удалён» допустим только при категории А или Б" % num)
        rows.append({"num": num, "suite": suite, "id": None if tid in ("—", "-", "") else tid, "test": test,
                     "cat": cat, "action": action, "replacement": repl})
    return rows, errors


def matches_key(row, test):
    if row["id"] and test.get("id") == row["id"]:
        return True
    suffix = row["test"]
    if not suffix or suffix in ("—", "-"):
        return False
    key = test["key"]
    if key == suffix:
        return True
    return key.endswith(suffix) and key[-len(suffix) - 1] in ".:>+ "


def referenced_in_replacements(test, rows):
    short = test["key"].split("::")[-1] if "::" in test["key"] else ".".join(test["key"].split(".")[-2:])
    for row in rows:
        text = row["replacement"]
        if test["key"] in text or short in text or (test.get("id") and test["id"] in text):
            return True
    return False


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--suite", required=True, choices=sorted(SUITES))
    p.add_argument("--before", required=True)
    p.add_argument("--after", required=True)
    p.add_argument("--registry", required=True)
    args = p.parse_args()

    try:
        before = json.load(open(args.before, encoding="utf-8"))
        after = json.load(open(args.after, encoding="utf-8"))
        rows, errors = parse_registry(args.registry)
    except (OSError, ValueError) as exc:
        print("Ошибка чтения: %s" % exc, file=sys.stderr)
        return 1
    for doc, name in ((before, "before"), (after, "after")):
        if doc.get("suite") != args.suite:
            print("%s: набор %s, ожидался %s" % (name, doc.get("suite"), args.suite), file=sys.stderr)
            return 1

    suite_rows = [r for r in rows if r["suite"] == args.suite]
    b = {t["key"]: t for t in before["tests"]}
    a = {t["key"]: t for t in after["tests"]}
    after_ids = {t["id"] for t in after["tests"] if t["id"]}
    before_ids = {t["id"] for t in before["tests"] if t["id"]}
    dotnet = args.suite != "vitest"

    vanished, appeared, changed, protected_removed = [], [], [], []
    for key, t in sorted(b.items()):
        if key in a:
            if a[key]["cases"] != t["cases"] and not any(matches_key(r, t) for r in suite_rows):
                changed.append("%s (cases %d -> %d)" % (key, t["cases"], a[key]["cases"]))
            continue
        if dotnet and t["id"] and t["id"] in after_ids:
            continue  # класс переименован/разбит, тест найден по TestCase-ID (§36.9.4)
        covering = [r for r in suite_rows if r["action"] in GONE_ACTIONS and matches_key(r, t)]
        if not covering:
            vanished.append(key)
        elif t.get("protectedZone") and any(r["action"] == "удалён" for r in covering):
            protected_removed.append(key)
    for key, t in sorted(a.items()):
        if key in b:
            continue
        if dotnet and t["id"] and t["id"] in before_ids:
            continue
        ok = any(r["action"] in NEW_ACTIONS and matches_key(r, t) for r in suite_rows) \
            or referenced_in_replacements(t, rows)
        if not ok:
            appeared.append(key)

    print("Набор: %s; до: %d запусков, после: %d; строк реестра: %d" %
          (args.suite, before["totalCases"], after["totalCases"], len(suite_rows)))
    sections = (("Исчезло без записи в реестре", vanished), ("Появилось без записи в реестре", appeared),
                ("Изменилось cases без записи в реестре", changed), ("Удалён защищённый", protected_removed),
                ("Ошибки разбора реестра", errors))
    bad = False
    for title, items in sections:
        print("%s: %d" % (title, len(items)))
        for item in items:
            print("  - " + item)
        bad = bad or bool(items)
    return 4 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
