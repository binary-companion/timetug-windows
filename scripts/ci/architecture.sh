#!/usr/bin/env bash
# Architecture rules and repository contracts. Runs on Linux (CI) or macOS; needs dotnet 10 and python3.
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet test tests/TimeTug.Architecture.Tests -p:EnableWindowsTargeting=true --nologo
python3 -m venv build/venv
build/venv/bin/python -m pip install --quiet -r scripts/ci/requirements-test.txt
build/venv/bin/python scripts/ci/check-repository-contracts.py
build/venv/bin/python -m unittest discover -s scripts/ci/tests -p 'test_*.py'
