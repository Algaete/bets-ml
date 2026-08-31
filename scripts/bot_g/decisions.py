from __future__ import annotations

from typing import Any

import numpy as np
import pandas as pd

from .config import BotGConfig
from .settlement import expected_profit, requires_ordinal_distribution, tilt_distribution_to_positive_probability
from .settlement_profiles import SettlementProfile, select_settlement_profile


def _clamp01(values: pd.Series) -> pd.Series:
    numeric = pd.to_numeric(values, errors="coerce")
    return numeric.where(np.isfinite(numeric), 0.0).clip(0.0, 1.0)


def _normalize_positive(values: pd.Series, upper: float) -> pd.Series:
    numeric = pd.to_numeric(values, errors="coerce")
    numeric = numeric.where(np.isfinite(numeric), 0.0)
    return (numeric / max(float(upper), 1e-9)).clip(0.0, 1.0)


def _selection_scores(rows: pd.DataFrame, config: BotGConfig) -> pd.Series:
    """Mirror BotGSelector.Score so offline and runtime rank identically."""
    weights = config.ranking
    ev = _normalize_positive(rows["ConservativeExpectedValue"], 0.20)
    edge = _normalize_positive(rows["ConservativeEdge"], 0.15)
    reliability = _clamp01(rows["CalibrationReliability"])
    quality = _clamp01(rows["DataQualityScore"])
    inverse_uncertainty = 1.0 - _normalize_positive(
        rows["ProbabilityUncertainty"], config.maximum_uncertainty
    )
    agreement = _clamp01(rows["ContextAgreementScore"])
    return (
        weights.conservative_expected_value_weight * ev
        + weights.conservative_edge_weight * edge
        + weights.calibration_reliability_weight * reliability
        + weights.data_quality_weight * quality
        + weights.inverse_uncertainty_weight * inverse_uncertainty
        + weights.context_agreement_weight * agreement
    ).clip(0.0, 1.0)


def _winner_indices(rows: pd.DataFrame) -> pd.Index:
    """Mirror BotGSelector.SelectBestPerFixture deterministic tie-breaks."""
    approved = rows.loc[rows["Decision"].eq("Approved")].copy()
    if approved.empty:
        return approved.index

    approved["__BookmakerSort"] = approved["Bookmaker"].astype(str).str.casefold()
    approved["__MarketSort"] = approved["MarketType"].map(
        {"TotalGoals": 0, "HomeTeamGoals": 1, "AwayTeamGoals": 2}
    )
    approved["__SelectionSort"] = approved["Selection"].map({"Over": 0, "Under": 1})
    winners = approved.sort_values(
        [
            "FixtureId",
            "GSelectionScore",
            "ConservativeExpectedValue",
            "ConservativeEdge",
            "__BookmakerSort",
            "__MarketSort",
            "__SelectionSort",
            "Line",
            "CandidateId",
        ],
        ascending=[True, False, False, False, True, True, True, True, True],
        kind="stable",
    ).groupby("FixtureId", sort=False).head(1)
    return winners.index


def _monotonicity_violation_indices(rows: pd.DataFrame, tolerance: float = 1e-9) -> pd.Index:
    """Return every modeled row in a line curve that violates GOALS probability ordering."""
    comparable = rows.loc[~rows["Decision"].eq("Abstain")].copy()
    comparable = comparable.loc[np.isfinite(pd.to_numeric(
        comparable["FinalProbability"], errors="coerce"
    ))]
    if comparable.empty:
        return comparable.index
    comparable["__BookmakerGroup"] = comparable["Bookmaker"].astype(str).str.casefold()
    violations: list[Any] = []
    keys = ["FixtureId"]
    if "PredictionTimestampUtc" in comparable.columns:
        keys.append("PredictionTimestampUtc")
    keys.extend(["__BookmakerGroup", "MarketType", "Selection"])
    for _, group in comparable.groupby(keys, sort=False):
        selection = str(group["Selection"].iloc[0])
        ordered = group.sort_values("Line", kind="stable")
        probabilities = ordered["FinalProbability"].to_numpy(dtype=float)
        if len(probabilities) < 2:
            continue
        differences = np.diff(probabilities)
        violates = bool(np.any(differences > tolerance)) if selection == "Over" else bool(
            np.any(differences < -tolerance)
        )
        if violates:
            violations.extend(ordered.index.tolist())
    return pd.Index(violations)


def apply_decisions(
    rows: pd.DataFrame,
    final_probability: np.ndarray,
    lower_probability: np.ndarray,
    upper_probability: np.ndarray,
    conservative_probability: np.ndarray,
    uncertainty: np.ndarray,
    ood: np.ndarray,
    calibration_reliability: np.ndarray,
    calibration_effective_sample_size: np.ndarray,
    profiles: list[SettlementProfile],
    config: BotGConfig,
) -> pd.DataFrame:
    result = rows.copy().reset_index(drop=True)
    result["FinalProbability"] = final_probability
    result["ProbabilityLowerBound"] = lower_probability
    result["ProbabilityUpperBound"] = upper_probability
    result["ProbabilityUncertainty"] = uncertainty
    result["ConservativeProbability"] = conservative_probability
    result["ConservativeEvProbability"] = conservative_probability
    result["CalibrationReliability"] = calibration_reliability
    result["CalibrationEffectiveSampleSize"] = calibration_effective_sample_size
    result["OutOfDistributionScore"] = ood
    result["RawEdge"] = result["FinalProbability"] - result["MarketNoVigProbability"]
    result["ConservativeEdge"] = result["ConservativeProbability"] - result["MarketNoVigProbability"]

    expected: list[float] = []
    conservative_expected: list[float] = []
    ordinal_available: list[bool] = []
    settlement_profile_key: list[str | None] = []
    for index, row in enumerate(result.itertuples(index=False)):
        profile = select_settlement_profile(
            row,
            profiles,
            config.thresholds.minimum_settlement_effective_sample_size,
        )
        needs_ordinal = requires_ordinal_distribution(float(row.Line))
        evidence = bool(profile and profile.ordinal_evidence_available)
        ordinal_available.append(not needs_ordinal or evidence)
        if profile is None:
            settlement_profile_key.append(None)
        else:
            settlement_profile_key.append(
                f"{profile.market_type}|{profile.selection}|{profile.bookmaker}|{profile.line:g}"
            )
        if needs_ordinal and profile is not None:
            raw_distribution = tilt_distribution_to_positive_probability(
                profile.probabilities, float(final_probability[index])
            )
            conservative_distribution = tilt_distribution_to_positive_probability(
                profile.probabilities, float(conservative_probability[index])
            )
            expected.append(expected_profit(raw_distribution, float(row.SelectedOdds)))
            conservative_expected.append(
                expected_profit(conservative_distribution, float(row.SelectedOdds))
            )
        elif not needs_ordinal:
            expected.append(float(final_probability[index] * row.SelectedOdds - 1.0))
            conservative_expected.append(
                float(conservative_probability[index] * row.SelectedOdds - 1.0)
            )
        else:
            expected.append(float("nan"))
            conservative_expected.append(float("nan"))
    result["ExpectedValue"] = expected
    result["ConservativeExpectedValue"] = conservative_expected
    result["OrdinalEvidenceAvailable"] = ordinal_available
    result["SettlementProfileKey"] = settlement_profile_key

    thresholds = config.thresholds
    decisions: list[str] = []
    reasons: list[str] = []
    for row in result.itertuples(index=False):
        abstain: list[str] = []
        rejected: list[str] = []
        if config.football_intelligence.enabled and not bool(
            getattr(row, "FootballIntelligenceEvidenceUsable", False)
        ):
            abstain.append("FootballIntelligenceUnavailable")
        if not row.OrdinalEvidenceAvailable:
            abstain.append("InsufficientSettlementEvidence")
        if row.ProbabilityUncertainty > thresholds.maximum_uncertainty:
            abstain.append("HighUncertainty")
        if row.OutOfDistributionScore > thresholds.maximum_ood_score:
            abstain.append("OutOfDistribution")
        if row.DataQualityScore < thresholds.minimum_data_quality:
            abstain.append("LowDataQuality")
        if row.CalibrationEffectiveSampleSize < config.calibration.minimum_effective_sample_size:
            abstain.append("InsufficientCalibrationEvidence")
        if row.CalibrationReliability < thresholds.minimum_calibration_reliability:
            abstain.append("CalibrationUnreliable")
        if row.HistoryCount < thresholds.minimum_history:
            abstain.append("InsufficientHistory")
        if row.ModelDisagreement > thresholds.maximum_model_disagreement:
            abstain.append("ModelDisagreement")
        if row.OddsAgeMinutes > thresholds.maximum_odds_age_minutes:
            abstain.append("StaleOdds")
        if not (thresholds.minimum_odds <= row.SelectedOdds <= thresholds.maximum_odds):
            rejected.append("OddsOutOfRange")
        if row.FinalProbability < thresholds.minimum_final_probability:
            rejected.append("LowFinalProbability")
        if row.ConservativeEdge < thresholds.minimum_conservative_edge:
            rejected.append("LowConservativeEdge")
        if not np.isfinite(row.ConservativeExpectedValue) or row.ConservativeExpectedValue < thresholds.minimum_conservative_ev:
            rejected.append("LowConservativeEV")
        if abstain:
            decisions.append("Abstain")
            reasons.append("|".join(abstain))
        elif rejected:
            decisions.append("Rejected")
            reasons.append("|".join(rejected))
        else:
            decisions.append("Approved")
            reasons.append("ApprovedConservativeEvidence")
    result["Decision"] = decisions
    result["DecisionReason"] = reasons
    monotonicity_violations = _monotonicity_violation_indices(result)
    if len(monotonicity_violations):
        result.loc[monotonicity_violations, "Decision"] = "Abstain"
        result.loc[monotonicity_violations, "DecisionReason"] = result.loc[
            monotonicity_violations, "DecisionReason"
        ].map(lambda reason: "|".join(dict.fromkeys(
            [part for part in str(reason).split("|") if part]
            + ["PredictionMonotonicityViolation"]
        )))
    result["GSelectionScore"] = _selection_scores(result, config)
    result["Published"] = False
    winner_indices = _winner_indices(result)
    lower_ranked = result["Decision"].eq("Approved") & ~result.index.isin(winner_indices)
    result.loc[lower_ranked, "Decision"] = "Rejected"
    result.loc[lower_ranked, "DecisionReason"] = result.loc[
        lower_ranked, "DecisionReason"
    ].map(lambda reason: "|".join(dict.fromkeys(
        [part for part in str(reason).split("|") if part] + ["LowerRankedCandidate"]
    )))
    result.loc[winner_indices, "Published"] = True
    return result


def coverage_metrics(rows: pd.DataFrame) -> dict[str, Any]:
    counts = rows["Decision"].value_counts().to_dict()
    total = max(len(rows), 1)
    return {
        "candidatesEvaluated": int(len(rows)),
        "candidatesApproved": int(counts.get("Approved", 0)),
        "candidatesRejected": int(counts.get("Rejected", 0)),
        "candidatesAbstained": int(counts.get("Abstain", 0)),
        "candidatesPublished": int(rows["Published"].sum()),
        "coverageRate": float(counts.get("Approved", 0) / total),
        "publicationRate": float(rows["Published"].sum() / total),
        "reasonCounts": rows["DecisionReason"].value_counts().to_dict(),
    }
