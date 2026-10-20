#!/usr/bin/env bash
# Captures the VM's screen to a PNG on the Mac (prlctl capture: the whole display, taskbar included).
#
# usage: vm-screenshot.sh <path.png>
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

case "${1:-}" in
  -h | --help)
    sed -n '2,5p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
    ;;
  '') die "usage: vm-screenshot.sh <path.png>" ;;
esac
[ $# -eq 1 ] || die "usage: vm-screenshot.sh <path.png>"
target="$1"
case "$target" in *.png) ;; *) die "the path must end in .png: $target" ;; esac
mkdir -p "$(dirname "$target")"

vm_require_prlctl
"$VM_SCRIPTS_DIR/vm-up.sh" --quiet --user
say "capturing the screen of \"$VM_NAME\" to $target"
prlctl capture "$VM_NAME" --file "$target" >/dev/null || die "prlctl capture failed"
[ -s "$target" ] || die "the capture is empty: $target"
if [ "$(head -c 4 "$target" | tail -c 3)" != "PNG" ]; then
  die "the capture is not a PNG: $target"
fi
say "wrote $(wc -c <"$target" | tr -d ' ') bytes"
