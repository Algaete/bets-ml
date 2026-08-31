from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pandas as pd


@dataclass(frozen=True)
class TemporalFold:
    number: int
    train: np.ndarray
    validation: np.ndarray
    validation_start: pd.Timestamp
    validation_end: pd.Timestamp
    knowledge_cutoff: pd.Timestamp


@dataclass(frozen=True)
class FinalSplit:
    development: np.ndarray
    final_test: np.ndarray
    final_start: pd.Timestamp


def _fixture_clock(frame: pd.DataFrame, indices: np.ndarray | None = None) -> pd.DataFrame:
    scoped = frame if indices is None else frame.iloc[indices]
    clocks = (
        scoped.groupby("FixtureId", sort=False)
        .agg(
            prediction_start=("PredictionTimestampUtc", "min"),
            prediction_end=("PredictionTimestampUtc", "max"),
            outcome_available=("OutcomeAvailableUtc", "max"),
        )
        .reset_index()
        .sort_values(["prediction_start", "FixtureId"], kind="stable")
        .reset_index(drop=True)
    )
    return clocks


def final_holdout(
    frame: pd.DataFrame,
    fraction: float,
    explicit_start: str | pd.Timestamp | None = None,
) -> FinalSplit:
    if not 0 < fraction < 0.5:
        raise ValueError("final_test_fraction must be between 0 and 0.5.")
    clocks = _fixture_clock(frame)
    if len(clocks) < 5:
        raise ValueError("At least five fixtures are required for a temporal final test.")
    if explicit_start is None:
        position = min(len(clocks) - 1, max(1, int(len(clocks) * (1.0 - fraction))))
        final_start = clocks.iloc[position]["prediction_start"]
    else:
        final_start = pd.to_datetime(explicit_start, utc=True, errors="raise")

    development_fixtures = set(clocks.loc[clocks["prediction_end"] < final_start, "FixtureId"])
    final_fixtures = set(clocks.loc[clocks["prediction_start"] >= final_start, "FixtureId"])
    crossing = set(clocks["FixtureId"]) - development_fixtures - final_fixtures
    if crossing:
        raise ValueError(
            "Fixture snapshots cross the final-test boundary; use one immutable prediction timestamp per fixture."
        )
    development = np.flatnonzero(frame["FixtureId"].isin(development_fixtures).to_numpy())
    final_test = np.flatnonzero(frame["FixtureId"].isin(final_fixtures).to_numpy())
    if not len(development) or not len(final_test):
        raise ValueError("Temporal final split produced an empty block.")
    if set(frame.iloc[development]["FixtureId"]) & set(frame.iloc[final_test]["FixtureId"]):
        raise RuntimeError("Fixture leakage detected in final split.")
    return FinalSplit(development, final_test, final_start)


def expanding_folds(
    frame: pd.DataFrame,
    indices: np.ndarray,
    fold_count: int,
    minimum_train_fixtures: int,
    minimum_validation_fixtures: int,
    embargo_hours: float,
    outcome_lag_hours: float,
) -> list[TemporalFold]:
    clocks = _fixture_clock(frame, indices)
    fixture_count = len(clocks)
    minimum_required = minimum_train_fixtures + minimum_validation_fixtures
    if fixture_count < minimum_required:
        raise ValueError(
            f"Need at least {minimum_required} development fixtures; found {fixture_count}."
        )
    available = fixture_count - minimum_train_fixtures
    validation_size = max(minimum_validation_fixtures, available // max(1, fold_count))
    starts = list(range(minimum_train_fixtures, fixture_count, validation_size))[-fold_count:]
    folds: list[TemporalFold] = []
    embargo = pd.Timedelta(hours=embargo_hours)
    outcome_lag = pd.Timedelta(hours=outcome_lag_hours)
    for number, start in enumerate(starts, 1):
        stop = min(fixture_count, start + validation_size)
        validation_clock = clocks.iloc[start:stop]
        if len(validation_clock) < minimum_validation_fixtures:
            continue
        validation_start = validation_clock["prediction_start"].min()
        knowledge_cutoff = validation_start - embargo - outcome_lag
        train_clock = clocks.iloc[:start]
        train_clock = train_clock[
            (train_clock["prediction_end"] < validation_start - embargo)
            & (train_clock["outcome_available"] < knowledge_cutoff)
        ]
        if len(train_clock) < minimum_train_fixtures:
            continue
        train_fixtures = set(train_clock["FixtureId"])
        validation_fixtures = set(validation_clock["FixtureId"])
        train = np.flatnonzero(frame["FixtureId"].isin(train_fixtures).to_numpy())
        validation = np.flatnonzero(frame["FixtureId"].isin(validation_fixtures).to_numpy())
        train = np.intersect1d(train, indices, assume_unique=False)
        validation = np.intersect1d(validation, indices, assume_unique=False)
        if train_fixtures & validation_fixtures:
            raise RuntimeError("Fixture leakage detected in walk-forward fold.")
        if frame.iloc[train]["OutcomeAvailableUtc"].max() >= knowledge_cutoff:
            raise RuntimeError("Outcome lag leakage detected in walk-forward fold.")
        folds.append(
            TemporalFold(
                number,
                train,
                validation,
                validation_start,
                validation_clock["prediction_end"].max(),
                knowledge_cutoff,
            )
        )
    if len(folds) < 2:
        raise ValueError("Could not build at least two leakage-safe expanding-window folds.")
    return folds


def assert_oof_fixture_integrity(frame: pd.DataFrame, folds: list[TemporalFold]) -> None:
    seen: set[str] = set()
    for fold in folds:
        train = set(frame.iloc[fold.train]["FixtureId"])
        validation = set(frame.iloc[fold.validation]["FixtureId"])
        if train & validation:
            raise AssertionError("A fixture appears in train and validation.")
        if seen & validation:
            raise AssertionError("A fixture appears in more than one OOF validation fold.")
        seen |= validation
