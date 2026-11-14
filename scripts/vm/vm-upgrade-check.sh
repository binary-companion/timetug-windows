#!/usr/bin/env bash
# Upgrade in place, in the Windows VM: builds the development package with an older engine and the checked-out one, installs the old one with synthetic
# engine data, installs the new one over it and checks that the engine reads the same accounts, settings, ledger and learned corrections (see
# vm-upgrade-check.ps1 for what is compared). Copies the small result files (no screenshots, no calendar content) back to the Mac. Exit code 0 only when
# every check passed.
#
# usage: vm-upgrade-check.sh [--old-engine VERSION] [--out DIR] [--skip-build]
#   --old-engine   the TimeTug.Engine version of the "before" package (default: the version pinned on origin/main in the VM checkout)
#   --out DIR      where results go (default build/vm-upgrade-check/<timestamp> in this repository)
#   --skip-build   reuse the two packages built earlier in the VM
#
# The VM builds the commit it has checked out: run vm-sync.sh first. Needs a signed-in user in the VM. The installed development package is replaced
# (its data too); do not run this in a VM whose TimeTug data you want to keep.
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

old_engine=""
out=""
build=1
while [ $# -gt 0 ]; do
  case "$1" in
    --old-engine)
      [ $# -ge 2 ] || die "--old-engine needs a version"
      old_engine="$2"
      shift
      ;;
    --out)
      [ $# -ge 2 ] || die "--out needs a folder"
      out="$2"
      shift
      ;;
    --skip-build) build=0 ;;
    -h | --help)
      sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) die "unknown argument: $1 (see --help)" ;;
  esac
  shift
done
case "$old_engine" in '' | [0-9]*) ;; *) die "--old-engine must be a version like 1.8.0, got \"$old_engine\"" ;; esac
case "$old_engine" in *[!0-9A-Za-z.-]*) die "--old-engine has characters a version cannot have: $old_engine" ;; esac
if [ -z "$out" ]; then out="$VM_REPO_ROOT/build/vm-upgrade-check/$(date +%Y%m%d-%H%M%S)"; fi

vm_require_prlctl
started=$SECONDS
mkdir -p "$out/logs"
"$VM_SCRIPTS_DIR/vm-up.sh" --user

# run_job <system|user> <name> <timeout> <show-pattern> <powershell code>: log everything, show the lines matching the pattern.
run_job() {
  local who="$1" name="$2" limit="$3" pattern="$4" code="$5"
  local codefile
  codefile="$(mktemp)"
  {
    status=0
    vm_job "$who" "$name" "$limit" "$code" || status=$?
    echo "$status" >"$codefile"
  } | tee "$out/logs/$name.log" | grep --line-buffered -E "$pattern" >&2 || true
  local result
  result="$(cat "$codefile")"
  rm -f "$codefile"
  return "${result:-1}"
}

old_argument=""
[ -n "$old_engine" ] && old_argument=" -OldEngineVersion '$old_engine'"

if [ "$build" -eq 1 ]; then
  say "building the old and the new development package in the VM (a few minutes)"
  run_job system upgrade-build 2400 '^\[|rror|Trusted|FAIL|fail|Exception' "& '$VM_REPO\\scripts\\vm\\vm-upgrade-check.ps1' -Stage Build$old_argument" || die "the build failed; see $out/logs/upgrade-build.log"
fi

say "running the upgrade check as the logged-in user"
status=0
run_job user upgrade-run 1800 '.' "& '$VM_REPO\\scripts\\vm\\vm-upgrade-check.ps1' -Stage Run$old_argument" || status=$?

say "copying the results to $out/results"
vm_get "$VM_REPO\\build\\upgrade-check\\results" "$out/results" || say "warning: could not copy the results back"
{
  echo
  echo "== upgrade check summary =="
  grep -E '^\[upgrade-check\]|^  (ok|FAIL) |upgrade check (passed|FAILED)' "$out/logs/upgrade-run.log" || echo "(no summary lines in the log; see $out/logs/upgrade-run.log)"
  if [ "$status" -eq 0 ]; then
    echo "RESULT: passed ($((SECONDS - started))s end to end)"
  else
    echo "RESULT: FAILED ($((SECONDS - started))s end to end)"
  fi
} >&2
exit "$status"
