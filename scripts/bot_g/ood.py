from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import numpy as np


@dataclass(frozen=True)
class OodFeatureProfile:
    name: str
    median: float
    mad: float
    p01: float
    p99: float
    minimum: float
    maximum: float
    sample_size: int

    def to_artifact(self) -> dict[str, float | str]:
        return {
            "name": self.name,
            "median": self.median,
            "mad": self.mad,
            "p01": self.p01,
            "p99": self.p99,
            "minimum": self.minimum,
            "maximum": self.maximum,
            "sampleSize": self.sample_size,
        }


def fit_ood_profiles(x: np.ndarray, feature_names: list[str] | tuple[str, ...]) -> list[OodFeatureProfile]:
    x = np.asarray(x, dtype=float)
    profiles: list[OodFeatureProfile] = []
    for index, name in enumerate(feature_names):
        if "=" in name:  # unseen categories are handled by the feature schema, not robust z scores.
            continue
        values = x[:, index]
        median = float(np.median(values))
        mad = float(np.median(np.abs(values - median)))
        profiles.append(
            OodFeatureProfile(
                name,
                median,
                mad,
                float(np.quantile(values, 0.01)),
                float(np.quantile(values, 0.99)),
                float(np.min(values)),
                float(np.max(values)),
                int(len(values)),
            )
        )
    return profiles


def ood_score(
    x: np.ndarray,
    feature_names: list[str] | tuple[str, ...],
    profiles: list[OodFeatureProfile],
    robust_z_score_threshold: float = 3.5,
    severe_robust_z_score: float = 8.0,
    minimum_reference_sample_size: int = 0,
) -> np.ndarray:
    lookup = {name: index for index, name in enumerate(feature_names)}
    component: list[np.ndarray] = []
    for profile in profiles:
        if profile.sample_size < minimum_reference_sample_size:
            continue
        values = x[:, lookup[profile.name]]
        percentile_scale = max((profile.p99 - profile.p01) / 4.652, 1e-9)
        # Mirror BotGRobustOodService: percentile spread is a fallback only
        # when MAD collapses, not a competing (larger) scale.
        scale = 1.4826 * profile.mad if profile.mad > 1e-9 else percentile_scale
        robust = np.abs(values - profile.median) / scale
        robust_score = np.clip(
            (robust - robust_z_score_threshold)
            / max(severe_robust_z_score - robust_z_score_threshold, 1e-9),
            0.0,
            1.0,
        )
        outside = (values < profile.p01) | (values > profile.p99)
        envelope_score = np.where(
            outside,
            np.maximum(0.25, robust_score),
            0.0,
        )
        component.append(np.maximum(robust_score, envelope_score))
    if not component:
        # Runtime treats absent eligible reference evidence as unavailable/OOD=1
        # so decisioning fails closed instead of manufacturing in-distribution status.
        return np.ones(len(x), dtype=float)
    matrix = np.column_stack(component)
    return np.clip(np.max(matrix, axis=1), 0.0, 1.0)


def artifact(profiles: list[OodFeatureProfile]) -> dict[str, Any]:
    return {
        "method": "robust-mad-percentile-v1",
        "scoreRange": [0.0, 1.0],
        "profiles": [profile.to_artifact() for profile in profiles],
    }
