from __future__ import annotations

import json
import tempfile
from dataclasses import replace
from pathlib import Path

import numpy as np
import pandas as pd

from .backtest import run_walk_forward_backtest, write_backtest_outputs
from .config import BotGConfig, CalibrationConfig, Thresholds
from .contracts import load_candidate_dataset, validate_candidate_frame
from .features import FeatureEncoder, engineer_features, market_logit
from .modeling import LogitResidualModel
from .pipeline import train_bot_g, write_training_outputs
from .preflight import build_preflight_report
from .settlement import expected_profit, settle
from .splits import assert_oof_fixture_integrity, expanding_folds, final_holdout
from .synthetic import synthetic_candidate_frame, write_synthetic_candidates


def _test_config(seed: int) -> BotGConfig:
    return BotGConfig(
        seed=seed,
        minimum_training_rows=120,
        minimum_validation_rows=24,
        outer_folds=3,
        inner_folds=3,
        bootstrap_models=4,
        bootstrap_metric_samples=12,
        max_iterations=350,
        thresholds=replace(
            Thresholds(),
            minimum_settlement_effective_sample_size=8.0,
            maximum_uncertainty=0.25,
        ),
        calibration=replace(CalibrationConfig(), minimum_rows=24),
    )


def _assert_settlement_economics() -> None:
    assert settle("Over", 2.25, 3, 2.0).state == "Win"
    half_loss = settle("Over", 2.25, 2, 2.0)
    assert half_loss.state == "HalfLoss" and abs(half_loss.profit_per_unit + 0.5) < 1e-12
    half_win = settle("Under", 2.25, 2, 2.0)
    assert half_win.state == "HalfWin" and abs(half_win.profit_per_unit - 0.5) < 1e-12
    assert settle("Over", 3.0, 3, 1.9).state == "Push"
    distribution = {"Win": 0.40, "HalfWin": 0.10, "Push": 0.10, "HalfLoss": 0.10, "Loss": 0.30}
    assert abs(expected_profit(distribution, 2.0) - 0.10) < 1e-12


def _assert_neutrality(rows) -> None:
    engineered = engineer_features(rows.iloc[:20])
    encoder = FeatureEncoder.fit(engineered, "market_both_context")
    x = encoder.transform(engineered)
    neutral = LogitResidualModel()
    neutral.mean_ = np.zeros(x.shape[1])
    neutral.scale_ = np.ones(x.shape[1])
    neutral.coefficient_ = np.zeros(x.shape[1])
    neutral.intercept_ = 0.0
    actual = neutral.predict_proba(x, market_logit(engineered))
    expected = engineered["MarketNoVigProbability"].to_numpy(dtype=float)
    assert np.allclose(actual, expected, atol=1e-12)


def _assert_temporal_guards(frame, config: BotGConfig) -> None:
    leaked = frame.copy()
    quote = leaked.loc[0, "QuoteId"]
    leaked.loc[leaked["QuoteId"].eq(quote), "FeatureAsOfUtc"] = leaked.loc[
        leaked["QuoteId"].eq(quote), "PredictionTimestampUtc"
    ].pipe(pd.to_datetime, utc=True).add(pd.Timedelta(seconds=1)).to_numpy()
    try:
        validate_candidate_frame(leaked, config)
    except ValueError as exc:
        assert "Anti-leakage" in str(exc)
    else:
        raise AssertionError("Feature cutoff leakage was not rejected.")


def run_self_test(fixtures: int = 480, seed: int = 20260819) -> dict[str, object]:
    """Exercise training/backtest without SQL and without creating production active.json."""
    config = _test_config(seed)
    raw = synthetic_candidate_frame(fixtures, seed)
    _assert_settlement_economics()
    _assert_temporal_guards(raw, config)
    with tempfile.TemporaryDirectory(prefix="bot-g-selftest-") as temporary:
        root = Path(temporary)
        input_path = write_synthetic_candidates(root / "synthetic-candidates.csv", fixtures, seed)
        dataset = load_candidate_dataset(input_path, config)
        preflight = build_preflight_report(dataset, config)
        assert preflight["trainingReady"] is True
        assert preflight["publicationEnabled"] is False
        assert preflight["automaticActivationEnabled"] is False
        _assert_neutrality(dataset.rows)

        engineered = engineer_features(dataset.rows)
        held_out = final_holdout(engineered, config.final_test_fraction)
        fold_rows = engineered.iloc[held_out.development]
        rows_per_fixture = len(fold_rows) / fold_rows["FixtureId"].nunique()
        folds = expanding_folds(
            engineered,
            held_out.development,
            config.outer_folds,
            max(20, int(np.ceil(config.minimum_training_rows / rows_per_fixture))),
            max(8, int(np.ceil(config.minimum_validation_rows / rows_per_fixture))),
            config.embargo_hours,
            config.outcome_lag_hours,
        )
        assert_oof_fixture_integrity(engineered, folds)
        assert not set(engineered.iloc[held_out.development]["FixtureId"]) & set(
            engineered.iloc[held_out.final_test]["FixtureId"]
        )
        for fold in folds:
            assert engineered.iloc[fold.train]["OutcomeAvailableUtc"].max() < fold.knowledge_cutoff

        trained = train_bot_g(
            dataset,
            config,
            evaluate_final_test=True,
            families=("logistic", "catboost", "xgboost", "lightgbm"),
            quick=True,
            synthetic=False,  # IsSynthetic in the contract must still force safety mode.
        )
        artifact = trained.artifact
        assert artifact["model"]["type"] == "LogitResidualLogistic"
        assert artifact["synthetic"] is True and artifact["deployable"] is False
        assert artifact["configurationVersion"] == config.configuration_version
        assert artifact["family"] == "GOALS"
        assert tuple(artifact["supportedMarkets"]) == config.supported_markets
        assert artifact["modelVersion"] == config.model_version
        assert artifact["featureSchemaVersion"] == config.feature_schema_version
        assert artifact["trainingContractVersion"] == config.training_contract_version
        assert artifact["footballIntelligence"]["enabled"] is True
        assert artifact["footballIntelligence"]["version"] == (
            config.football_intelligence.version
        )
        assert artifact["runtimeSettings"] == {
            "maximumAbsoluteResidualLogit": 4.0,
            "minimumSettlementEffectiveSampleSize": (
                config.thresholds.minimum_settlement_effective_sample_size
            ),
            "settlementEvidenceLagHours": int(config.outcome_lag_hours),
        }
        assert artifact["uncertainty"] == {
            "version": config.uncertainty_version,
            "method": "fixture-cluster bootstrap dispersion plus calibration sampling error",
            "members": len(artifact["ensemble"]),
            "confidenceZScore": config.uncertainty_confidence_z,
            "conservativeLambda": config.uncertainty_conservative_lambda,
            "useLowerBound": config.uncertainty_use_lower_bound,
            "minimumUncertainty": config.minimum_uncertainty,
            "maximumUncertainty": config.maximum_uncertainty,
        }
        assert artifact["ood"]["version"] == config.ood_version
        assert artifact["ood"]["method"] == "robust-mad-percentile-v1"
        assert artifact["ood"]["minimumReferenceSampleSize"] == (
            config.ood_minimum_reference_sample_size
        )
        assert artifact["ood"]["robustZScoreThreshold"] == config.ood_robust_z_score_threshold
        assert artifact["ood"]["severeRobustZScore"] == config.ood_severe_robust_z_score
        assert artifact["training"]["legacyModelVersions"]
        assert artifact["training"]["model2026Versions"]
        assert len(artifact["training"]["marketLineages"]) == 3
        assert artifact["calibration"] and artifact["oodFeatureStats"]
        assert all(member.get("name") for member in artifact["ensemble"])
        runtime_names = {item["name"] for item in artifact["model"]["features"]}
        assert all(name and name[0].islower() for name in runtime_names)
        assert trained.report["realMetricsGenerated"] is False

        output = root / "artifacts"
        paths = write_training_outputs(trained, output, config, activate=False)
        assert not (output / "active.json").exists()
        try:
            write_training_outputs(trained, output, config, activate=True)
        except ValueError as exc:
            assert "Automatic Bot G activation is disabled" in str(exc)
        else:
            raise AssertionError("Synthetic artifact activation was not rejected.")
        assert not (output / "active.json").exists()
        parsed = json.loads(Path(paths["artifact"]).read_text(encoding="utf-8"))
        assert parsed["trainedThroughUtc"].endswith("+00:00")
        try:
            write_training_outputs(trained, output, config, activate=False)
        except FileExistsError as exc:
            assert "immutable" in str(exc)
        else:
            raise AssertionError("Versioned Bot G experiment outputs were overwritten.")

        backtest = run_walk_forward_backtest(
            dataset,
            config,
            include_final_test=False,
            quick=True,
            synthetic=False,
        )
        backtest_paths = write_backtest_outputs(backtest, output, config.model_version)
        assert backtest.report["finalTestIncluded"] is False
        assert backtest.report["realMetricsGenerated"] is False
        assert backtest.report["activationAllowed"] is False
        assert not (output / "active.json").exists()

        return {
            "status": "PASS",
            "syntheticFixtures": fixtures,
            "syntheticRows": len(dataset.rows),
            "trainingFolds": len(trained.report["walkForwardFolds"]),
            "backtestCompletedFolds": sum(
                fold["status"] == "COMPLETE" for fold in backtest.report["folds"]
            ),
            "artifactContract": {
                "modelFeatures": len(artifact["model"]["features"]),
                "ensembleMembers": len(artifact["ensemble"]),
                "calibrationProfiles": len(artifact["calibration"]),
                "oodFeatures": len(artifact["oodFeatureStats"]),
                "settlementProfiles": len(artifact["settlementProfiles"]),
            },
            "outputsWereTemporary": True,
            "activeJsonCreated": False,
            "realMetricsGenerated": False,
            "backtestOutputs": sorted(backtest_paths),
        }
