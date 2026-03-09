#!/usr/bin/env python3

from __future__ import annotations

import argparse
import pathlib
import shutil

from common import ensure_directory, replace_tree, resolve_path
from common import sync_unity_sample_media

PRESERVED_PLUGIN_NAMES = set()

RUNTIME_EXCLUDE_NAMES = {
    "DEPENDENCIES.txt",
}


def remove_path(path: pathlib.Path) -> None:
    if not path.exists():
        return
    if path.is_dir():
        shutil.rmtree(path)
    else:
        path.unlink()


def prune_runtime_docs(root: pathlib.Path) -> None:
    if not root.exists():
        return
    for path in root.rglob("*"):
        if path.is_file() and path.name in RUNTIME_EXCLUDE_NAMES:
            path.unlink()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--public-root", default=".")
    parser.add_argument("--unity-project", required=True)
    parser.add_argument("--plugins-root", required=True)
    parser.add_argument("--managed-runtime-root", default="")
    parser.add_argument("--build-support-root", default="")
    args = parser.parse_args()

    public_root = pathlib.Path(args.public_root).resolve()
    unity_project = resolve_path(public_root, args.unity_project)
    plugins_root = resolve_path(public_root, args.plugins_root)
    managed_runtime_root = (
        resolve_path(public_root, args.managed_runtime_root)
        if args.managed_runtime_root
        else None
    )
    build_support_root = (
        resolve_path(public_root, args.build_support_root)
        if args.build_support_root
        else None
    )

    if not plugins_root.exists():
        raise FileNotFoundError(f"插件目录不存在: {plugins_root}")

    destination_plugins = unity_project / "Assets" / "Plugins"
    ensure_directory(destination_plugins)
    for item in destination_plugins.iterdir():
        if item.name in PRESERVED_PLUGIN_NAMES:
            continue
        remove_path(item)

    for item in plugins_root.iterdir():
        target = destination_plugins / item.name
        remove_path(target)
        if item.is_dir():
            replace_tree(item, target)
            prune_runtime_docs(target)
        else:
            if item.name not in RUNTIME_EXCLUDE_NAMES:
                shutil.copy2(item, target)

    if managed_runtime_root and managed_runtime_root.exists():
        destination_runtime = unity_project / "Assets" / "UnityAV"
        ensure_directory(destination_runtime)
        for item in managed_runtime_root.iterdir():
            target = destination_runtime / item.name
            remove_path(target)
            if item.is_dir():
                replace_tree(item, target)
            else:
                shutil.copy2(item, target)

    if build_support_root and build_support_root.exists():
        destination_support = unity_project / "BuildSupport"
        ensure_directory(destination_support)
        for item in build_support_root.iterdir():
            target = destination_support / item.name
            if item.is_dir():
                replace_tree(item, target)
            else:
                shutil.copy2(item, target)

    sync_unity_sample_media(public_root, unity_project)

    print(f"[unity-sync] unity_project={unity_project}")
    print(f"[unity-sync] plugins_root={plugins_root}")
    if managed_runtime_root and managed_runtime_root.exists():
        print(f"[unity-sync] managed_runtime_root={managed_runtime_root}")
    if build_support_root and build_support_root.exists():
        print(f"[unity-sync] build_support_root={build_support_root}")
    print(f"[unity-sync] sample_media_root={public_root / 'TestFiles'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
