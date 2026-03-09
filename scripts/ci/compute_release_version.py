#!/usr/bin/env python3

from __future__ import annotations

import argparse
import os
import pathlib
import re
import subprocess
import tomllib


SEMVER_PATTERN = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)$")


def parse_version(value: str) -> tuple[int, int, int]:
    match = SEMVER_PATTERN.match(value.strip())
    if not match:
        raise ValueError(f"无效版本号: {value}")
    return tuple(int(group) for group in match.groups())


def version_to_string(version: tuple[int, int, int]) -> str:
    return f"{version[0]}.{version[1]}.{version[2]}"


def android_version_code(version: tuple[int, int, int]) -> int:
    return (version[0] * 10000) + (version[1] * 100) + version[2]


def load_cargo_version(core_root: pathlib.Path) -> tuple[int, int, int]:
    cargo_toml = core_root / "Cargo.toml"
    with cargo_toml.open("rb") as handle:
        data = tomllib.load(handle)
    return parse_version(data["package"]["version"])


def list_git_tags(project_root: pathlib.Path) -> list[tuple[int, int, int]]:
    output = subprocess.check_output(
        ["git", "tag", "--list", "v*"],
        cwd=str(project_root),
        text=True,
    )
    versions: list[tuple[int, int, int]] = []
    for line in output.splitlines():
        line = line.strip()
        if not line:
            continue
        match = SEMVER_PATTERN.match(line)
        if match:
            versions.append(tuple(int(group) for group in match.groups()))
    return versions


def compute_next_version(
    cargo_version: tuple[int, int, int],
    tag_versions: list[tuple[int, int, int]],
) -> tuple[int, int, int]:
    if not tag_versions:
        return cargo_version

    latest_tag = max(tag_versions)
    if cargo_version > latest_tag:
        return cargo_version

    return (latest_tag[0], latest_tag[1], latest_tag[2] + 1)


def write_github_output(version: str, tag: str, android_code: int) -> None:
    output_path = pathlib.Path.cwd() / ".tmp-github-output"
    github_output = pathlib.Path(os.environ.get("GITHUB_OUTPUT", str(output_path)))
    github_output.parent.mkdir(parents=True, exist_ok=True)
    with github_output.open("a", encoding="utf-8") as handle:
        handle.write(f"version={version}\n")
        handle.write(f"tag={tag}\n")
        handle.write(f"android_version_code={android_code}\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--core-root", default=".")
    parser.add_argument("--github-output", action="store_true")
    args = parser.parse_args()

    public_root = pathlib.Path(args.public_root).resolve()
    core_root = pathlib.Path(args.core_root).resolve()
    cargo_version = load_cargo_version(core_root)
    tag_versions = list_git_tags(public_root)
    next_version = compute_next_version(cargo_version, tag_versions)
    version = version_to_string(next_version)
    tag = f"v{version}"
    android_code = android_version_code(next_version)

    print(f"version={version}")
    print(f"tag={tag}")
    print(f"android_version_code={android_code}")

    if args.github_output:
        write_github_output(version, tag, android_code)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
