#!/usr/bin/env bash
# Интерактивная установка Chronos.Agent на Linux (framework-dependent, для jobs/тестов с DLL).
# — при необходимости ставит .NET 8 runtime (официальный dotnet-install.sh);
# — спрашивает порт, каталог данных, API key, опционально регистрацию в Master;
# — распаковывает zip с GitHub Release и ставит systemd-сервис.
#
# Использование (root):
#   sudo ./chronos-agent.sh [zip|каталог]   # то же, что setup (удобная обёртка)
#   sudo ./setup-chronos-agent.sh [/path/to/chronos-agent-linux-x64.zip]
#   sudo ./setup-chronos-agent.sh [/path/to/unzipped/folder]   # каталог, где лежит Chronos.Agent.dll
# Рядом должен быть chronos-agent-lib.sh (входит в zip и репозиторий).
#
# Если аргумент не передан — спросит путь к zip или к распакованной папке.
# Нужны: curl; unzip — только если указываешь .zip (при отсутствии попробует apt-get install unzip).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[[ -f "$SCRIPT_DIR/chronos-agent-lib.sh" ]] || {
  echo "Нужен файл chronos-agent-lib.sh рядом с этим скриптом: $SCRIPT_DIR" >&2
  exit 1
}
# shellcheck source=chronos-agent-lib.sh
source "$SCRIPT_DIR/chronos-agent-lib.sh"
chronos_enable_err_trap

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

# Chronos.Agent — ASP.NET Core: нужен shared framework Microsoft.AspNetCore.App, не только Microsoft.NETCore.App.
has_dotnet8_runtime() {
  command -v dotnet >/dev/null 2>&1 || return 1
  dotnet --list-runtimes 2>/dev/null | grep -qE 'Microsoft\.AspNetCore\.App 8\.'
}

ensure_dotnet_runtime() {
  if has_dotnet8_runtime; then
    echo "Найден ASP.NET Core 8 runtime: $(command -v dotnet)"
    return 0
  fi
  echo "ASP.NET Core 8 runtime (Microsoft.AspNetCore.App) не найден."
  read -rp "Установить через https://dot.net/v1/dotnet-install.sh (--runtime aspnetcore) в $DOTNET_INSTALL_DIR? [Y/n]: " ans
  [[ "${ans:-Y}" =~ ^[Nn]$ ]] && die "Установи aspnetcore-runtime-8.0 (apt) или dotnet-install.sh --runtime aspnetcore."

  command -v curl >/dev/null 2>&1 || die "Нужен curl."
  install -d -m 0755 "$DOTNET_INSTALL_DIR"
  tmp_sh="$(mktemp)"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$tmp_sh"
  chmod +x "$tmp_sh"
  "$tmp_sh" --channel 8.0 --runtime aspnetcore --install-dir "$DOTNET_INSTALL_DIR"
  rm -f "$tmp_sh"

  ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet 2>/dev/null || true
  export PATH="$DOTNET_INSTALL_DIR:$PATH"
  hash -r 2>/dev/null || true

  has_dotnet8_runtime || die "После установки всё ещё нет Microsoft.AspNetCore.App 8.x. Проверь: dotnet --list-runtimes"
  echo "Установлен ASP.NET Core 8 runtime."
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
  # root:root: пользователь chronos ещё может не существовать (запись до install-chronos-agent.sh).
  # После установки файлов сервиса делаем chown root:chronos при необходимости.
  install -m 0640 -o root -g root "$tmp" "$ENV_FILE"
  rm -f "$tmp"
  echo "Записан $ENV_FILE"
}

# --- main ---
chronos_step "Источник установки (zip или каталог с Chronos.Agent.dll)"
SRC="$(read_source_path "${1:-}")"
chronos_ok "источник выбран"

USE_DIR=false
if [[ "$SRC" == DIR:* ]]; then
  EXTRACT="${SRC#DIR:}"
  USE_DIR=true
else
  ZIP_PATH="${SRC#ZIP:}"
  chronos_step "Утилита unzip (нужна для .zip)"
  ensure_unzip
  chronos_ok "unzip доступен"
fi

chronos_step "Проверка .NET 8 (Microsoft.AspNetCore.App)"
ensure_dotnet_runtime
chronos_ok "runtime доступен"

echo
chronos_step "Параметры сервиса (порт, данные, API key, Master)"
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
chronos_ok "параметры введены"

echo
chronos_step "Распаковка и проверка файлов агента"
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
chronos_ok "Chronos.Agent.dll и deploy-linux на месте"

chronos_step "Запись $ENV_FILE (до копирования в /opt)"
write_environment "$PORT" "$DATA_DIR_IN" "$API_KEY" "$MASTER_URL" "$AGENT_BASE" "$MASTER_KEY" "$LOCATION"
chronos_ok "конфиг записан"

chronos_step "Копирование в /opt и systemd (install-chronos-agent.sh)"
echo "Сообщение install про «Skipping environment» ожидаемо: $ENV_FILE уже создан выше."
# DATA_DIR для user chronos должен совпадать с install (useradd --home-dir)
"$EXTRACT/deploy-linux/install-chronos-agent.sh" \
  --publish-dir "$EXTRACT" \
  --data-dir "$DATA_DIR_IN" \
  --skip-env \
  --no-start
chronos_ok "файлы сервиса и unit установлены"

if [[ "$USE_DIR" != true ]]; then
  trap - EXIT
  rm -rf "$EXTRACT"
fi

chronos_step "Права на environment и запуск $SERVICE_NAME"
if id chronos &>/dev/null && [[ -f "$ENV_FILE" ]]; then
  chown root:chronos "$ENV_FILE" 2>/dev/null || true
fi

systemctl restart "$SERVICE_NAME"
chronos_ok "сервис перезапущен"

echo ""
echo "  [OK] Установка завершена успешно."
echo ""
echo "Готово."
echo "  Статус:  systemctl status $SERVICE_NAME"
echo "  Логи:    journalctl -u $SERVICE_NAME -f"
echo "  URL:     http://<этот-хост>:${PORT}/"
echo
