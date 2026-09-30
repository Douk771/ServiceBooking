#!/usr/bin/env bash
# Отдельный локальный стек для съёмки скриншотов goods (ARCHITECTURE_CYCLE30.md §30.7.1).
# compose-проект sb-shots, свой том, порты 55432/55000. Обычный dev-стек не затрагивается.
set -euo pipefail

usage() {
  cat <<'USAGE'
Использование: stack.sh up | down | reset | --help

  up     поднять postgres и api проекта sb-shots (том сохраняется)
  down   остановить стек, том сохраняется
  reset  down -v (том удаляется) + up -d --build: чистая база и обнулённые лимиты

Порты: API 55000, Postgres 55432 (переопределяются SB_API_PORT / SB_DB_PORT).
Коды выхода: 0 успех, 1 ошибка Docker/compose, 64 неверный аргумент.
USAGE
}

cmd="${1:-}"
case "$cmd" in
  -h|--help) usage; exit 0 ;;
  up|down|reset) ;;
  *) echo "stack.sh: нужна команда up, down или reset (см. --help)" >&2; exit 64 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"

# colima: без этих переменных Docker не находится (сокета /var/run/docker.sock нет).
if [ -z "${DOCKER_HOST:-}" ] && [ -S "$HOME/.colima/default/docker.sock" ]; then
  export DOCKER_HOST="unix://$HOME/.colima/default/docker.sock"
fi
export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE="${TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE:-/var/run/docker.sock}"

export SB_DB_PORT="${SB_DB_PORT:-55432}"
export SB_API_PORT="${SB_API_PORT:-55000}"
export SB_DB_NAME="${SB_DB_NAME:-servicebooking}"

if ! command -v docker >/dev/null 2>&1; then
  echo "stack.sh: docker не найден в PATH" >&2; exit 1
fi
if ! docker info >/dev/null 2>&1; then
  echo "stack.sh: Docker не отвечает. Для colima выполните 'colima start'." >&2; exit 1
fi

compose() { docker compose -p sb-shots -f "$repo_root/docker-compose.yml" "$@"; }

case "$cmd" in
  up)    compose up -d --build postgres api ;;
  down)  compose down ;;
  reset) compose down -v; compose up -d --build postgres api ;;
esac
echo "stack.sh $cmd: готово (проект sb-shots, API http://localhost:$SB_API_PORT)"
