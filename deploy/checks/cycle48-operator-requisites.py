#!/usr/bin/env python3
"""Precheck выката цикла 48: contracts/legal/operator-public.json == legal.values.json (DO-48-3).

ARCHITECTURE_CYCLE48_STOREFRONT.md §48.3.3, API_CONTRACT_CYCLE48_STOREFRONT.md §48.30.1.
Вызывается из deploy/deploy-remote.sh (check_operator_requisites) и из deploy/ci/test-requisites-precheck.sh.

Использование: cycle48-operator-requisites.py <operator-public.json> <legal.values.json>

Правило: для каждого ключа из sharedWithLegalValues значение в legal.values.json -> values[key] должно быть СТРОГО равно
значению в operator-public.json -> values[key] (ordinal, без trim и нормализации).
Коды выхода: 0 — всё совпало либо legal.values.json на хосте нет (первичная установка, WARNING);
             1 — битый JSON, маркер «ЗАПОЛНИТЬ», пустое значение или расхождение (ключ и оба значения в stderr).
"""
import json
import os
import sys


def load(path, what):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError) as e:
        print(f"ERROR: {what} ({path}) is not readable JSON: {e}", file=sys.stderr)
        sys.exit(1)


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    public_path, values_path = argv[1], argv[2]

    public = load(public_path, "operator-public.json")
    shared = public.get("sharedWithLegalValues")
    pub_values = public.get("values")
    if not isinstance(shared, list) or not shared or not isinstance(pub_values, dict):
        print(f"ERROR: {public_path} has no sharedWithLegalValues / values", file=sys.stderr)
        return 1

    bad = False
    for key, value in pub_values.items():
        if not isinstance(value, str) or value == "":
            print(f"ERROR: operator-public.json: '{key}' is empty or not a string", file=sys.stderr)
            bad = True
        elif "ЗАПОЛНИТЬ" in value:
            print(f"ERROR: operator-public.json: '{key}' still holds the placeholder marker 'ЗАПОЛНИТЬ' (DO-48-1)", file=sys.stderr)
            bad = True
    if bad:
        return 1

    if not os.path.isfile(values_path):
        print(f"WARNING: {values_path} does not exist on this host (first-time setup) — requisites precheck skipped.", file=sys.stderr)
        return 0

    live = load(values_path, "legal.values.json").get("values")
    if not isinstance(live, dict):
        print(f"ERROR: {values_path} has no 'values' object", file=sys.stderr)
        return 1

    for key in shared:
        site = pub_values.get(key)
        if site is None:
            print(f"ERROR: key '{key}' is listed in sharedWithLegalValues but has no value in operator-public.json", file=sys.stderr)
            bad = True
            continue
        if key not in live:
            print(f"MISMATCH {key}: absent in {values_path}; operator-public.json: {site!r}", file=sys.stderr)
            bad = True
        elif live[key] != site:
            print(f"MISMATCH {key}: operator-public.json: {site!r}; {values_path}: {live[key]!r}", file=sys.stderr)
            bad = True
        else:
            print(f"  {key}: совпадает")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
