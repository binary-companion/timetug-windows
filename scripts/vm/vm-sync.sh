#!/usr/bin/env bash
# Puts the repository state you are looking at on the Mac into the VM's checkout (C:\src\timetug-windows).
#
# usage: vm-sync.sh [--patch] [<ref>]
#   (no arguments)  the current local branch: the VM fetches origin and checks out origin/<branch>.
#                   The branch must be pushed; unpushed commits are reported, never sent.
#   <ref>           a branch, tag or commit instead of the current branch (a branch is checked out as that branch,
#                   anything else detached)
#   --patch         also send uncommitted and untracked (not ignored) local work as a patch. HEAD must already be on
#                   origin; the VM is put on exactly that commit, cleaned, and the patch applied on top.
#
# The VM checkout is reset to the target (git checkout -f, reset --hard, clean -fd; ignored build output is kept), so do
# not keep work in it. Secrets never travel: the patch skips ignored files and a patch containing a private key is refused.
set -euo pipefail
# shellcheck source=lib.sh
. "$(dirname "$0")/lib.sh"

patch=0
ref=""
while [ $# -gt 0 ]; do
  case "$1" in
    --patch) patch=1 ;;
    -h | --help)
      sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    -*) die "unknown option: $1 (see --help)" ;;
    *)
      [ -z "$ref" ] || die "only one ref can be given (got \"$ref\" and \"$1\")"
      ref="$1"
      ;;
  esac
  shift
done

cd "$VM_REPO_ROOT"
git rev-parse --git-dir >/dev/null 2>&1 || die "$VM_REPO_ROOT is not a git checkout"
git fetch -q origin || die "git fetch origin failed on the Mac"

head_sha="$(git rev-parse HEAD)"
branch="$(git symbolic-ref -q --short HEAD || true)"

# What the VM should check out: "branch:<name>" or "detach:<ref>".
if [ "$patch" -eq 1 ]; then
  [ -z "$ref" ] || die "--patch sends your working tree on top of HEAD; do not combine it with a ref"
  if [ -z "$(git branch -r --contains "$head_sha" 2>/dev/null)" ]; then
    die "HEAD ($(git rev-parse --short HEAD)) is not on origin; push it first (the patch applies on top of a commit the VM can fetch)"
  fi
  target="detach:$head_sha"
elif [ -n "$ref" ]; then
  if git ls-remote --exit-code --heads origin "$ref" >/dev/null 2>&1; then
    target="branch:$ref"
  else
    target="detach:$ref"
  fi
else
  [ -n "$branch" ] || die "HEAD is detached; name a branch, tag or commit: vm-sync.sh <ref>"
  git rev-parse -q --verify "refs/remotes/origin/$branch" >/dev/null || die "branch \"$branch\" is not on origin; push it first"
  ahead="$(git rev-list --count "origin/$branch..HEAD")"
  if [ "$ahead" -gt 0 ]; then
    say "warning: $ahead local commit(s) on \"$branch\" are not pushed; the VM gets origin/$branch, not your HEAD"
  fi
  target="branch:$branch"
fi

patch_file=""
if [ "$patch" -eq 1 ]; then
  scratch="$(mktemp -d)"
  trap 'rm -rf "$scratch"' EXIT
  patch_file="$scratch/work.patch"
  # A scratch index leaves your real index alone; "add -A" honours .gitignore, so ignored files (OAuth ids) are not included.
  GIT_INDEX_FILE="$scratch/index" git read-tree HEAD
  GIT_INDEX_FILE="$scratch/index" git add -A
  GIT_INDEX_FILE="$scratch/index" git diff --cached --binary HEAD >"$patch_file"
  if [ ! -s "$patch_file" ]; then
    say "no uncommitted work: syncing the commit only"
    patch_file=""
  elif grep -qE -- '^\+.*-----BEGIN [A-Z ]*PRIVATE KEY-----' "$patch_file"; then
    die "the patch contains a private key; refusing to send it"
  fi
fi

vm_require_prlctl
"$VM_SCRIPTS_DIR/vm-up.sh" --quiet

case "$target" in
  branch:*) say "syncing the VM to branch ${target#branch:}" ;;
  detach:*) say "syncing the VM to ${target#detach:}" ;;
esac
checkout="git -C \$repo fetch -q --prune --tags origin
if (\$LASTEXITCODE) { throw 'git fetch failed in the VM' }"
case "$target" in
  branch:*) checkout="$checkout
git -C \$repo checkout -q -f -B '${target#branch:}' 'origin/${target#branch:}'" ;;
  detach:*) checkout="$checkout
git -C \$repo checkout -q -f --detach '${target#detach:}'" ;;
esac
vm_ps "\$repo = '$VM_REPO'
$checkout
if (\$LASTEXITCODE) { throw 'git checkout failed in the VM' }
git -C \$repo reset -q --hard
git -C \$repo clean -fdq
if (\$LASTEXITCODE) { throw 'git clean failed in the VM' }" || die "could not check out the target in the VM"

if [ -n "$patch_file" ]; then
  say "sending the uncommitted work as a patch ($(wc -c <"$patch_file" | tr -d ' ') bytes)"
  vm_ps "\$repo = '$VM_REPO'
\$in = [Console]::OpenStandardInput()
\$file = '$VM_WORK\\work.patch'
New-Item -ItemType Directory -Force '$VM_WORK' | Out-Null
\$out = [IO.File]::Create(\$file); \$in.CopyTo(\$out); \$out.Dispose()
git -C \$repo apply --whitespace=nowarn \$file
if (\$LASTEXITCODE) { throw 'git apply failed in the VM (is the VM checkout on the same commit?)' }
Remove-Item \$file" <"$patch_file" || die "the patch did not apply in the VM"
fi

vm_ps "git -C '$VM_REPO' log --oneline -1
\$changed = @(git -C '$VM_REPO' status --short).Count
\"working tree: \$changed changed path(s) on top of that commit\"" | sed 's/^/[vm] VM is at: /' >&2
