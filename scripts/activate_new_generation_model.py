#!/usr/bin/env python3
"""Validate and install an immutable official Home Corners 2026 deployment bundle."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path
from typing import Any


TARGET = "TargetHomeCorners"
ALLOWED_STATUSES = {"active", "active_candidate"}


def read_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(f"{path.name} must contain a JSON object.")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def trusted_relative(root: Path, relative: str) -> Path:
    if not relative or Path(relative).is_absolute():
        raise ValueError("Bundle file names must be non-empty relative paths.")
    resolved_root = root.resolve()
    resolved = (resolved_root / relative).resolve()
    if resolved != resolved_root and resolved_root not in resolved.parents:
        raise ValueError(f"Bundle path escapes the trusted source: {relative}")
    return resolved


def checksum_entries(source: Path) -> dict[str, str]:
    checksum_path = source / "checksums.sha256"
    if not checksum_path.is_file():
        raise ValueError("Missing checksums.sha256.")
    entries: dict[str, str] = {}
    for line in checksum_path.read_text(encoding="utf-8").splitlines():
        parts = line.strip().split(maxsplit=1)
        if len(parts) != 2:
            raise ValueError("checksums.sha256 contains an invalid line.")
        expected, relative = parts
        path = trusted_relative(source, relative.strip())
        if not path.is_file():
            raise ValueError(f"Checksummed file is missing: {relative.strip()}")
        actual = sha256(path)
        if actual != expected.lower():
            raise ValueError(f"Checksum mismatch for {relative.strip()}.")
        entries[relative.strip()] = actual
    if not entries:
        raise ValueError("checksums.sha256 is empty.")
    return entries


def validate(source: Path) -> tuple[dict[str, Any], dict[str, Any], dict[str, str]]:
    source = source.resolve()
    entries = checksum_entries(source)
    manifest = read_json(source / "deployment_manifest.json")
    metadata = read_json(source / str(manifest.get("metadata_file", "model_metadata.json")))
    if manifest.get("target") != TARGET or metadata.get("target") != TARGET:
        raise ValueError(f"Manifest and metadata must target {TARGET}.")
    if manifest.get("status") not in ALLOWED_STATUSES:
        raise ValueError(f"Bundle status is not active: {manifest.get('status')!r}.")
    version = str(manifest.get("model_version", ""))
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", version):
        raise ValueError("model_version is missing or unsafe.")
    preferred = str(manifest.get("preferred_model_file", ""))
    runtime_file = str(manifest.get("runtime_file", ""))
    if Path(preferred).suffix.lower() != ".cbm":
        raise ValueError("preferred_model_file must be the native CatBoost .cbm artifact.")
    for relative in (preferred, runtime_file, str(manifest.get("metadata_file", ""))):
        path = trusted_relative(source, relative)
        if not path.is_file() or path.name not in entries:
            raise ValueError(f"Required checksummed bundle file is missing: {relative}")

    manifest_hashes = manifest.get("sha256")
    if not isinstance(manifest_hashes, dict):
        raise ValueError("Manifest has no sha256 object.")
    for relative, expected in manifest_hashes.items():
        if entries.get(relative) != str(expected).lower():
            raise ValueError(f"Manifest checksum differs from checksums.sha256 for {relative}.")

    features = metadata.get("features")
    categorical = metadata.get("categorical_features")
    numeric = metadata.get("numeric_features")
    medians = metadata.get("numeric_medians")
    if not isinstance(features, list) or len(features) != 60 or not all(isinstance(v, str) for v in features):
        raise ValueError("Home Corners 2026 metadata must define exactly 60 ordered features.")
    if len(features) != len(set(features)):
        raise ValueError("model_metadata.json contains duplicate features.")
    leaked = [name for name in features if name.lower().startswith("target")]
    if leaked:
        raise ValueError("Target leakage detected: " + ", ".join(leaked))
    if not isinstance(categorical, list) or not isinstance(numeric, list):
        raise ValueError("Metadata feature type lists are missing.")
    typed = categorical + numeric
    if len(typed) != len(set(typed)) or set(typed) != set(features):
        raise ValueError("Categorical and numeric features do not partition the schema exactly.")
    if not isinstance(medians, dict) or set(medians) != set(numeric):
        raise ValueError("numeric_medians must cover every numeric feature exactly.")
    return manifest, metadata, entries


def install(source: Path, models_root: Path, requested_version: str | None) -> Path:
    manifest, _metadata, entries = validate(source)
    version = str(manifest["model_version"])
    if requested_version is not None and requested_version != version:
        raise ValueError(f"Requested version '{requested_version}' differs from manifest '{version}'.")
    destination = models_root.resolve() / version / "corners" / "home"
    if destination.exists():
        raise ValueError(f"Destination already exists and is immutable: {destination}")
    destination.mkdir(parents=True)
    for relative in entries:
        origin = trusted_relative(source, relative)
        target = trusted_relative(destination, relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(origin, target)
    shutil.copy2(source / "checksums.sha256", destination / "checksums.sha256")
    validate(destination)
    return destination


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--models-root", default="models/football", type=Path)
    parser.add_argument("--version")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()
    try:
        source = args.source.expanduser().resolve()
        manifest, _metadata, entries = validate(source)
        if args.validate_only:
            print(
                f"Validated {manifest['model_version']} ({len(entries)} checksummed files, target {TARGET})."
            )
            return 0
        destination = install(source, args.models_root, args.version)
        print(f"Validated Home Corners 2026 bundle installed at {destination}")
        print(f"Set NEW_GENERATION_ML_ACTIVE_VERSION={manifest['model_version']} and restart the API.")
        return 0
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"Activation blocked: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
