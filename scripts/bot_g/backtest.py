from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd

from .calibration import fit_hierarchical_calibration
from .config import BotGConfig
from .contracts import CandidateDataset
from .decisions import coverage_metrics
from .evaluation import (
    drift_report,
    economic_metrics,
    ev_buckets,
    fixture_bootstrap,
    paired_f_comparison,
    predictive_metrics,
    promotion_scorecard,
)
from .features import FeatureEncoder, engineer_features, market_logit
from .modeling import LogitResidualModel, bootstrap_logit_ensemble
from .ood import fit_ood_profiles
from .pipeline import _oof_logit, _predict_deployment
from .settlement_profiles import fit_settlement_profiles
from .splits import expanding_folds, final_holdout


@dataclass
class BacktestResult:
    report: dict[str, Any]
    candidates: pd.DataFrame
    synthetic: bool


def run_walk_forward_backtest(
    dataset: CandidateDataset,
    config: BotGConfig,
    *,
    include_final_test: bool = False,
    final_test_start: str | None = None,
    quick: bool = False,
    synthetic: bool = False,
) -> BacktestResult:
    synthetic = bool(synthetic or dataset.metadata.get("declaredSynthetic", False))
    rows = engineer_features(dataset.rows)
    final = final_holdout(rows, config.final_test_fraction, final_test_start)
    scope = np.arange(len(rows), dtype=int) if include_final_test else final.development
    scoped = rows.iloc[scope]
    rows_per_fixture = len(scoped) / scoped["FixtureId"].nunique()
    minimum_train = max(20, int(np.ceil(config.minimum_training_rows / max(rows_per_fixture, 1))))
    minimum_validation = max(8, int(np.ceil(config.minimum_validation_rows / max(rows_per_fixture, 1))))
    outer = expanding_folds(
        rows,
        scope,
        config.outer_folds,
        minimum_train,
        minimum_validation,
        config.embargo_hours,
        config.outcome_lag_hours,
    )
    candidates: list[pd.DataFrame] = []
    fold_reports: list[dict[str, Any]] = []
    for fold in outer:
        train = rows.iloc[fold.train].copy()
        validation = rows.iloc[fold.validation].copy().reset_index(drop=True)
        try:
            inner_min_train = max(12, minimum_train // 2)
            inner_min_validation = max(4, minimum_validation // 2)
            inner = expanding_folds(
                rows,
                fold.train,
                config.inner_folds,
                inner_min_train,
                inner_min_validation,
                config.embargo_hours,
                config.outcome_lag_hours,
            )
            inner_oof, _, inner_details = _oof_logit(
                rows, inner, "market_both_context", config
            )
            inner_mask = np.isfinite(inner_oof)
            # Calibration evidence must itself be resolved before this outer validation cutoff.
            inner_mask &= (rows["OutcomeAvailableUtc"] < fold.knowledge_cutoff).to_numpy()
            if inner_mask.sum() < config.calibration.minimum_rows:
                raise ValueError("Insufficient inner OOF rows for leakage-safe calibration.")
            calibration_profiles, calibration_metadata = fit_hierarchical_calibration(
                rows.loc[inner_mask].reset_index(drop=True),
                inner_oof[inner_mask],
                rows.loc[inner_mask, "TargetPositiveReturn"].to_numpy(dtype=int),
                config.calibration,
            )
        except ValueError as exc:
            fold_reports.append({
                "fold": fold.number,
                "status": "SKIPPED",
                "reason": str(exc),
                "validationStartUtc": fold.validation_start.isoformat(),
            })
            continue

        encoder = FeatureEncoder.fit(train, "market_both_context")
        x_train = encoder.transform(train)
        y_train = train["TargetPositiveReturn"].to_numpy(dtype=int)
        offset_train = market_logit(train, config.calibration.clip)
        central = LogitResidualModel(config.l2, config.max_iterations).fit(
            x_train, y_train, offset_train
        )
        ensemble = bootstrap_logit_ensemble(
            x_train,
            y_train,
            offset_train,
            train["FixtureId"].astype(str).to_numpy(),
            min(config.bootstrap_models, 4) if quick else config.bootstrap_models,
            config.seed + fold.number * 100,
            config.l2,
            config.max_iterations,
        )
        ood_profiles = fit_ood_profiles(x_train, encoder.feature_names)
        settlement_profiles = fit_settlement_profiles(
            train, config.thresholds.minimum_settlement_effective_sample_size
        )
        predicted = _predict_deployment(
            validation,
            encoder,
            central,
            ensemble,
            calibration_profiles,
            settlement_profiles,
            ood_profiles,
            config,
        )
        predicted["BacktestFold"] = fold.number
        predicted["KnowledgeCutoffUtc"] = fold.knowledge_cutoff
        candidates.append(predicted)
        fold_reports.append({
            "fold": fold.number,
            "status": "COMPLETE",
            "trainRows": int(len(train)),
            "trainFixtures": int(train["FixtureId"].nunique()),
            "validationRows": int(len(predicted)),
            "validationFixtures": int(predicted["FixtureId"].nunique()),
            "validationStartUtc": fold.validation_start.isoformat(),
            "validationEndUtc": fold.validation_end.isoformat(),
            "knowledgeCutoffUtc": fold.knowledge_cutoff.isoformat(),
            "innerFolds": inner_details,
            "calibration": calibration_metadata,
            "drift": drift_report(
                x_train,
                encoder.transform(validation),
                encoder.feature_names,
            ),
        })
    if not candidates:
        raise ValueError("No walk-forward folds had enough prior OOF calibration evidence.")
    combined = pd.concat(candidates, ignore_index=True).sort_values(
        ["PredictionTimestampUtc", "FixtureId", "CandidateId"], kind="stable"
    ).reset_index(drop=True)
    y = combined["TargetPositiveReturn"].to_numpy(dtype=int)
    model_metrics = predictive_metrics(y, combined["FinalProbability"].to_numpy(dtype=float))
    market_metrics = predictive_metrics(y, combined["MarketNoVigProbability"].to_numpy(dtype=float))
    published = economic_metrics(combined, combined["Published"])
    report = {
        "status": "COMPLETE",
        "mode": "walk-forward expanding-window with fixture grouping, embargo and outcome lag",
        "datasetSha256": dataset.sha256,
        "synthetic": synthetic,
        "realMetricsGenerated": not synthetic,
        "finalTestIncluded": bool(include_final_test),
        "finalTestStartUtc": final.final_start.isoformat(),
        "activationAllowed": False,
        "folds": fold_reports,
        "predictive": model_metrics,
        "marketBaseline": market_metrics,
        "economicPublished": published,
        "economicAllCandidates": economic_metrics(combined),
        "coverage": coverage_metrics(combined),
        "evBuckets": ev_buckets(combined),
        "pairedF": paired_f_comparison(combined),
        "fixtureBootstrap": fixture_bootstrap(
            combined,
            "FinalProbability",
            min(50, config.bootstrap_metric_samples) if quick else config.bootstrap_metric_samples,
            config.seed + 777,
        ),
        "promotion": promotion_scorecard(
            model_metrics,
            market_metrics,
            published,
            config.promotion_minimum_fixtures,
            include_final_test and not synthetic,
            len(fold_reports),
        ),
        "warning": (
            "Synthetic outputs are structural tests, not real performance metrics."
            if synthetic
            else "Backtest output never activates a production artifact."
        ),
    }
    return BacktestResult(report, combined, synthetic)


def write_backtest_outputs(result: BacktestResult, output_dir: Path, version: str) -> dict[str, str]:
    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = output_dir / f"{version}.backtest.json"
    candidate_path = output_dir / f"{version}.backtest.csv"
    existing = [str(path) for path in (report_path, candidate_path) if path.exists()]
    if existing:
        raise FileExistsError(
            "Bot G backtest outputs are immutable; use a new model/configuration version. "
            f"Existing paths: {', '.join(existing)}"
        )
    report_path.write_text(
        json.dumps(result.report, indent=2, ensure_ascii=False, allow_nan=False), encoding="utf-8"
    )
    result.candidates.to_csv(candidate_path, index=False)
    return {"report": str(report_path), "candidates": str(candidate_path)}
