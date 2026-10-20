#!/usr/bin/env bash
# Self-test for scripts/vm/*.sh: argument parsing, VM state handling and what is sent to the VM, against a stub `prlctl`.
# No Parallels, no VM, no network. Runs on macOS (bash 3.2) and Linux.   usage: scripts/vm/tests/test-vm-scripts.sh
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
vm_dir="$(cd "$here/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
failures=0
checks=0

# ---- the stub: logs every call, keeps the VM state in a file ------------------------------------------------------------------
mkdir -p "$work/bin"
cat >"$work/bin/prlctl" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"$STUB_LOG"
verb="$1"
state="$(cat "$STUB_STATE")"
case "$verb" in
  status) if [ "$2" = "Test VM" ]; then echo "VM $2 exist $state"; else echo "Failed to get VM config: The virtual machine could not be found." >&2; exit 255; fi ;;
  resume | start) echo running >"$STUB_STATE" ;;
  suspend) echo suspended >"$STUB_STATE" ;;
  capture)
    [ -z "${STUB_CAPTURE_FAIL:-}" ] || exit 1
    file="${4:-}"
    if [ -n "${STUB_CAPTURE_EMPTY:-}" ]; then : >"$file"; elif [ -n "${STUB_CAPTURE_TEXT:-}" ]; then echo "not an image" >"$file"; else printf '\211PNG\r\n\032\nrest' >"$file"; fi
    ;;
  exec)
    # exec <vm> [--current-user] <program> ...
    if [ -n "${STUB_STDIN:-}" ] && [ "${*#*EncodedCommand}" != "$*" ]; then
      data="$(cat)"
      [ -z "$data" ] || printf '%s\n' "$data" >"$STUB_STDIN"
    fi
    if [ "${*#*echo ready}" != "$*" ]; then
      [ -z "${STUB_NEVER_READY:-}" ] && [ "$state" = running ] && printf 'ready\r\n' && exit 0
      exit 1
    fi
    [ -z "${STUB_EXEC_OUTPUT:-}" ] || printf '%s\n' "$STUB_EXEC_OUTPUT"
    exit "${STUB_EXEC_RC:-0}"
    ;;
esac
STUB
chmod +x "$work/bin/prlctl"
export PATH="$work/bin:$PATH"
export STUB_LOG="$work/calls.log" STUB_STATE="$work/state" VM_NAME="Test VM"
unset STUB_CAPTURE_FAIL STUB_CAPTURE_EMPTY STUB_CAPTURE_TEXT STUB_NEVER_READY STUB_EXEC_OUTPUT STUB_EXEC_RC STUB_STDIN

reset() {
  : >"$STUB_LOG"
  echo "${1:-running}" >"$STUB_STATE"
  unset STUB_CAPTURE_FAIL STUB_CAPTURE_EMPTY STUB_CAPTURE_TEXT STUB_NEVER_READY STUB_EXEC_OUTPUT STUB_EXEC_RC STUB_STDIN
}
calls() { cat "$STUB_LOG"; }
# The log with every -EncodedCommand argument replaced by the PowerShell it carries.
decoded_calls() {
  while IFS= read -r line; do
    case "$line" in
      *"-EncodedCommand "*)
        prefix="${line%%-EncodedCommand *}"
        encoded="${line##*-EncodedCommand }"
        printf '%s-EncodedCommand <<%s>>\n' "$prefix" "$(printf '%s' "$encoded" | { base64 -d 2>/dev/null || base64 -D; } | iconv -f UTF-16LE -t UTF-8)"
        ;;
      *) printf '%s\n' "$line" ;;
    esac
  done <"$STUB_LOG"
}

pass() { checks=$((checks + 1)); printf 'PASS %s\n' "$1"; }
fail() {
  checks=$((checks + 1))
  failures=$((failures + 1))
  printf 'FAIL %s: %s\n' "$1" "$2"
}
# expect <name> <expected exit code> <output must contain> -- command...   (the output is stdout and stderr together)
expect() {
  local name="$1" want="$2" contains="$3"
  shift 4
  local output status=0
  output="$("$@" 2>&1)" || status=$?
  if [ "$status" -ne "$want" ]; then
    fail "$name" "exit $status, wanted $want; output: $output"
  elif [ -n "$contains" ] && [ "${output#*"$contains"}" = "$output" ]; then
    fail "$name" "output lacks \"$contains\": $output"
  else
    pass "$name"
  fi
}
log_has() { if [ "$(calls | grep -cF -- "$2")" -gt 0 ]; then pass "$1"; else fail "$1" "no call containing \"$2\" in: $(calls | tr '\n' '|')"; fi; }
log_lacks() { if [ "$(calls | grep -cF -- "$2")" -eq 0 ]; then pass "$1"; else fail "$1" "unexpected call \"$2\""; fi; }
decoded_has() {
  local all
  all="$(decoded_calls)"
  if [ "${all#*"$2"}" != "$all" ]; then pass "$1"; else fail "$1" "no PowerShell containing \"$2\" in: $(printf '%s' "$all" | tr '\n' '|')"; fi
}

# ---- vm-up ---------------------------------------------------------------------------------------------------------------------
reset running
expect "vm-up: running VM is left alone" 0 "is ready" -- "$vm_dir/vm-up.sh"
log_lacks "vm-up: no resume for a running VM" "resume"
log_lacks "vm-up: no start for a running VM" "start"
expect "vm-up --quiet prints nothing when already up" 0 "" -- "$vm_dir/vm-up.sh" --quiet
if [ -z "$("$vm_dir/vm-up.sh" --quiet 2>&1)" ]; then pass "vm-up --quiet: really silent"; else fail "vm-up --quiet: really silent" "printed something"; fi

reset suspended
expect "vm-up: a suspended VM is resumed" 0 "resuming" -- "$vm_dir/vm-up.sh"
log_has "vm-up: prlctl resume called" "resume Test VM"
reset suspended
"$vm_dir/vm-up.sh" >/dev/null 2>&1
if [ "$(cat "$STUB_STATE")" = running ]; then pass "vm-up: VM is running afterwards"; else fail "vm-up: VM is running afterwards" "$(cat "$STUB_STATE")"; fi
"$vm_dir/vm-up.sh" >/dev/null 2>&1
if [ "$(calls | grep -c '^resume')" -eq 1 ]; then pass "vm-up: idempotent (second run does not resume again)"; else fail "vm-up: idempotent" "$(calls | tr '\n' '|')"; fi

reset stopped
expect "vm-up: a stopped VM is started" 0 "starting" -- "$vm_dir/vm-up.sh"
log_has "vm-up: prlctl start called" "start Test VM"
reset paused
expect "vm-up: a paused VM is resumed" 0 "resuming" -- "$vm_dir/vm-up.sh"
reset stuck
expect "vm-up: an odd state is an error" 1 'state "stuck"' -- "$vm_dir/vm-up.sh"

reset running
expect "vm-up --user waits on the user session" 0 "is ready" -- "$vm_dir/vm-up.sh" --user
log_has "vm-up --user: asked as the current user" "exec Test VM --current-user cmd /c echo ready"
expect "vm-up: unknown option" 1 "unknown argument: --bogus" -- "$vm_dir/vm-up.sh" --bogus
expect "vm-up: --timeout needs a number" 1 "whole number" -- "$vm_dir/vm-up.sh" --timeout abc
expect "vm-up: --timeout needs a value" 1 "--timeout needs" -- "$vm_dir/vm-up.sh" --timeout
reset running
export STUB_NEVER_READY=1
expect "vm-up: gives up when Parallels Tools never answers" 1 "did not answer within 2s" -- "$vm_dir/vm-up.sh" --timeout 2
unset STUB_NEVER_READY
expect "vm-up: a VM that does not exist" 1 "no Parallels VM named" -- env VM_NAME="Nope" "$vm_dir/vm-up.sh"

# ---- vm-run --------------------------------------------------------------------------------------------------------------------
reset running
expect "vm-run: no command is an error" 1 "no command given" -- "$vm_dir/vm-run.sh"
expect "vm-run: unknown option" 1 "unknown option: --bogus" -- "$vm_dir/vm-run.sh" --bogus x
reset running
"$vm_dir/vm-run.sh" cmd /c ver >/dev/null 2>&1
log_has "vm-run: runs as SYSTEM by default" "exec Test VM cmd /c ver"
reset running
"$vm_dir/vm-run.sh" --user cmd /c ver >/dev/null 2>&1
log_has "vm-run --user: runs as the current user" "exec Test VM --current-user cmd /c ver"
reset running
"$vm_dir/vm-run.sh" -- --weird-program arg >/dev/null 2>&1
log_has "vm-run --: everything after it is the command" "exec Test VM --weird-program arg"
reset suspended
"$vm_dir/vm-run.sh" cmd /c ver >/dev/null 2>&1
log_has "vm-run: resumes a suspended VM first" "resume Test VM"
log_has "vm-run: then runs the command" "exec Test VM cmd /c ver"
reset running
export STUB_EXEC_RC=7
expect "vm-run: the command's exit code is returned" 7 "" -- "$vm_dir/vm-run.sh" cmd /c "exit 7"
unset STUB_EXEC_RC
reset running
"$vm_dir/vm-run.sh" --user --ps 'Get-Process "TimeTug"' >/dev/null 2>&1
decoded_has "vm-run --ps: sends the code through -EncodedCommand" "Get-Process \"TimeTug\""
decoded_has "vm-run --ps: stops on errors" "ErrorActionPreference='Stop'"
log_has "vm-run --user --ps: as the current user" "exec Test VM --current-user powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand"

# ---- vm-suspend ----------------------------------------------------------------------------------------------------------------
reset running
expect "vm-suspend: suspends a running VM" 0 "is suspended" -- "$vm_dir/vm-suspend.sh"
log_has "vm-suspend: prlctl suspend called" "suspend Test VM"
expect "vm-suspend: an already suspended VM is fine" 0 "already suspended" -- "$vm_dir/vm-suspend.sh"
if [ "$(calls | grep -c '^suspend')" -eq 1 ]; then pass "vm-suspend: idempotent"; else fail "vm-suspend: idempotent" "suspend called twice"; fi
reset stopped
expect "vm-suspend: a stopped VM is left alone" 0 "already stopped" -- "$vm_dir/vm-suspend.sh"
reset stuck
expect "vm-suspend: an odd state is an error" 1 'state "stuck"' -- "$vm_dir/vm-suspend.sh"
expect "vm-suspend: unknown argument" 1 "unknown argument" -- "$vm_dir/vm-suspend.sh" now

# ---- vm-screenshot -------------------------------------------------------------------------------------------------------------
reset running
expect "vm-screenshot: needs a path" 1 "usage:" -- "$vm_dir/vm-screenshot.sh"
expect "vm-screenshot: path must be a png" 1 "must end in .png" -- "$vm_dir/vm-screenshot.sh" "$work/shot.jpg"
expect "vm-screenshot: writes the capture" 0 "wrote" -- "$vm_dir/vm-screenshot.sh" "$work/deep/er/shot.png"
if [ -s "$work/deep/er/shot.png" ]; then pass "vm-screenshot: file exists in a new folder"; else fail "vm-screenshot: file exists" "missing"; fi
log_has "vm-screenshot: prlctl capture used" "capture Test VM --file $work/deep/er/shot.png"
export STUB_CAPTURE_FAIL=1
expect "vm-screenshot: a failing capture is an error" 1 "prlctl capture failed" -- "$vm_dir/vm-screenshot.sh" "$work/fail.png"
unset STUB_CAPTURE_FAIL
export STUB_CAPTURE_EMPTY=1
expect "vm-screenshot: an empty capture is an error" 1 "empty" -- "$vm_dir/vm-screenshot.sh" "$work/empty.png"
unset STUB_CAPTURE_EMPTY
export STUB_CAPTURE_TEXT=1
expect "vm-screenshot: a capture that is not a PNG is an error" 1 "not a PNG" -- "$vm_dir/vm-screenshot.sh" "$work/text.png"
unset STUB_CAPTURE_TEXT

# ---- vm-selftest (argument checks only: the run itself needs a real VM) ---------------------------------------------------------
expect "vm-selftest: unknown option" 1 "unknown option: --bogus" -- "$vm_dir/vm-selftest.sh" --bogus
expect "vm-selftest: bad platform" 1 "ARM64 or x64" -- "$vm_dir/vm-selftest.sh" --platform x86
expect "vm-selftest: two scenarios" 1 "only one scenario" -- "$vm_dir/vm-selftest.sh" tug-basic tug-join
expect "vm-selftest: bad scenario name" 1 "not a scenario name" -- "$vm_dir/vm-selftest.sh" 'tug;basic'
expect "vm-selftest: --hold needs a number" 1 "whole number" -- "$vm_dir/vm-selftest.sh" tug-basic --hold ten
expect "vm-selftest: --hold needs one scenario" 1 "one named scenario" -- "$vm_dir/vm-selftest.sh" --hold 5
expect "vm-selftest: missing artifact folder" 1 "artifact folder not found" -- "$vm_dir/vm-selftest.sh" --artifact "$work/none"
mkdir -p "$work/artifact"
expect "vm-selftest: --artifact and --no-build" 1 "contradict" -- "$vm_dir/vm-selftest.sh" --artifact "$work/artifact" --no-build
expect "vm-selftest: --artifact needs a folder" 1 "--artifact needs" -- "$vm_dir/vm-selftest.sh" --artifact
expect "vm-selftest: help" 0 "usage: vm-selftest.sh" -- "$vm_dir/vm-selftest.sh" --help

# ---- vm-sync: a real repository with a bare origin, the VM scripts copied in, the stub for the VM -----------------------------
repo="$work/repo"
mkdir -p "$repo/scripts" "$work/origin.git"
git init -q --bare "$work/origin.git"
git init -q "$repo"
cp -R "$vm_dir" "$repo/scripts/vm"
rm -rf "$repo/scripts/vm/tests"
git -C "$repo" config user.email test@example.com
git -C "$repo" config user.name Test
printf 'ignored.txt\n' >"$repo/.gitignore"
printf 'one\n' >"$repo/tracked.txt"
git -C "$repo" add -A
git -C "$repo" commit -q -m first
git -C "$repo" branch -M work
git -C "$repo" remote add origin "$work/origin.git"
git -C "$repo" push -q origin work
sync="$repo/scripts/vm/vm-sync.sh"

reset running
expect "vm-sync: unknown option" 1 "unknown option: --bogus" -- "$sync" --bogus
expect "vm-sync: two refs" 1 "only one ref" -- "$sync" a b
expect "vm-sync: --patch with a ref" 1 "do not combine" -- "$sync" --patch main
"$sync" >/dev/null 2>&1
decoded_has "vm-sync: fetches in the VM" "fetch -q --prune --tags origin"
decoded_has "vm-sync: checks out the current branch from origin" "checkout -q -f -B 'work' 'origin/work'"
decoded_has "vm-sync: resets and cleans the VM checkout" "clean -fdq"
reset running
git -C "$repo" tag v1
git -C "$repo" push -q origin v1
"$sync" v1 >/dev/null 2>&1
decoded_has "vm-sync <tag>: detached checkout" "checkout -q -f --detach 'v1'"
reset running
"$sync" work >/dev/null 2>&1
decoded_has "vm-sync <branch>: branch checkout" "checkout -q -f -B 'work' 'origin/work'"
reset running
printf 'two\n' >>"$repo/tracked.txt"
git -C "$repo" commit -q -am second
expect "vm-sync: unpushed commits are reported, not sent" 0 "1 local commit(s)" -- "$sync"
expect "vm-sync --patch: HEAD must be on origin" 1 "is not on origin" -- "$sync" --patch
git -C "$repo" push -q origin work
reset running
printf 'three\n' >>"$repo/tracked.txt"
printf 'fresh\n' >"$repo/untracked-new.txt"
printf 'secret\n' >"$repo/ignored.txt"
export STUB_STDIN="$work/patch.in"
"$sync" --patch >/dev/null 2>&1 </dev/null
unset STUB_STDIN
git -C "$repo" rev-parse HEAD >"$work/head"
decoded_has "vm-sync --patch: detaches at the pushed commit" "checkout -q -f --detach '$(cat "$work/head")'"
decoded_has "vm-sync --patch: applies the patch in the VM" "git -C \$repo apply --whitespace=nowarn"
if grep -q '^+three' "$work/patch.in" 2>/dev/null; then pass "vm-sync --patch: carries the tracked change"; else fail "vm-sync --patch: tracked change" "$(cat "$work/patch.in" 2>/dev/null)"; fi
if grep -q 'untracked-new.txt' "$work/patch.in" 2>/dev/null; then pass "vm-sync --patch: carries the untracked file"; else fail "vm-sync --patch: untracked file" "missing"; fi
if grep -q 'ignored.txt' "$work/patch.in" 2>/dev/null; then fail "vm-sync --patch: leaves ignored files out" "ignored.txt is in the patch"; else pass "vm-sync --patch: leaves ignored files out"; fi
if git -C "$repo" status --short | grep -q tracked.txt; then pass "vm-sync --patch: leaves the real index and tree alone"; else fail "vm-sync --patch: real tree" "changed"; fi
if git -C "$repo" diff --cached --quiet; then pass "vm-sync --patch: nothing staged in the real index"; else fail "vm-sync --patch: real index" "staged changes appeared"; fi
printf -- '-----BEGIN %s-----\nx\n' "RSA PRIVATE KEY" >"$repo/leak.txt"
expect "vm-sync --patch: refuses a private key" 1 "private key" -- "$sync" --patch
rm "$repo/leak.txt"
git -C "$repo" checkout -q --detach
expect "vm-sync: a detached HEAD needs a ref" 1 "HEAD is detached" -- "$sync"

# ---- lib.sh helpers -------------------------------------------------------------------------------------------------------------
# shellcheck source=../lib.sh
. "$vm_dir/lib.sh"
status=0
run_with_timeout 5 true || status=$?
if [ "$status" -eq 0 ]; then pass "run_with_timeout: success passes through"; else fail "run_with_timeout: success" "$status"; fi
status=0
run_with_timeout 5 bash -c 'exit 3' || status=$?
if [ "$status" -eq 3 ]; then pass "run_with_timeout: exit code passes through"; else fail "run_with_timeout: exit code" "$status"; fi
status=0
run_with_timeout 1 sleep 20 || status=$?
if [ "$status" -eq 124 ]; then pass "run_with_timeout: a slow command is stopped (124)"; else fail "run_with_timeout: slow command" "$status"; fi
cleaned="$(printf '%s\n' '#< CLIXML' '<Objs Version="1.1.0.1" xmlns="x"><S S="Error">boom : bad_x000D__x000A_</S><S S="Error">At line:1 &lt;here&gt;_x000D__x000A_</S></Objs>' 'plain line' | vm_clean_stderr 2>&1)"
if [ "$cleaned" = "$(printf 'boom : bad\nAt line:1 <here>\nplain line')" ]; then pass "vm_clean_stderr: CLIXML errors become plain lines"; else fail "vm_clean_stderr" "$cleaned"; fi

printf '\n%s check(s), %s failed\n' "$checks" "$failures"
[ "$failures" -eq 0 ]
