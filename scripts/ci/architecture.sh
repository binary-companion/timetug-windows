#!/usr/bin/env bash
# Architecture rules and repository contracts. Runs on Linux (CI) or macOS; needs dotnet 10 and python3.
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet test tests/TimeTug.Architecture.Tests -p:EnableWindowsTargeting=true --nologo
python3 -m venv build/venv
build/venv/bin/python -m pip install --quiet -r scripts/ci/requirements-test.txt
build/venv/bin/python scripts/ci/check-repository-contracts.py
build/venv/bin/python -m unittest discover -s scripts/ci/tests -p 'test_*.py'

# The VM helper scripts: shellcheck-clean, their shell self-test (a stub prlctl, no VM), and the PowerShell logic of the installer.
# On CI (CI=true) shellcheck and pwsh must exist; elsewhere a missing tool is reported and skipped.
tool_or_skip() {
  command -v "$1" >/dev/null 2>&1 && return 0
  [ -z "${CI:-}" ] || { echo "$1 is required on CI" >&2; exit 1; }
  echo "skipping: $1 is not installed"
  return 1
}
if tool_or_skip shellcheck; then shellcheck -x -P SCRIPTDIR scripts/vm/*.sh scripts/vm/tests/*.sh; fi
scripts/vm/tests/test-vm-scripts.sh </dev/null
if tool_or_skip pwsh; then pwsh -NoProfile -File scripts/vm/tests/vm-install.tests.ps1; fi
