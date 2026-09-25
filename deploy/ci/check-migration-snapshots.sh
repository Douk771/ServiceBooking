#!/usr/bin/env bash
# ARCHITECTURE_CYCLE17.md §308.2 "Designer snapshots are monotonic" — the cheap check that would have
# caught C15-4 the day it happened: a Designer.cs snapshot (the design-time model as EF Core saw it AT
# that migration) silently losing properties that both an EARLIER migration's snapshot AND today's
# AppDbContextModelSnapshot.cs still have — with no DropColumn(...) in that migration's own *.cs to
# explain the gap. That's exactly what happened to Company's five Address* properties between
# 20260924065320_AddCompanyAddressVerification.Designer.cs and the later
# 20260924074741_ResetUnverifiedPhoneNumberConfirmedMirror.Designer.cs.
#
# What this does NOT replace: the "Migrations snapshot drift" step (§308.1, `dotnet-ef migrations add
# __DriftProbe` with an empty Up() as the pass condition) is the AUTHORITATIVE check — it asks EF Core
# itself whether AppDbContextModelSnapshot.cs matches the CURRENT model. This script is a much cheaper,
# purely textual check that pinpoints WHICH past migration/entity/property regressed, without invoking
# dotnet-ef or a database.
#
# Scope, and why: only properties that AppDbContextModelSnapshot.cs (today's true model) still has are
# checked. A property that was genuinely removed from the model at some point in the project's real
# history (and is gone from the final snapshot too) is NOT flagged here — that is legitimate schema
# evolution, not drift, and re-litigating years of already-applied migrations is out of scope for a cheap
# CI guard. For every property the model has TODAY: starting from the first Designer.cs where it
# appears, it must appear in every later Designer.cs up to and including the final snapshot, UNLESS that
# migration's own (non-Designer) *.cs file actually DropColumn's it (a legitimate drop-then-re-add later
# is allowed — the "first appearance after the drop" restarts the run).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MIGRATIONS_DIR="$REPO_ROOT/ServiceBooking.Infrastructure/Migrations"
SNAPSHOT_FILE="$MIGRATIONS_DIR/AppDbContextModelSnapshot.cs"

python3 - "$MIGRATIONS_DIR" "$SNAPSHOT_FILE" <<'PYEOF'
import re
import sys
from pathlib import Path

migrations_dir = Path(sys.argv[1])
snapshot_file = Path(sys.argv[2])

ENTITY_RE = re.compile(r'modelBuilder\.Entity\("([^"]+)",\s*b\s*=>')
PROPERTY_RE = re.compile(r'b\.Property<[^>]+>\("([^"]+)"\)')
DROPCOLUMN_RE = re.compile(r'DropColumn\(\s*name:\s*"([^"]+)"')


def entities_and_properties(text: str) -> dict[str, set[str]]:
    """Splits the file on each `modelBuilder.Entity("X", b =>` occurrence and collects every
    `b.Property<...>("Name")` appearing before the NEXT such occurrence. A Designer.cs typically lists
    each entity twice (once for scalar properties/keys, once later for relationships/navigations) — this
    unions properties across all of an entity's occurrences in one file, which is only ever additive."""
    matches = list(ENTITY_RE.finditer(text))
    result: dict[str, set[str]] = {}
    for i, m in enumerate(matches):
        start = m.end()
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        block = text[start:end]
        props = set(PROPERTY_RE.findall(block))
        result.setdefault(m.group(1), set()).update(props)
    return result


def dropped_column_names(text: str) -> set[str]:
    return set(DROPCOLUMN_RE.findall(text))


# Historical baseline (documented, not silently swallowed): migrations before this one were produced by
# repeated cross-branch/cycle merges before this monotonicity convention existed, and their Designer.cs
# snapshots genuinely show properties (the 20260921-20260922 migration cluster) and even whole entities
# for a single migration (PhoneVerificationSession/VerifiedPhone vanish for exactly
# 20260924065320_AddCompanyAddressVerification, then reappear in 20260924074741) vanishing and
# reappearing — a known, already-applied-to-prod artifact
# of that merge history, not a live drift bug worth re-litigating now (doing so would mean hand-auditing
# dozens of already-shipped migrations for zero safety benefit, since the DB already reflects
# AppDbContextModelSnapshot.cs's current, correct state; the PhoneVerification entities are also
# explicitly out of this cycle's scope per ARCHITECTURE_CYCLE17.md §300.1 Р6/§316 Р-17-1). Recorded as
# technical debt C17-8 in CURRENT_STATE.md §9 (block "C17 — долг, зафиксированный циклом 17") rather
# than fixed here — that entry carries the by-name list of every gap and the rule below. This baseline starts right AFTER the
# migration this cycle's own C15-4 fix touches (074741, ARCHITECTURE_CYCLE17.md §308.1) — the authoritative
# `dotnet-ef migrations add __DriftProbe` step already proves that specific fix is correct; this script's
# job from here on is to keep the FUTURE chain (074741 -> ... -> today's snapshot) from regressing again,
# not to re-validate the one historical pair it just fixed. Bumping this constant backwards to "cover" a
# new failure defeats the point of the check — only move it forward, and only for a documented reason.
BASELINE_MIGRATION = "20260924074741_ResetUnverifiedPhoneNumberConfirmedMirror"

designer_files = sorted(migrations_dir.glob("*.Designer.cs"), key=lambda p: p.name)
if not designer_files:
    print(f"::error::No *.Designer.cs files found under {migrations_dir}", file=sys.stderr)
    sys.exit(1)
if not snapshot_file.exists():
    print(f"::error::{snapshot_file} not found", file=sys.stderr)
    sys.exit(1)

# entity -> property -> ordered list of (designer_filename, present: bool, dropped_here: bool)
history: dict[str, dict[str, list[tuple[str, bool, bool]]]] = {}

for designer_file in designer_files:
    entities = entities_and_properties(designer_file.read_text(encoding="utf-8"))
    migration_cs = designer_file.with_name(designer_file.name.removesuffix(".Designer.cs") + ".cs")
    dropped = dropped_column_names(migration_cs.read_text(encoding="utf-8")) if migration_cs.exists() else set()

    for entity, props in entities.items():
        entity_history = history.setdefault(entity, {})
        for prop in props:
            entity_history.setdefault(prop, []).append((designer_file.name, True, prop in dropped))

# Now fold in "absent" entries so each property has one entry per designer file it COULD have appeared
# in (from its first appearance onward), to detect gaps.
all_designer_names = [f.name for f in designer_files]
baseline_idx = next(
    (i for i, name in enumerate(all_designer_names) if name.startswith(BASELINE_MIGRATION)), 0
)
for entity, props in history.items():
    for prop, occurrences in props.items():
        present_files = {name for name, present, _ in occurrences}
        first_idx = max(min(all_designer_names.index(name) for name in present_files), baseline_idx)
        full_run = []
        for name in all_designer_names[first_idx:]:
            if name in present_files:
                full_run.append((name, True, False))
            else:
                migration_cs = migrations_dir / (name.removesuffix(".Designer.cs") + ".cs")
                dropped_text = migration_cs.read_text(encoding="utf-8") if migration_cs.exists() else ""
                full_run.append((name, False, prop in dropped_column_names(dropped_text)))
        history[entity][prop] = full_run

snapshot_entities = entities_and_properties(snapshot_file.read_text(encoding="utf-8"))

failures: list[str] = []

# Проверка новейшего Designer'а отдельно от прогона по истории.
#
# Прогон ниже строит историю только из файлов, где свойство ЕСТЬ, и молча пропускает свойство,
# которого нет ни в одном Designer'е (ветка `if not run: continue`). Из-за этого мимо проходила
# ровно та форма дефекта C15-3, ради которой скрипт и писался: миграция, сгенерированная по
# испорченному снапшоту, не содержит новой колонки в своём же Designer'е. Проверено: удаление
# Company.ClientRescheduleMinHours из Designer'а миграции, которая его добавила, давало OK.
#
# Инвариант, который это закрывает, простой: после последней миграции ничего не применялось,
# поэтому её Designer обязан совпадать с сегодняшним AppDbContextModelSnapshot.cs.
if designer_files:
    newest_designer = designer_files[-1]
    newest_entities = entities_and_properties(newest_designer.read_text(encoding="utf-8"))
    for entity, final_props in snapshot_entities.items():
        for prop in sorted(final_props - newest_entities.get(entity, set())):
            failures.append(
                f"entity '{entity}', property '{prop}': присутствует в AppDbContextModelSnapshot.cs, "
                f"но отсутствует в новейшем снапшоте {newest_designer.name}. После последней миграции "
                f"ничего не применялось, значит эти два файла обязаны совпадать — расхождение значит, "
                f"что миграция сгенерирована по испорченному снапшоту (C15-3)"
            )
for entity, final_props in snapshot_entities.items():
    for prop in final_props:
        run = history.get(entity, {}).get(prop)
        if not run:
            # Property exists today but never appeared in any committed Designer.cs snapshot — can
            # legitimately happen for an entity/property pair whose migration predates the earliest
            # migration in this repo (squash/history-trim); not this script's concern.
            continue
        # Walk the run; a gap resets "last seen" only if explained by a DropColumn in that same file.
        missing_unexplained = [name for name, present, dropped_here in run if not present and not dropped_here]
        if missing_unexplained:
            failures.append(
                f"entity '{entity}', property '{prop}': missing from {missing_unexplained} with no "
                f"matching DropColumn(...) in that migration's own .cs file, even though it's present in "
                f"AppDbContextModelSnapshot.cs today"
            )

if failures:
    print("Migration Designer snapshots are not monotonic (ARCHITECTURE_CYCLE17.md §308.2):", file=sys.stderr)
    for f in sorted(failures):
        print(f"  - {f}", file=sys.stderr)
    sys.exit(1)

print(f"OK: {len(designer_files)} Designer.cs snapshot(s) checked against AppDbContextModelSnapshot.cs, "
      f"no unexplained property loss found.")
PYEOF
