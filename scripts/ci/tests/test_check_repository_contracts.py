import subprocess, sys, tempfile, unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / 'check-repository-contracts.py'
SHA = 'a' * 40
def windows_job(name, runner, artifact):
    return f'''  {name}:
    runs-on: {runner}
    steps:
      - uses: actions/checkout@{SHA} # v7
      - run: echo "${{{{ secrets.GITHUB_TOKEN }}}}"
      - run: ./scripts/ci/build-msix.ps1
      - run: ./scripts/ci/stage-msix-artifact.ps1
      - run: ./scripts/ci/run-selftest.ps1
      - run: ./scripts/ci/run-a11y.ps1
      - uses: actions/upload-artifact@{SHA} # v7
        with:
          name: {artifact}
          path: build/msix-artifact/
          retention-days: 14
      - uses: actions/upload-artifact@{SHA} # v7
        with:
          name: selftest
          path: |
            build/selftest/
            !build/selftest/**/engine-data/**
'''

CI = f'''name: CI
on: [pull_request]
jobs:
{windows_job('windows-x64', 'windows-latest', 'msix-x64')}{windows_job('windows-arm64', 'windows-11-arm', 'msix-arm64')}  architecture:
    runs-on: ubuntu-latest
    steps:
      - run: echo ok
'''

RELEASE = f'''name: Windows release
on:
  release:
    types: [published]
  workflow_dispatch:
permissions:
  contents: read
jobs:
  plan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@{SHA} # v7
      - run: ./scripts/release/resolve-release.ps1
      - run: ./scripts/release/assert-on-default-branch.ps1
  build:
    needs: plan
    runs-on: windows-latest
    environment: release
    steps:
      - uses: actions/checkout@{SHA} # v7
      - run: ./scripts/release/build-release-msix.ps1 -Flavour Store -RequireOAuthIds
        env:
          GOOGLE_OAUTH_CLIENT_ID: ${{{{ secrets.GOOGLE_OAUTH_CLIENT_ID }}}}
          GOOGLE_OAUTH_CLIENT_SECRET: ${{{{ secrets.GOOGLE_OAUTH_CLIENT_SECRET }}}}
          MICROSOFT_OAUTH_CLIENT_ID: ${{{{ secrets.MICROSOFT_OAUTH_CLIENT_ID }}}}
      - uses: actions/upload-artifact@{SHA} # v7
        with:
          name: store-package
          path: build/release/*.msix
'''

DRYRUN = f'''name: Windows release dry run
on: [pull_request]
permissions:
  contents: read
jobs:
  dry-run:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@{SHA} # v7
      - run: ./scripts/release/build-release-msix.ps1 -Flavour Store -DryRun
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
        '.github/workflows/windows-release.yml': RELEASE,
        '.github/workflows/windows-release-dryrun.yml': DRYRUN,
        '.gitignore': '*.pfx\n*.p12\n*.pvk\n*.key\n*.snk\n',
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

    def test_both_windows_jobs_must_build_stage_and_upload_the_development_msix(self):
        for script in ('build-msix.ps1', 'stage-msix-artifact.ps1'):
            self.fails(f'job windows-arm64 does not run {script}', **{'.github/workflows/ci.yml': CI.replace(script, 'other.ps1').replace('other.ps1', script, 1)})
        self.fails('job windows-x64 must upload exactly one artifact named msix-x64', **{'.github/workflows/ci.yml': CI.replace('msix-x64', 'package-x64')})
        self.fails('job windows-arm64 must upload exactly one artifact named msix-arm64', **{'.github/workflows/ci.yml': CI.replace('name: msix-arm64', 'name: selftest')})

    def test_the_msix_artifact_uploads_only_the_staged_folder_for_14_days(self):
        self.fails('must upload only build/msix-artifact/', **{'.github/workflows/ci.yml': CI.replace('path: build/msix-artifact/', 'path: build/msix', 1)})
        self.fails('retention-days: 14', **{'.github/workflows/ci.yml': CI.replace('retention-days: 14', 'retention-days: 90', 1)})

    def test_no_artifact_may_name_key_material_or_the_whole_workspace(self):
        for path in ('build/msix/dev.pfx', 'build/**/*.p12', 'secrets/signing.KEY', 'certs/a.pem'):
            self.fails('names key material', **{'.github/workflows/ci.yml': CI.replace('build/selftest/\n', f'{path}\n', 1)})
        for path in ('.', '**'):
            self.fails('uploads the whole workspace', **{'.github/workflows/ci.yml': CI.replace('build/selftest/\n', f'{path}\n', 1)})

    def test_key_files_must_not_be_committed(self):
        for name in ('signing/dev.pfx', 'build/x.P12', 'a/b.key', 'c.pem', 'd.snk'):
            self.fails('key material is committed', **{name: 'binary'})

    def test_a_private_key_block_must_not_be_committed(self):
        block = '-----BEGIN ' + 'RSA PRIVATE KEY' + '-----\nabc\n'
        self.fails('contains a private key block', **{'docs/notes.md': block})
        self.fails('contains a private key block', **{'scripts/x.ps1': '-----BEGIN ' + 'PRIVATE KEY' + '-----\n'})

    def test_scripts_must_not_export_the_signing_key(self):
        self.fails('exports or names a signing key file', **{'scripts/ci/x.ps1': 'Export-PfxCertificate -Cert $c -FilePath dev.pfx'})
        self.fails('exports or names a signing key file', **{'.github/workflows/other.yml': 'name: X\non: [push]\njobs:\n  x:\n    runs-on: ubuntu-latest\n    steps:\n      - run: openssl pkcs12 -export -out a\n'})
        self.assertEqual(run(**{'scripts/ci/x.ps1': 'Export-Certificate -Cert $c -FilePath dev.cer'}).returncode, 0)

    def test_gitignore_must_ignore_key_files(self):
        self.fails('*.pfx is not ignored', **{'.gitignore': '*.p12\n*.pvk\n*.key\n*.snk\n'})
        self.fails('missing documented path: .gitignore', **{'.gitignore': None})

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

    def release(self, text):
        return {'.github/workflows/windows-release.yml': text}

    def test_the_release_workflow_may_read_the_oauth_secrets_in_the_release_environment(self):
        self.assertEqual(run().returncode, 0)

    def test_the_oauth_secrets_are_refused_outside_the_release_environment(self):
        self.fails('uses secret GOOGLE_OAUTH_CLIENT_ID in job build', **self.release(RELEASE.replace('    environment: release\n', '')))
        self.fails('uses secret GOOGLE_OAUTH_CLIENT_ID in job plan', **self.release(RELEASE.replace('      - run: ./scripts/release/resolve-release.ps1\n', '      - run: echo "${{ secrets.GOOGLE_OAUTH_CLIENT_ID }}"\n')))
        other = 'name: X\non: [push]\njobs:\n  x:\n    runs-on: ubuntu-latest\n    environment: release\n    steps:\n      - run: echo "${{ secrets.GOOGLE_OAUTH_CLIENT_ID }}"\n'
        self.fails('other.yml: uses secret GOOGLE_OAUTH_CLIENT_ID', **{'.github/workflows/other.yml': other})
        self.fails('uses secret GOOGLE_OAUTH_CLIENT_ID', **{'.github/workflows/windows-release-dryrun.yml': DRYRUN + '        env:\n          X: ${{ secrets.GOOGLE_OAUTH_CLIENT_ID }}\n'})

    def test_no_other_secret_is_allowed_in_the_release_workflow(self):
        self.fails('uses secret PARTNER_CENTER_TOKEN', **self.release(RELEASE.replace('MICROSOFT_OAUTH_CLIENT_ID }}', 'MICROSOFT_OAUTH_CLIENT_ID }} ${{ secrets.PARTNER_CENTER_TOKEN }}')))

    def test_the_release_workflow_runs_only_on_a_release_or_by_hand(self):
        self.fails('must run only on a published release or by hand', **self.release(RELEASE.replace('  workflow_dispatch:\n', '  workflow_dispatch:\n  pull_request:\n')))
        self.fails('must run only on a published release or by hand', **self.release(RELEASE.replace('  workflow_dispatch:\n', '  workflow_dispatch:\n  push:\n')))

    def test_the_release_workflow_has_read_only_permissions(self):
        self.fails('permissions must be exactly contents: read', **self.release(RELEASE.replace('contents: read', 'contents: write')))
        self.fails('permissions must be exactly contents: read', **self.release(RELEASE.replace('contents: read', 'contents: read\n  id-token: write')))

    def test_the_build_job_needs_the_environment_and_real_ids_and_is_no_dry_run(self):
        self.fails('so it must run in the `release` environment', **self.release(RELEASE.replace('    environment: release\n', '    environment: production\n')))
        self.fails('must pass -RequireOAuthIds', **self.release(RELEASE.replace(' -RequireOAuthIds', '')))
        self.fails('must not be a dry run', **self.release(RELEASE.replace('-Flavour Store', '-Flavour Store -DryRun')))

    def test_the_plan_job_must_decide_and_check_the_ref(self):
        self.fails('the plan job must run resolve-release.ps1', **self.release(RELEASE.replace('resolve-release.ps1', 'other.ps1')))
        self.fails('the plan job must run assert-on-default-branch.ps1', **self.release(RELEASE.replace('assert-on-default-branch.ps1', 'other.ps1')))

    def test_the_release_workflow_never_submits_or_publishes(self):
        for step in ('./submit-to-store.ps1 -submission', 'gh release upload v1 a.msix', 'curl -X POST https://example.invalid', 'msstore publish', 'winget upload'):
            self.fails('talks to a store or publishes', **self.release(RELEASE + f'      - run: {step}\n'))

    def test_the_dry_run_has_no_environment_and_uses_fake_ids(self):
        self.fails('must not use an environment', **{'.github/workflows/windows-release-dryrun.yml': DRYRUN.replace('    runs-on: windows-latest\n', '    runs-on: windows-latest\n    environment: release\n')})
        self.fails('must build with -DryRun', **{'.github/workflows/windows-release-dryrun.yml': DRYRUN.replace(' -DryRun', '')})
        self.fails('must not require real OAuth ids', **{'.github/workflows/windows-release-dryrun.yml': DRYRUN + '      - run: ./x.ps1 -RequireOAuthIds\n'})

    def test_both_release_workflows_must_exist(self):
        self.fails('missing documented path: .github/workflows/windows-release.yml', **{'.github/workflows/windows-release.yml': None})
        self.fails('missing documented path: .github/workflows/windows-release-dryrun.yml', **{'.github/workflows/windows-release-dryrun.yml': None})

if __name__ == '__main__':
    unittest.main()
