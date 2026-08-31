from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import numpy as np
import pandas as pd

from .settlement import SETTLEMENT_STATES, distribution_from_states, normalize_distribution


def line_kind(line: float) -> str:
    fraction = round(float(line) - np.floor(float(line)), 2)
    if fraction in {0.25, 0.75}:
        return "quarter"
    if fraction == 0.0:
        return "integer"
    return "half"


@dataclass(frozen=True)
class SettlementProfile:
    family: str
    market_type: str
    selection: str
    bookmaker: str
    line: float
    probabilities: dict[str, float]
    sample_size: int
    effective_sample_size: float
    ordinal_evidence_available: bool
    evidence_available_through_utc: pd.Timestamp

    def to_artifact(self) -> dict[str, Any]:
        distribution_names = {
            "Win": "win",
            "HalfWin": "halfWin",
            "Push": "push",
            "HalfLoss": "halfLoss",
            "Loss": "loss",
        }
        return {
            "key": {
                "family": self.family,
                "marketType": None if self.market_type == "*" else self.market_type,
                "selection": None if self.selection == "*" else self.selection,
                "bookmaker": None if self.bookmaker == "*" else self.bookmaker,
            },
            "line": self.line,
            "distribution": {
                distribution_names[state]: self.probabilities[state]
                for state in SETTLEMENT_STATES
            },
            "sampleSize": self.sample_size,
            "effectiveSampleSize": self.effective_sample_size,
            "evidenceAvailableThroughUtc": self.evidence_available_through_utc.isoformat(),
        }


def _profile(
    rows: pd.DataFrame,
    scope: tuple[str, str, str, float],
    prior: dict[str, float],
    prior_strength: float,
    minimum_effective: float,
) -> SettlementProfile:
    market, selection, bookmaker, line = scope
    counts = rows["SettlementState"].value_counts().to_dict()
    denominator = len(rows) + prior_strength
    probabilities = {
        state: (float(counts.get(state, 0)) + prior_strength * prior[state]) / denominator
        for state in SETTLEMENT_STATES
    }
    effective = float(rows["FixtureId"].nunique())
    return SettlementProfile(
        "GOALS", market, selection, bookmaker, line, normalize_distribution(probabilities),
        int(len(rows)), effective, effective >= minimum_effective,
        rows["OutcomeAvailableUtc"].max(),
    )


def fit_settlement_profiles(
    rows: pd.DataFrame,
    minimum_effective: float,
    prior_strength: float = 30.0,
) -> list[SettlementProfile]:
    scoped = rows.copy()
    scoped["_ProfileLine"] = scoped["Line"].round(4)
    profiles: list[SettlementProfile] = []
    global_by_line: dict[float, dict[str, float]] = {}
    for line, subset in scoped.groupby("_ProfileLine", sort=True):
        line = float(line)
        raw = distribution_from_states(subset["SettlementState"], alpha=0.5)
        global_by_line[line] = raw
        effective = float(subset["FixtureId"].nunique())
        profiles.append(
            SettlementProfile(
                "GOALS", "*", "*", "*", line, raw, int(len(subset)), effective,
                effective >= minimum_effective,
                subset["OutcomeAvailableUtc"].max(),
            )
        )
    grouping = [
        (["MarketType", "_ProfileLine"], lambda key: (key[0], "*", "*", float(key[1]))),
        (
            ["MarketType", "Selection", "_ProfileLine"],
            lambda key: (key[0], key[1], "*", float(key[2])),
        ),
        (
            ["MarketType", "Selection", "Bookmaker", "_ProfileLine"],
            lambda key: (key[0], key[1], key[2], float(key[3])),
        ),
    ]
    for columns, key_builder in grouping:
        for key, subset in scoped.groupby(columns, sort=True):
            if not isinstance(key, tuple):
                key = (key,)
            scope = key_builder(key)
            profiles.append(
                _profile(
                    subset, scope, global_by_line[scope[3]], prior_strength, minimum_effective
                )
            )
    return profiles


def select_settlement_profile(
    row: Any,
    profiles: list[SettlementProfile],
    minimum_effective_sample_size: float = 0.0,
) -> SettlementProfile | None:
    line = round(float(row.Line), 4)
    lookup = {
        (profile.market_type, profile.selection, profile.bookmaker, profile.line): profile
        for profile in profiles
    }
    keys = (
        (str(row.MarketType), str(row.Selection), str(row.Bookmaker), line),
        (str(row.MarketType), str(row.Selection), "*", line),
        (str(row.MarketType), "*", "*", line),
        ("*", "*", "*", line),
    )
    for key in keys:
        if key in lookup and lookup[key].effective_sample_size >= minimum_effective_sample_size:
            return lookup[key]
    return None
