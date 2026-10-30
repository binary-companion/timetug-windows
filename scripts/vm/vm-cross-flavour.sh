#!/usr/bin/env bash
# Runs the cross-flavour-instance self-test in the Windows VM: builds a locally signed Direct package, installs it beside the development package,
# runs the scenario (the development copy holds its marker; the Direct app must show the collision notice and exit without touching its data),
# and removes the Direct package again. Exit code 0 only when the scenario passed.
#
# usage: vm-cross-flavour.sh [--keep] [--out DIR]
#   --keep   leave the Direct package installed afterwards (for looking at it)
#   --out    where the scenario's results go (default build/vm-selftest/<timestamp>)
#
# The VM builds the commit it has checked out: run vm-sync.sh first. Needs a signed-in user in the VM.
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

keep=0
out=""
while [ $# -gt 0 ]; do
  case "$1" in
    --keep) keep=1 ;;
    --out)
      [ $# -ge 2 ] || die "--out needs a folder"
      out="$2"
      shift
      ;;
    -h | --help)
      sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) die "unknown option: $1 (see --help)" ;;
  esac
  shift
done

vm_require_prlctl
"$VM_SCRIPTS_DIR/vm-up.sh" --user >&2

script="$VM_REPO\\scripts\\vm\\vm-cross-flavour.ps1"
say "building and installing a locally signed Direct package"
vm_job system cross-build 1500 "& '$script' -Stage Build"
vm_job user cross-install 300 "& '$script' -Stage Install"

status=0
selftest_args=(cross-flavour-instance)
[ -z "$out" ] || selftest_args+=(--out "$out")
"$VM_SCRIPTS_DIR/vm-selftest.sh" "${selftest_args[@]}" || status=$?

if [ "$keep" -eq 0 ]; then
  say "removing the Direct package again"
  vm_job user cross-cleanup 300 "& '$script' -Stage Cleanup" || true
  vm_job system cross-cleanup-system 300 "& '$script' -Stage Cleanup" || true
fi
exit "$status"
