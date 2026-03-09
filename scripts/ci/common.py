#!/usr/bin/env python3

from __future__ import annotations

import pathlib
import shutil
import subprocess

UNITY_MANAGED_RUNTIME_PATHS = (
    "UnityAV.Runtime.asmdef",
    "UnityAV.Runtime.asmdef.meta",
    "Runtime",
    "Runtime.meta",
)

UNITY_SAMPLE_MEDIA_FILES = (
    "SampleVideo_1280x720_10mb.mp4",
)


def resolve_path(project_root: pathlib.Path, value: str) -> pathlib.Path:
    path = pathlib.Path(value)
    if not path.is_absolute():
        path = project_root / path
    return path.resolve()


def run(
    cmd: list[str],
    cwd: pathlib.Path,
    prefix: str,
    dry_run: bool,
    env: dict[str, str] | None = None,
) -> None:
    print(f"[{prefix}]", " ".join(cmd))
    if dry_run:
        return
    subprocess.run(cmd, cwd=str(cwd), check=True, env=env)


def ensure_directory(path: pathlib.Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def recreate_directory(path: pathlib.Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


def copy_file(source: pathlib.Path, destination: pathlib.Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def replace_tree(source: pathlib.Path, destination: pathlib.Path) -> None:
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(source, destination)


def copy_unity_managed_runtime(project_root: pathlib.Path, output_root: pathlib.Path) -> None:
    source_assets_root = project_root / "UnityAVExample" / "Assets"
    source_runtime_dir = source_assets_root / "UnityAV"
    destination_assets_root = output_root / "Assets"
    destination_runtime_dir = destination_assets_root / "UnityAV"

    if not source_runtime_dir.exists():
        raise FileNotFoundError(f"Unity 托管源码目录不存在: {source_runtime_dir}")

    if destination_runtime_dir.exists():
        shutil.rmtree(destination_runtime_dir)
    destination_runtime_dir.mkdir(parents=True, exist_ok=True)

    source_runtime_meta = source_assets_root / "UnityAV.meta"
    if source_runtime_meta.exists():
        copy_file(source_runtime_meta, destination_assets_root / "UnityAV.meta")

    for relative_path in UNITY_MANAGED_RUNTIME_PATHS:
        source_path = source_runtime_dir / relative_path
        destination_path = destination_runtime_dir / relative_path
        if source_path.is_dir():
            replace_tree(source_path, destination_path)
        else:
            copy_file(source_path, destination_path)


def sync_unity_sample_media(project_root: pathlib.Path, unity_project: pathlib.Path) -> None:
    source_root = project_root / "TestFiles"
    destination_root = unity_project / "Assets" / "StreamingAssets"
    ensure_directory(destination_root)

    for file_name in UNITY_SAMPLE_MEDIA_FILES:
        source_path = source_root / file_name
        if not source_path.exists():
            raise FileNotFoundError(f"Unity 样例媒体不存在: {source_path}")
        copy_file(source_path, destination_root / file_name)


def write_lines(path: pathlib.Path, lines: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
