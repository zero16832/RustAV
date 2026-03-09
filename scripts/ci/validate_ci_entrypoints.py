#!/usr/bin/env python3

from __future__ import annotations

import pathlib
import subprocess
import sys

from common import run


def main() -> int:
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--core-root", default=".")
    args = parser.parse_args()

    script_dir = pathlib.Path(__file__).resolve().parent
    public_root = pathlib.Path(args.public_root).resolve()
    core_root = pathlib.Path(args.core_root).resolve()

    entrypoints = [
        script_dir / "common.py",
        script_dir / "build_windows_unity_plugin.py",
        script_dir / "build_android_unity_plugin.py",
        script_dir / "build_ios_unity_plugin.py",
        script_dir / "build_unity_plugins.py",
        script_dir / "assemble_unity_plugins_bundle.py",
        script_dir / "compute_release_version.py",
        script_dir / "sync_unity_plugins_to_project.py",
        script_dir / "validate_ci_entrypoints.py",
        script_dir / "zip_directory.py",
    ]

    run(
        [sys.executable, "-m", "py_compile", *[str(path) for path in entrypoints]],
        cwd=public_root,
        prefix="ci-validate",
        dry_run=False,
    )

    dry_run_commands = [
        [
            sys.executable,
            str(script_dir / "build_windows_unity_plugin.py"),
            "--public-root",
            str(public_root),
            "--core-root",
            str(core_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "build_android_unity_plugin.py"),
            "--public-root",
            str(public_root),
            "--core-root",
            str(core_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "build_ios_unity_plugin.py"),
            "--public-root",
            str(public_root),
            "--core-root",
            str(core_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "assemble_unity_plugins_bundle.py"),
            "--public-root",
            str(public_root),
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "build_unity_plugins.py"),
            "--public-root",
            str(public_root),
            "--core-root",
            str(core_root),
            "--platform",
            "all",
            "--dry-run",
        ],
        [
            sys.executable,
            str(script_dir / "compute_release_version.py"),
            "--public-root",
            str(public_root),
            "--core-root",
            str(core_root),
        ],
    ]

    for cmd in dry_run_commands:
        run(cmd, cwd=public_root, prefix="ci-validate", dry_run=False)

    print("[ci-validate] all CI entrypoint dry-run checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
