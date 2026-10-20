#!/usr/bin/env bash
# Brings the Windows VM up and waits until Parallels Tools answers. Safe to run any number of times.
#
# usage: vm-up.sh [--user] [--quiet] [--timeout SECONDS]
#   --user      also wait until the logged-in user's desktop session answers (needed to launch the app)
#   --quiet     print nothing when the VM was already up
#   --timeout   how long to wait for Parallels Tools (default 240)
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

need_user=0
quiet=0
timeout=240
while [ $# -gt 0 ]; do
  case "$1" in
    --user) need_user=1 ;;
    --quiet) quiet=1 ;;
    --timeout)
      [ $# -ge 2 ] || die "--timeout needs a number of seconds"
      timeout="$2"
      shift
      ;;
    -h | --help)
      sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) die "unknown argument: $1 (see --help)" ;;
  esac
  shift
done
case "$timeout" in '' | *[!0-9]*) die "--timeout needs a whole number of seconds, got \"$timeout\"" ;; esac

vm_require_prlctl
started=$SECONDS
state="$(vm_state)"
acted=0
case "$state" in
  running) ;;
  suspended | paused)
    say "VM \"$VM_NAME\" is $state: resuming"
    prlctl resume "$VM_NAME" >/dev/null || die "prlctl resume failed"
    acted=1
    ;;
  stopped)
    say "VM \"$VM_NAME\" is stopped: starting"
    prlctl start "$VM_NAME" >/dev/null || die "prlctl start failed"
    acted=1
    ;;
  resuming | starting) acted=1 ;;
  *) die "VM \"$VM_NAME\" is in state \"$state\"; wait for it or fix it in Parallels" ;;
esac

wait_for() {
  local label="$1"
  shift
  local answer
  while true; do
    answer="$(run_with_timeout 20 vm_exec "$@" cmd /c "echo ready" 2>/dev/null)" || answer=""
    case "$answer" in *ready*) return 0 ;; esac
    [ $((SECONDS - started)) -lt "$timeout" ] || die "$label did not answer within ${timeout}s (is Parallels Tools running in the VM? is the user signed in?)"
    sleep 2
  done
}

wait_for "Parallels Tools in \"$VM_NAME\""
if [ "$need_user" -eq 1 ]; then
  wait_for "the logged-in user session in \"$VM_NAME\"" --user
fi
if [ "$acted" -eq 1 ] || [ "$quiet" -eq 0 ]; then
  say "VM \"$VM_NAME\" is ready ($((SECONDS - started))s)"
fi
