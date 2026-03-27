#!/usr/bin/env bash
# Интерактивная установка Chronos.Agent на Linux (framework-dependent, для jobs/тестов с DLL).
# — при необходимости ставит .NET 8 runtime (официальный dotnet-install.sh);
# — спрашивает порт, каталог данных, API key, опционально регистрацию в Master;
# — распаковывает zip с GitHub Release и ставит systemd-сервис.
#
# Использование (root):
#   sudo ./setup-chronos-agent.sh [/path/to/chronos-agent-linux-x64.zip]
#   sudo ./setup-chronos-agent.sh [/path/to/unzipped/folder]   # каталог, где лежит Chronos.Agent.dll
#
# Если аргумент не передан — спросит путь к zip или к распакованной папке.
# Нужны: curl; unzip — только если указываешь .zip (при отсутствии попробует apt-get install unzip).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-/usr/lib/dotnet}"
SERVICE_NAME="${SERVICE_NAME:-chronos-agent}"
ENV_DIR="/etc/chronos-agent"
ENV_FILE="$ENV_DIR/environment"

die() { echo "Error: $*" >&2; exit 1; }

[[ "$(id -u)" -eq 0 ]] || die "Запусти от root: sudo $0"

ensure_unzip() {
  command -v unzip >/dev/null 2>&1 && return 0
  if command -v apt-get >/dev/null 2>&1; then
    echo "Устанавливаю unzip..."
    apt-get update -qq && apt-get install -y unzip
    return 0
  fi
  die "Нужен unzip. Установи пакет unzip и повтори."
}

has_dotnet8_runtime() {
  command -v dotnet >/dev/null 2>&1 || return 1
  dotnet --list-runtimes 2>/dev/null | grep -qE 'Microsoft\.NETCore\.App 8\.'
}

ensure_dotnet_runtime() {
  if has_dotnet8_runtime; then
    echo "Найден .NET 8 runtime: $(command -v dotnet)"
    return 0
  fi
  echo ".NET 8 runtime не найден в PATH."
  read -rp "Установить через https://dot.net/v1/dotnet-install.sh в $DOTNET_INSTALL_DIR? [Y/n]: " ans
  [[ "${ans:-Y}" =~ ^[Nn]$ ]] && die "Установи dotnet-runtime-8.0 и запусти скрипт снова."

  command -v curl >/dev/null 2>&1 || die "Нужен curl."
  install -d -m 0755 "$DOTNET_INSTALL_DIR"
  tmp_sh="$(mktemp)"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$tmp_sh"
  chmod +x "$tmp_sh"
  "$tmp_sh" --channel 8.0 --runtime dotnet --install-dir "$DOTNET_INSTALL_DIR"
  rm -f "$tmp_sh"

  ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet 2>/dev/null || true
  export PATH="$DOTNET_INSTALL_DIR:$PATH"
  hash -r 2>/dev/null || true

  has_dotnet8_runtime || die "После установки dotnet всё ещё не видит runtime 8. Проверь PATH (должен быть $DOTNET_INSTALL_DIR)."
  echo "Установлен .NET 8 runtime."
}

read_source_path() {
  local p="${1:-}"
  while [[ -z "$p" ]]; do
    read -rp "Путь к chronos-agent-linux-x64.zip или к распакованной папке (с Chronos.Agent.dll): " p
    p="${p/#\~/$HOME}"
  done
  if [[ -d "$p" && -f "$p/Chronos.Agent.dll" ]]; then
    printf 'DIR:%s' "$(cd "$p" && pwd)"
  elif [[ -f "$p" ]]; then
    printf 'ZIP:%s' "$p"
  else
    die "Не найдено: $p (нужен .zip или каталог с Chronos.Agent.dll)"
  fi
}

write_environment() {
  local port="$1" data_dir="$2" api_key="$3" master_url="$4" agent_base="$5" master_key="$6" location="$7"

  install -d -m 0755 -o root -g root "$ENV_DIR"
  local tmp
  tmp="$(mktemp)"
  {
    echo "ASPNETCORE_URLS=http://0.0.0.0:${port}"
    echo "CHRONOS_AGENT_APP_PATH=${data_dir}"
    echo "CHRONOS_AGENT_DOCKER_COMPOSE_EXECUTABLE=auto"
    echo "CHRONOS_AGENT_DOCKER_EXECUTABLE=docker"
    [[ -n "$api_key" ]] && printf '%s\n' "CHRONOS_AGENT_API_KEY=${api_key}"
    if [[ -n "$master_url" && -n "$agent_base" ]]; then
      printf '%s\n' "CHRONOS_MASTER_URL=${master_url}"
      printf '%s\n' "CHRONOS_AGENT_BASE_URL=${agent_base}"
      [[ -n "$master_key" ]] && printf '%s\n' "CHRONOS_MASTER_API_KEY=${master_key}"
      [[ -n "$location" ]] && printf '%s\n' "CHRONOS_AGENT_LOCATION=${location}"
    fi
  } > "$tmp"
  install -m 0640 -o root -g chronos "$tmp" "$ENV_FILE"
  rm -f "$tmp"
  echo "Записан $ENV_FILE"
}

# --- main ---
SRC="$(read_source_path "${1:-}")"
USE_DIR=false
if [[ "$SRC" == DIR:* ]]; then
  EXTRACT="${SRC#DIR:}"
  USE_DIR=true
else
  ZIP_PATH="${SRC#ZIP:}"
  ensure_unzip
fi

ensure_dotnet_runtime

echo
echo "=== Настройка агента (Enter = значение по умолчанию) ==="
read -rp "Порт HTTP [5050]: " PORT
PORT="${PORT:-5050}"

read -rp "Каталог проектов compose и состояния [/var/lib/chronos-agent]: " DATA_DIR_IN
DATA_DIR_IN="${DATA_DIR_IN:-/var/lib/chronos-agent}"

read -rp "API key для заголовка X-API-Key (пусто = без авторизации): " API_KEY

MASTER_URL=""
AGENT_BASE=""
MASTER_KEY=""
LOCATION=""
read -rp "Регистрация в Chronos.Master? (y/N): " REG_MASTER
if [[ "${REG_MASTER:-}" =~ ^[Yy]$ ]]; then
  read -rp "URL Master (например http://10.0.0.1:5000): " MASTER_URL
  read -rp "Публичный URL этого агента (как master достучится): " AGENT_BASE
  read -rp "API key Master (если нужен, иначе Enter): " MASTER_KEY
  read -rp "Метка локации (location, необязательно): " LOCATION
fi

echo
if [[ "$USE_DIR" == true ]]; then
  echo "Используется каталог: $EXTRACT"
else
  echo "Распаковка $ZIP_PATH ..."
  EXTRACT="$(mktemp -d)"
  trap 'rm -rf "$EXTRACT"' EXIT
  unzip -q -o "$ZIP_PATH" -d "$EXTRACT"
fi

[[ -f "$EXTRACT/Chronos.Agent.dll" ]] || die "Нет Chronos.Agent.dll — нужен zip с GitHub (framework-dependent)."
[[ -f "$EXTRACT/deploy-linux/install-chronos-agent.sh" ]] || die "В архиве нет deploy-linux/install-chronos-agent.sh"

chmod +x "$EXTRACT/deploy-linux/install-chronos-agent.sh"

echo "Установка файлов сервиса..."
# DATA_DIR для user chronos должен совпадать с install (useradd --home-dir)
"$EXTRACT/deploy-linux/install-chronos-agent.sh" \
  --publish-dir "$EXTRACT" \
  --data-dir "$DATA_DIR_IN" \
  --skip-env \
  --no-start

if [[ "$USE_DIR" != true ]]; then
  trap - EXIT
  rm -rf "$EXTRACT"
fi

write_environment "$PORT" "$DATA_DIR_IN" "$API_KEY" "$MASTER_URL" "$AGENT_BASE" "$MASTER_KEY" "$LOCATION"

systemctl restart "$SERVICE_NAME"

echo
echo "Готово."
echo "  Статус:  systemctl status $SERVICE_NAME"
echo "  Логи:    journalctl -u $SERVICE_NAME -f"
echo "  URL:     http://<этот-хост>:${PORT}/"
echo
