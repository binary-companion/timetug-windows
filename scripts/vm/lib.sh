#!/usr/bin/env bash
# Shared helpers for the scripts in scripts/vm. Source this file; do not run it.
#
# Environment:
#   VM_NAME   name of the Parallels VM (default "Windows 11")
#   VM_REPO   the repository inside the VM (default C:\src\timetug-windows)
# Progress messages go to stderr, so stdout carries only a command's own output.

VM_NAME="${VM_NAME:-Windows 11}"
VM_REPO="${VM_REPO:-C:\\src\\timetug-windows}"
# A folder both SYSTEM and the logged-in user can write: job logs and transferred packages.
VM_WORK='C:\Users\Public\timetug-vm'
VM_SCRIPTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034  # used by the scripts that source this file
VM_REPO_ROOT="$(cd "$VM_SCRIPTS_DIR/../.." && pwd)"

say() { printf '[vm] %s\n' "$*" >&2; }
die() {
  printf '[vm] error: %s\n' "$*" >&2
  exit 1
}

vm_require_prlctl() {
  command -v prlctl >/dev/null 2>&1 || die "prlctl not found; this needs Parallels Desktop on the Mac"
}

# Prints running, suspended, paused, stopped, ... as Parallels reports it.
vm_state() {
  local line
  line="$(prlctl status "$VM_NAME" 2>&1)" || die "no Parallels VM named \"$VM_NAME\" ($line)"
  printf '%s\n' "${line##* }"
}

# Runs a command with a time limit (macOS has no timeout(1)). Returns 124 when the limit was hit.
run_with_timeout() {
  local seconds="$1"
  shift
  "$@" &
  local pid=$!
  (
    sleep "$seconds"
    kill "$pid" 2>/dev/null
  ) >/dev/null 2>&1 &
  local watchdog=$!
  local status=0
  wait "$pid" 2>/dev/null || status=$?
  if kill -0 "$watchdog" 2>/dev/null; then
    pkill -P "$watchdog" 2>/dev/null || true
    kill "$watchdog" 2>/dev/null || true
    wait "$watchdog" 2>/dev/null || true
  else
    # The watchdog fired (or finished) first.
    [ "$status" -ge 128 ] && status=124
  fi
  return "$status"
}

# vm_exec [--user] <program> [arguments...>: prlctl exec as SYSTEM, or as the logged-in user with --user.
vm_exec() {
  if [ "${1:-}" = "--user" ]; then
    shift
    prlctl exec "$VM_NAME" --current-user "$@"
  else
    prlctl exec "$VM_NAME" "$@"
  fi
}

# Windows PowerShell reports errors to prlctl as CLIXML on stderr. Turn that back into plain lines.
vm_clean_stderr() {
  awk '
    /^#< CLIXML/ { next }
    /^<Objs/ {
      line = $0
      while (match(line, /<S S="Error">[^<]*<\/S>/)) {
        text = substr(line, RSTART + 13, RLENGTH - 13 - 4)
        gsub(/_x000D__x000A_/, "", text)
        gsub(/&lt;/, "<", text); gsub(/&gt;/, ">", text); gsub(/&amp;/, "\\&", text); gsub(/&quot;/, "\"", text)
        if (text !~ /^[[:space:]]*$/) print text
        line = substr(line, RSTART + RLENGTH)
      }
      next
    }
    /^<\/Objs>/ { next }
    { print }
  ' >&2
}

# vm_ps [--user] <powershell code>: runs the code through -EncodedCommand (no quoting problems). Exit code is PowerShell's.
vm_ps() {
  local user=()
  if [ "${1:-}" = "--user" ]; then
    user=(--user)
    shift
  fi
  local code="\$ProgressPreference='SilentlyContinue'; \$ErrorActionPreference='Stop'; $1"
  local encoded
  encoded="$(printf '%s' "$code" | iconv -t UTF-16LE | base64 | tr -d '\n')"
  local errors status=0
  errors="$(mktemp)"
  vm_exec ${user[@]+"${user[@]}"} powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand "$encoded" 2>"$errors" || status=$?
  vm_clean_stderr <"$errors"
  rm -f "$errors"
  return "$status"
}

# vm_put <local dir> <vm dir>: replaces the VM folder with a copy of the local one (a tar stream through prlctl exec).
vm_put() {
  local source="$1" destination="$2"
  [ -d "$source" ] || die "not a folder: $source"
  vm_ps "if (Test-Path '$destination') { Remove-Item -Recurse -Force '$destination' }; New-Item -ItemType Directory -Force '$destination' | Out-Null" >/dev/null
  COPYFILE_DISABLE=1 tar -cf - -C "$source" . | prlctl exec "$VM_NAME" tar -xf - -C "$destination"
}

# vm_get <vm dir> <local dir> [tar exclude pattern]: copies a VM folder into a local folder.
vm_get() {
  local source="$1" destination="$2" exclude="${3:-}"
  mkdir -p "$destination"
  if [ -n "$exclude" ]; then
    prlctl exec "$VM_NAME" tar -cf - --exclude "$exclude" -C "$source" . | tar -xf - -C "$destination"
  else
    prlctl exec "$VM_NAME" tar -cf - -C "$source" . | tar -xf - -C "$destination"
  fi
}

# vm_job <system|user> <name> <timeout seconds> <powershell code>
# Runs code in the VM detached, logging to a file, and follows the log from the Mac. prlctl exec drops its connection
# on long commands, so nothing here depends on one staying open. Returns the job's exit code (1 when it threw).
vm_job() {
  local who="$1" name="$2" limit="$3" code="$4"
  local flag=()
  [ "$who" = "user" ] && flag=(--user)
  local inner
  inner="\$d = '$VM_WORK'; \$log = \"\$d\\$name.log\"
\$exitCode = 0
try { & { $code } *>&1 | ForEach-Object { Add-Content -Path \$log -Value \"\$_\" } } catch { Add-Content -Path \$log -Value \"error: \$(\$_.Exception.Message)\"; \$exitCode = 1 }
Set-Content -Path \"\$log.exit\" -Value \$exitCode"
  local encoded
  encoded="$(printf '%s' "$inner" | base64 | tr -d '\n')"
  vm_ps ${flag[@]+"${flag[@]}"} "\$d = '$VM_WORK'; New-Item -ItemType Directory -Force \$d | Out-Null
Remove-Item \"\$d\\$name.*\" -Force -ErrorAction SilentlyContinue
Set-Content -Path \"\$d\\$name.ps1\" -Value ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$encoded'))) -Encoding UTF8
Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', \"\$d\\$name.ps1\"" >/dev/null || die "could not start the VM job $name"

  local seen=0 waited=0 reply status
  while [ "$waited" -le "$limit" ]; do
    sleep "${VM_JOB_POLL_SECONDS:-4}"
    waited=$((waited + ${VM_JOB_POLL_SECONDS:-4}))
    reply="$(vm_ps ${flag[@]+"${flag[@]}"} "\$log = '$VM_WORK\\$name.log'
if (Test-Path \$log) { Get-Content \$log | Select-Object -Skip $seen }
if (Test-Path \"\$log.exit\") { '@@EXIT:' + ((Get-Content \"\$log.exit\") -join '') } else { '@@RUN' }" 2>/dev/null)" || continue
    # The reply is the new log lines followed by one line saying whether the job is over.
    local count
    count=$(($(printf '%s\n' "$reply" | wc -l) - 1))
    if [ "$count" -gt 0 ]; then
      printf '%s\n' "$reply" | sed '$d'
      seen=$((seen + count))
    fi
    status="$(printf '%s\n' "$reply" | tail -n 1)"
    case "$status" in
      '@@EXIT:'*)
        status="${status#@@EXIT:}"
        status="${status//[^0-9]/}"
        return "${status:-1}"
        ;;
    esac
  done
  say "job $name did not finish within ${limit}s; its log stays at $VM_WORK\\$name.log in the VM"
  return 124
}
