#!/usr/bin/env python3

from __future__ import annotations

import argparse
import pathlib
import sys

from common import run


def script_path(script_dir: pathlib.Path, name: str) -> pathlib.Path:
    return script_dir / name


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--platform",
        choices=["windows", "android", "ios", "package", "all"],
        required=True,
    )
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--core-root", default=".")
    parser.add_argument("--output-root")
    parser.add_argument("--cargo-ndk-output", default="target/android-unity-libs")
    parser.add_argument("--abi", default="arm64-v8a")
    parser.add_argument("--manifest-path", default="ios-staticlib/Cargo.toml")
    parser.add_argument("--target-dir", default="target/ios-staticlib")
    parser.add_argument("--xcframework-output", default="target/apple-unity/RustAV.xcframework")
    parser.add_argument("--windows-artifact", default="artifacts/windows")
    parser.add_argument("--android-artifact", default="artifacts/android")
    parser.add_argument("--ios-artifact", default="artifacts/ios")
    parser.add_argument("--bundle-root", default="UnityPlugins")
    parser.add_argument("--zip-output", default="RustAV-UnityPlugins.zip")
    parser.add_argument("--configuration", default="release")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    script_dir = pathlib.Path(__file__).resolve().parent
    public_root = pathlib.Path(args.public_root).resolve()
    core_root = pathlib.Path(args.core_root).resolve()

    commands: list[list[str]] = []

    if args.platform in ("windows", "all"):
        commands.append(
            [
                sys.executable,
                str(script_path(script_dir, "build_windows_unity_plugin.py")),
                "--public-root",
                str(public_root),
                "--core-root",
                str(core_root),
                "--output-root",
                args.output_root or "target/unity-package/windows",
                "--configuration",
                args.configuration,
                *([] if not args.dry_run else ["--dry-run"]),
            ]
        )

    if args.platform in ("android", "all"):
        commands.append(
            [
                sys.executable,
                str(script_path(script_dir, "build_android_unity_plugin.py")),
                "--public-root",
                str(public_root),
                "--core-root",
                str(core_root),
                "--output-root",
                args.output_root or "target/unity-package/android",
                "--cargo-ndk-output",
                args.cargo_ndk_output,
                "--abi",
                args.abi,
                *([] if not args.dry_run else ["--dry-run"]),
            ]
        )

    if args.platform in ("ios", "all"):
        commands.append(
            [
                sys.executable,
                str(script_path(script_dir, "build_ios_unity_plugin.py")),
                "--public-root",
                str(public_root),
                "--core-root",
                str(core_root),
                "--manifest-path",
                args.manifest_path,
                "--target-dir",
                args.target_dir,
                "--output-root",
                args.output_root or "target/unity-package/ios",
                "--xcframework-output",
                args.xcframework_output,
                *([] if not args.dry_run else ["--dry-run"]),
            ]
        )

    if args.platform in ("package", "all"):
        commands.append(
            [
                sys.executable,
                str(script_path(script_dir, "assemble_unity_plugins_bundle.py")),
                "--public-root",
                str(public_root),
                "--windows-artifact",
                args.windows_artifact,
                "--android-artifact",
                args.android_artifact,
                "--ios-artifact",
                args.ios_artifact,
                "--bundle-root",
                args.bundle_root,
                "--zip-output",
                args.zip_output,
                *([] if not args.dry_run else ["--dry-run"]),
            ]
        )

    for cmd in commands:
        run(cmd, cwd=public_root, prefix="unity-build", dry_run=args.dry_run)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
