from __future__ import annotations

import importlib.metadata
import hashlib
import json
import os
import platform
import shutil
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import numpy as np
import pandas as pd

from .calibration import (
    CalibrationProfile,
    calibration_effective_sample_size,
    calibrate_hierarchical,
    fit_hierarchical_calibration,
)
from .config import BotGConfig
from .contracts import CandidateDataset, contract_document, write_contract
from .decisions import apply_decisions, coverage_metrics
from .evaluation import (
    drift_report,
    economic_metrics,
    ev_buckets,
    fixture_bootstrap,
    paired_f_comparison,
    predictive_metrics,
    promotion_scorecard,
)
from .features import ABLATIONS, FeatureEncoder, engineer_features, market_logit
from .modeling import (
    LogitResidualModel,
    available_families,
    bootstrap_logit_ensemble,
    fit_optional_classifier,
    predict_optional_classifier,
)
from .ood import artifact as ood_artifact
from .ood import fit_ood_profiles, ood_score
from .settlement_profiles import SettlementProfile, fit_settlement_profiles
from .splits import assert_oof_fixture_integrity, expanding_folds, final_holdout


@dataclass
class TrainingResult:
    artifact: dict[str, Any]
    report: dict[str, Any]
    oof_rows: pd.DataFrame
    final_rows: pd.DataFrame | None
    synthetic: bool


def _package_versions() -> dict[str, str | None]:
    names = ("numpy", "pandas", "scipy", "scikit-learn", "catboost", "xgboost", "lightgbm")
    versions: dict[str, str | None] = {}
    for name in names:
        try:
            versions[name] = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            versions[name] = None
    return versions


def _git_metadata(root: Path) -> dict[str, Any]:
    def run(*args: str) -> str | None:
        try:
            value = subprocess.run(
                ["git", *args], cwd=root, check=True, capture_output=True, text=True
            ).stdout.strip()
            return value or None
        except (OSError, subprocess.CalledProcessError):
            return None

    status = run("status", "--short")
    return {
        "commit": run("rev-parse", "HEAD"),
        "branch": run("branch", "--show-current"),
        "dirty": bool(status),
        "statusEntryCount": len(status.splitlines()) if status else 0,
    }


def _minimum_fixture_counts(rows_per_fixture: float, config: BotGConfig) -> tuple[int, int]:
    minimum_train = max(20, int(np.ceil(config.minimum_training_rows / max(rows_per_fixture, 1))))
    minimum_validation = max(8, int(np.ceil(config.minimum_validation_rows / max(rows_per_fixture, 1))))
    return minimum_train, minimum_validation


def _oof_logit(
    rows: pd.DataFrame,
    folds: list[Any],
    ablation: str,
    config: BotGConfig,
) -> tuple[np.ndarray, np.ndarray, list[dict[str, Any]]]:
    prediction = np.full(len(rows), np.nan, dtype=float)
    fold_number = np.full(len(rows), -1, dtype=int)
    details: list[dict[str, Any]] = []
    for fold in folds:
        train = rows.iloc[fold.train]
        validation = rows.iloc[fold.validation]
        encoder = FeatureEncoder.fit(train, ablation)
        x_train = encoder.transform(train)
        x_validation = encoder.transform(validation)
        model = LogitResidualModel(config.l2, config.max_iterations).fit(
            x_train,
            train["TargetPositiveReturn"].to_numpy(dtype=int),
            market_logit(train, config.calibration.clip),
        )
        prediction[fold.validation] = model.predict_proba(
            x_validation, market_logit(validation, config.calibration.clip)
        )
        fold_number[fold.validation] = fold.number
        details.append({
            "fold": fold.number,
            "trainRows": int(len(train)),
            "trainFixtures": int(train["FixtureId"].nunique()),
            "validationRows": int(len(validation)),
            "validationFixtures": int(validation["FixtureId"].nunique()),
            "validationStartUtc": fold.validation_start.isoformat(),
            "validationEndUtc": fold.validation_end.isoformat(),
            "knowledgeCutoffUtc": fold.knowledge_cutoff.isoformat(),
            "featureCount": len(encoder.feature_names),
            "converged": model.converged_,
        })
    return prediction, fold_number, details


def _family_comparison(
    rows: pd.DataFrame,
    folds: list[Any],
    logistic_prediction: np.ndarray,
    families: Iterable[str],
    config: BotGConfig,
    quick: bool,
    eligible_mask: np.ndarray | None = None,
) -> dict[str, Any]:
    requested = list(dict.fromkeys(families))
    availability = available_families()
    output: dict[str, Any] = {}
    common_mask = np.isfinite(logistic_prediction)
    if eligible_mask is not None:
        common_mask &= np.asarray(eligible_mask, dtype=bool)
    y = rows["TargetPositiveReturn"].to_numpy(dtype=int)
    for family in requested:
        if family not in availability:
            output[family] = {"status": "unavailable", "reason": "Unknown family."}
            continue
        if not availability[family]["available"]:
            output[family] = {
                "status": "unavailable",
                "version": availability[family]["version"],
                "reason": "Python distribution is not installed.",
            }
            continue
        if family == "logistic":
            output[family] = {
                "status": "complete",
                "version": availability[family]["version"],
                "metrics": predictive_metrics(y[common_mask], logistic_prediction[common_mask]),
                "sameRowsAsLogistic": True,
                "deploymentEligible": True,
            }
            continue
        prediction = np.full(len(rows), np.nan, dtype=float)
        errors: list[str] = []
        for fold in folds:
            train = rows.iloc[fold.train]
            validation = rows.iloc[fold.validation]
            try:
                encoder = FeatureEncoder.fit(train, "market_both_context")
                x_train = encoder.transform(train)
                x_validation = encoder.transform(validation)
                model = fit_optional_classifier(
                    family,
                    x_train,
                    train["TargetPositiveReturn"].to_numpy(dtype=int),
                    market_logit(train, config.calibration.clip),
                    config.seed + fold.number,
                    quick,
                )
                prediction[fold.validation] = predict_optional_classifier(
                    model, x_validation, market_logit(validation, config.calibration.clip)
                )
            except Exception as exc:  # Optional candidates must fail independently.
                errors.append(f"fold {fold.number}: {type(exc).__name__}: {exc}")
        mask = common_mask & np.isfinite(prediction)
        if mask.sum() < config.minimum_validation_rows:
            output[family] = {
                "status": "error",
                "version": availability[family]["version"],
                "errors": errors,
                "predictedRows": int(mask.sum()),
                "sameRowsAsLogistic": bool(np.array_equal(mask, common_mask)),
            }
        else:
            output[family] = {
                "status": "complete" if not errors else "partial",
                "version": availability[family]["version"],
                "metrics": predictive_metrics(y[mask], prediction[mask]),
                "errors": errors,
                "sameRowsAsLogistic": bool(np.array_equal(mask, common_mask)),
                "deploymentEligible": False,
                "note": "Comparison only; deployment remains neutral logit-residual LR.",
            }
    for family, info in availability.items():
        if family not in output:
            output[family] = {
                "status": "not_requested",
                "available": info["available"],
                "version": info["version"],
            }
    return output


def _predict_deployment(
    rows: pd.DataFrame,
    encoder: FeatureEncoder,
    central: LogitResidualModel,
    ensemble: list[LogitResidualModel],
    calibration_profiles: list[CalibrationProfile],
    settlement_profiles: list[SettlementProfile],
    ood_profiles: list[Any],
    config: BotGConfig,
) -> pd.DataFrame:
    x = encoder.transform(rows)
    offset = market_logit(rows, config.calibration.clip)
    central_raw = central.predict_proba(x, offset)
    central_calibrated, reliability = calibrate_hierarchical(
        rows, central_raw, calibration_profiles, config.calibration
    )
    raw_members: list[np.ndarray] = [central_raw]
    for member in ensemble:
        raw_members.append(member.predict_proba(x, offset))
    matrix = np.column_stack(raw_members)
    ensemble_dispersion = np.std(matrix, axis=1)
    effective_n = np.maximum(
        calibration_effective_sample_size(rows, calibration_profiles), 1.0
    )
    sampling_error = np.sqrt(central_calibrated * (1.0 - central_calibrated) / effective_n)
    uncertainty = np.clip(
        np.sqrt(ensemble_dispersion ** 2 + sampling_error ** 2),
        config.minimum_uncertainty,
        config.maximum_uncertainty,
    )
    final_probability = central_calibrated
    lower = np.clip(
        final_probability - config.uncertainty_confidence_z * uncertainty,
        0.0,
        final_probability,
    )
    upper = np.clip(
        final_probability + config.uncertainty_confidence_z * uncertainty,
        final_probability,
        1.0,
    )
    conservative_probability = (
        lower.copy()
        if config.uncertainty_use_lower_bound
        else np.clip(
            final_probability - config.uncertainty_conservative_lambda * uncertainty,
            0.0,
            final_probability,
        )
    )
    candidate_ood = ood_score(
        x,
        encoder.feature_names,
        ood_profiles,
        config.ood_robust_z_score_threshold,
        config.ood_severe_robust_z_score,
        config.ood_minimum_reference_sample_size,
    )
    result = apply_decisions(
        rows,
        final_probability,
        lower,
        upper,
        conservative_probability,
        uncertainty,
        candidate_ood,
        reliability,
        effective_n,
        settlement_profiles,
        config,
    )
    result["CentralRawProbability"] = central_raw
    result["CentralCalibratedProbability"] = central_calibrated
    result["EnsembleDispersion"] = ensemble_dispersion
    return result


def _artifact(
    dataset: CandidateDataset,
    config: BotGConfig,
    encoder: FeatureEncoder,
    central: LogitResidualModel,
    ensemble: list[LogitResidualModel],
    calibration_profiles: list[CalibrationProfile],
    calibration_metadata: dict[str, Any],
    ood_profiles: list[Any],
    settlement_profiles: list[SettlementProfile],
    trained_rows: pd.DataFrame,
    experiment: dict[str, Any],
    synthetic: bool,
) -> dict[str, Any]:
    trained_through = trained_rows["OutcomeAvailableUtc"].max()
    model_artifact = central.to_artifact(encoder.feature_names)
    members = []
    for index, member in enumerate(ensemble):
        value = member.to_artifact(encoder.feature_names)
        members.append({
            "name": f"fixture-bootstrap-{index:03d}",
            "intercept": value["intercept"],
            "features": value["features"],
        })

    runtime_calibration: list[dict[str, Any]] = []
    for profile in calibration_profiles:
        if profile.method == "Platt":
            log_probability = profile.slope
            log_one_minus = -profile.slope
        elif profile.method == "Beta":
            log_probability = profile.beta_a
            log_one_minus = -profile.beta_b
        else:  # Identity is exactly sigmoid(log(p) - log(1-p)).
            log_probability = 1.0
            log_one_minus = -1.0
        runtime_calibration.append({
            "key": {
                "family": profile.family,
                "marketType": None if profile.market_type == "*" else profile.market_type,
                "selection": None if profile.selection == "*" else profile.selection,
                "bookmaker": None if profile.bookmaker == "*" else profile.bookmaker,
            },
            "version": config.calibration_version,
            "method": "BetaCalibration",
            "intercept": profile.intercept,
            "logProbabilityCoefficient": log_probability,
            "logOneMinusProbabilityCoefficient": log_one_minus,
            "sampleSize": profile.sample_size,
            "effectiveSampleSize": profile.effective_sample_size,
            "evidenceAvailableThroughUtc": profile.evidence_available_through_utc.isoformat(),
        })
    runtime_ood = [
        {
            "name": profile.name,
            "median": profile.median,
            "medianAbsoluteDeviation": profile.mad,
            "percentile01": profile.p01,
            "percentile99": profile.p99,
            "sampleSize": profile.sample_size,
        }
        for profile in ood_profiles
    ]
    return {
        "modelVersion": config.model_version,
        "featureSchemaVersion": config.feature_schema_version,
        "configurationVersion": config.configuration_version,
        "trainedThroughUtc": trained_through.isoformat(),
        "family": "GOALS",
        "supportedMarkets": list(config.supported_markets),
        "synthetic": synthetic,
        "deployable": False,
        "runtimeSettings": {
            "maximumAbsoluteResidualLogit": 4.0,
            "minimumSettlementEffectiveSampleSize": (
                config.thresholds.minimum_settlement_effective_sample_size
            ),
            "settlementEvidenceLagHours": int(config.outcome_lag_hours),
        },
        "model": model_artifact,
        "ensemble": members,
        "featureEncoder": encoder.to_dict(),
        "calibration": runtime_calibration,
        "calibrationMetadata": {
            "version": config.calibration_version,
            "hierarchy": ["global", "marketType", "selection", "bookmaker"],
            **calibration_metadata,
        },
        "oodFeatureStats": runtime_ood,
        "ood": {
            "version": config.ood_version,
            "minimumReferenceSampleSize": config.ood_minimum_reference_sample_size,
            "robustZScoreThreshold": config.ood_robust_z_score_threshold,
            "severeRobustZScore": config.ood_severe_robust_z_score,
            **ood_artifact(ood_profiles),
        },
        "settlementProfiles": [profile.to_artifact() for profile in settlement_profiles],
        "uncertainty": {
            "version": config.uncertainty_version,
            "method": "fixture-cluster bootstrap dispersion plus calibration sampling error",
            "members": len(ensemble),
            "confidenceZScore": config.uncertainty_confidence_z,
            "conservativeLambda": config.uncertainty_conservative_lambda,
            "useLowerBound": config.uncertainty_use_lower_bound,
            "minimumUncertainty": config.minimum_uncertainty,
            "maximumUncertainty": config.maximum_uncertainty,
        },
        "training": {
            "rows": int(len(trained_rows)),
            "fixtures": int(trained_rows["FixtureId"].nunique()),
            "predictionStartUtc": trained_rows["PredictionTimestampUtc"].min().isoformat(),
            "predictionEndUtc": trained_rows["PredictionTimestampUtc"].max().isoformat(),
            "outcomeKnowledgeEndUtc": trained_through.isoformat(),
            "legacyModelVersions": sorted(trained_rows["LegacyModelVersion"].unique().tolist()),
            "model2026Versions": sorted(trained_rows["Model2026Version"].unique().tolist()),
            "allCandidateSidesRequired": True,
            "quarterLinesRequireOrdinalEvidence": True,
        },
        "experiment": experiment,
        "dataset": {"path": str(dataset.source_path), "sha256": dataset.sha256, **dataset.metadata},
    }


def train_bot_g(
    dataset: CandidateDataset,
    config: BotGConfig,
    *,
    evaluate_final_test: bool = False,
    final_test_start: str | None = None,
    families: Iterable[str] = ("logistic", "catboost", "xgboost", "lightgbm"),
    quick: bool = False,
    synthetic: bool = False,
    repository_root: Path | None = None,
) -> TrainingResult:
    synthetic = bool(synthetic or dataset.metadata.get("declaredSynthetic", False))
    rows = engineer_features(dataset.rows)
    split = final_holdout(rows, config.final_test_fraction, final_test_start)
    prediction_cutoff = split.final_start - pd.Timedelta(hours=config.embargo_hours)
    knowledge_cutoff = prediction_cutoff - pd.Timedelta(hours=config.outcome_lag_hours)
    rows_per_fixture = len(rows.iloc[split.development]) / rows.iloc[split.development]["FixtureId"].nunique()
    minimum_train, minimum_validation = _minimum_fixture_counts(rows_per_fixture, config)
    folds = expanding_folds(
        rows,
        split.development,
        config.outer_folds,
        minimum_train,
        minimum_validation,
        config.embargo_hours,
        config.outcome_lag_hours,
    )
    assert_oof_fixture_integrity(rows, folds)

    ablation_report: dict[str, Any] = {}
    oof_by_ablation: dict[str, np.ndarray] = {}
    ablation_row_signature: str | None = None
    fold_number: np.ndarray | None = None
    fold_details: list[dict[str, Any]] = []
    y_all = rows["TargetPositiveReturn"].to_numpy(dtype=int)
    resolved_before_final = (
        (rows["PredictionTimestampUtc"] < prediction_cutoff)
        & (rows["OutcomeAvailableUtc"] < knowledge_cutoff)
    ).to_numpy(dtype=bool)
    for ablation in ABLATIONS:
        prediction, current_fold, details = _oof_logit(rows, folds, ablation, config)
        mask = np.isfinite(prediction) & resolved_before_final
        row_signature = hashlib.sha256(
            "\n".join(rows.loc[mask, "CandidateId"].astype(str)).encode("utf-8")
        ).hexdigest()
        if ablation_row_signature is None:
            ablation_row_signature = row_signature
        elif row_signature != ablation_row_signature:
            raise RuntimeError("Ablations did not evaluate the exact same temporal OOF rows.")
        oof_by_ablation[ablation] = prediction
        ablation_report[ablation] = {
            "sameRowsSignature": row_signature,
            "metrics": predictive_metrics(y_all[mask], prediction[mask]),
            "rows": int(mask.sum()),
        }
        if ablation == "market_both_context":
            fold_number = current_fold
            fold_details = details
    deployment_oof = oof_by_ablation["market_both_context"]
    oof_mask = np.isfinite(deployment_oof) & (
        rows["OutcomeAvailableUtc"] < knowledge_cutoff
    ).to_numpy()
    if int(oof_mask.sum()) < config.minimum_validation_rows:
        raise ValueError("Insufficient leakage-safe OOF rows for calibration.")
    market_baseline = rows.loc[oof_mask, "MarketNoVigProbability"].to_numpy(dtype=float)
    market_metrics = predictive_metrics(y_all[oof_mask], market_baseline)

    family_report = _family_comparison(
        rows,
        folds,
        deployment_oof,
        families,
        config,
        quick,
        resolved_before_final,
    )
    calibration_profiles, calibration_metadata = fit_hierarchical_calibration(
        rows.loc[oof_mask].reset_index(drop=True),
        deployment_oof[oof_mask],
        y_all[oof_mask],
        config.calibration,
    )
    calibration_metadata["evidenceAvailableThroughUtc"] = (
        rows.loc[oof_mask, "OutcomeAvailableUtc"].max().isoformat()
    )
    calibration_metadata["source"] = "temporal fixture-grouped OOF predictions only"
    calibration_metadata["rows"] = int(oof_mask.sum())
    calibration_metadata["candidateRowsSha256"] = hashlib.sha256(
        "\n".join(rows.loc[oof_mask, "CandidateId"].astype(str)).encode("utf-8")
    ).hexdigest()
    calibrated_oof, oof_reliability = calibrate_hierarchical(
        rows.loc[oof_mask].reset_index(drop=True),
        deployment_oof[oof_mask],
        calibration_profiles,
        config.calibration,
    )
    oof_metrics = predictive_metrics(y_all[oof_mask], calibrated_oof)
    oof_rows = rows.loc[oof_mask].copy().reset_index(drop=True)
    oof_rows["RawMetaProbability"] = deployment_oof[oof_mask]
    oof_rows["FinalProbability"] = calibrated_oof
    oof_rows["CalibrationReliability"] = oof_reliability
    if fold_number is not None:
        oof_rows["Fold"] = fold_number[oof_mask]

    training_mask = (
        rows.index.isin(split.development)
        & (rows["PredictionTimestampUtc"] < prediction_cutoff)
        & (rows["OutcomeAvailableUtc"] < knowledge_cutoff)
    )
    trained_rows = rows.loc[training_mask].copy().reset_index(drop=True)
    if len(trained_rows) < config.minimum_training_rows:
        raise ValueError(
            f"At least {config.minimum_training_rows} resolved candidate rows are required before "
            f"the final-test cutoff; found {len(trained_rows)}."
        )
    encoder = FeatureEncoder.fit(trained_rows, "market_both_context")
    train_x = encoder.transform(trained_rows)
    train_y = trained_rows["TargetPositiveReturn"].to_numpy(dtype=int)
    train_offset = market_logit(trained_rows, config.calibration.clip)
    central = LogitResidualModel(config.l2, config.max_iterations).fit(
        train_x, train_y, train_offset
    )
    ensemble = bootstrap_logit_ensemble(
        train_x,
        train_y,
        train_offset,
        trained_rows["FixtureId"].astype(str).to_numpy(),
        config.bootstrap_models,
        config.seed,
        config.l2,
        config.max_iterations,
    )
    ood_profiles = fit_ood_profiles(train_x, encoder.feature_names)
    settlement_profiles = fit_settlement_profiles(
        trained_rows,
        config.thresholds.minimum_settlement_effective_sample_size,
    )

    root = repository_root or Path(__file__).resolve().parents[2]
    run_timestamp = datetime.now(timezone.utc).isoformat()
    experiment_id = hashlib.sha256(
        (dataset.sha256 + config.configuration_version + config.model_version + run_timestamp)
        .encode("utf-8")
    ).hexdigest()[:32]
    experiment = {
        "experimentId": experiment_id,
        "runTimestampUtc": run_timestamp,
        "createdAtUtc": run_timestamp,
        "datasetVersion": dataset.sha256,
        "configurationVersion": config.configuration_version,
        "featureSchemaVersion": config.feature_schema_version,
        "modelVersion": config.model_version,
        "trainingThroughUtc": trained_rows["OutcomeAvailableUtc"].max().isoformat(),
        "artifactFile": f"{config.model_version}.json",
        "seed": config.seed,
        "python": sys.version,
        "platform": platform.platform(),
        "packages": _package_versions(),
        "git": _git_metadata(root),
        "config": config.to_dict(),
        "finalTest": {
            "startUtc": split.final_start.isoformat(),
            "rows": int(len(split.final_test)),
            "fixtures": int(rows.iloc[split.final_test]["FixtureId"].nunique()),
            "evaluated": bool(evaluate_final_test),
            "untouchedWhenFalse": True,
        },
    }
    artifact = _artifact(
        dataset,
        config,
        encoder,
        central,
        ensemble,
        calibration_profiles,
        calibration_metadata,
        ood_profiles,
        settlement_profiles,
        trained_rows,
        experiment,
        synthetic,
    )

    final_rows: pd.DataFrame | None = None
    final_report: dict[str, Any] = {
        "status": "LOCKED",
        "message": "Final test was not read for metrics. Use --evaluate-final-test explicitly once.",
    }
    drift: dict[str, Any] | None = None
    if evaluate_final_test:
        final_input = rows.iloc[split.final_test].copy().reset_index(drop=True)
        final_rows = _predict_deployment(
            final_input,
            encoder,
            central,
            ensemble,
            calibration_profiles,
            settlement_profiles,
            ood_profiles,
            config,
        )
        model_metrics = predictive_metrics(
            final_rows["TargetPositiveReturn"].to_numpy(dtype=int),
            final_rows["FinalProbability"].to_numpy(dtype=float),
        )
        final_market_metrics = predictive_metrics(
            final_rows["TargetPositiveReturn"].to_numpy(dtype=int),
            final_rows["MarketNoVigProbability"].to_numpy(dtype=float),
        )
        published_economic = economic_metrics(final_rows, final_rows["Published"])
        drift = drift_report(train_x, encoder.transform(final_input), encoder.feature_names)
        final_report = {
            "status": "EVALUATED",
            "predictive": model_metrics,
            "marketBaseline": final_market_metrics,
            "economicPublished": published_economic,
            "allCandidatesEconomic": economic_metrics(final_rows),
            "coverage": coverage_metrics(final_rows),
            "evBuckets": ev_buckets(final_rows),
            "pairedF": paired_f_comparison(final_rows),
            "fixtureBootstrap": fixture_bootstrap(
                final_rows,
                "FinalProbability",
                config.bootstrap_metric_samples if not quick else min(50, config.bootstrap_metric_samples),
                config.seed + 99,
            ),
            "drift": drift,
            "promotion": promotion_scorecard(
                model_metrics,
                final_market_metrics,
                published_economic,
                config.promotion_minimum_fixtures,
                True,
                len(fold_details) + 1,
            ),
        }
    report = {
        "experiment": experiment,
        "dataset": {"sha256": dataset.sha256, **dataset.metadata},
        "walkForwardFolds": fold_details,
        "marketBaselineOof": market_metrics,
        "ablationsOofSameRows": ablation_report,
        "familyComparisonOof": family_report,
        "calibrationTrainingOof": {
            "metrics": oof_metrics,
            "metadata": calibration_metadata,
            "warning": "Calibrator diagnostics use its OOF training predictions; promotion uses final/backtest only.",
        },
        "finalTest": final_report,
        "deploymentFit": {
            "centralConverged": central.converged_,
            "ensembleMembers": len(ensemble),
            "ensembleConverged": int(sum(member.converged_ for member in ensemble)),
            "allConverged": bool(
                central.converged_ and all(member.converged_ for member in ensemble)
            ),
        },
        "synthetic": synthetic,
        "realMetricsGenerated": not synthetic and evaluate_final_test,
        "warning": (
            "Synthetic outputs are structural checks only; every metric is non-production."
            if synthetic
            else (
                "Only development/OOF diagnostics exist; the final test remains locked."
                if not evaluate_final_test
                else "Final-test metrics are real only to the extent the immutable input provenance is valid."
            )
        ),
        "activationAllowed": bool(
            not synthetic
            and evaluate_final_test
            and final_report.get("promotion", {}).get("status") == "PASS"
            and central.converged_
            and all(member.converged_ for member in ensemble)
        ),
    }
    artifact["deployable"] = report["activationAllowed"]
    return TrainingResult(artifact, report, oof_rows, final_rows, synthetic)


def _json_default(value: Any) -> Any:
    if isinstance(value, (np.integer,)):
        return int(value)
    if isinstance(value, (np.floating,)):
        return float(value)
    if isinstance(value, (pd.Timestamp, datetime)):
        return value.isoformat()
    raise TypeError(f"Object of type {type(value).__name__} is not JSON serializable")


def write_training_outputs(
    result: TrainingResult,
    output_dir: Path,
    config: BotGConfig,
    *,
    activate: bool,
) -> dict[str, str]:
    if activate:
        if result.synthetic:
            raise ValueError("Synthetic Bot G artifacts can never be activated.")
        if not result.report.get("activationAllowed"):
            raise ValueError("Activation requires a real evaluated final test and a PASS promotion scorecard.")
    output_dir.mkdir(parents=True, exist_ok=True)
    artifact_path = output_dir / f"{config.model_version}.json"
    report_path = output_dir / f"{config.model_version}.report.json"
    oof_path = output_dir / f"{config.model_version}.oof.csv"
    contract_path = output_dir / f"{config.feature_schema_version}.contract.json"
    final_path = output_dir / f"{config.model_version}.final-test.csv"
    immutable_outputs = [artifact_path, report_path, oof_path]
    if result.final_rows is not None:
        immutable_outputs.append(final_path)
    existing = [str(path) for path in immutable_outputs if path.exists()]
    if existing:
        raise FileExistsError(
            "Bot G versioned outputs are immutable; bump model_version and configuration_version. "
            f"Existing paths: {', '.join(existing)}"
        )
    expected_contract = json.dumps(contract_document(), indent=2)
    if contract_path.exists() and contract_path.read_text(encoding="utf-8") != expected_contract:
        raise FileExistsError(
            "The existing feature-schema contract differs; bump feature_schema_version."
        )
    artifact_path.write_text(
        json.dumps(result.artifact, indent=2, ensure_ascii=False, default=_json_default, allow_nan=False),
        encoding="utf-8",
    )
    report_path.write_text(
        json.dumps(result.report, indent=2, ensure_ascii=False, default=_json_default, allow_nan=False),
        encoding="utf-8",
    )
    result.oof_rows.to_csv(oof_path, index=False)
    if result.final_rows is not None:
        result.final_rows.to_csv(final_path, index=False)
    if not contract_path.exists():
        write_contract(contract_path)
    if activate:
        temporary = output_dir / ".active.json.tmp"
        shutil.copyfile(artifact_path, temporary)
        os.replace(temporary, output_dir / "active.json")
    return {
        "artifact": str(artifact_path),
        "report": str(report_path),
        "oof": str(oof_path),
        "contract": str(contract_path),
    }
