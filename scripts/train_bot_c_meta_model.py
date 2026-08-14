#!/usr/bin/env python3
"""Train and export Bot C's temporal LogisticRegression meta-model.

The input contains every historical candidate (approved and rejected), its immutable
feature snapshot, and its final settlement. The final 20% by time is never used for
training, calibration or threshold selection.
"""

from __future__ import annotations

import argparse
import json
import math
import shutil
import tempfile
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from sklearn.calibration import calibration_curve
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import brier_score_loss, log_loss, roc_auc_score
from sklearn.model_selection import TimeSeriesSplit
from sklearn.preprocessing import StandardScaler


FEATURE_SCHEMA_VERSION = "bot-c-features-1.0.0"
NUMERIC_FEATURES = [
    "baseCalibratedProbability",
    "odds",
    "rawImpliedProbability",
    "marketNoVigProbability",
    "baseEdge",
    "baseExpectedValue",
    "basePredictedValue",
    "line",
    "baseLineMargin",
    "baseLineDistanceSigma",
    "contextExpectedValue",
    "contextLineMargin",
    "contextLineDistanceSigma",
    "combinedExactLineShrunkHitRate",
    "combinedMedian",
    "combinedStandardDeviation",
    "combinedIqr",
    "combinedMad",
    "trend",
    "contextAgreementScore",
    "dataQualityScore",
]
MARKETS = [
    "TotalCorners", "HomeTeamCorners", "AwayTeamCorners",
    "TotalGoals", "HomeTeamGoals", "AwayTeamGoals",
    "TotalShots", "HomeTeamShots", "AwayTeamShots",
    "TotalShotsOnGoal", "HomeTeamShotsOnGoal", "AwayTeamShotsOnGoal",
]
SIDES = ["Over", "Under"]


@dataclass(frozen=True)
class Dataset:
    rows: pd.DataFrame
    x: np.ndarray
    y: np.ndarray
    net_return: np.ndarray
    feature_names: list[str]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="CSV or JSONL exported from AutomatedBotPickEvaluations.")
    parser.add_argument("--output-dir", type=Path, default=Path("models/bot-c-meta"))
    parser.add_argument("--model-version", default=f"bot-c-meta-{datetime.now(timezone.utc):%Y%m%d-%H%M%S}")
    parser.add_argument("--activate", action="store_true", help="Atomically replace active.json after validation.")
    parser.add_argument("--minimum-rows", type=int, default=200)
    parser.add_argument("--self-test", action="store_true")
    return parser.parse_args()


def load_frame(path: Path) -> pd.DataFrame:
    if path.suffix.lower() in {".jsonl", ".ndjson"}:
        return pd.read_json(path, lines=True)
    if path.suffix.lower() == ".json":
        return pd.read_json(path)
    return pd.read_csv(path)


def get_value(row: pd.Series, *names: str) -> Any:
    normalized = {str(key).replace("_", "").lower(): value for key, value in row.items()}
    for name in names:
        value = normalized.get(name.replace("_", "").lower())
        if value is not None and not (isinstance(value, float) and math.isnan(value)):
            return value
    return None


def parse_snapshot(value: Any) -> dict[str, Any]:
    if isinstance(value, dict):
        return value
    if not isinstance(value, str) or not value.strip():
        raise ValueError("FeatureSnapshotJson is required.")
    parsed = json.loads(value)
    if not isinstance(parsed, dict):
        raise ValueError("FeatureSnapshotJson must be a JSON object.")
    return parsed


def nested(data: dict[str, Any], *path: str, default: float | None = None) -> float | None:
    current: Any = data
    for part in path:
        if not isinstance(current, dict) or part not in current:
            return default
        current = current[part]
    try:
        value = float(current)
        return value if math.isfinite(value) else default
    except (TypeError, ValueError):
        return default


def settle(side: str, line: float, actual: float, odds: float) -> tuple[str, float]:
    # Split Asian quarter lines into their two adjacent half-stakes.
    fraction = round(line - math.floor(line), 2)
    component_lines = [line]
    if fraction == 0.25:
        component_lines = [math.floor(line), math.floor(line) + 0.5]
    elif fraction == 0.75:
        component_lines = [math.floor(line) + 0.5, math.floor(line) + 1.0]

    returns: list[float] = []
    outcomes: list[int] = []
    for component in component_lines:
        delta = actual - component if side.lower() == "over" else component - actual
        if delta > 0:
            outcomes.append(1)
            returns.append(odds - 1.0)
        elif delta < 0:
            outcomes.append(-1)
            returns.append(-1.0)
        else:
            outcomes.append(0)
            returns.append(0.0)
    net = float(np.mean(returns))
    if outcomes == [1]: status = "Win"
    elif outcomes == [-1]: status = "Loss"
    elif all(value == 0 for value in outcomes): status = "Push"
    elif 1 in outcomes and 0 in outcomes: status = "HalfWin"
    elif -1 in outcomes and 0 in outcomes: status = "HalfLoss"
    else: status = "Push"
    return status, net


def feature_map(snapshot: dict[str, Any]) -> dict[str, float]:
    direct = nested_dict(snapshot, "metaModel", "numericFeatures")
    if direct:
        result = {name: float(direct[name]) for name in NUMERIC_FEATURES if name in direct}
        if len(result) == len(NUMERIC_FEATURES):
            return result

    selected_odds = nested(snapshot, "market", "selectedOdds")
    raw_implied = nested(snapshot, "marketProbability", "rawImpliedProbability")
    no_vig = nested(snapshot, "marketProbability", "marketNoVigProbability", default=raw_implied)
    calibrated = nested(snapshot, "model", "baseCalibratedProbability")
    base_edge = nested(snapshot, "marketProbability", "baseEdge")
    base_ev = nested(snapshot, "marketProbability", "baseExpectedValue")
    values = {
        "baseCalibratedProbability": calibrated,
        "odds": selected_odds,
        "rawImpliedProbability": raw_implied,
        "marketNoVigProbability": no_vig,
        "baseEdge": base_edge if base_edge is not None else calibrated - no_vig if calibrated is not None and no_vig is not None else None,
        "baseExpectedValue": base_ev if base_ev is not None else calibrated * selected_odds - 1 if calibrated is not None and selected_odds is not None else None,
        "basePredictedValue": nested(snapshot, "model", "basePredictedValue"),
        "line": nested(snapshot, "market", "line"),
        "baseLineMargin": nested(snapshot, "lineDistance", "baseMargin"),
        "baseLineDistanceSigma": nested(snapshot, "lineDistance", "baseDistanceSigma"),
        "contextExpectedValue": nested(snapshot, "context", "contextExpected"),
        "contextLineMargin": nested(snapshot, "lineDistance", "contextMargin"),
        "contextLineDistanceSigma": nested(snapshot, "lineDistance", "contextDistanceSigma"),
        "combinedExactLineShrunkHitRate": nested(snapshot, "hitRates", "combinedHitRate"),
        "combinedMedian": nested(snapshot, "history", "combined", "median"),
        "combinedStandardDeviation": nested(snapshot, "history", "combined", "standardDeviation"),
        "combinedIqr": nested(snapshot, "history", "combined", "interquartileRange"),
        "combinedMad": nested(snapshot, "history", "combined", "medianAbsoluteDeviation"),
        "trend": nested(snapshot, "trend", "combined"),
        "contextAgreementScore": nested(snapshot, "agreement", "agreementScore"),
        "dataQualityScore": nested(snapshot, "quality", "dataQuality"),
    }
    missing = [name for name, value in values.items() if value is None or not math.isfinite(float(value))]
    if missing:
        raise ValueError(f"Snapshot lacks runtime features: {', '.join(missing)}")
    return {name: float(value) for name, value in values.items()}


def nested_dict(data: dict[str, Any], *path: str) -> dict[str, Any] | None:
    current: Any = data
    for part in path:
        if not isinstance(current, dict) or part not in current:
            return None
        current = current[part]
    return current if isinstance(current, dict) else None


def build_dataset(frame: pd.DataFrame) -> Dataset:
    records: list[dict[str, Any]] = []
    for _, row in frame.iterrows():
        snapshot = parse_snapshot(get_value(row, "feature_snapshot_json", "FeatureSnapshotJson"))
        schema = snapshot.get("featureSchemaVersion")
        if schema != FEATURE_SCHEMA_VERSION:
            continue
        market = str(get_value(row, "market_type", "MarketType") or snapshot.get("market", {}).get("marketType", ""))
        side = str(get_value(row, "selected_side", "SelectedSide") or snapshot.get("market", {}).get("selectedSide", ""))
        if market not in MARKETS or side not in SIDES:
            continue
        match_date = pd.to_datetime(get_value(row, "match_date_utc", "match_date", "MatchDate"), utc=True, errors="coerce")
        if pd.isna(match_date):
            continue
        features = feature_map(snapshot)
        base_raw_probability = nested(snapshot, "model", "baseRawProbability", default=features["baseCalibratedProbability"])
        odds = float(get_value(row, "selected_odds", "odds", "SelectedOdds") or features["odds"])
        status = get_value(row, "settlement_status", "SettlementStatus", "status")
        net_return = get_value(row, "net_return", "NetReturn", "profit")
        if status is None:
            actual = get_value(row, "actual_value", "ActualValue")
            line = get_value(row, "line_value", "LineValue")
            if actual is None or line is None:
                continue
            status, calculated_return = settle(side, float(line), float(actual), odds)
            net_return = calculated_return if net_return is None else net_return
        status = str(status).lower()
        if status in {"push", "void", "cancelled", "canceled"}:
            continue
        target = 1 if status in {"win", "won", "halfwin", "half_win", "ganada", "ganada completa"} else 0
        if net_return is None:
            net_return = odds - 1 if target else -1
        records.append({
            "match_date": match_date,
            "market": market,
            "side": side,
            "target": target,
            "net_return": float(net_return),
            "base_raw_probability": float(base_raw_probability),
            **features,
        })

    if not records:
        raise ValueError("No usable settled candidates were found.")
    rows = pd.DataFrame(records).sort_values("match_date", kind="stable").reset_index(drop=True)
    feature_names = NUMERIC_FEATURES + [f"marketType={value}" for value in MARKETS] + [f"selection={value}" for value in SIDES]
    columns = [rows[name].to_numpy(dtype=float) for name in NUMERIC_FEATURES]
    columns.extend((rows["market"] == value).to_numpy(dtype=float) for value in MARKETS)
    columns.extend((rows["side"] == value).to_numpy(dtype=float) for value in SIDES)
    return Dataset(rows, np.column_stack(columns), rows["target"].to_numpy(dtype=int), rows["net_return"].to_numpy(dtype=float), feature_names)


def sigmoid(values: np.ndarray) -> np.ndarray:
    clipped = np.clip(values, -40, 40)
    return 1.0 / (1.0 + np.exp(-clipped))


def safe_metrics(y: np.ndarray, probability: np.ndarray, net_return: np.ndarray) -> dict[str, float]:
    probability = np.clip(probability, 1e-9, 1 - 1e-9)
    selected = probability >= 0.55
    selected_returns = net_return[selected]
    metrics = {
        "rows": float(len(y)),
        "positiveRate": float(np.mean(y)),
        "brier": float(brier_score_loss(y, probability)),
        "logLoss": float(log_loss(y, probability, labels=[0, 1])),
        "selectedPicks": float(selected.sum()),
        "yieldAt055": float(selected_returns.mean()) if len(selected_returns) else 0.0,
        "profitAt055": float(selected_returns.sum()),
        "maximumDrawdownAt055": maximum_drawdown(selected_returns),
    }
    metrics["rocAuc"] = float(roc_auc_score(y, probability)) if len(np.unique(y)) == 2 else 0.5
    fraction, mean = calibration_curve(y, probability, n_bins=min(10, max(2, len(y) // 20)), strategy="quantile")
    metrics["expectedCalibrationError"] = float(np.mean(np.abs(fraction - mean))) if len(mean) else 0.0
    return metrics


def maximum_drawdown(returns: np.ndarray) -> float:
    if len(returns) == 0:
        return 0.0
    balance = np.concatenate(([0.0], np.cumsum(returns)))
    peaks = np.maximum.accumulate(balance)
    return float(np.max(peaks - balance))


def fit_base_calibration_profiles(rows: pd.DataFrame, y: np.ndarray, minimum_rows: int = 80) -> dict[str, Any]:
    profiles: dict[str, Any] = {}
    scopes: list[tuple[str, np.ndarray]] = [("*", np.ones(len(rows), dtype=bool))]
    scopes.extend((market, (rows["market"] == market).to_numpy()) for market in MARKETS)
    scopes.extend(
        (f"{market}:{side}", ((rows["market"] == market) & (rows["side"] == side)).to_numpy())
        for market in MARKETS for side in SIDES
    )
    for key, mask in scopes:
        if int(mask.sum()) < minimum_rows or len(np.unique(y[mask])) < 2:
            continue
        probability = np.clip(rows.loc[mask, "base_raw_probability"].to_numpy(dtype=float), 1e-9, 1 - 1e-9)
        logits = np.log(probability / (1 - probability)).reshape(-1, 1)
        model = LogisticRegression(max_iter=2000, random_state=20260813)
        model.fit(logits, y[mask])
        slope = float(model.coef_[0, 0])
        if slope <= 0:
            continue
        profiles[key] = {
            "modelName": "Platt",
            "modelVersion": "generated-with-meta-training-report",
            "intercept": float(model.intercept_[0]),
            "slope": slope,
            "trainingSampleCount": int(mask.sum()),
            "trainedThroughUtc": rows.iloc[-1]["match_date"].isoformat(),
        }
    return profiles


def fit(args: argparse.Namespace, dataset: Dataset) -> tuple[dict[str, Any], dict[str, Any]]:
    row_count = len(dataset.y)
    if row_count < args.minimum_rows:
        raise ValueError(f"At least {args.minimum_rows} settled candidates are required; found {row_count}.")
    if len(np.unique(dataset.y)) != 2:
        raise ValueError("The dataset must contain wins and losses.")

    holdout_start = max(2, int(row_count * 0.80))
    x_development, y_development = dataset.x[:holdout_start], dataset.y[:holdout_start]
    development_rows = dataset.rows.iloc[:holdout_start]
    x_holdout, y_holdout = dataset.x[holdout_start:], dataset.y[holdout_start:]
    return_holdout = dataset.net_return[holdout_start:]
    if len(x_holdout) < 10:
        raise ValueError("The untouched temporal holdout must contain at least 10 candidates.")

    folds = min(5, max(2, len(x_development) // 40))
    splitter = TimeSeriesSplit(n_splits=folds)
    oof_probability = np.full(len(x_development), np.nan)
    for train_index, validation_index in splitter.split(x_development):
        if len(np.unique(y_development[train_index])) < 2:
            continue
        scaler = StandardScaler().fit(x_development[train_index])
        model = LogisticRegression(max_iter=3000, random_state=20260813)
        model.fit(scaler.transform(x_development[train_index]), y_development[train_index])
        oof_probability[validation_index] = model.predict_proba(scaler.transform(x_development[validation_index]))[:, 1]

    calibration_mask = np.isfinite(oof_probability)
    if calibration_mask.sum() < 20 or len(np.unique(y_development[calibration_mask])) < 2:
        calibration_intercept, calibration_slope = 0.0, 1.0
    else:
        logits = np.log(np.clip(oof_probability[calibration_mask], 1e-9, 1 - 1e-9)
                        / (1 - np.clip(oof_probability[calibration_mask], 1e-9, 1 - 1e-9))).reshape(-1, 1)
        calibration = LogisticRegression(max_iter=2000, random_state=20260813)
        calibration.fit(logits, y_development[calibration_mask])
        calibration_intercept = float(calibration.intercept_[0])
        calibration_slope = float(calibration.coef_[0, 0])

    scaler = StandardScaler().fit(x_development)
    logistic = LogisticRegression(max_iter=3000, random_state=20260813)
    logistic.fit(scaler.transform(x_development), y_development)
    raw_holdout = logistic.predict_proba(scaler.transform(x_holdout))[:, 1]
    calibrated_holdout = sigmoid(calibration_intercept + calibration_slope * np.log(
        np.clip(raw_holdout, 1e-9, 1 - 1e-9) / (1 - np.clip(raw_holdout, 1e-9, 1 - 1e-9))))

    hist = HistGradientBoostingClassifier(max_depth=4, learning_rate=0.05, max_iter=200, random_state=20260813)
    hist.fit(x_development, y_development)
    comparison = {
        "LogisticRegressionCalibrated": safe_metrics(y_holdout, calibrated_holdout, return_holdout),
        "HistGradientBoosting": safe_metrics(y_holdout, hist.predict_proba(x_holdout)[:, 1], return_holdout),
    }
    base_calibration_profiles = fit_base_calibration_profiles(development_rows, y_development)
    artifact = {
        "modelType": "LogisticRegression",
        "modelName": "BotC Pick Selector Meta Model",
        "modelVersion": args.model_version,
        "featureSchemaVersion": FEATURE_SCHEMA_VERSION,
        "trainedThroughUtc": dataset.rows.iloc[holdout_start - 1]["match_date"].isoformat(),
        "intercept": float(logistic.intercept_[0]),
        "features": [
            {"name": name, "mean": float(mean), "scale": float(scale), "coefficient": float(coefficient)}
            for name, mean, scale, coefficient in zip(dataset.feature_names, scaler.mean_, scaler.scale_, logistic.coef_[0])
        ],
        "calibration": {"method": "Platt", "intercept": calibration_intercept, "slope": calibration_slope},
        "validationMetrics": comparison["LogisticRegressionCalibrated"],
        "training": {
            "strategy": "expanding-window TimeSeriesSplit",
            "developmentRows": int(holdout_start),
            "holdoutRows": int(row_count - holdout_start),
            "holdoutStartUtc": dataset.rows.iloc[holdout_start]["match_date"].isoformat(),
            "allCandidatesRequired": True,
            "pushesExcludedFromBinaryV1": True,
        },
    }
    report = {
        "modelVersion": args.model_version,
        "featureSchemaVersion": FEATURE_SCHEMA_VERSION,
        "candidateRows": row_count,
        "temporalHoldout": {
            "startUtc": dataset.rows.iloc[holdout_start]["match_date"].isoformat(),
            "endUtc": dataset.rows.iloc[-1]["match_date"].isoformat(),
        },
        "comparison": comparison,
        "recommendedBaseCalibrationProfiles": base_calibration_profiles,
        "baseCalibrationPolicy": "MarketType:Selection when >=80 settled candidates, then MarketType, then global '*'.",
        "activationRequested": bool(args.activate),
    }
    return artifact, report


def write_outputs(args: argparse.Namespace, artifact: dict[str, Any], report: dict[str, Any]) -> Path:
    args.output_dir.mkdir(parents=True, exist_ok=True)
    version_path = args.output_dir / f"{args.model_version}.json"
    version_path.write_text(json.dumps(artifact, ensure_ascii=False, indent=2), encoding="utf-8")
    (args.output_dir / f"{args.model_version}.report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.activate:
        temporary = args.output_dir / ".active.json.tmp"
        shutil.copyfile(version_path, temporary)
        temporary.replace(args.output_dir / "active.json")
    return version_path


def synthetic_frame(rows: int = 360) -> pd.DataFrame:
    rng = np.random.default_rng(20260813)
    start = datetime(2024, 1, 1, tzinfo=timezone.utc)
    records = []
    for index in range(rows):
        probability = float(np.clip(0.42 + 0.001 * (index % 120) + rng.normal(0, 0.06), 0.08, 0.92))
        odds = float(np.clip(1 / max(0.36, probability - 0.05), 1.6, 2.3))
        won = int(rng.random() < probability)
        numeric = {name: float(rng.normal()) for name in NUMERIC_FEATURES}
        numeric.update({
            "baseCalibratedProbability": probability,
            "odds": odds,
            "rawImpliedProbability": 1 / odds,
            "marketNoVigProbability": max(0.01, 1 / odds - 0.02),
            "baseEdge": probability - (1 / odds - 0.02),
            "baseExpectedValue": probability * odds - 1,
            "contextAgreementScore": float(np.clip(probability + rng.normal(0, 0.1), 0, 1)),
            "dataQualityScore": float(np.clip(0.75 + rng.normal(0, 0.1), 0, 1)),
        })
        market = MARKETS[index % len(MARKETS)]
        side = SIDES[index % 2]
        snapshot = {"featureSchemaVersion": FEATURE_SCHEMA_VERSION, "metaModel": {"numericFeatures": numeric}}
        records.append({
            "match_date_utc": start + timedelta(days=index),
            "market_type": market,
            "selected_side": side,
            "selected_odds": odds,
            "settlement_status": "Win" if won else "Loss",
            "net_return": odds - 1 if won else -1,
            "feature_snapshot_json": json.dumps(snapshot),
        })
    return pd.DataFrame(records)


def run_self_test(args: argparse.Namespace) -> None:
    with tempfile.TemporaryDirectory(prefix="bot-c-meta-test-") as directory:
        test_args = argparse.Namespace(**vars(args))
        test_args.output_dir = Path(directory)
        test_args.minimum_rows = 200
        test_args.activate = True
        dataset = build_dataset(synthetic_frame())
        artifact, report = fit(test_args, dataset)
        path = write_outputs(test_args, artifact, report)
        assert path.exists() and (Path(directory) / "active.json").exists()
        assert len(artifact["features"]) == len(NUMERIC_FEATURES) + len(MARKETS) + len(SIDES)
        assert report["candidateRows"] == 360
        print("PASS Bot C temporal training pipeline self-test")


def main() -> None:
    args = parse_args()
    if args.self_test:
        run_self_test(args)
        return
    if args.input is None:
        raise SystemExit("--input is required unless --self-test is used.")
    dataset = build_dataset(load_frame(args.input))
    artifact, report = fit(args, dataset)
    path = write_outputs(args, artifact, report)
    print(json.dumps({"status": "complete", "artifact": str(path), "report": report}, ensure_ascii=False))


if __name__ == "__main__":
    main()
