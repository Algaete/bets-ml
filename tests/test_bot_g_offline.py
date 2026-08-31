from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
import pandas as pd


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPOSITORY_ROOT / "scripts"))

from bot_g.calibration import CalibrationProfile, calibrate_hierarchical  # noqa: E402
from bot_g.config import BotGConfig, CalibrationConfig  # noqa: E402
from bot_g.contracts import load_candidate_dataset, validate_candidate_frame  # noqa: E402
from bot_g.decisions import (  # noqa: E402
    apply_decisions,
    _monotonicity_violation_indices,
    _selection_scores,
    _winner_indices,
)
from bot_g.evaluation import paired_f_comparison, promotion_scorecard  # noqa: E402
from bot_g.features import FeatureEncoder, engineer_features, market_logit  # noqa: E402
from bot_g.modeling import LogitResidualModel  # noqa: E402
from bot_g.ood import OodFeatureProfile, ood_score  # noqa: E402
from bot_g.settlement import settle  # noqa: E402
from bot_g.splits import assert_oof_fixture_integrity, expanding_folds, final_holdout  # noqa: E402
from bot_g.synthetic import synthetic_candidate_frame, write_synthetic_candidates  # noqa: E402


class BotGOfflineContractTests(unittest.TestCase):
    def test_default_conservative_probability_matches_runtime_lower_bound(self) -> None:
        self.assertTrue(BotGConfig().uncertainty_use_lower_bound)

    def test_selection_score_and_tie_breaks_match_runtime(self) -> None:
        config = BotGConfig()
        score_rows = pd.DataFrame({
            "ConservativeExpectedValue": [0.10, -0.10],
            "ConservativeEdge": [0.075, -0.10],
            "CalibrationReliability": [0.50, float("nan")],
            "DataQualityScore": [0.75, 2.0],
            "ProbabilityUncertainty": [0.125, float("nan")],
            "ContextAgreementScore": [0.50, -1.0],
        })
        scores = _selection_scores(score_rows, config)
        self.assertAlmostEqual(0.525, scores.iloc[0])
        # C# treats non-finite normalized uncertainty as zero, hence inverse=one.
        self.assertAlmostEqual(0.20, scores.iloc[1])

        tied = pd.DataFrame({
            "FixtureId": [1, 1, 1],
            "CandidateId": ["c", "b", "a"],
            "Decision": ["Approved", "Approved", "Approved"],
            "GSelectionScore": [0.8, 0.8, 0.8],
            "ConservativeExpectedValue": [0.05, 0.06, 0.06],
            "ConservativeEdge": [0.04, 0.03, 0.03],
            "Bookmaker": ["Zeta", "beta", "Alpha"],
            "MarketType": ["TotalGoals", "TotalGoals", "TotalGoals"],
            "Selection": ["Over", "Over", "Over"],
            "Line": [2.5, 2.5, 2.5],
        })
        self.assertEqual([2], _winner_indices(tied).tolist())

    def test_prediction_monotonicity_gate_detects_inconsistent_line_curves(self) -> None:
        curves = pd.DataFrame({
            "FixtureId": [1, 1, 1, 1],
            "Bookmaker": ["Book", "book", "Book", "Book"],
            "MarketType": ["TotalGoals"] * 4,
            "Selection": ["Over", "Over", "Under", "Under"],
            "Line": [2.5, 3.5, 2.5, 3.5],
            "FinalProbability": [0.50, 0.60, 0.40, 0.55],
            "Decision": ["Approved"] * 4,
        })
        # Over must not increase as the line rises; the valid Under curve is untouched.
        self.assertEqual([0, 1], _monotonicity_violation_indices(curves).tolist())

    def test_configured_conservative_probability_drives_edge_and_ev(self) -> None:
        rows = pd.DataFrame({
            "Line": [2.5],
            "SelectedOdds": [2.0],
            "MarketNoVigProbability": [0.5],
            "DataQualityScore": [0.9],
            "HistoryCount": [20],
            "ModelDisagreement": [0.0],
            "OddsAgeMinutes": [1.0],
            "ContextAgreementScore": [1.0],
            "FixtureId": ["fixture"],
            "CandidateId": ["candidate"],
            "Bookmaker": ["book"],
            "MarketType": ["TotalGoals"],
            "Selection": ["Over"],
        })
        decisions = apply_decisions(
            rows,
            final_probability=np.array([0.70]),
            lower_probability=np.array([0.55]),
            upper_probability=np.array([0.80]),
            conservative_probability=np.array([0.60]),
            uncertainty=np.array([0.05]),
            ood=np.array([0.0]),
            calibration_reliability=np.array([0.9]),
            calibration_effective_sample_size=np.array([100.0]),
            profiles=[],
            config=BotGConfig(uncertainty_use_lower_bound=False),
        )
        self.assertAlmostEqual(0.60, decisions.loc[0, "ConservativeProbability"])
        self.assertAlmostEqual(0.10, decisions.loc[0, "ConservativeEdge"])
        self.assertAlmostEqual(0.20, decisions.loc[0, "ConservativeExpectedValue"])

    def test_only_fixture_winner_remains_approved_offline(self) -> None:
        rows = pd.DataFrame({
            "Line": [2.5, 2.5],
            "SelectedOdds": [2.0, 2.0],
            "MarketNoVigProbability": [0.5, 0.5],
            "DataQualityScore": [0.9, 0.9],
            "HistoryCount": [20, 20],
            "ModelDisagreement": [0.0, 0.0],
            "OddsAgeMinutes": [1.0, 1.0],
            "ContextAgreementScore": [1.0, 1.0],
            "FixtureId": ["fixture", "fixture"],
            "CandidateId": ["best", "lower"],
            "Bookmaker": ["alpha", "zeta"],
            "MarketType": ["TotalGoals", "TotalGoals"],
            "Selection": ["Over", "Over"],
            "PredictionTimestampUtc": pd.to_datetime([
                "2026-01-01T00:00:00Z", "2026-01-01T00:00:00Z"
            ]),
        })
        decisions = apply_decisions(
            rows,
            final_probability=np.array([0.70, 0.65]),
            lower_probability=np.array([0.62, 0.58]),
            upper_probability=np.array([0.78, 0.72]),
            conservative_probability=np.array([0.62, 0.58]),
            uncertainty=np.array([0.04, 0.05]),
            ood=np.array([0.0, 0.0]),
            calibration_reliability=np.array([0.9, 0.9]),
            calibration_effective_sample_size=np.array([100.0, 100.0]),
            profiles=[],
            config=BotGConfig(),
        )
        self.assertEqual(1, int(decisions["Published"].sum()))
        self.assertEqual("Approved", decisions.loc[0, "Decision"])
        self.assertEqual("Rejected", decisions.loc[1, "Decision"])
        self.assertIn("LowerRankedCandidate", decisions.loc[1, "DecisionReason"])

    def test_hierarchical_calibration_uses_raw_probability_at_every_level(self) -> None:
        rows = pd.DataFrame({
            "MarketType": ["TotalGoals"],
            "Selection": ["Over"],
            "Bookmaker": ["Book"],
        })
        timestamp = pd.Timestamp("2025-01-01T00:00:00Z")
        profiles = [
            CalibrationProfile(
                "GOALS", "*", "*", "*", "Platt", 0.5, 1.0, 0.0, 0.0,
                10.0, 10, timestamp,
            ),
            CalibrationProfile(
                "GOALS", "TotalGoals", "*", "*", "Platt", -0.3, 1.2, 0.0, 0.0,
                10.0, 10, timestamp,
            ),
        ]
        config = CalibrationConfig(
            global_prior_strength=10.0,
            market_prior_strength=10.0,
            side_prior_strength=10.0,
            bookmaker_prior_strength=10.0,
        )
        raw = np.array([0.4])
        calibrated, _ = calibrate_hierarchical(rows, raw, profiles, config)
        global_target = profiles[0].calibrate(raw)[0]
        market_target = profiles[1].calibrate(raw)[0]
        expected = 0.5 * (0.5 * raw[0] + 0.5 * global_target) + 0.5 * market_target
        self.assertAlmostEqual(expected, calibrated[0])

    def test_ood_mad_scale_matches_runtime_even_with_wide_percentiles(self) -> None:
        profile = OodFeatureProfile(
            "feature", 0.0, 1.0, -100.0, 100.0, -100.0, 100.0, 100,
        )
        actual = ood_score(np.array([[10.0]]), ["feature"], [profile])[0]
        robust_z = 10.0 / 1.4826
        expected = np.clip((robust_z - 3.5) / (8.0 - 3.5), 0.0, 1.0)
        self.assertAlmostEqual(expected, actual)
        unavailable = ood_score(
            np.array([[0.0]]), ["feature"], [profile], minimum_reference_sample_size=101
        )[0]
        self.assertEqual(1.0, unavailable)

    def test_jsonl_two_sided_contract_loads(self) -> None:
        with tempfile.TemporaryDirectory(prefix="bot-g-unit-") as temporary:
            path = Path(temporary) / "input.jsonl"
            synthetic_candidate_frame(120).to_json(path, orient="records", lines=True)
            dataset = load_candidate_dataset(path, BotGConfig())
        self.assertEqual(240, len(dataset.rows))
        self.assertTrue(dataset.metadata["declaredSynthetic"])

    def test_quarter_line_settlement(self) -> None:
        self.assertEqual(settle("Over", 2.25, 2, 2.0).state, "HalfLoss")
        self.assertEqual(settle("Under", 2.25, 2, 2.0).state, "HalfWin")
        self.assertEqual(settle("Over", 2.75, 3, 1.9).state, "HalfWin")
        self.assertEqual(settle("Under", 2.75, 3, 1.9).state, "HalfLoss")

    def test_feature_timestamp_may_equal_prediction_but_not_follow_it(self) -> None:
        frame = synthetic_candidate_frame(120)
        quote = frame.loc[0, "QuoteId"]
        mask = frame["QuoteId"].eq(quote)
        frame.loc[mask, "FeatureAsOfUtc"] = frame.loc[mask, "PredictionTimestampUtc"].to_numpy()
        validate_candidate_frame(frame, BotGConfig())
        frame.loc[mask, "FeatureAsOfUtc"] = (
            pd.to_datetime(frame.loc[mask, "PredictionTimestampUtc"], utc=True)
            + pd.Timedelta(seconds=1)
        ).to_numpy()
        with self.assertRaisesRegex(ValueError, "Anti-leakage"):
            validate_candidate_frame(frame, BotGConfig())

    def test_market_offset_is_neutral_at_zero_adjustment(self) -> None:
        with tempfile.TemporaryDirectory(prefix="bot-g-unit-") as temporary:
            path = write_synthetic_candidates(Path(temporary) / "input.csv", 120)
            rows = engineer_features(load_candidate_dataset(path, BotGConfig()).rows.iloc[:20])
        encoder = FeatureEncoder.fit(rows, "market_both_context")
        x = encoder.transform(rows)
        model = LogitResidualModel()
        model.mean_ = np.zeros(x.shape[1])
        model.scale_ = np.ones(x.shape[1])
        model.coefficient_ = np.zeros(x.shape[1])
        self.assertTrue(np.allclose(
            model.predict_proba(x, market_logit(rows)),
            rows["MarketNoVigProbability"],
            atol=1e-12,
        ))

    def test_paired_f_report_has_predictive_economics_and_shared_differences(self) -> None:
        rows = pd.DataFrame({
            "FixtureId": ["shared", "g-only", "f-only"],
            "FPublished": [True, False, True],
            "Published": [True, True, False],
            "ProfitLoss": [0.9, -1.0, 0.8],
            "SettlementState": ["Win", "Loss", "Win"],
            "SelectedOdds": [1.9, 2.0, 1.8],
            "TargetPositiveReturn": [1, 0, 1],
            "FinalProbability": [0.60, 0.55, 0.52],
            "ConservativeEdge": [0.05, 0.03, 0.01],
            "ConservativeExpectedValue": [0.08, 0.04, 0.01],
            "FProbability": [0.58, 0.51, 0.59],
            "FEdge": [0.03, 0.01, 0.04],
            "FExpectedValue": [0.05, 0.01, 0.06],
        })
        report = paired_f_comparison(rows)
        self.assertTrue(report["available"])
        self.assertEqual(1, report["shared"]["resolved"])
        self.assertIn("brier", report["shared"]["gPredictive"])
        self.assertIn("logLoss", report["shared"]["fPredictive"])
        self.assertAlmostEqual(0.02, report["sharedMeanDifferences"]["probabilityGMinusF"])
        self.assertEqual(1, report["gRejectedButFSelected"])
        self.assertEqual(1, report["fRejectedButGSelected"])

    def test_promotion_requires_independent_fixture_count_not_candidate_rows(self) -> None:
        model = {
            "brier": 0.10, "logLoss": 0.30,
            "calibrationSlope": 1.0, "calibrationIntercept": 0.0,
        }
        market = {"brier": 0.12, "logLoss": 0.32}
        economics = {"resolved": 1_000, "fixtures": 199, "yield": 0.02, "profitFactor": 1.2}
        report = promotion_scorecard(model, market, economics, 200, True, 2)
        self.assertFalse(report["checks"]["minimumResolvedFixtures"])
        self.assertEqual("FAIL", report["status"])

    def test_fixture_groups_and_outcome_lag(self) -> None:
        with tempfile.TemporaryDirectory(prefix="bot-g-unit-") as temporary:
            path = write_synthetic_candidates(Path(temporary) / "input.csv", 180)
            rows = engineer_features(load_candidate_dataset(path, BotGConfig()).rows)
        split = final_holdout(rows, 0.2)
        folds = expanding_folds(rows, split.development, 3, 30, 8, 24.0, 8.0)
        assert_oof_fixture_integrity(rows, folds)
        self.assertFalse(
            set(rows.iloc[split.development]["FixtureId"])
            & set(rows.iloc[split.final_test]["FixtureId"])
        )
        for fold in folds:
            self.assertLess(rows.iloc[fold.train]["OutcomeAvailableUtc"].max(), fold.knowledge_cutoff)


if __name__ == "__main__":
    unittest.main()
