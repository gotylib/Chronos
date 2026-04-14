#!/usr/bin/env bash
# Единая точка входа: setup | install | uninstall с одинаковыми [OK]/[FAIL] в подскриптах.
#
#   sudo ./chronos-agent.sh                    # интерактивный setup (как setup-chronos-agent.sh)
#   sudo ./chronos-agent.sh /path/to.zip       # setup с путём к zip или каталогу
#   sudo ./chronos-agent.sh setup [путь]
#   sudo ./chronos-agent.sh install --publish-dir /path ...
#   sudo ./chronos-agent.sh uninstall [--yes] [--keep-data]
#
# Рядом должен лежать chronos-agent-lib.sh (входит в zip с GitHub).

set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Запусти от root: sudo $0 ..." >&2
  exit 1
fi

if [[ ! -f "$DEPLOY_DIR/chronos-agent-lib.sh" ]]; then
  echo "[FAIL] Не найден $DEPLOY_DIR/chronos-agent-lib.sh (нужен полный каталог deploy/linux из репозитория или zip)." >&2
  exit 1
fi

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage 0
fi

if [[ $# -eq 0 ]]; then
  exec bash "$DEPLOY_DIR/setup-chronos-agent.sh"
fi

case "${1:-}" in
  setup)
    shift
    exec bash "$DEPLOY_DIR/setup-chronos-agent.sh" "$@"
    ;;
  install)
    shift
    exec bash "$DEPLOY_DIR/install-chronos-agent.sh" "$@"
    ;;
  uninstall)
    shift
    exec bash "$DEPLOY_DIR/uninstall-chronos-agent.sh" "$@"
    ;;
  *)
    exec bash "$DEPLOY_DIR/setup-chronos-agent.sh" "$@"
    ;;
esac
