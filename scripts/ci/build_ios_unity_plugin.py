#!/usr/bin/env python3

import argparse
import os
import pathlib
from common import (
    copy_file,
    copy_unity_managed_runtime,
    ensure_directory,
    replace_tree,
    resolve_path,
    run,
    write_lines,
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--core-root", default=".")
    parser.add_argument("--manifest-path", default="ios-staticlib/Cargo.toml")
    parser.add_argument("--target-dir", default="target/ios-staticlib")
    parser.add_argument("--output-root", default="target/unity-package/ios")
    parser.add_argument("--xcframework-output", default="target/apple-unity/RustAV.xcframework")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    public_root = pathlib.Path(args.public_root).resolve()
    core_root = pathlib.Path(args.core_root).resolve()
    manifest_path = resolve_path(core_root, args.manifest_path)
    target_dir = resolve_path(core_root, args.target_dir)
    output_root = resolve_path(public_root, args.output_root)
    xcframework_output = resolve_path(public_root, args.xcframework_output)
    header_root = public_root / "include"

    env = dict(**os.environ)
    env["CARGO_TARGET_DIR"] = str(target_dir)

    run(
        [
            "cargo",
            "build",
            "--manifest-path",
            str(manifest_path),
            "--release",
            "--lib",
            "--locked",
            "--target",
            "aarch64-apple-ios",
            "--features",
            "mobile-ffmpeg-build",
        ],
        cwd=core_root,
        prefix="ios-build",
        dry_run=args.dry_run,
        env=env,
    )
    run(
        [
            "cargo",
            "build",
            "--manifest-path",
            str(manifest_path),
            "--release",
            "--lib",
            "--locked",
            "--target",
            "aarch64-apple-ios-sim",
            "--features",
            "mobile-ffmpeg-build",
        ],
        cwd=core_root,
        prefix="ios-build",
        dry_run=args.dry_run,
        env=env,
    )

    if args.dry_run:
        run(
            [
                "xcodebuild",
                "-create-xcframework",
                "-library",
                str(target_dir / "aarch64-apple-ios" / "release" / "librustav_native.a"),
                "-headers",
                str(header_root),
                "-library",
                str(target_dir / "aarch64-apple-ios-sim" / "release" / "librustav_native.a"),
                "-headers",
                str(header_root),
                "-output",
                str(xcframework_output),
            ],
            cwd=public_root,
            prefix="ios-build",
            dry_run=True,
        )
        return 0

    ensure_directory(xcframework_output.parent)
    run(
        [
            "xcodebuild",
            "-create-xcframework",
            "-library",
            str(target_dir / "aarch64-apple-ios" / "release" / "librustav_native.a"),
            "-headers",
            str(header_root),
            "-library",
            str(target_dir / "aarch64-apple-ios-sim" / "release" / "librustav_native.a"),
            "-headers",
            str(header_root),
            "-output",
            str(xcframework_output),
        ],
        cwd=public_root,
        prefix="ios-build",
        dry_run=False,
    )

    copy_unity_managed_runtime(public_root, output_root)
    unity_dir = output_root / "Assets" / "Plugins" / "iOS"
    support_dir = output_root / "BuildSupport" / "iOS"
    ensure_directory(unity_dir)
    ensure_directory(support_dir)

    copy_file(
        target_dir / "aarch64-apple-ios" / "release" / "librustav_native.a",
        unity_dir / "librustav_native.a",
    )
    copy_file(public_root / "include" / "RustAV.h", unity_dir / "RustAV.h")

    xcframework_dest = support_dir / "RustAV.xcframework"
    replace_tree(xcframework_output, xcframework_dest)

    dependency_file = unity_dir / "DEPENDENCIES.txt"
    write_lines(
        dependency_file,
        [
            "iOS Unity 插件目录：Assets/Plugins/iOS",
            "",
            "自动集成文件：",
            "  - librustav_native.a",
            "  - RustAV.h",
            "",
            "额外运行时依赖：",
            "  - 无；FFmpeg 已静态链接进静态库",
            "",
            "附加构建支持：",
            "  - BuildSupport/iOS/RustAV.xcframework",
            "",
        ],
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
