#!/usr/bin/env bash
# Runs PowerShell code on a Windows PC over SSH (the remote x64 verification; see docs/manual-tests/x64-remote-verification.md). The code is
# sent with -EncodedCommand, so no quoting is needed on either side, and runs in the SSH logon session: NO desktop. Anything that needs the
# desktop (windows, tray, UI Automation, the self-test) goes through run-in-user-session.ps1, e.g.
#   ssh-ps.sh --host ttagent@192.168.1.20 "& \$HOME\timetug\scripts\vm\run-in-user-session.ps1 -ScriptPath \$HOME\timetug\scripts\vm\start-cycles.ps1 -Name c1"
#
# usage: ssh-ps.sh --host USER@HOST [--key FILE] [--port N] [--put LOCAL REMOTE] <powershell code>
#   --host   the account and address of the PC (required; never guessed)
#   --key    the private key to use (default: ~/.ssh/timetug_x64_test)
#   --port   the SSH port (default 22)
#   --put    first copy a local file or folder to REMOTE (a path under the remote home, e.g. timetug/trial) with scp; may be given more than once
# Nothing is downloaded or installed by this script; it only runs what it is given. Exit code: PowerShell's, 255 when the connection fails.
set -euo pipefail

die() {
  printf '[ssh-ps] error: %s\n' "$*" >&2
  exit 1
}

host=""
key="$HOME/.ssh/timetug_x64_test"
port=22
puts=()
code=""
while [ $# -gt 0 ]; do
  case "$1" in
    --host)
      [ $# -ge 2 ] || die "--host needs USER@HOST"
      host="$2"
      shift
      ;;
    --key)
      [ $# -ge 2 ] || die "--key needs a file"
      key="$2"
      shift
      ;;
    --port)
      [ $# -ge 2 ] || die "--port needs a number"
      port="$2"
      shift
      ;;
    --put)
      [ $# -ge 3 ] || die "--put needs LOCAL and REMOTE"
      puts+=("$2" "$3")
      shift 2
      ;;
    -h | --help)
      sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    -*) die "unknown option: $1 (see --help)" ;;
    *)
      [ -z "$code" ] || die "only one block of PowerShell code can be given"
      code="$1"
      ;;
  esac
  shift
done
[ -n "$host" ] || die "--host USER@HOST is required"
case "$host" in *@*) ;; *) die "--host must look like user@address, got \"$host\"" ;; esac
case "$port" in '' | *[!0-9]*) die "--port needs a whole number, got \"$port\"" ;; esac
[ -n "$code" ] || [ ${#puts[@]} -gt 0 ] || die "no PowerShell code and nothing to put"
[ -f "$key" ] || die "no private key at $key"

ssh_options=(-i "$key" -p "$port" -o IdentitiesOnly=yes -o PasswordAuthentication=no -o BatchMode=yes -o StrictHostKeyChecking=yes)

i=0
while [ $i -lt ${#puts[@]} ]; do
  local_path="${puts[$i]}"
  remote_path="${puts[$((i + 1))]}"
  [ -e "$local_path" ] || die "not found: $local_path"
  scp -r -i "$key" -P "$port" -o IdentitiesOnly=yes -o PasswordAuthentication=no -o BatchMode=yes -o StrictHostKeyChecking=yes "$local_path" "$host:$remote_path"
  i=$((i + 2))
done

[ -n "$code" ] || exit 0
wrapped="\$ProgressPreference='SilentlyContinue'; \$ErrorActionPreference='Stop'; $code"
encoded="$(printf '%s' "$wrapped" | iconv -t UTF-16LE | base64 | tr -d '\n')"
exec ssh "${ssh_options[@]}" "$host" powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand "$encoded"
