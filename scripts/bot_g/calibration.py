from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import numpy as np
import pandas as pd
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import log_loss

from .config import CalibrationConfig
from .modeling import sigmoid


@dataclass(frozen=True)
class CalibrationProfile:
    family: str
    market_type: str
    selection: str
    bookmaker: str
    method: str
    intercept: float
    slope: float
    beta_a: float
    beta_b: float
    effective_sample_size: float
    sample_size: int
    evidence_available_through_utc: pd.Timestamp

    def calibrate(self, probability: np.ndarray, clip: float = 1e-6) -> np.ndarray:
        value = np.clip(np.asarray(probability, dtype=float), clip, 1.0 - clip)
        if self.method == "Identity":
            return value
        if self.method == "Platt":
            logit = np.log(value / (1.0 - value))
            return sigmoid(self.intercept + self.slope * logit)
        if self.method == "Beta":
            return sigmoid(
                self.intercept
                + self.beta_a * np.log(value)
                + self.beta_b * (-np.log1p(-value))
            )
        raise ValueError(f"Unknown calibration method: {self.method}")

    def to_artifact(self) -> dict[str, Any]:
        return {
            "family": self.family,
            "marketType": self.market_type,
            "selection": self.selection,
            "bookmaker": self.bookmaker,
            "method": self.method,
            "intercept": self.intercept,
            "slope": self.slope,
            "betaA": self.beta_a,
            "betaB": self.beta_b,
            "effectiveSampleSize": self.effective_sample_size,
            "sampleSize": self.sample_size,
            "evidenceAvailableThroughUtc": self.evidence_available_through_utc.isoformat(),
        }


def _effective_size(y: np.ndarray) -> float:
    if not len(y):
        return 0.0
    positive = float(np.sum(y == 1))
    negative = float(np.sum(y == 0))
    return 4.0 * positive * negative / max(positive + negative, 1.0)


def _fit_profile(
    probability: np.ndarray,
    y: np.ndarray,
    method: str,
    scope: tuple[str, str, str],
    minimum_rows: int,
    clip: float,
    evidence_available_through_utc: pd.Timestamp,
) -> CalibrationProfile | None:
    probability = np.clip(np.asarray(probability, dtype=float), clip, 1.0 - clip)
    y = np.asarray(y, dtype=int)
    effective = _effective_size(y)
    if len(y) < minimum_rows or len(np.unique(y)) < 2 or effective < minimum_rows / 2:
        return None
    if method == "Platt":
        design = np.log(probability / (1.0 - probability)).reshape(-1, 1)
    elif method == "Beta":
        design = np.column_stack([np.log(probability), -np.log1p(-probability)])
    else:
        raise ValueError(f"Unsupported calibration method: {method}")
    model = LogisticRegression(C=1e6, max_iter=3_000, random_state=20260819)
    model.fit(design, y)
    coefficient = model.coef_[0]
    if method == "Platt" and coefficient[0] <= 0:
        return None
    if method == "Beta" and (coefficient[0] < 0 or coefficient[1] < 0):
        return None
    market_type, selection, bookmaker = scope
    return CalibrationProfile(
        "GOALS",
        market_type,
        selection,
        bookmaker,
        method,
        float(model.intercept_[0]),
        float(coefficient[0]) if method == "Platt" else 1.0,
        float(coefficient[0]) if method == "Beta" else 0.0,
        float(coefficient[1]) if method == "Beta" else 0.0,
        effective,
        int(len(y)),
        evidence_available_through_utc,
    )


def fit_hierarchical_calibration(
    rows: pd.DataFrame,
    probability: np.ndarray,
    y: np.ndarray,
    config: CalibrationConfig,
) -> tuple[list[CalibrationProfile], dict[str, Any]]:
    probability = np.asarray(probability, dtype=float)
    y = np.asarray(y, dtype=int)
    candidates: dict[str, CalibrationProfile] = {}
    scores: dict[str, float | None] = {}
    for method in ("Platt", "Beta"):
        profile = _fit_profile(
            probability,
            y,
            method,
            ("*", "*", "*"),
            config.minimum_rows,
            config.clip,
            rows["OutcomeAvailableUtc"].max(),
        )
        if profile is None:
            scores[method] = None
        else:
            candidates[method] = profile
            scores[method] = float(
                log_loss(y, profile.calibrate(probability, config.clip), labels=[0, 1])
            )
    if candidates:
        method = min(candidates, key=lambda key: float(scores[key]))
        global_profile = candidates[method]
    else:
        method = "Identity"
        global_profile = CalibrationProfile(
            "GOALS", "*", "*", "*", "Identity", 0.0, 1.0, 0.0, 0.0,
            _effective_size(y), int(len(y)), rows["OutcomeAvailableUtc"].max(),
        )
    profiles = [global_profile]

    scopes: list[tuple[str, str, str, np.ndarray]] = []
    for market in sorted(rows["MarketType"].unique()):
        market_mask = rows["MarketType"].eq(market).to_numpy(dtype=bool)
        scopes.append((market, "*", "*", market_mask))
        for selection in sorted(rows.loc[market_mask, "Selection"].unique()):
            side_mask = market_mask & rows["Selection"].eq(selection).to_numpy(dtype=bool)
            scopes.append((market, selection, "*", side_mask))
            for bookmaker in sorted(rows.loc[side_mask, "Bookmaker"].unique()):
                exact = side_mask & rows["Bookmaker"].eq(bookmaker).to_numpy(dtype=bool)
                scopes.append((market, selection, bookmaker, exact))
    if method != "Identity":
        for market, selection, bookmaker, mask in scopes:
            profile = _fit_profile(
                probability[mask], y[mask], method, (market, selection, bookmaker),
                config.minimum_rows, config.clip, rows.loc[mask, "OutcomeAvailableUtc"].max(),
            )
            if profile is not None:
                profiles.append(profile)
    return profiles, {"selectedMethod": method, "globalCandidateLogLoss": scores}


def _profile_map(profiles: list[CalibrationProfile]) -> dict[tuple[str, str, str], CalibrationProfile]:
    return {(p.market_type, p.selection, p.bookmaker): p for p in profiles}


def calibrate_hierarchical(
    rows: pd.DataFrame,
    probability: np.ndarray,
    profiles: list[CalibrationProfile],
    config: CalibrationConfig,
) -> tuple[np.ndarray, np.ndarray]:
    raw = np.asarray(probability, dtype=float)
    mapping = _profile_map(profiles)
    calibrated = raw.copy()
    reliability = np.zeros(len(rows), dtype=float)
    levels = (
        (lambda row: ("*", "*", "*"), config.global_prior_strength),
        (lambda row: (row.MarketType, "*", "*"), config.market_prior_strength),
        (lambda row: (row.MarketType, row.Selection, "*"), config.side_prior_strength),
        (lambda row: (row.MarketType, row.Selection, row.Bookmaker), config.bookmaker_prior_strength),
    )
    for index, row in enumerate(rows.itertuples(index=False)):
        raw_value = raw[index]
        value = raw_value
        residual_unreliability = 1.0
        for key_builder, strength in levels:
            profile = mapping.get(key_builder(row))
            if profile is None:
                continue
            weight = profile.effective_sample_size / (profile.effective_sample_size + strength)
            # Every hierarchical profile is fitted against the same OOF candidate
            # probability. This mirrors BotGHierarchicalCalibrationService: blend
            # sequentially, but never recursively calibrate a calibrated value.
            target = float(profile.calibrate(np.array([raw_value]), config.clip)[0])
            value = (1.0 - weight) * value + weight * target
            residual_unreliability *= 1.0 - weight
        calibrated[index] = value
        reliability[index] = 1.0 - residual_unreliability
    return np.clip(calibrated, config.clip, 1.0 - config.clip), reliability


def calibration_effective_sample_size(
    rows: pd.DataFrame,
    profiles: list[CalibrationProfile],
) -> np.ndarray:
    """Return the most-specific applied profile's effective N, as the .NET runtime does."""
    mapping = _profile_map(profiles)
    values = np.zeros(len(rows), dtype=float)
    for index, row in enumerate(rows.itertuples(index=False)):
        keys = (
            ("*", "*", "*"),
            (row.MarketType, "*", "*"),
            (row.MarketType, row.Selection, "*"),
            (row.MarketType, row.Selection, row.Bookmaker),
        )
        selected = [mapping[key] for key in keys if key in mapping]
        if selected:
            values[index] = selected[-1].effective_sample_size
    return values
