import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name('resolve_release_version.py')
spec = importlib.util.spec_from_file_location('resolve_release_version', MODULE_PATH)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
sys.modules[spec.name] = module
spec.loader.exec_module(module)


def git(repo: Path, *args: str) -> str:
    result = subprocess.run(['git', *args], cwd=repo, text=True, capture_output=True, check=True)
    return result.stdout.strip()


def init_repo() -> Path:
    repo = Path(tempfile.mkdtemp(prefix='release-version-test-'))
    git(repo, 'init', '-b', 'master')
    git(repo, 'config', 'user.email', 'release-bot@example.invalid')
    git(repo, 'config', 'user.name', 'Release Bot')
    (repo / 'src/WinCare').mkdir(parents=True)
    (repo / 'src/WinCare/WinCare.psd1').write_text("@{\n    ModuleVersion = '1.1.0'\n}\n", encoding='utf-8')
    (repo / 'CHANGELOG.md').write_text('# Release Notes\n\n## 1.1.0\n\n- Existing release.\n', encoding='utf-8')
    git(repo, 'add', '.')
    git(repo, 'commit', '-m', 'chore: initial release state')
    git(repo, 'tag', '-a', 'v1.1.0', '-m', 'v1.1.0')
    return repo


class SemVerTests(unittest.TestCase):
    def test_parses_and_orders_semver_tags(self) -> None:
        self.assertEqual(str(module.SemVer.parse_tag('v2.10.3')), '2.10.3')
        self.assertIsNone(module.SemVer.parse_tag('release-2.0.0'))
        self.assertGreater(module.SemVer(2, 0, 0), module.SemVer(1, 99, 99))

    def test_auto_bump_uses_conventional_commit_precedence(self) -> None:
        self.assertEqual(module.choose_bump(['fix: correct typo'], 'auto'), 'patch')
        self.assertEqual(module.choose_bump(['fix: one', 'feat(ui): add panel'], 'auto'), 'minor')
        self.assertEqual(module.choose_bump(['feat!: remove legacy API'], 'auto'), 'major')
        self.assertEqual(module.choose_bump(['feat: change\n\nBREAKING CHANGE: format'], 'auto'), 'major')

    def test_explicit_bump_overrides_commit_messages(self) -> None:
        self.assertEqual(module.choose_bump(['feat!: break everything'], 'patch'), 'patch')

    def test_updates_manifest_and_changelog_once(self) -> None:
        root = Path(tempfile.mkdtemp(prefix='release-files-test-'))
        manifest = root / 'WinCare.psd1'
        changelog = root / 'CHANGELOG.md'
        manifest.write_text("@{\n ModuleVersion = '1.1.0'\n}\n", encoding='utf-8')
        changelog.write_text('# Notes\n\n## 1.1.0\n\n- A\n', encoding='utf-8')
        module.update_release_files(manifest, changelog, module.SemVer(1, 2, 0))
        self.assertIn("ModuleVersion = '1.2.0'", manifest.read_text(encoding='utf-8'))
        self.assertIn('## 1.2.0', changelog.read_text(encoding='utf-8'))


class RepositoryResolutionTests(unittest.TestCase):
    def test_creates_minor_release_commit_and_tag_from_feat_commit(self) -> None:
        repo = init_repo()
        (repo / 'feature.txt').write_text('feature\n', encoding='utf-8')
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'feat: add automated release identity')

        result = module.resolve_dispatch(repo, 'auto')

        self.assertEqual(result.tag, 'v1.2.0')
        self.assertTrue(result.created)
        self.assertEqual(git(repo, 'show', '-s', '--format=%s', 'v1.2.0^{}'), 'chore(release): v1.2.0')
        self.assertEqual(git(repo, 'show', 'v1.2.0:src/WinCare/WinCare.psd1').split("ModuleVersion = '")[1].split("'")[0], '1.2.0')

    def test_manifest_version_is_a_floor_above_latest_tag(self) -> None:
        repo = init_repo()
        manifest = repo / 'src/WinCare/WinCare.psd1'
        manifest.write_text("@{\n    ModuleVersion = '1.2.0'\n}\n", encoding='utf-8')
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'chore: prepare automated release')

        result = module.resolve_dispatch(repo, 'auto')

        self.assertEqual(result.tag, 'v1.2.0')
        self.assertEqual(result.bump, 'manifest-floor')

    def test_rerun_recovers_existing_unpublished_release_tag(self) -> None:
        repo = init_repo()
        (repo / 'feature.txt').write_text('feature\n', encoding='utf-8')
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'feat: add automated release identity')
        source_head = git(repo, 'rev-parse', 'HEAD')
        first = module.resolve_dispatch(repo, 'auto')
        git(repo, 'checkout', '--detach', source_head)

        recovered = module.resolve_dispatch(repo, 'auto')

        self.assertFalse(recovered.created)
        self.assertEqual(recovered.mode, 'recover')
        self.assertEqual(recovered.tag, first.tag)
        self.assertEqual(recovered.source_commit, first.source_commit)

    def test_next_branch_commit_bumps_from_generated_release_tag(self) -> None:
        repo = init_repo()
        (repo / 'feature.txt').write_text('feature\n', encoding='utf-8')
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'feat: add automated release identity')
        branch_head = git(repo, 'rev-parse', 'HEAD')
        module.resolve_dispatch(repo, 'auto')
        git(repo, 'checkout', '-B', 'master', branch_head)
        (repo / 'fix.txt').write_text('fix\n', encoding='utf-8')
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'fix: correct release summary')

        result = module.resolve_dispatch(repo, 'auto')

        self.assertEqual(result.tag, 'v1.2.1')
        self.assertTrue(result.created)

    def test_divergent_latest_tag_uses_merge_base_and_global_version_floor(self) -> None:
        repo = init_repo()
        common = git(repo, 'rev-parse', 'HEAD')
        git(repo, 'checkout', '-b', 'release-line')
        (repo / 'release-only.txt').write_text('breaking release-only work\n', encoding='utf-8')
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'feat!: release-only breaking change')
        module.update_release_files(
            repo / 'src/WinCare/WinCare.psd1',
            repo / 'CHANGELOG.md',
            module.SemVer(2, 1, 0),
        )
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'chore(release): v2.1.0')
        git(repo, 'tag', '-a', 'v2.1.0', '-m', 'v2.1.0')

        git(repo, 'checkout', 'master')
        (repo / 'fix.txt').write_text('current-line fix\n', encoding='utf-8')
        git(repo, 'add', '.')
        git(repo, 'commit', '-m', 'fix: repair current release line')

        result = module.resolve_dispatch(repo, 'auto')

        self.assertEqual(result.tag, 'v2.1.1')
        self.assertEqual(result.baseline_commit, common)
        self.assertEqual(result.mode, 'diverged')
        self.assertEqual(result.bump, 'patch')
        self.assertTrue(result.created)

    def test_push_tag_requires_manifest_version_match(self) -> None:
        repo = init_repo()
        with self.assertRaisesRegex(ValueError, 'does not match'):
            module.resolve_tag_push(repo, 'v1.2.0')


class ReleaseWorkflowTests(unittest.TestCase):
    def setUp(self) -> None:
        self.workflow = (Path(__file__).resolve().parents[1] / '.github/workflows/release.yml').read_text(encoding='utf-8')

    def test_manual_dispatch_exposes_automatic_and_explicit_bumps(self) -> None:
        for value in ('auto', 'patch', 'minor', 'major'):
            self.assertIn(f'          - {value}', self.workflow)
        self.assertIn("REQUESTED_BUMP: ${{ inputs.bump || 'auto' }}", self.workflow)

    def test_release_identity_is_resolved_before_validation(self) -> None:
        resolver = self.workflow.index('Resolve immutable release identity')
        validation = self.workflow.index('Run complete Windows validation and candidate build')
        self.assertLess(resolver, validation)
        self.assertIn('tools/resolve_release_version.py', self.workflow)
        self.assertIn('SOURCE_COMMIT: ${{ steps.version.outputs.source_commit }}', self.workflow)

    def test_release_runs_are_globally_serialized(self) -> None:
        self.assertIn('group: release-${{ github.repository }}', self.workflow)
        self.assertNotIn('group: release-${{ github.workflow }}-${{ github.ref }}', self.workflow)

    def test_publication_asset_list_is_flat_and_complete(self) -> None:
        self.assertNotIn('$assets=@(@(', self.workflow)
        self.assertIn('if($assets.Count -ne 14)', self.workflow)
        self.assertIn("Join-Path $candidateRoot 'promotion-manifest.json'", self.workflow)

    def test_existing_release_is_verified_instead_of_republished(self) -> None:
        self.assertIn("if: steps.publication.outputs.exists != 'true'", self.workflow)
        self.assertIn('Verify published or pre-existing immutable release', self.workflow)
        self.assertIn('Immutable release byte mismatch', self.workflow)
        self.assertIn('gh release download $env:RELEASE_TAG', self.workflow)


if __name__ == '__main__':
    unittest.main()
