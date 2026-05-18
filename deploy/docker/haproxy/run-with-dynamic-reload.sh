#!/bin/sh
# Watch chronos-tcp.cfg (written by Chronos.Master); SIGHUP haproxy so new listen ports apply.
# File must use LF line endings (Windows CRLF breaks "set" and options).

CFG_MAIN="/usr/local/etc/haproxy/haproxy.cfg"
CFG_DYN="/usr/local/etc/haproxy/dynamic/chronos-tcp.cfg"

cleanup() {
  [ -n "${HPID:-}" ] && kill -TERM "$HPID" 2>/dev/null || true
  exit 0
}
trap cleanup TERM INT

haproxy -db -f "$CFG_MAIN" -f "$CFG_DYN" &
HPID=$!

md5() {
  if [ -f "$CFG_DYN" ]; then
    md5sum "$CFG_DYN" | awk '{print $1}'
  else
    echo ""
  fi
}

prev="$(md5)"
while sleep 2; do
  if ! kill -0 "$HPID" 2>/dev/null; then
    echo "[haproxy-wrap] haproxy exited; restarting."
    haproxy -db -f "$CFG_MAIN" -f "$CFG_DYN" &
    HPID=$!
    prev="$(md5)"
    continue
  fi
  cur="$(md5)"
  if [ "$cur" != "$prev" ]; then
    prev="$cur"
    echo "[haproxy-wrap] $CFG_DYN changed, sending SIGHUP to haproxy (pid $HPID)."
    kill -s HUP "$HPID" 2>/dev/null || true
  fi
done
