#!/usr/bin/env bash
# Builds (or installs a CI artifact), runs the self-test scenarios in the Windows VM, copies the results and
# screenshots back to the Mac and prints the pass/fail summary. Exit code 0 only when every scenario passed.
#
# usage: vm-selftest.sh [<scenario>|all] [--artifact DIR] [--no-build] [--hold SECONDS] [--out DIR] [--platform ARM64|x64]
#   <scenario>      one scenario from scripts/ci/selftest-scenarios.txt, or all (default)
#   --artifact DIR  install this CI artifact instead of building: the unpacked msix-arm64 folder, e.g. from
#                   gh run download <run> --name msix-arm64 --dir DIR
#   --no-build      reuse the package already installed in the VM (no build, no install)
#   --hold SECONDS  keep the first thing a scenario shows on screen this long (for vm-screenshot.sh); one scenario only
#   --out DIR       where results go (default build/vm-selftest/<timestamp> in this repository)
#   --platform      ARM64 (default, what the VM runs) or x64 (needs the x64 .NET desktop runtime in the VM)
#
# Without --artifact the VM builds the commit it has checked out: run vm-sync.sh first. Needs a signed-in user in the VM.
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

scenario="all"
artifact=""
build=1
hold=0
out=""
platform="${VM_PLATFORM:-ARM64}"
scenario_given=0
while [ $# -gt 0 ]; do
  case "$1" in
    --artifact)
      [ $# -ge 2 ] || die "--artifact needs a folder"
      artifact="$2"
      shift
      ;;
    --no-build) build=0 ;;
    --hold)
      [ $# -ge 2 ] || die "--hold needs a number of seconds"
      hold="$2"
      shift
      ;;
    --out)
      [ $# -ge 2 ] || die "--out needs a folder"
      out="$2"
      shift
      ;;
    --platform)
      [ $# -ge 2 ] || die "--platform needs ARM64 or x64"
      platform="$2"
      shift
      ;;
    -h | --help)
      sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    -*) die "unknown option: $1 (see --help)" ;;
    *)
      [ "$scenario_given" -eq 0 ] || die "only one scenario can be named (got \"$scenario\" and \"$1\")"
      scenario="$1"
      scenario_given=1
      ;;
  esac
  shift
done
case "$hold" in '' | *[!0-9]*) die "--hold needs a whole number of seconds, got \"$hold\"" ;; esac
case "$platform" in ARM64 | x64) ;; *) die "--platform must be ARM64 or x64, got \"$platform\"" ;; esac
case "$scenario" in *[!a-z0-9-]*) die "not a scenario name: $scenario" ;; esac
if [ "$hold" -gt 0 ] && [ "$scenario" = "all" ]; then die "--hold needs one named scenario"; fi
if [ -n "$artifact" ]; then
  [ -d "$artifact" ] || die "artifact folder not found: $artifact"
  [ "$build" -eq 1 ] || die "--artifact and --no-build contradict each other"
fi
if [ -z "$out" ]; then out="$VM_REPO_ROOT/build/vm-selftest/$(date +%Y%m%d-%H%M%S)"; fi

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

if [ -n "$artifact" ]; then
  say "copying the artifact to the VM"
  vm_put "$artifact" 'C:\src\vm-artifact'
  if [ -f "$artifact/commit.txt" ]; then
    say "the artifact was built from commit $(tr -d '[:space:]' <"$artifact/commit.txt")"
  fi
  say "trusting the certificate (SYSTEM) and installing the package (logged-in user)"
  run_job system install-trust 300 '.' "& 'C:\\src\\vm-artifact\\vm-install.ps1' -Stage Trust" || die "trusting the certificate failed; see $out/logs/install-trust.log"
  run_job user install 600 '.' "& 'C:\\src\\vm-artifact\\vm-install.ps1' -Stage Install" || die "installing the package failed; see $out/logs/install.log"
elif [ "$build" -eq 1 ]; then
  local_head="$(git -C "$VM_REPO_ROOT" rev-parse HEAD 2>/dev/null || true)"
  vm_head="$(vm_ps "git -C '$VM_REPO' rev-parse HEAD" 2>/dev/null | tr -d '[:space:]')" || vm_head=""
  if [ -n "$local_head" ] && [ "$local_head" != "$vm_head" ]; then
    say "note: the VM is at ${vm_head:-an unknown commit}, this checkout at $local_head (vm-sync.sh puts them together)"
  fi
  say "building the development package in the VM ($platform; takes a few minutes)"
  run_job system build 1500 '^\[|rror|Trusted|FAIL|fail|Exception' "& '$VM_REPO\\scripts\\ci\\build-msix.ps1' -Platform $platform -Stage Build" || die "the build failed; see $out/logs/build.log"
  say "installing the package for the logged-in user"
  run_job user install 600 'Installed|rror|fail|Exception' "& '$VM_REPO\\scripts\\ci\\build-msix.ps1' -Platform $platform -Stage Install" || die "the install failed; see $out/logs/install.log"
fi

say "running the self-test ($scenario) in the VM"
hold_argument=""
[ "$hold" -gt 0 ] && hold_argument=" -HoldSeconds $hold"
test_status=0
run_job user selftest 1800 '.' "& '$VM_REPO\\scripts\\ci\\run-selftest.ps1' -Scenario '$scenario' -OutputFolder '$VM_REPO\\build\\selftest'$hold_argument" || test_status=$?

say "copying results and screenshots to $out/selftest"
vm_get "$VM_REPO\\build\\selftest" "$out/selftest" engine-data || say "warning: could not copy the results back"

shots="$(find "$out/selftest" -name '*.png' 2>/dev/null | wc -l | tr -d ' ')"
results="$(find "$out/selftest" -name result.json 2>/dev/null | wc -l | tr -d ' ')"
{
  echo
  echo "== self-test summary =="
  grep -E '^(PASS|FAIL) |^ +- |scenario\(s\) in|self-test (passed|failed)' "$out/logs/selftest.log" || echo "(no summary lines in the log; see $out/logs/selftest.log)"
  echo "$results result.json file(s), $shots screenshot(s) in $out/selftest"
  if [ "$test_status" -eq 0 ]; then
    echo "RESULT: passed ($((SECONDS - started))s end to end)"
  else
    echo "RESULT: FAILED ($((SECONDS - started))s end to end)"
  fi
} >&2
exit "$test_status"
