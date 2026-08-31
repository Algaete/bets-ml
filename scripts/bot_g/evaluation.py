from __future__ import annotations

import math
from typing import Any, Callable

import numpy as np
import pandas as pd
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import brier_score_loss, log_loss, roc_auc_score


def _clip(probability: np.ndarray) -> np.ndarray:
    return np.clip(np.asarray(probability, dtype=float), 1e-6, 1.0 - 1e-6)


def calibration_error(y: np.ndarray, probability: np.ndarray, bins: int = 10) -> float:
    table = pd.DataFrame({"y": y, "p": probability})
    try:
        table["bucket"] = pd.qcut(table["p"], q=min(bins, table["p"].nunique()), duplicates="drop")
    except ValueError:
        return 0.0
    grouped = table.groupby("bucket", observed=True).agg(n=("y", "size"), actual=("y", "mean"), predicted=("p", "mean"))
    return float(np.sum(grouped["n"] / len(table) * np.abs(grouped["actual"] - grouped["predicted"])))


def calibration_slope_intercept(y: np.ndarray, probability: np.ndarray) -> tuple[float, float]:
    if len(np.unique(y)) < 2:
        return 0.0, 0.0
    probability = _clip(probability)
    logit = np.log(probability / (1.0 - probability)).reshape(-1, 1)
    model = LogisticRegression(C=1e6, max_iter=3_000, random_state=20260819).fit(logit, y)
    return float(model.coef_[0, 0]), float(model.intercept_[0])


def predictive_metrics(y: np.ndarray, probability: np.ndarray) -> dict[str, float | int]:
    y = np.asarray(y, dtype=int)
    probability = _clip(probability)
    slope, intercept = calibration_slope_intercept(y, probability)
    return {
        "rows": int(len(y)),
        "positiveRate": float(np.mean(y)),
        "brier": float(brier_score_loss(y, probability)),
        "logLoss": float(log_loss(y, probability, labels=[0, 1])),
        "auc": float(roc_auc_score(y, probability)) if len(np.unique(y)) == 2 else 0.5,
        "ece": calibration_error(y, probability),
        "calibrationSlope": slope,
        "calibrationIntercept": intercept,
    }


def maximum_drawdown(returns: np.ndarray) -> float:
    if not len(returns):
        return 0.0
    balance = np.concatenate(([0.0], np.cumsum(np.asarray(returns, dtype=float))))
    peak = np.maximum.accumulate(balance)
    return float(np.max(peak - balance))


def _longest(values: np.ndarray, predicate: Callable[[float], bool]) -> int:
    longest = current = 0
    for value in values:
        if predicate(float(value)):
            current += 1
            longest = max(longest, current)
        else:
            current = 0
    return longest


def economic_metrics(rows: pd.DataFrame, selected: np.ndarray | pd.Series | None = None) -> dict[str, Any]:
    scoped = rows if selected is None else rows.loc[np.asarray(selected, dtype=bool)]
    returns = scoped["ProfitLoss"].to_numpy(dtype=float)
    positive_profit = float(returns[returns > 0].sum()) if len(returns) else 0.0
    negative_profit = float(-returns[returns < 0].sum()) if len(returns) else 0.0
    counts = scoped["SettlementState"].value_counts().to_dict()
    return {
        "resolved": int(len(scoped)),
        "fixtures": int(scoped["FixtureId"].nunique()) if len(scoped) else 0,
        "won": int(counts.get("Win", 0)),
        "halfWon": int(counts.get("HalfWin", 0)),
        "push": int(counts.get("Push", 0)),
        "halfLost": int(counts.get("HalfLoss", 0)),
        "lost": int(counts.get("Loss", 0)),
        "stake": float(len(scoped)),
        "profitLoss": float(returns.sum()) if len(returns) else 0.0,
        "yield": float(returns.mean()) if len(returns) else 0.0,
        "averageOdds": float(scoped["SelectedOdds"].mean()) if len(scoped) else 0.0,
        "profitFactor": positive_profit / negative_profit if negative_profit > 0 else None,
        "maximumDrawdown": maximum_drawdown(returns),
        "longestLosingStreak": _longest(returns, lambda value: value < 0),
        "longestWinningStreak": _longest(returns, lambda value: value > 0),
    }


def fixture_bootstrap(
    rows: pd.DataFrame,
    probability_column: str,
    samples: int,
    seed: int,
) -> dict[str, dict[str, float]]:
    fixtures = rows["FixtureId"].astype(str).unique()
    if len(fixtures) < 2 or samples <= 0:
        return {}
    by_fixture = {fixture: np.flatnonzero(rows["FixtureId"].astype(str).to_numpy() == fixture) for fixture in fixtures}
    rng = np.random.default_rng(seed)
    values: dict[str, list[float]] = {"brier": [], "logLoss": [], "yield": []}
    y_all = rows["TargetPositiveReturn"].to_numpy(dtype=int)
    p_all = rows[probability_column].to_numpy(dtype=float)
    returns = rows["ProfitLoss"].to_numpy(dtype=float)
    for _ in range(samples):
        selected = rng.choice(fixtures, size=len(fixtures), replace=True)
        indices = np.concatenate([by_fixture[value] for value in selected])
        y = y_all[indices]
        p = _clip(p_all[indices])
        values["brier"].append(float(np.mean((y - p) ** 2)))
        values["logLoss"].append(float(log_loss(y, p, labels=[0, 1])))
        values["yield"].append(float(np.mean(returns[indices])))
    return {
        name: {
            "mean": float(np.mean(metric)),
            "lower95": float(np.quantile(metric, 0.025)),
            "upper95": float(np.quantile(metric, 0.975)),
        }
        for name, metric in values.items()
    }


EV_BUCKETS = [-np.inf, 0.02, 0.05, 0.10, 0.15, 0.20, 0.30, np.inf]
EV_LABELS = ["<2%", "2-5%", "5-10%", "10-15%", "15-20%", "20-30%", ">30%"]


def ev_buckets(rows: pd.DataFrame) -> list[dict[str, Any]]:
    table = rows.copy()
    table["_bucket"] = pd.cut(table["ExpectedValue"], bins=EV_BUCKETS, labels=EV_LABELS, right=False)
    output: list[dict[str, Any]] = []
    for keys, scoped in table.groupby(["_bucket", "MarketType", "Selection", "Bookmaker"], observed=True):
        bucket, market, selection, bookmaker = keys
        output.append({
            "bucket": str(bucket),
            "marketType": market,
            "selection": selection,
            "bookmaker": bookmaker,
            "n": int(len(scoped)),
            "fixtures": int(scoped["FixtureId"].nunique()),
            "averagePredictedEV": float(scoped["ExpectedValue"].mean()),
            "averageConservativeEV": float(scoped["ConservativeExpectedValue"].mean()),
            "actualYield": float(scoped["ProfitLoss"].mean()),
            "averageOdds": float(scoped["SelectedOdds"].mean()),
            "brier": float(np.mean((scoped["TargetPositiveReturn"] - scoped["FinalProbability"]) ** 2)),
        })
    return output


def paired_f_comparison(rows: pd.DataFrame) -> dict[str, Any]:
    if "FPublished" not in rows.columns:
        return {"available": False, "reason": "FPublished column was not supplied."}
    f = rows["FPublished"].fillna(False).astype(bool).to_numpy()
    g = rows["Published"].fillna(False).astype(bool).to_numpy()

    def scoped_metrics(mask: np.ndarray) -> dict[str, Any]:
        scoped = rows.loc[mask]
        output = economic_metrics(rows, mask)
        if len(scoped):
            output["gPredictive"] = predictive_metrics(
                scoped["TargetPositiveReturn"].to_numpy(dtype=int),
                scoped["FinalProbability"].to_numpy(dtype=float),
            )
        else:
            output["gPredictive"] = None
        if (
            "FProbability" in scoped.columns
            and len(scoped)
            and scoped["FProbability"].notna().all()
        ):
            output["fPredictive"] = predictive_metrics(
                scoped["TargetPositiveReturn"].to_numpy(dtype=int),
                scoped["FProbability"].to_numpy(dtype=float),
            )
        else:
            output["fPredictive"] = None
        return output

    shared = f & g
    differences: dict[str, float] | None = None
    difference_columns = {
        "probabilityGMinusF": ("FinalProbability", "FProbability"),
        "edgeGMinusF": ("ConservativeEdge", "FEdge"),
        "expectedValueGMinusF": ("ConservativeExpectedValue", "FExpectedValue"),
    }
    if (
        shared.any()
        and all(right in rows.columns for _, right in difference_columns.values())
        and all(rows.loc[shared, right].notna().all() for _, right in difference_columns.values())
    ):
        differences = {
            name: float((rows.loc[shared, left] - rows.loc[shared, right]).mean())
            for name, (left, right) in difference_columns.items()
        }
    return {
        "available": True,
        "signature": ["FixtureId", "Bookmaker", "MarketType", "Selection", "Line"],
        "shared": scoped_metrics(shared),
        "gOnly": scoped_metrics(g & ~f),
        "fOnly": scoped_metrics(f & ~g),
        "gRejectedButFSelected": int((f & ~g).sum()),
        "fRejectedButGSelected": int((g & ~f).sum()),
        "sharedMeanDifferences": differences,
        "sharedDifferenceUnavailableReason": None if differences is not None else (
            "Shared rows and optional FProbability/FEdge/FExpectedValue are required."
        ),
        "neitherCandidates": int((~f & ~g).sum()),
    }


def population_stability_index(train: np.ndarray, observed: np.ndarray, bins: int = 10) -> float:
    boundaries = np.unique(np.quantile(train, np.linspace(0, 1, bins + 1)))
    if len(boundaries) < 3:
        return 0.0
    boundaries[0], boundaries[-1] = -np.inf, np.inf
    expected = np.histogram(train, bins=boundaries)[0] / max(len(train), 1)
    actual = np.histogram(observed, bins=boundaries)[0] / max(len(observed), 1)
    expected = np.clip(expected, 1e-6, None)
    actual = np.clip(actual, 1e-6, None)
    return float(np.sum((actual - expected) * np.log(actual / expected)))


def drift_report(
    train_x: np.ndarray,
    observed_x: np.ndarray,
    feature_names: list[str] | tuple[str, ...],
) -> dict[str, Any]:
    values = [
        {"feature": name, "psi": population_stability_index(train_x[:, i], observed_x[:, i])}
        for i, name in enumerate(feature_names)
        if "=" not in name
    ]
    values.sort(key=lambda item: item["psi"], reverse=True)
    return {
        "method": "PSI using development quantile bins",
        "maximumPsi": max((item["psi"] for item in values), default=0.0),
        "alert": any(item["psi"] >= 0.25 for item in values),
        "features": values,
    }


def promotion_scorecard(
    model_metrics: dict[str, Any],
    market_metrics: dict[str, Any],
    economic: dict[str, Any] | None,
    minimum_fixtures: int,
    final_test_evaluated: bool,
    independent_oos_windows: int = 0,
) -> dict[str, Any]:
    checks = {
        "realFinalTestEvaluated": bool(final_test_evaluated),
        "secondWalkForwardOosWindow": independent_oos_windows >= 2,
        "minimumResolvedFixtures": bool(economic and economic["fixtures"] >= minimum_fixtures),
        "brierBeatsMarket": model_metrics["brier"] < market_metrics["brier"],
        "logLossBeatsMarket": model_metrics["logLoss"] < market_metrics["logLoss"],
        "calibrationSlopeReasonable": 0.75 <= model_metrics["calibrationSlope"] <= 1.25,
        "calibrationInterceptReasonable": abs(model_metrics["calibrationIntercept"]) <= 0.20,
        "positiveYield": bool(economic and economic["yield"] > 0),
        "positiveProfitFactor": bool(
            economic and economic["profitFactor"] is not None and economic["profitFactor"] > 1
        ),
    }
    return {
        "status": "PASS" if all(checks.values()) else "FAIL",
        "checks": checks,
        "independentOosWindows": int(independent_oos_windows),
        "syntheticDataCanNeverPromote": True,
    }
