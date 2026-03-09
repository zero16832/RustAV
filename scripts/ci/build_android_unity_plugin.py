#!/usr/bin/env python3

import argparse
import pathlib
from common import (
    copy_file,
    copy_unity_managed_runtime,
    ensure_directory,
    resolve_path,
    run,
    write_lines,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--core-root", default=".")
    parser.add_argument("--output-root", default="target/unity-package/android")
    parser.add_argument("--cargo-ndk-output", default="target/android-unity-libs")
    parser.add_argument("--abi", default="arm64-v8a")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    public_root = pathlib.Path(args.public_root).resolve()
    core_root = pathlib.Path(args.core_root).resolve()
    output_root = resolve_path(public_root, args.output_root)
    cargo_ndk_output = resolve_path(core_root, args.cargo_ndk_output)

    run(
        [
            "cargo",
            "ndk",
            "-t",
            args.abi,
            "-o",
            str(cargo_ndk_output),
            "build",
            "--release",
            "--lib",
            "--locked",
            "--features",
            "mobile-ffmpeg-build",
        ],
        cwd=core_root,
        prefix="android-build",
        dry_run=args.dry_run,
    )

    if args.dry_run:
        return 0

    copy_unity_managed_runtime(public_root, output_root)
    package_dir = output_root / "Assets" / "Plugins" / "Android" / args.abi
    ensure_directory(package_dir)

    source_lib = cargo_ndk_output / args.abi / "librustav_native.so"
    copy_file(source_lib, package_dir / source_lib.name)

    dependency_file = package_dir / "DEPENDENCIES.txt"
    write_lines(
        dependency_file,
        [
            "Android Unity 插件目录：Assets/Plugins/Android/arm64-v8a",
            "",
            "运行时文件：",
            "  - librustav_native.so",
            "",
            "额外运行时依赖：",
            "  - 无；FFmpeg 已静态链接进 librustav_native.so",
            "",
        ],
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
