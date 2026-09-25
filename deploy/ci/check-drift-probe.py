#!/usr/bin/env python3
"""Проверяет, что пробная миграция __DriftProbe получилась пустой.

ARCHITECTURE_CYCLE17.md §308.1. Непустой Up() значит, что AppDbContextModelSnapshot.cs
разошёлся с моделью: следующая настоящая миграция будет сгенерирована по испорченному
снапшоту и попытается применить уже применённые изменения — ровно дефект C15-3.

Жил инлайном в .github/workflows/ci.yml. Там он ломал YAML: строки python начинались с
первой колонки и обрывали блочный скаляр `run: |`, из-за чего GitHub не разбирал весь
workflow целиком. Вынесен файлом — заодно его можно запускать руками.
"""
import re
import sys

if len(sys.argv) != 2:
    print("usage: check-drift-probe.py <путь к *__DriftProbe.cs>", file=sys.stderr)
    sys.exit(2)

text = open(sys.argv[1], encoding="utf-8").read()
match = re.search(
    r"protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{(.*?)\n\s*\}",
    text,
    re.S,
)
body = match.group(1).strip() if match else None

if body:
    print(
        "::error::AppDbContextModelSnapshot.cs has drifted from the current model — "
        "Up() is not empty. See ARCHITECTURE_CYCLE17.md §308.1.",
        file=sys.stderr,
    )
    print(text, file=sys.stderr)
    sys.exit(1)
