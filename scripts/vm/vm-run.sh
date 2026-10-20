#!/usr/bin/env bash
# Runs a command in the Windows VM and returns its exit code. Starts the VM first when needed.
#
# usage: vm-run.sh [--user] [--ps] [--] <command> [arguments...]
#   (no flag)   runs as NT AUTHORITY\SYSTEM in session 0: builds, git, signing, installing certificates
#   --user      runs as the logged-in user on the real desktop (prlctl exec --current-user): installing the package,
#               launching the app, screenshots
#   --ps        the rest of the line is PowerShell code, passed with -EncodedCommand so quoting is not a problem
#
# Without --ps the arguments go to prlctl exec as they are: quote Windows paths with single quotes on the Mac side
# ('C:\src\x'), because the Mac shell eats backslashes. Examples:
#   vm-run.sh cmd /c "cd /d C:\src\timetug-windows && git log --oneline -3"
#   vm-run.sh --user --ps 'Get-Process TimeTug -ErrorAction SilentlyContinue'
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

user=()
powershell=0
while [ $# -gt 0 ]; do
  case "$1" in
    --user) user=(--user) ;;
    --ps) powershell=1 ;;
    -h | --help)
      sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    --)
      shift
      break
      ;;
    -*) die "unknown option: $1 (see --help)" ;;
    *) break ;;
  esac
  shift
done
[ $# -gt 0 ] || die "no command given (see --help)"

vm_require_prlctl
"$VM_SCRIPTS_DIR/vm-up.sh" --quiet ${user[@]+"${user[@]}"}
if [ "$powershell" -eq 1 ]; then
  vm_ps ${user[@]+"${user[@]}"} "$*"
else
  vm_exec ${user[@]+"${user[@]}"} "$@"
fi
