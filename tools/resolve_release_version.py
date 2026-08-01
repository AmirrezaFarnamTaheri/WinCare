#!/usr/bin/env python3
from __future__ import annotations

import argparse
import dataclasses
import re
import subprocess
from pathlib import Path
from typing import Iterable, Sequence


_TAG_RE = re.compile(r"^v(?P<major>0|[1-9]\d*)\.(?P<minor>0|[1-9]\d*)\.(?P<patch>0|[1-9]\d*)$")
_MANIFEST_RE = re.compile(r"(?m)^(?P<prefix>\s*ModuleVersion\s*=\s*')(?P<version>[^']+)(?P<suffix>'\s*)$")
_CHANGELOG_RE = re.compile(r"(?m)^##\s+(?P<version>\d+\.\d+\.\d+)\s*$")
_BREAKING_SUBJECT_RE = re.compile(r"^[a-zA-Z][a-zA-Z0-9-]*(?:\([^\r\n)]+\))?!:")
_FEATURE_SUBJECT_RE = re.compile(r"^feat(?:\([^\r\n)]+\))?:", re.IGNORECASE)
_RELEASE_SUBJECT_RE = re.compile(r"^chore\(release\):\s+(v\d+\.\d+\.\d+)$")


@dataclasses.dataclass(frozen=True, order=True)
class SemVer:
    major: int
    minor: int
    patch: int

    @classmethod
    def parse_tag(cls, value: str) -> "SemVer | None":
        match = _TAG_RE.fullmatch(value.strip())
        if not match:
            return None
        return cls(*(int(match.group(name)) for name in ("major", "minor", "patch")))

    def bump(self, kind: str) -> "SemVer":
        if kind == "major":
            return SemVer(self.major + 1, 0, 0)
        if kind == "minor":
            return SemVer(self.major, self.minor + 1, 0)
        if kind == "patch":
            return SemVer(self.major, self.minor, self.patch + 1)
        raise ValueError(f"Unsupported bump kind: {kind}")

    @property
    def tag(self) -> str:
        return f"v{self}"

    def __str__(self) -> str:
        return f"{self.major}.{self.minor}.{self.patch}"


@dataclasses.dataclass(frozen=True)
class Resolution:
    version: SemVer
    tag: str
    source_commit: str
    created: bool
    mode: str
    baseline_commit: str
    bump: str

    def as_outputs(self) -> dict[str, str]:
        return {
            "version": str(self.version),
            "tag": self.tag,
            "source_commit": self.source_commit,
            "created": str(self.created).lower(),
            "mode": self.mode,
            "baseline_commit": self.baseline_commit,
            "bump": self.bump,
        }


def run_git(repo: Path, *args: str, check: bool = True) -> str:
    result = subprocess.run(
        ["git", *args],
        cwd=repo,
        text=True,
        capture_output=True,
        check=False,
    )
    if check and result.returncode != 0:
        detail = result.stderr.strip() or result.stdout.strip() or f"exit {result.returncode}"
        raise RuntimeError(f"git {' '.join(args)} failed: {detail}")
    return result.stdout.strip()


def choose_bump(messages: Iterable[str], requested: str) -> str:
    if requested not in {"auto", "patch", "minor", "major"}:
        raise ValueError(f"Unsupported bump selection: {requested}")
    if requested != "auto":
        return requested

    saw_feature = False
    for message in messages:
        normalized = message.replace("\r\n", "\n")
        subject = normalized.split("\n", 1)[0].strip()
        if _BREAKING_SUBJECT_RE.match(subject) or re.search(r"(?im)^BREAKING(?: |-)?CHANGE:\s*\S", normalized):
            return "major"
        if _FEATURE_SUBJECT_RE.match(subject):
            saw_feature = True
    return "minor" if saw_feature else "patch"


def read_manifest_version(manifest_path: Path) -> SemVer:
    text = manifest_path.read_text(encoding="utf-8")
    matches = list(_MANIFEST_RE.finditer(text))
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one ModuleVersion assignment in {manifest_path}")
    parsed = SemVer.parse_tag(f"v{matches[0].group('version')}")
    if parsed is None:
        raise ValueError(f"Manifest ModuleVersion is not strict SemVer: {matches[0].group('version')}")
    return parsed


def update_release_files(manifest_path: Path, changelog_path: Path, version: SemVer) -> None:
    manifest = manifest_path.read_text(encoding="utf-8")
    manifest_matches = list(_MANIFEST_RE.finditer(manifest))
    if len(manifest_matches) != 1:
        raise ValueError(f"Expected exactly one ModuleVersion assignment in {manifest_path}")
    manifest = _MANIFEST_RE.sub(lambda match: f"{match.group('prefix')}{version}{match.group('suffix')}", manifest, count=1)
    manifest_path.write_text(manifest, encoding="utf-8", newline="\n")

    changelog = changelog_path.read_text(encoding="utf-8")
    changelog_matches = list(_CHANGELOG_RE.finditer(changelog))
    if not changelog_matches:
        raise ValueError(f"Expected a SemVer release heading in {changelog_path}")
    changelog = _CHANGELOG_RE.sub(f"## {version}", changelog, count=1)
    changelog_path.write_text(changelog, encoding="utf-8", newline="\n")


def list_release_tags(repo: Path) -> list[tuple[SemVer, str]]:
    tags: list[tuple[SemVer, str]] = []
    for raw in run_git(repo, "tag", "--list", "v*").splitlines():
        parsed = SemVer.parse_tag(raw)
        if parsed is not None:
            tags.append((parsed, raw))
    tags.sort(key=lambda item: item[0])
    return tags


def is_ancestor(repo: Path, ancestor: str, descendant: str) -> bool:
    result = subprocess.run(
        ["git", "merge-base", "--is-ancestor", ancestor, descendant],
        cwd=repo,
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode not in {0, 1}:
        raise RuntimeError(result.stderr.strip() or "git merge-base failed")
    return result.returncode == 0


def commit_parents(repo: Path, commit: str) -> list[str]:
    parents = run_git(repo, "show", "-s", "--format=%P", commit)
    return parents.split() if parents else []


def determine_baseline(repo: Path, latest_tag: str, head: str) -> tuple[str, str, str]:
    tag_commit = run_git(repo, "rev-list", "-n", "1", latest_tag)
    if tag_commit == head:
        return tag_commit, tag_commit, "recover"

    subject = run_git(repo, "show", "-s", "--format=%s", tag_commit)
    parents = commit_parents(repo, tag_commit)
    release_match = _RELEASE_SUBJECT_RE.fullmatch(subject)
    if release_match and release_match.group(1) == latest_tag and len(parents) == 1 and is_ancestor(repo, parents[0], head):
        if parents[0] == head:
            return parents[0], tag_commit, "recover"
        return parents[0], tag_commit, "advance"

    if is_ancestor(repo, tag_commit, head):
        return tag_commit, tag_commit, "recover" if tag_commit == head else "advance"

    raise ValueError(
        f"Latest release tag {latest_tag} is not related to HEAD {head}; refusing to infer a version across divergent history"
    )


def commit_messages(repo: Path, baseline: str, head: str) -> list[str]:
    if baseline == head:
        return []
    raw = run_git(repo, "log", "--format=%s%n%b%x00", f"{baseline}..{head}")
    return [entry.strip() for entry in raw.split("\x00") if entry.strip()]


def resolve_dispatch(
    repo: Path,
    requested_bump: str,
    manifest_path: Path | None = None,
    changelog_path: Path | None = None,
) -> Resolution:
    repo = repo.resolve()
    manifest_path = manifest_path or repo / "src/WinCare/WinCare.psd1"
    changelog_path = changelog_path or repo / "CHANGELOG.md"
    head = run_git(repo, "rev-parse", "HEAD")
    tags = list_release_tags(repo)

    if tags:
        current_version, latest_tag = tags[-1]
        baseline, tag_commit, mode = determine_baseline(repo, latest_tag, head)
        if mode == "recover":
            return Resolution(current_version, latest_tag, tag_commit, False, "recover", baseline, "none")
    else:
        current_version = SemVer(0, 0, 0)
        latest_tag = ""
        baseline = run_git(repo, "rev-list", "--max-parents=0", "HEAD").splitlines()[0]

    messages = commit_messages(repo, baseline, head)
    if not messages and tags:
        raise ValueError(f"No commits found after {latest_tag}; nothing new to release")
    bump = choose_bump(messages, requested_bump)
    next_version = current_version.bump(bump)
    manifest_version = read_manifest_version(manifest_path)
    if manifest_version > next_version:
        next_version = manifest_version
        bump = "manifest-floor"
    update_release_files(manifest_path, changelog_path, next_version)

    run_git(repo, "add", str(manifest_path.relative_to(repo)), str(changelog_path.relative_to(repo)))
    staged = run_git(repo, "diff", "--cached", "--name-only")
    if not staged:
        raise ValueError(f"Automated version {next_version} produced no source changes")
    run_git(repo, "commit", "-m", f"chore(release): {next_version.tag}")
    source_commit = run_git(repo, "rev-parse", "HEAD")
    run_git(repo, "tag", "-a", next_version.tag, "-m", next_version.tag, source_commit)
    return Resolution(next_version, next_version.tag, source_commit, True, "create", baseline, bump)


def resolve_tag_push(repo: Path, ref_name: str, manifest_path: Path | None = None) -> Resolution:
    version = SemVer.parse_tag(ref_name)
    if version is None:
        raise ValueError(f"Release ref is not a strict SemVer tag: {ref_name}")
    repo = repo.resolve()
    manifest_path = manifest_path or repo / "src/WinCare/WinCare.psd1"
    manifest_version = read_manifest_version(manifest_path)
    if manifest_version != version:
        raise ValueError(f"Tag {ref_name} does not match manifest version {manifest_version}")
    head = run_git(repo, "rev-parse", "HEAD")
    return Resolution(version, ref_name, head, False, "tag", head, "none")


def write_github_outputs(path: Path, outputs: dict[str, str]) -> None:
    with path.open("a", encoding="utf-8", newline="\n") as handle:
        for key, value in outputs.items():
            if "\n" in value or "\r" in value:
                raise ValueError(f"Output {key} contains a newline")
            handle.write(f"{key}={value}\n")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Resolve and materialize an immutable WinCare release version")
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--event-name", choices=("workflow_dispatch", "push"), required=True)
    parser.add_argument("--ref-name", default="")
    parser.add_argument("--bump", choices=("auto", "patch", "minor", "major"), default="auto")
    parser.add_argument("--manifest", type=Path, default=Path("src/WinCare/WinCare.psd1"))
    parser.add_argument("--changelog", type=Path, default=Path("CHANGELOG.md"))
    parser.add_argument("--github-output", type=Path)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    repo = args.repo.resolve()
    manifest = args.manifest if args.manifest.is_absolute() else repo / args.manifest
    changelog = args.changelog if args.changelog.is_absolute() else repo / args.changelog
    if args.event_name == "push":
        resolution = resolve_tag_push(repo, args.ref_name, manifest)
    else:
        resolution = resolve_dispatch(repo, args.bump, manifest, changelog)
    outputs = resolution.as_outputs()
    if args.github_output:
        write_github_outputs(args.github_output, outputs)
    for key, value in outputs.items():
        print(f"{key}={value}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
