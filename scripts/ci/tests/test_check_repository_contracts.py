import subprocess, sys, tempfile, unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / 'check-repository-contracts.py'
SHA = 'a' * 40
CI = f'''name: CI
on: [pull_request]
jobs:
  windows-x64:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@{SHA} # v7
      - run: echo "${{{{ secrets.GITHUB_TOKEN }}}}"
      - run: ./scripts/ci/run-selftest.ps1
      - run: ./scripts/ci/run-a11y.ps1
  windows-arm64:
    runs-on: windows-11-arm
    steps:
      - uses: ./local-action
      - run: ./scripts/ci/run-selftest.ps1
      - run: ./scripts/ci/run-a11y.ps1
  architecture:
    runs-on: ubuntu-latest
    steps:
      - run: echo ok
'''

SCENARIOS = '''# comment
hello  1  90  selftest
a11y-hold  0  300  a11y
'''
SCENARIO_CLASSES = {
    'src/TimeTug.App/SelfTest/Scenarios/HelloScenario.cs': 'internal static class HelloScenario { public const string Name = "hello"; }\n',
    'src/TimeTug.App/SelfTest/Scenarios/A11yHoldScenario.cs': 'internal static class A11yHoldScenario { public const string Name = "a11y-hold"; }\n',
}

def make(root: Path, **overrides):
    files = {
        'LICENSE': 'MIT License ... Commons Clause License Condition v1.0\n',
        'AGENTS.md': '# Agents\n',
        '.gitattributes': '* text=auto eol=lf\n',
        '.github/dependabot.yml': 'version: 2\n',
        '.github/workflows/ci.yml': CI,
        'scripts/ci/selftest-scenarios.txt': SCENARIOS,
        **SCENARIO_CLASSES,
    }
    files.update(overrides)
    for name, text in files.items():
        if text is None:
            continue
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

def run(**overrides):
    with tempfile.TemporaryDirectory() as directory:
        make(Path(directory), **overrides)
        return subprocess.run([sys.executable, str(SCRIPT), directory], capture_output=True, text=True)

class RepositoryContracts(unittest.TestCase):
    def fails(self, message, **overrides):
        result = run(**overrides)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn(message, result.stderr)

    def test_a_conforming_repository_passes(self):
        result = run()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('PASS', result.stdout)

    def test_missing_files_fail(self):
        self.fails('missing documented path: AGENTS.md', **{'AGENTS.md': None})
        self.fails('missing documented path: LICENSE', LICENSE=None)
        self.fails('missing documented path: .github/dependabot.yml', **{'.github/dependabot.yml': None})

    def test_license_needs_the_commons_clause(self):
        self.fails('Commons Clause', LICENSE='MIT License\n')

    def test_gitattributes_needs_the_eol_rule(self):
        self.fails('text=auto eol=lf', **{'.gitattributes': '* text=auto\n'})

    def test_unpinned_actions_fail(self):
        self.fails('not pinned', **{'.github/workflows/ci.yml': CI.replace(SHA, 'v7')})
        self.fails('not pinned', **{'.github/workflows/ci.yml': CI.replace(SHA, 'b' * 39)})

    def test_unpinned_actions_in_other_workflows_fail(self):
        other = 'name: X\non: [push]\njobs:\n  x:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@main\n'
        self.fails('other.yml', **{'.github/workflows/other.yml': other})

    def test_required_jobs_must_exist(self):
        for job in ('windows-x64', 'windows-arm64', 'architecture'):
            self.fails(f'missing documented job {job}', **{'.github/workflows/ci.yml': CI.replace(job, job + '-x')})

    def test_both_windows_jobs_must_run_the_selftest_and_the_accessibility_scan(self):
        for script in ('run-selftest.ps1', 'run-a11y.ps1'):
            self.fails(f'job windows-x64 does not run {script}', **{'.github/workflows/ci.yml': CI.replace(script, 'other.ps1', 1)})

    def test_scenario_classes_and_the_list_must_agree(self):
        extra = 'internal static class NewScenario { public const string Name = "tug-new"; }\n'
        self.fails("only in the classes ['tug-new']", **{'src/TimeTug.App/SelfTest/Scenarios/NewScenario.cs': extra})
        self.fails("only in selftest-scenarios.txt ['tug-gone']", **{'scripts/ci/selftest-scenarios.txt': SCENARIOS + 'tug-gone  1  60  selftest\n'})

    def test_scenario_list_lines_are_checked(self):
        self.fails('expected "name min-screenshots', **{'scripts/ci/selftest-scenarios.txt': SCENARIOS + 'broken line\n'})
        self.fails('hello is listed twice', **{'scripts/ci/selftest-scenarios.txt': SCENARIOS + 'hello  1  90  selftest\n'})
        self.fails('missing documented path: scripts/ci/selftest-scenarios.txt', **{'scripts/ci/selftest-scenarios.txt': None})

    def test_pull_request_target_is_banned(self):
        self.fails('pull_request_target', **{'.github/workflows/ci.yml': CI.replace('[pull_request]', '[pull_request_target]')})

    def test_other_secrets_are_banned(self):
        self.fails('SIGNING_KEY', **{'.github/workflows/ci.yml': CI.replace('GITHUB_TOKEN', 'SIGNING_KEY')})
        self.fails('whole secrets context', **{'.github/workflows/ci.yml': CI.replace('secrets.GITHUB_TOKEN', 'toJSON(secrets)')})

if __name__ == '__main__':
    unittest.main()
