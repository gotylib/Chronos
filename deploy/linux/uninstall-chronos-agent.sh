#!/usr/bin/env bash
# Полное удаление Chronos.Agent: systemd-сервис, бинарники, конфиг, данные, пользователь chronos.
#
# По умолчанию спрашивает подтверждение. Требует root.
#
# Usage:
#   sudo ./uninstall-chronos-agent.sh
#   sudo ./uninstall-chronos-agent.sh --yes
#
# Опции:
#   --yes, -y     Не спрашивать подтверждение
#   --keep-data   Не удалять каталог данных и пользователя chronos (сервис и /opt, /etc всё равно снимаются)
#
# Не удаляет: установленный .NET runtime и прочие системные пакеты (см. setup-chronos-agent.sh).

set -euo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/chronos-agent}"
DATA_DIR="${DATA_DIR:-/var/lib/chronos-agent}"
ENV_DIR="${ENV_DIR:-/etc/chronos-agent}"
SERVICE_NAME="${SERVICE_NAME:-chronos-agent}"
AGENT_USER="${AGENT_USER:-chronos}"

YES=false
KEEP_DATA=false

usage() {
  sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --yes|-y) YES=true; shift ;;
    --keep-data) KEEP_DATA=true; shift ;;
    -h|--help) usage 0 ;;
    *) echo "Unknown option: $1" >&2; usage 1 ;;
  esac
done

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run as root (sudo)." >&2
  exit 1
fi

if [[ "$KEEP_DATA" == true ]]; then
  echo "Режим --keep-data: $DATA_DIR и пользователь $AGENT_USER не удаляются."
fi

if [[ "$YES" != true ]]; then
  echo "Будет удалено:"
  echo "  - сервис systemd: $SERVICE_NAME"
  echo "  - $INSTALL_DIR"
  echo "  - $ENV_DIR"
  if [[ "$KEEP_DATA" != true ]]; then
    echo "  - $DATA_DIR (и пользователь $AGENT_USER, если есть — домашний каталог при стандартной установке)"
  fi
  read -rp "Продолжить? [y/N]: " ans
  [[ "${ans:-}" =~ ^[Yy]$ ]] || { echo "Отмена."; exit 0; }
fi

systemctl stop "${SERVICE_NAME}.service" 2>/dev/null || true
systemctl disable "${SERVICE_NAME}.service" 2>/dev/null || true

UNIT="/etc/systemd/system/${SERVICE_NAME}.service"
if [[ -f "$UNIT" ]]; then
  rm -f "$UNIT"
  echo "Удалён $UNIT"
fi

systemctl daemon-reload 2>/dev/null || true

if [[ -e "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
  echo "Удалён $INSTALL_DIR"
fi

if [[ -e "$ENV_DIR" ]]; then
  rm -rf "$ENV_DIR"
  echo "Удалён $ENV_DIR"
fi

if [[ "$KEEP_DATA" != true ]]; then
  if id "$AGENT_USER" &>/dev/null; then
    if userdel -r "$AGENT_USER" 2>/dev/null; then
      echo "Удалён пользователь $AGENT_USER (включая домашний каталог, если он совпадал с записью в passwd)"
    else
      userdel "$AGENT_USER" 2>/dev/null || true
      echo "Пользователь $AGENT_USER удалён или отсутствовал"
    fi
  fi
  if [[ -d "$DATA_DIR" ]]; then
    rm -rf "$DATA_DIR"
    echo "Удалён $DATA_DIR"
  fi
fi

echo
echo "Готово. Chronos Agent снят с хоста."
if [[ "$KEEP_DATA" == true ]]; then
  echo "Сохранены $DATA_DIR и пользователь $AGENT_USER."
fi
echo
