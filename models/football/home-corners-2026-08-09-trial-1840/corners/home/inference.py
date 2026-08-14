"""Runtime autocontenido para inferencia con el bundle CatBoost de deployment."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from catboost import CatBoostRegressor


def sha256_file(path: Path, block_size: int = 1024 * 1024) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(block_size), b""):
            digest.update(block)
    return digest.hexdigest()


class HomeCornersDeploymentModel:
    """Carga un modelo nativo CatBoost y aplica su contrato de features."""

    def __init__(self, bundle_dir: str | Path, verify_checksums: bool = True) -> None:
        self.bundle_dir = Path(bundle_dir).expanduser().resolve()
        metadata_path = self.bundle_dir / "model_metadata.json"
        manifest_path = self.bundle_dir / "deployment_manifest.json"
        if not metadata_path.is_file():
            raise FileNotFoundError(metadata_path)
        if not manifest_path.is_file():
            raise FileNotFoundError(manifest_path)
        self.metadata: dict[str, Any] = json.loads(metadata_path.read_text(encoding="utf-8"))
        self.manifest: dict[str, Any] = json.loads(manifest_path.read_text(encoding="utf-8"))
        if self.metadata.get("target") != "TargetHomeCorners":
            raise ValueError("El bundle no corresponde a TargetHomeCorners.")
        self.features = list(self.metadata["features"])
        self.categorical = list(self.metadata["categorical_features"])
        self.numeric = list(self.metadata["numeric_features"])
        self.numeric_medians = {
            key: float(value) for key, value in self.metadata["numeric_medians"].items()
        }
        leaked = [feature for feature in self.features if feature.lower().startswith("target")]
        if leaked:
            raise ValueError(f"Contrato inválido: targets presentes en X: {leaked}")
        if verify_checksums:
            self._verify_checksums()
        model_path = self.bundle_dir / self.manifest["preferred_model_file"]
        self.model = CatBoostRegressor()
        self.model.load_model(str(model_path), format="cbm")

    def _verify_checksums(self) -> None:
        for filename, expected in self.manifest.get("sha256", {}).items():
            path = self.bundle_dir / filename
            if not path.is_file():
                raise FileNotFoundError(path)
            actual = sha256_file(path)
            if actual != expected:
                raise ValueError(f"Checksum inválido para {filename}: {actual} != {expected}")

    def _prepare(
        self, values: pd.DataFrame | list[dict[str, Any]] | dict[str, Any]
    ) -> pd.DataFrame:
        if isinstance(values, dict):
            frame = pd.DataFrame([values])
        elif isinstance(values, list):
            frame = pd.DataFrame(values)
        else:
            frame = values.copy()
        supplied_targets = [column for column in frame if column.lower().startswith("target")]
        if supplied_targets:
            raise ValueError(f"La inferencia rechaza columnas Target*: {supplied_targets}")
        missing = [feature for feature in self.features if feature not in frame]
        if missing:
            raise ValueError(f"Faltan features requeridas: {missing}")
        prepared = frame[self.features].copy()
        for column in self.categorical:
            prepared[column] = prepared[column].astype("string").fillna("Unknown").astype(str)
        for column in self.numeric:
            prepared[column] = pd.to_numeric(prepared[column], errors="coerce").fillna(
                self.numeric_medians[column]
            )
        return prepared

    def predict(self, values: pd.DataFrame | list[dict[str, Any]] | dict[str, Any]) -> pd.DataFrame:
        prepared = self._prepare(values)
        raw = np.asarray(self.model.predict(prepared), dtype=float)
        clipped = np.maximum(0.0, raw)
        return pd.DataFrame(
            {
                "prediction_raw": raw,
                "prediction_clipped": clipped,
                "prediction_rounded": np.rint(clipped).astype(int),
            }
        )
