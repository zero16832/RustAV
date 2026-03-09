#!/usr/bin/env python3

import argparse
import os
import pathlib

from common import (
    copy_file,
    copy_unity_managed_runtime,
    recreate_directory,
    resolve_path,
    run,
    write_lines,
)


WINDOWS_RUNTIME_PATTERNS = (
    "avcodec-*.dll",
    "avdevice-*.dll",
    "avfilter-*.dll",
    "avformat-*.dll",
    "avutil-*.dll",
    "swresample-*.dll",
    "swscale-*.dll",
)


def locate_vcpkg_root(public_root: pathlib.Path, core_root: pathlib.Path) -> pathlib.Path | None:
    candidates: list[pathlib.Path] = []

    env_root = os.environ.get("VCPKG_ROOT")
    if env_root:
        candidates.append(pathlib.Path(env_root))

    candidates.append(public_root / ".vcpkg")
    candidates.append(core_root / ".vcpkg")
    candidates.append(pathlib.Path("C:/vcpkg"))

    for candidate in candidates:
        if (candidate / "installed" / "x64-windows" / "bin").exists():
            return candidate

    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--core-root", default=".")
    parser.add_argument("--output-root", default="target/unity-package/windows")
    parser.add_argument("--configuration", default="release")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    public_root = pathlib.Path(args.public_root).resolve()
    core_root = pathlib.Path(args.core_root).resolve()
    output_root = resolve_path(public_root, args.output_root)

    configuration = args.configuration
    package_root = output_root / "Assets" / "Plugins" / "x86_64"
    artifact_dll = core_root / "target" / configuration / "rustav_native.dll"

    run(
        ["cargo", "build", f"--{configuration}", "--lib", "--locked"],
        cwd=core_root,
        prefix="windows-build",
        dry_run=args.dry_run,
    )

    if args.dry_run:
        return 0

    copy_unity_managed_runtime(public_root, output_root)
    recreate_directory(package_root)
    copy_file(artifact_dll, package_root / artifact_dll.name)

    runtime_dlls: list[pathlib.Path] = []
    vcpkg_root = locate_vcpkg_root(public_root, core_root)
    if vcpkg_root:
        runtime_dir = vcpkg_root / "installed" / "x64-windows" / "bin"
        if runtime_dir.exists():
            seen_names: set[str] = set()
            for pattern in WINDOWS_RUNTIME_PATTERNS:
                for dll in sorted(runtime_dir.glob(pattern)):
                    if dll.name in seen_names:
                        continue
                    runtime_dlls.append(dll)
                    seen_names.add(dll.name)
            for dll in runtime_dlls:
                copy_file(dll, package_root / dll.name)

    dependency_file = package_root / "DEPENDENCIES.txt"
    lines = [
        "Windows Unity 插件目录：Assets/Plugins/x86_64",
        "",
        "运行时文件：",
        "  - rustav_native.dll",
    ]
    for dll in runtime_dlls:
        lines.append(f"  - {dll.name}")
    write_lines(dependency_file, lines)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
