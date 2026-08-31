from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Any


@dataclass(frozen=True)
class Thresholds:
    minimum_odds: float = 1.60
    maximum_odds: float = 2.20
    minimum_final_probability: float = 0.54
    minimum_conservative_edge: float = 0.02
    minimum_conservative_ev: float = 0.015
    minimum_data_quality: float = 0.65
    minimum_calibration_reliability: float = 0.30
    minimum_history: int = 8
    maximum_uncertainty: float = 0.08
    maximum_ood_score: float = 0.70
    maximum_model_disagreement: float = 1.50
    maximum_odds_age_minutes: float = 120.0
    minimum_settlement_effective_sample_size: float = 40.0


@dataclass(frozen=True)
class CalibrationConfig:
    minimum_rows: int = 40
    minimum_effective_sample_size: float = 20.0
    global_prior_strength: float = 80.0
    market_prior_strength: float = 60.0
    side_prior_strength: float = 40.0
    bookmaker_prior_strength: float = 40.0
    clip: float = 1e-6


@dataclass(frozen=True)
class RankingConfig:
    conservative_expected_value_weight: float = 0.35
    conservative_edge_weight: float = 0.25
    calibration_reliability_weight: float = 0.15
    data_quality_weight: float = 0.10
    inverse_uncertainty_weight: float = 0.10
    context_agreement_weight: float = 0.05


@dataclass(frozen=True)
class BotGConfig:
    configuration_version: str = "bot-g-goals-market-1.0.0"
    feature_schema_version: str = "bot-g-goals-features-1.0.0"
    model_version: str = "bot-g-market-meta-1.0.0"
    calibration_version: str = "bot-g-calibration-1.0.0"
    uncertainty_version: str = "bot-g-uncertainty-1.0.0"
    ood_version: str = "bot-g-ood-1.0.0"
    seed: int = 20260819
    final_test_fraction: float = 0.20
    minimum_training_rows: int = 200
    minimum_validation_rows: int = 40
    promotion_minimum_fixtures: int = 200
    outer_folds: int = 4
    inner_folds: int = 4
    embargo_hours: float = 24.0
    outcome_lag_hours: float = 8.0
    bootstrap_models: int = 12
    bootstrap_metric_samples: int = 300
    lower_quantile: float = 0.10
    uncertainty_confidence_z: float = 1.645
    uncertainty_conservative_lambda: float = 1.0
    uncertainty_use_lower_bound: bool = True
    minimum_uncertainty: float = 0.005
    maximum_uncertainty: float = 0.25
    ood_minimum_reference_sample_size: int = 30
    ood_robust_z_score_threshold: float = 3.5
    ood_severe_robust_z_score: float = 8.0
    l2: float = 1.0
    max_iterations: int = 1_000
    supported_markets: tuple[str, ...] = (
        "TotalGoals",
        "HomeTeamGoals",
        "AwayTeamGoals",
    )
    supported_sides: tuple[str, ...] = ("Over", "Under")
    thresholds: Thresholds = field(default_factory=Thresholds)
    calibration: CalibrationConfig = field(default_factory=CalibrationConfig)
    ranking: RankingConfig = field(default_factory=RankingConfig)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)
