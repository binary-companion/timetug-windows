#!/usr/bin/env python3
"""Repository invariants that the compiler does not enforce. Usage: check-repository-contracts.py [root]"""
import re
import subprocess
import sys
from pathlib import Path

import yaml

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
errors = []

def require(condition, message):
    if not condition:
        errors.append(message)

# Documented top-level files.
for documented in ('LICENSE', 'AGENTS.md', '.gitattributes', '.github/dependabot.yml'):
    require((root / documented).is_file(), f'missing documented path: {documented}')

license_file = root / 'LICENSE'
if license_file.is_file():
    require('Commons Clause' in license_file.read_text(), 'LICENSE: the Commons Clause condition is missing')

attributes = root / '.gitattributes'
if attributes.is_file():
    lines = [line.split() for line in attributes.read_text().splitlines()]
    require(any(line[:1] == ['*'] and 'text=auto' in line and 'eol=lf' in line for line in lines),
            '.gitattributes: the rule "* text=auto eol=lf" is missing')

def load(path):
    try:
        data = yaml.safe_load(path.read_text())
    except yaml.YAMLError as problem:
        require(False, f'{path.relative_to(root)}: not valid YAML: {problem}')
        return {}
    return data if isinstance(data, dict) else {}

def uses_values(node):
    """Every value of a `uses:` key anywhere in a parsed YAML document."""
    if isinstance(node, dict):
        for key, value in node.items():
            if key == 'uses' and isinstance(value, str):
                yield value
            else:
                yield from uses_values(value)
    elif isinstance(node, list):
        for item in node:
            yield from uses_values(item)

def strings(node, key=None):
    if isinstance(node, str):
        yield key, node
    elif isinstance(node, dict):
        for k, v in node.items():
            yield from strings(k)
            yield from strings(v, k)
    elif isinstance(node, list):
        for item in node:
            yield from strings(item, key)

SECRET_NAME = re.compile(r"(?<![\w.])secrets\s*(?:\.\s*([A-Za-z0-9_]+)|\[\s*['\"]([^'\"]+)['\"]\s*\])", re.I)
WHOLE_CONTEXT = re.compile(r"(?<![\w.'\"])secrets\b(?!\s*\.\s*\w|\s*\[\s*['\"][^'\"]+['\"]\s*\])", re.I)

def secret_use(node):
    """(names of secrets referenced, whether the whole secrets context is used) anywhere in node."""
    names, whole = set(), False
    for key, text in strings(node):
        expressions = re.findall(r'\$\{\{(.*?)\}\}', text, re.S) + ([text] if key == 'if' else [])
        for expression in expressions:
            names |= {(m.group(1) or m.group(2)).upper() for m in SECRET_NAME.finditer(expression)}
            whole = whole or bool(WHOLE_CONTEXT.search(expression))
    return names, whole

workflows = sorted((root / '.github' / 'workflows').glob('*.y*ml'))
require(bool(workflows), 'missing documented path: .github/workflows')

# Third-party actions are pinned by a full commit SHA, never a tag or branch. Local actions (./) are fine.
PINNED = re.compile(r'^[\w.-]+/[\w./-]+@[0-9a-f]{40}$')
files = workflows + sorted((root / '.github' / 'actions').glob('**/action.y*ml'))
for file in files:
    for value in uses_values(load(file)):
        require(value.startswith('./') or PINNED.match(value),
                f'{file.relative_to(root)}: action is not pinned to a 40-hex commit SHA: {value}')

# The CI jobs become required checks; a renamed or deleted one would silently stop enforcing the rules.
ci = root / '.github' / 'workflows' / 'ci.yml'
require(ci.is_file(), 'missing documented path: .github/workflows/ci.yml')
if ci.is_file():
    jobs = load(ci).get('jobs')
    for documented in ('windows-x64', 'windows-arm64', 'architecture'):
        require(isinstance(jobs, dict) and documented in jobs, f'.github/workflows/ci.yml: missing documented job {documented}')

# Both Windows jobs run every self-test scenario and the accessibility scan, so neither can be dropped by editing the workflow.
if ci.is_file():
    jobs = load(ci).get('jobs')
    for job in ('windows-x64', 'windows-arm64'):
        steps = jobs.get(job, {}).get('steps', []) if isinstance(jobs, dict) and isinstance(jobs.get(job), dict) else []
        commands = ' '.join(str(step.get('run', '')) for step in steps if isinstance(step, dict))
        for script in ('run-selftest.ps1', 'run-a11y.ps1'):
            require(script in commands, f'.github/workflows/ci.yml: job {job} does not run {script}')

# The self-test scenarios: the classes (their Name constants), the list the run scripts read and the minimum screenshots per scenario
# agree, so a scenario cannot go missing from the run, and a run script cannot pass over one.
scenario_list = root / 'scripts' / 'ci' / 'selftest-scenarios.txt'
scenario_folder = root / 'src' / 'TimeTug.App' / 'SelfTest' / 'Scenarios'
require(scenario_list.is_file(), 'missing documented path: scripts/ci/selftest-scenarios.txt')
require(scenario_folder.is_dir(), 'missing documented path: src/TimeTug.App/SelfTest/Scenarios')
if scenario_list.is_file() and scenario_folder.is_dir():
    listed = {}
    for number, line in enumerate(scenario_list.read_text().splitlines(), 1):
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        fields = line.split()
        if len(fields) != 4 or not fields[1].isdigit() or not fields[2].isdigit() or fields[3] not in ('selftest', 'a11y'):
            require(False, f'scripts/ci/selftest-scenarios.txt:{number}: expected "name min-screenshots timeout-seconds selftest|a11y": {line.strip()}')
            continue
        require(fields[0] not in listed, f'scripts/ci/selftest-scenarios.txt: {fields[0]} is listed twice')
        listed[fields[0]] = fields[3]
    declared = set()
    for source in scenario_folder.glob('*.cs'):
        declared |= set(re.findall(r'const string Name = "([a-z0-9-]+)"', source.read_text()))
    require(declared == set(listed),
            f'self-test scenarios differ: only in the classes {sorted(declared - set(listed))}, only in selftest-scenarios.txt {sorted(set(listed) - declared)}')

# The development MSIX is built for both architectures on every run and uploaded with its PUBLIC certificate and the install
# script only. The signing key is throwaway and never leaves the job: no key file is committed or uploaded, and no script
# exports one.
KEY_EXTENSIONS = ('.pfx', '.p12', '.pvk', '.key', '.pem', '.snk', '.jks', '.keystore')
if ci.is_file():
    jobs = load(ci).get('jobs')
    for job, artifact in (('windows-x64', 'msix-x64'), ('windows-arm64', 'msix-arm64')):
        steps = jobs.get(job, {}).get('steps', []) if isinstance(jobs, dict) and isinstance(jobs.get(job), dict) else []
        commands = ' '.join(str(step.get('run', '')) for step in steps if isinstance(step, dict))
        for script in ('build-msix.ps1', 'stage-msix-artifact.ps1'):
            require(script in commands, f'.github/workflows/ci.yml: job {job} does not run {script}')
        uploads = [step.get('with', {}) for step in steps if isinstance(step, dict)
                   and str(step.get('uses', '')).startswith('actions/upload-artifact@') and isinstance(step.get('with'), dict)]
        msix_upload = [upload for upload in uploads if upload.get('name') == artifact]
        require(len(msix_upload) == 1, f'.github/workflows/ci.yml: job {job} must upload exactly one artifact named {artifact}')
        for upload in msix_upload:
            require(str(upload.get('path', '')).strip() == 'build/msix-artifact/',
                    f'.github/workflows/ci.yml: {artifact} must upload only build/msix-artifact/ (the staged public files)')
            require(upload.get('retention-days') == 14, f'.github/workflows/ci.yml: {artifact} must have retention-days: 14')

for workflow in workflows:
    for job in (load(workflow).get('jobs') or {}).values():
        for step in (job.get('steps') or []) if isinstance(job, dict) else []:
            if not (isinstance(step, dict) and str(step.get('uses', '')).startswith('actions/upload-artifact@')):
                continue
            paths = str((step.get('with') or {}).get('path', '')).splitlines()
            for path in (line.strip().lstrip('!').strip() for line in paths):
                if not path:
                    continue
                require(path not in ('.', './', '*', '**', '**/*', '/'), f'{workflow.name}: an artifact uploads the whole workspace ({path})')
                require(not any(extension in path.lower() for extension in KEY_EXTENSIONS),
                        f'{workflow.name}: an artifact path names key material ({path})')

gitignore = root / '.gitignore'
if gitignore.is_file():
    ignored = {line.strip() for line in gitignore.read_text().splitlines()}
    for extension in ('.pfx', '.p12', '.pvk', '.key', '.snk'):
        require('*' + extension in ignored, f'.gitignore: *{extension} is not ignored')
else:
    require(False, 'missing documented path: .gitignore')

def tracked_files():
    """Paths that are committed (git ls-files) or, outside a git checkout, every file under root."""
    try:
        listing = subprocess.run(['git', '-C', str(root), 'ls-files', '-z'], capture_output=True, check=True).stdout
        names = [name for name in listing.decode().split('\0') if name]
        if names:
            return [root / name for name in names]
    except (OSError, subprocess.CalledProcessError):
        pass
    return [path for path in root.rglob('*') if path.is_file() and '.git' not in path.relative_to(root).parts]

PRIVATE_KEY_BLOCK = re.compile('-----BEGIN ' + r'(?:[A-Z0-9]+ )*' + 'PRIVATE KEY' + '-----')
KEY_EXPORT = re.compile(r'Export-PfxCertificate|ExportPkcs12|X509ContentType\]?::(?:Pfx|Pkcs12)|openssl\s+pkcs12|\.pfx\b', re.I)
for path in tracked_files():
    relative = path.relative_to(root)
    require(path.suffix.lower() not in KEY_EXTENSIONS, f'{relative}: key material is committed')
    if not path.is_file() or path.stat().st_size > 2_000_000:
        continue
    text = path.read_text(errors='ignore')
    require(not PRIVATE_KEY_BLOCK.search(text), f'{relative}: contains a private key block')
    if path.suffix in ('.ps1', '.sh', '.yml', '.yaml'):
        require(not KEY_EXPORT.search(text), f'{relative}: exports or names a signing key file (the throwaway key must never leave the job)')

# Pull-request code never meets secrets: no pull_request_target, and only the automatic token anywhere.
for workflow in workflows:
    data = load(workflow)
    triggers = data.get(True, data.get('on')) or {}
    names = triggers if isinstance(triggers, (list, dict)) else [triggers]
    require('pull_request_target' not in names, f'{workflow.name}: pull_request_target is banned')
    used, whole = secret_use(data)
    for secret in sorted(used - {'GITHUB_TOKEN'}):
        require(False, f'{workflow.name}: uses secret {secret} (only GITHUB_TOKEN is allowed in this repository)')
    require(not whole, f'{workflow.name}: uses the whole secrets context')

if errors:
    for error in errors:
        print(f'FAIL: {error}', file=sys.stderr)
    sys.exit(1)
print('PASS: repository contracts')
