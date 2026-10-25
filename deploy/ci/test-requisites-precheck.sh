#!/usr/bin/env bash
# CY48-D1 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.15, DO-48-3): сверка реквизитов выката на фикстурах.
# Гоняет deploy/checks/cycle48-operator-requisites.py (его вызывает check_operator_requisites в deploy-remote.sh):
#   равны -> 0; расхождение -> 1 и ключ в выводе; нет legal.values.json -> 0 + WARNING; маркер ЗАПОЛНИТЬ -> 1; битый JSON -> 1;
#   ключа нет в legal.values.json -> 1; разница в один пробел (ordinal, без trim) -> 1.
# Плюс реальный contracts/legal/operator-public.json не должен содержать маркера (иначе в develop не попасть).
set -euo pipefail
cd "$(dirname "$0")/../.."
CHECK=deploy/checks/cycle48-operator-requisites.py
W=$(mktemp -d); trap 'rm -rf "$W"' EXIT
fail() { echo "FAIL: $*"; exit 1; }

cat > "$W/public.json" <<'JSON'
{"sharedWithLegalValues": ["НАИМЕНОВАНИЕ_ОПЕРАТОРА", "ИНН_ОПЕРАТОРА"],
 "values": {"НАИМЕНОВАНИЕ_ОПЕРАТОРА": "Иванов Иван", "ИНН_ОПЕРАТОРА": "123456789012", "ТОЛЬКО_САЙТ": "x"}}
JSON
mk_values() { printf '{"values": {"НАИМЕНОВАНИЕ_ОПЕРАТОРА": "%s", "ИНН_ОПЕРАТОРА": "%s", "ПРОЧЕЕ": "y"}}' "$1" "$2" > "$W/values.json"; }
run() { python3 -I "$CHECK" "$W/public.json" "$1" >"$W/out" 2>&1; echo $?; }

mk_values "Иванов Иван" "123456789012"
[ "$(run "$W/values.json")" = 0 ] || { cat "$W/out"; fail "равные значения должны давать 0"; }

mk_values "Иванов Иван" "123456789013"
[ "$(run "$W/values.json")" = 1 ] || fail "расхождение должно давать 1"
grep -q 'ИНН_ОПЕРАТОРА' "$W/out" || fail "в выводе нет ключа расхождения"

mk_values "Иванов Иван " "123456789012"
[ "$(run "$W/values.json")" = 1 ] || fail "лишний пробел (без trim) должен давать 1"
grep -q 'НАИМЕНОВАНИЕ_ОПЕРАТОРА' "$W/out" || fail "в выводе нет ключа НАИМЕНОВАНИЕ_ОПЕРАТОРА"

echo '{"values": {"ИНН_ОПЕРАТОРА": "123456789012"}}' > "$W/values.json"
[ "$(run "$W/values.json")" = 1 ] || fail "отсутствующий ключ в legal.values.json должен давать 1"

[ "$(run "$W/нет-такого-файла.json")" = 0 ] || { cat "$W/out"; fail "нет legal.values.json -> 0"; }
grep -q 'WARNING' "$W/out" || fail "нет WARNING при отсутствии legal.values.json"

echo '{ битый' > "$W/values.json"
[ "$(run "$W/values.json")" = 1 ] || fail "битый legal.values.json должен давать 1"

mk_values "Иванов Иван" "123456789012"
sed 's/"x"/"ЗАПОЛНИТЬ (DO-48-1): что-то"/' "$W/public.json" > "$W/public-marker.json"
python3 -I "$CHECK" "$W/public-marker.json" "$W/values.json" >"$W/out" 2>&1 && fail "маркер ЗАПОЛНИТЬ должен давать 1"
grep -q 'ЗАПОЛНИТЬ' "$W/out" || fail "в выводе нет упоминания маркера"
echo '{ битый' > "$W/public-broken.json"
python3 -I "$CHECK" "$W/public-broken.json" "$W/values.json" >"$W/out" 2>&1 && fail "битый operator-public.json должен давать 1"

# Реальный источник: маркера быть не должно (на хосте без legal.values.json шаг вернёт 0 с WARNING).
python3 -I "$CHECK" contracts/legal/operator-public.json "$W/нет-файла.json" >"$W/out" 2>&1 || { cat "$W/out"; fail "contracts/legal/operator-public.json не проходит precheck (маркер ЗАПОЛНИТЬ?)"; }

echo "OK: precheck реквизитов (CY48-D1)"
