#!/usr/bin/env bash
# Общие функции для deploy-скриптов Chronos Agent (source, не запускать напрямую).
# shellcheck shell=bash

if [[ -n "${CHRONOS_AGENT_LIB_LOADED:-}" ]]; then
  return 0 2>/dev/null || exit 0
fi
CHRONOS_AGENT_LIB_LOADED=1

CHRONOS_STEP=0

chronos_step() {
  CHRONOS_STEP=$((CHRONOS_STEP + 1))
  echo ""
  echo "═══════════════════════════════════════════════════════════════"
  echo "  [$CHRONOS_STEP] $*"
  echo "═══════════════════════════════════════════════════════════════"
}

chronos_ok() {
  echo "  [OK] $*"
}

chronos_fail_msg() {
  echo "  [FAIL] $*" >&2
}

# Вызывается из trap ERR (set -e): печатает код и не дублирует exit, если уже обработано.
chronos_on_err() {
  local ec=$?
  echo ""
  echo ">>> [FAIL] Команда завершилась с кодом $ec. Смотрите сообщение об ошибке выше."
  exit "$ec"
}

chronos_enable_err_trap() {
  trap 'chronos_on_err' ERR
}
