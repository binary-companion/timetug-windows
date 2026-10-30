#!/usr/bin/env bash
# Runs the cross-flavour-instance self-test in the Windows VM: builds a locally signed Direct package, installs it beside the development package,
# runs the scenario (the development copy holds its marker; the Direct app must show the collision notice and exit without touching its data),
# and removes the Direct package again. Exit code 0 only when the scenario passed.
#
# usage: vm-cross-flavour.sh [--keep] [--out DIR]
#   --keep   leave the Direct package installed afterwards (for looking at it); the id file and the trusted certificate still go
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
cleanup_args=""
[ "$keep" -eq 0 ] || cleanup_args=" -KeepPackage"

# Whatever happens below, the VM is put back: the Direct package (unless --keep), the id file and the trusted throwaway certificate.
# A cleanup that fails turns a passing run into a failure, so a dirty VM is never reported as clean.
cleaned=0
cleanup() {
  [ "$cleaned" -eq 0 ] || return 0
  cleaned=1
  local failed=0
  say "putting the VM back"
  vm_job user cross-cleanup 300 "& '$script' -Stage Cleanup$cleanup_args" || failed=1
  vm_job system cross-cleanup-system 300 "& '$script' -Stage Cleanup$cleanup_args" || failed=1
  return "$failed"
}
finish() {
  local code=$?
  local cleanup_status=0
  cleanup || cleanup_status=$?
  if [ "$code" -eq 0 ] && [ "$cleanup_status" -ne 0 ]; then
    say "the self-test passed but the cleanup did not finish: check the VM (Get-AppxPackage BinaryCompanion.TimeTug, Cert:\\LocalMachine\\TrustedPeople)"
    code=3
  fi
  exit "$code"
}
trap finish EXIT

say "building and installing a locally signed Direct package"
vm_job system cross-build 1500 "& '$script' -Stage Build"
vm_job user cross-install 300 "& '$script' -Stage Install"

selftest_args=(cross-flavour-instance)
[ -z "$out" ] || selftest_args+=(--out "$out")
"$VM_SCRIPTS_DIR/vm-selftest.sh" "${selftest_args[@]}"
