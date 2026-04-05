#!/usr/bin/env bash
# Install Chronos.Agent as a systemd service (starts on boot, restarts on failure).
#
# Usage (on Linux, as root):
#   1) From published output:
#      dotnet publish src/Chronos.Agent/Chronos.Agent.csproj -c Release -o /tmp/chronos-agent-publish
#      sudo ./install-chronos-agent.sh --publish-dir /tmp/chronos-agent-publish
#
#   2) From repo with SDK on the server:
#      sudo ./install-chronos-agent.sh --build --repo-root /path/to/Chronos
#
#   3) One-liner after copying the deploy/linux folder:
#      sudo ./install-chronos-agent.sh --build --repo-root "$(pwd)/../.."
#
# Then edit /etc/chronos-agent/environment and: systemctl restart chronos-agent
#
# Requires: ASP.NET Core 8 runtime (Microsoft.AspNetCore.App), не только базовый dotnet — агент это веб-приложение.

set -euo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/chronos-agent}"
DATA_DIR="${DATA_DIR:-/var/lib/chronos-agent}"
ENV_DIR="${ENV_DIR:-/etc/chronos-agent}"
SERVICE_NAME="${SERVICE_NAME:-chronos-agent}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

usage() {
  sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
  echo
  echo "Options:"
  echo "  --publish-dir DIR   Published output (Chronos.Agent.dll + dependencies)"
  echo "  --build             Run 'dotnet publish' (needs SDK); use with --repo-root"
  echo "  --repo-root PATH    Repository root (directory containing src/Chronos.Agent)"
  echo "  --install-dir PATH  Default: $INSTALL_DIR"
  echo "  --data-dir PATH     Default: $DATA_DIR (CHRONOS_AGENT_APP_PATH in env example)"
  echo "  --skip-env          Do not create /etc/chronos-agent/environment (e.g. setup-chronos-agent.sh writes it)"
  echo "  --no-start          Do not systemctl restart (after writing env yourself)"
  exit "${1:-0}"
}

PUBLISH_DIR=""
BUILD=false
REPO_ROOT=""
SKIP_ENV=false
NO_START=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish-dir) PUBLISH_DIR="$2"; shift 2 ;;
    --skip-env) SKIP_ENV=true; shift ;;
    --no-start) NO_START=true; shift ;;
    --build) BUILD=true; shift ;;
    --repo-root) REPO_ROOT="$2"; shift 2 ;;
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --data-dir) DATA_DIR="$2"; shift 2 ;;
    -h|--help) usage 0 ;;
    *) echo "Unknown option: $1"; usage 1 ;;
  esac
done

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run as root (sudo)." >&2
  exit 1
fi

if [[ -n "$PUBLISH_DIR" && "$BUILD" == true ]]; then
  echo "Use either --publish-dir or --build, not both." >&2
  exit 1
fi

TMP_PUBLISH=""
cleanup() {
  [[ -n "$TMP_PUBLISH" && -d "$TMP_PUBLISH" ]] && rm -rf "$TMP_PUBLISH"
}
trap cleanup EXIT

if [[ "$BUILD" == true ]]; then
  [[ -n "$REPO_ROOT" ]] || { echo "--repo-root required with --build" >&2; exit 1; }
  command -v dotnet >/dev/null 2>&1 || { echo "'dotnet' not found. Install .NET 8 SDK or use --publish-dir." >&2; exit 1; }
  TMP_PUBLISH="$(mktemp -d)"
  dotnet publish "$REPO_ROOT/src/Chronos.Agent/Chronos.Agent.csproj" -c Release -o "$TMP_PUBLISH"
  PUBLISH_DIR="$TMP_PUBLISH"
fi

[[ -n "$PUBLISH_DIR" ]] || { echo "Specify --publish-dir DIR or --build --repo-root PATH" >&2; usage 1; }
[[ -f "$PUBLISH_DIR/Chronos.Agent.dll" ]] || { echo "Chronos.Agent.dll not found in $PUBLISH_DIR" >&2; exit 1; }

if ! id chronos &>/dev/null; then
  useradd --system --home-dir "$DATA_DIR" --create-home --shell /usr/sbin/nologin chronos
fi

usermod -aG docker chronos 2>/dev/null || echo "Warning: group 'docker' not found; add user chronos to docker group manually for compose access."

install -d -o root -g root -m 0755 "$INSTALL_DIR"
install -d -o chronos -g chronos -m 0750 "$DATA_DIR"
install -d -o root -g root -m 0755 "$ENV_DIR"

shopt -s nullglob
rm -rf "${INSTALL_DIR:?}/"*
shopt -u nullglob
cp -a "$PUBLISH_DIR"/. "$INSTALL_DIR/"
chown -R root:root "$INSTALL_DIR"
chmod -R a+rX "$INSTALL_DIR"
# Writable only where needed (logs, none by default in /opt)
find "$INSTALL_DIR" -type d -exec chmod 755 {} \;
find "$INSTALL_DIR" -type f -exec chmod 644 {} \;

ENV_FILE="$ENV_DIR/environment"
ENV_EXAMPLE="$SCRIPT_DIR/chronos-agent.env.example"
if [[ "$SKIP_ENV" == true ]]; then
  echo "Skipping $ENV_FILE (create it before first start, or use setup-chronos-agent.sh)"
elif [[ ! -f "$ENV_FILE" ]]; then
  if [[ -f "$ENV_EXAMPLE" ]]; then
    install -m 640 -o root -g chronos "$ENV_EXAMPLE" "$ENV_FILE"
    echo "Created $ENV_FILE — edit it, then: systemctl restart $SERVICE_NAME"
  else
    echo "ASPNETCORE_URLS=http://0.0.0.0:5050
CHRONOS_AGENT_APP_PATH=$DATA_DIR" > "$ENV_FILE"
    chown root:chronos "$ENV_FILE"
    chmod 640 "$ENV_FILE"
  fi
else
  echo "Keeping existing $ENV_FILE"
fi

install -m 0644 "$SCRIPT_DIR/chronos-agent.service" "/etc/systemd/system/${SERVICE_NAME}.service"

systemctl daemon-reload
systemctl enable "$SERVICE_NAME"
if [[ "$NO_START" != true ]]; then
  systemctl restart "$SERVICE_NAME"
fi

echo
echo "Installed $SERVICE_NAME"
echo "  Status:  systemctl status $SERVICE_NAME"
echo "  Logs:    journalctl -u $SERVICE_NAME -f"
echo "  Config:  $ENV_FILE"
[[ "$NO_START" == true ]] && echo "  Note:    service not started (--no-start); run: systemctl restart $SERVICE_NAME"
echo
