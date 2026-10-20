#!/usr/bin/env bash
# Suspends the Windows VM (a fast resume later). Does nothing when it is already suspended or stopped.
#
# usage: vm-suspend.sh
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

case "${1:-}" in
  -h | --help)
    sed -n '2,5p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
    ;;
  '') ;;
  *) die "unknown argument: $1 (see --help)" ;;
esac

vm_require_prlctl
state="$(vm_state)"
case "$state" in
  running)
    say "suspending \"$VM_NAME\""
    prlctl suspend "$VM_NAME" >/dev/null || die "prlctl suspend failed"
    say "VM \"$VM_NAME\" is suspended"
    ;;
  suspended | paused | stopped) say "VM \"$VM_NAME\" is already $state: nothing to do" ;;
  *) die "VM \"$VM_NAME\" is in state \"$state\"; not suspending" ;;
esac
