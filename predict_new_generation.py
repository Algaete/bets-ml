#!/usr/bin/env python3
"""Persistent inference bridge for trusted 2026 football count-model bundles."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


SUPPORTED_TARGETS = {
    "TargetHomeCorners",
    "TargetAwayCorners",
    "TargetTotalCorners",
    "TargetHomeShots",
    "TargetAwayShots",
    "TargetTotalShots",
    "TargetHomeShotsOnGoal",
    "TargetAwayShotsOnGoal",
    "TargetTotalShotsOnGoal",
    "TargetHomeGoals",
    "TargetAwayGoals",
    "TargetTotalGoals",
}
ALLOWED_STATUSES = {"active", "active_candidate"}


class ModelPackageError(RuntimeError):
    pass


@dataclass(frozen=True)
class LoadedPackage:
    manifest_path: Path
    bundle_dir: Path
    manifest: dict[str, Any]
    metadata: dict[str, Any]
    runtime_path: Path
    model_path: Path

    @property
    def target(self) -> str:
        return str(self.manifest["target"])


def _read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except FileNotFoundError as exc:
        raise ModelPackageError(f"Required model package file is missing: {path.name}") from exc
    except json.JSONDecodeError as exc:
        raise ModelPackageError(f"Invalid JSON in model package file {path.name}: {exc}") from exc
    if not isinstance(value, dict):
        raise ModelPackageError(f"Model package file {path.name} must contain a JSON object.")
    return value


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _trusted_relative(bundle_dir: Path, relative: str, label: str) -> Path:
    if not relative or Path(relative).is_absolute():
        raise ModelPackageError(f"{label} must be a non-empty relative path.")
    resolved_root = bundle_dir.resolve()
    resolved = (resolved_root / relative).resolve()
    if resolved != resolved_root and resolved_root not in resolved.parents:
        raise ModelPackageError(f"{label} escapes the trusted model package directory.")
    return resolved


def _verify_checksum_file(bundle_dir: Path) -> None:
    checksum_path = bundle_dir / "checksums.sha256"
    try:
        lines = checksum_path.read_text(encoding="utf-8").splitlines()
    except FileNotFoundError as exc:
        raise ModelPackageError("Required model package file is missing: checksums.sha256") from exc
    if not lines:
        raise ModelPackageError("checksums.sha256 is empty.")
    for line in lines:
        parts = line.strip().split(maxsplit=1)
        if len(parts) != 2:
            raise ModelPackageError("checksums.sha256 contains an invalid line.")
        expected, relative = parts
        path = _trusted_relative(bundle_dir, relative.strip(), "checksum path")
        if not path.is_file():
            raise ModelPackageError(f"Checksummed bundle file is missing: {path.name}")
        actual = _sha256(path)
        if actual != expected.lower():
            raise ModelPackageError(
                f"Checksum mismatch for {path.name}: expected {expected.lower()}, got {actual}."
            )


def load_package(manifest_path: Path) -> LoadedPackage:
    manifest_path = manifest_path.expanduser().resolve()
    bundle_dir = manifest_path.parent
    _verify_checksum_file(bundle_dir)
    manifest = _read_json(manifest_path)
    if manifest.get("status") not in ALLOWED_STATUSES:
        raise ModelPackageError(f"Deployment bundle status is not active: {manifest.get('status')!r}.")
    target = str(manifest.get("target", ""))
    if target not in SUPPORTED_TARGETS:
        raise ModelPackageError(f"Unsupported count-model target: {target!r}.")

    model_path = _trusted_relative(
        bundle_dir, str(manifest.get("preferred_model_file", "")), "preferred_model_file"
    )
    metadata_path = _trusted_relative(
        bundle_dir, str(manifest.get("metadata_file", "")), "metadata_file"
    )
    runtime_path = _trusted_relative(
        bundle_dir, str(manifest.get("runtime_file", "")), "runtime_file"
    )
    if model_path.suffix.lower() != ".cbm":
        raise ModelPackageError("The preferred inference artifact must be the native CatBoost .cbm file.")
    for path in (model_path, metadata_path, runtime_path):
        if not path.is_file():
            raise ModelPackageError(f"Production bundle file is missing: {path.name}")

    expected_hashes = manifest.get("sha256")
    if not isinstance(expected_hashes, dict):
        raise ModelPackageError("deployment_manifest.json must contain sha256 checksums.")
    for path in (model_path, metadata_path, runtime_path):
        expected = str(expected_hashes.get(path.name, "")).lower()
        actual = _sha256(path)
        if not expected or actual != expected:
            raise ModelPackageError(
                f"Checksum mismatch for {path.name}: expected {expected or '[missing]'}, got {actual}."
            )

    metadata = _read_json(metadata_path)
    if metadata.get("target") != target:
        raise ModelPackageError(
            f"Metadata target {metadata.get('target')!r} differs from manifest target {target!r}."
        )
    features = metadata.get("features")
    categorical = metadata.get("categorical_features")
    numeric = metadata.get("numeric_features")
    medians = metadata.get("numeric_medians")
    if not isinstance(features, list) or not features or not all(isinstance(v, str) for v in features):
        raise ModelPackageError("model_metadata.json must define a non-empty ordered features list.")
    if len(features) != len(set(features)):
        raise ModelPackageError("model_metadata.json contains duplicate feature names.")
    leaked = [name for name in features if name.lower().startswith("target")]
    if leaked:
        raise ModelPackageError(f"Target columns are forbidden at inference: {', '.join(leaked)}")
    if not isinstance(categorical, list) or not isinstance(numeric, list):
        raise ModelPackageError("Metadata must define categorical_features and numeric_features lists.")
    typed = categorical + numeric
    if len(typed) != len(set(typed)) or set(typed) != set(features):
        raise ModelPackageError("Metadata feature types do not partition the ordered schema exactly.")
    if not isinstance(medians, dict) or set(medians) != set(numeric):
        raise ModelPackageError("Metadata numeric_medians must cover every numeric feature exactly.")

    return LoadedPackage(
        manifest_path, bundle_dir, manifest, metadata, runtime_path, model_path
    )


def package_info(package: LoadedPackage, *, loaded: bool = False) -> dict[str, Any]:
    features = list(package.metadata["features"])
    return {
        "status": "healthy" if loaded else "ready",
        "ready": True,
        "loaded": loaded,
        "target": package.target,
        "modelVersion": package.manifest.get("model_version"),
        "trainedThrough": package.manifest.get("trained_through"),
        "featureSet": package.metadata.get("feature_set"),
        "algorithm": package.metadata.get("family"),
        "trainedAt": package.manifest.get("trained_at"),
        "datasetSha256": package.manifest.get("dataset_sha256"),
        "features": features,
        "featureCount": len(features),
        "categoricalFeatures": package.metadata.get("categorical_features", []),
        "numericFeatures": package.metadata.get("numeric_features", []),
        "warnings": [],
    }


def catalog_info(packages: Iterable[LoadedPackage], *, loaded: bool) -> dict[str, Any]:
    models = [package_info(package, loaded=loaded) for package in packages]
    return {
        "status": "healthy" if loaded else "ready",
        "ready": bool(models),
        "available": bool(models),
        "loaded": loaded,
        "totalModels": len(models),
        "readyModels": len(models),
        "models": models,
        "warnings": [],
    }


def _load_model(package: LoadedPackage) -> Any:
    module_name = (
        "football_count_bundle_"
        + hashlib.sha256(str(package.runtime_path).encode()).hexdigest()[:12]
    )
    spec = importlib.util.spec_from_file_location(module_name, package.runtime_path)
    if spec is None or spec.loader is None:
        raise ModelPackageError("The official bundle runtime could not be imported.")
    module = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(module)
        model_class = getattr(module, "FootballCountDeploymentModel", None)
        if model_class is None:
            model_class = getattr(module, "HomeCornersDeploymentModel")
        model = model_class(package.bundle_dir, verify_checksums=True)
    except Exception as exc:
        raise ModelPackageError(
            f"Official runtime for {package.target} could not load: {type(exc).__name__}: {exc}"
        ) from exc
    if list(getattr(model, "features", [])) != list(package.metadata["features"]):
        raise ModelPackageError("Loaded CBM runtime feature order differs from model_metadata.json.")
    return model


def _prepare_payload(package: LoadedPackage, supplied: dict[str, Any]) -> dict[str, Any]:
    if not isinstance(supplied, dict):
        raise ModelPackageError("Prediction features must be a JSON object.")
    leaked = [name for name in supplied if name.lower().startswith("target")]
    if leaked:
        raise ModelPackageError(f"Target columns are forbidden at inference: {', '.join(leaked)}")
    ordered = list(package.metadata["features"])
    missing = [name for name in ordered if name not in supplied]
    unexpected = [name for name in supplied if name not in ordered]
    if missing:
        raise ModelPackageError(f"Required features could not be built: {', '.join(missing)}")
    if unexpected:
        raise ModelPackageError(f"Unexpected inference features: {', '.join(unexpected)}")
    return {name: supplied[name] for name in ordered}


def predict_with_model(package: LoadedPackage, model: Any, supplied: dict[str, Any]) -> dict[str, Any]:
    ordered = _prepare_payload(package, supplied)
    try:
        result = model.predict(ordered)
        row = result.iloc[0]
        raw = float(row["prediction_raw"])
        clipped = float(row["prediction_clipped"])
        rounded = int(row["prediction_rounded"])
    except ModelPackageError:
        raise
    except Exception as exc:
        raise ModelPackageError(f"Official CBM inference failed: {type(exc).__name__}: {exc}") from exc
    if not math.isfinite(raw) or not math.isfinite(clipped):
        raise ModelPackageError("Model returned a non-finite prediction.")
    expected_clipped = max(0.0, raw)
    if abs(clipped - expected_clipped) > 1e-12:
        raise ModelPackageError("Official runtime returned an invalid clipped prediction.")
    return {
        "target": package.target,
        "predictionRaw": raw,
        "predictionClipped": clipped,
        "predictionRounded": rounded,
        "modelVersion": package.manifest.get("model_version"),
        "trainedThrough": package.manifest.get("trained_through"),
        "featureSet": package.metadata.get("feature_set"),
        "warnings": [],
    }


def _normalize_manifest_paths(manifest_paths: Path | Iterable[Path]) -> list[Path]:
    if isinstance(manifest_paths, Path):
        return [manifest_paths]
    return list(manifest_paths)


def _load_registry(manifest_paths: Path | Iterable[Path]) -> dict[str, LoadedPackage]:
    packages: dict[str, LoadedPackage] = {}
    for manifest_path in _normalize_manifest_paths(manifest_paths):
        package = load_package(manifest_path)
        if package.target in packages:
            raise ModelPackageError(f"Duplicate active model target: {package.target}.")
        packages[package.target] = package
    if not packages:
        raise ModelPackageError("At least one model manifest is required.")
    return packages


def _select_target(packages: dict[str, LoadedPackage], target: Any) -> str:
    if target is None and len(packages) == 1:
        return next(iter(packages))
    if not isinstance(target, str) or target not in packages:
        raise ModelPackageError(f"Requested target is not loaded: {target!r}.")
    return target


def run(
    action: str,
    manifest_paths: Path | Iterable[Path],
    payload: dict[str, Any] | None = None,
) -> dict[str, Any]:
    packages = _load_registry(manifest_paths)
    if action == "info":
        if len(packages) == 1:
            return package_info(next(iter(packages.values())))
        return catalog_info(packages.values(), loaded=False)
    models = {target: _load_model(package) for target, package in packages.items()}
    if action == "health":
        if len(packages) == 1:
            return package_info(next(iter(packages.values())), loaded=True)
        return catalog_info(packages.values(), loaded=True)
    if action == "predict":
        if payload is None:
            raise ModelPackageError("Prediction payload is required.")
        target = _select_target(packages, None)
        return predict_with_model(packages[target], models[target], payload)
    if action == "predict_many":
        if not isinstance(payload, dict):
            raise ModelPackageError("Multi-model prediction payload must be a JSON object.")
        predictions = []
        for target, features in payload.items():
            selected = _select_target(packages, target)
            predictions.append(predict_with_model(packages[selected], models[selected], features))
        return {"predictions": predictions}
    raise ModelPackageError(f"Unsupported action: {action}")


def serve(manifest_paths: Path | Iterable[Path]) -> int:
    try:
        packages = _load_registry(manifest_paths)
        models = {target: _load_model(package) for target, package in packages.items()}
        ready = catalog_info(packages.values(), loaded=True)
        print(json.dumps({"event": "ready", "result": ready}, ensure_ascii=False), flush=True)
    except ModelPackageError as exc:
        print(json.dumps({"error": str(exc)}, ensure_ascii=False), file=sys.stderr, flush=True)
        return 2

    for line in sys.stdin:
        if not line.strip():
            continue
        request_id: Any = None
        try:
            request = json.loads(line)
            if not isinstance(request, dict):
                raise ModelPackageError("Worker request must be a JSON object.")
            request_id = request.get("id")
            action = request.get("action")
            if action == "health":
                result = catalog_info(packages.values(), loaded=True)
            elif action == "predict":
                target = _select_target(packages, request.get("target"))
                result = predict_with_model(packages[target], models[target], request.get("payload"))
            elif action == "predict_many":
                payloads = request.get("payload")
                if not isinstance(payloads, dict) or not payloads:
                    raise ModelPackageError("Multi-model prediction payload must contain target payloads.")
                predictions = []
                for target, features in payloads.items():
                    selected = _select_target(packages, target)
                    predictions.append(
                        predict_with_model(packages[selected], models[selected], features)
                    )
                result = {"predictions": predictions}
            else:
                raise ModelPackageError(f"Unsupported worker action: {action!r}")
            response = {"id": request_id, "ok": True, "result": result}
        except (ModelPackageError, json.JSONDecodeError) as exc:
            response = {"id": request_id, "ok": False, "error": str(exc)}
        print(json.dumps(response, ensure_ascii=False), flush=True)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--action", choices=("info", "health", "predict", "predict_many", "serve"), required=True
    )
    parser.add_argument("--manifest", action="append", required=True)
    args = parser.parse_args()
    manifests = [Path(value) for value in args.manifest]
    if args.action == "serve":
        return serve(manifests)
    try:
        payload = json.load(sys.stdin) if args.action in {"predict", "predict_many"} else None
        print(json.dumps(run(args.action, manifests, payload), ensure_ascii=False))
        return 0
    except (ModelPackageError, json.JSONDecodeError) as exc:
        print(json.dumps({"error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
