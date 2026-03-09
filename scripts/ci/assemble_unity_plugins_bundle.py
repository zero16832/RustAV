#!/usr/bin/env python3

import argparse
import pathlib
import zipfile
from common import copy_file, recreate_directory, replace_tree, resolve_path


def copy_tree_contents(source: pathlib.Path, destination: pathlib.Path) -> None:
    if not source.exists():
        return

    for item in source.iterdir():
        target = destination / item.name
        if item.is_dir():
            replace_tree(item, target)
        else:
            copy_file(item, target)


def zip_directory(source_dir: pathlib.Path, zip_path: pathlib.Path) -> None:
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in source_dir.rglob("*"):
            archive.write(path, path.relative_to(source_dir.parent))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--windows-artifact", default="artifacts/windows")
    parser.add_argument("--android-artifact", default="artifacts/android")
    parser.add_argument("--ios-artifact", default="artifacts/ios")
    parser.add_argument("--bundle-root", default="UnityPlugins")
    parser.add_argument("--zip-output", default="RustAV-UnityPlugins.zip")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    public_root = pathlib.Path(args.public_root).resolve()

    windows_artifact = resolve_path(public_root, args.windows_artifact)
    android_artifact = resolve_path(public_root, args.android_artifact)
    ios_artifact = resolve_path(public_root, args.ios_artifact)
    bundle_root = resolve_path(public_root, args.bundle_root)
    zip_output = resolve_path(public_root, args.zip_output)

    print(f"[bundle] windows={windows_artifact}")
    print(f"[bundle] android={android_artifact}")
    print(f"[bundle] ios={ios_artifact}")
    print(f"[bundle] bundle_root={bundle_root}")
    print(f"[bundle] zip_output={zip_output}")
    if args.dry_run:
        return 0

    recreate_directory(bundle_root)

    copy_tree_contents(windows_artifact, bundle_root)
    copy_tree_contents(android_artifact, bundle_root)
    copy_tree_contents(ios_artifact, bundle_root)
    copy_file(public_root / "UNITY_PLUGIN_PACKAGE_LAYOUT.md", bundle_root / "README.md")

    if zip_output.exists():
        zip_output.unlink()
    zip_directory(bundle_root, zip_output)
    print(f"assembled {zip_output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
