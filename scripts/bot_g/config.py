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
class FootballIntelligenceConfig:
    enabled: bool = True
    version: str = "football-intelligence-adjustment-1.0.0"
    weight: float = 0.35
    maximum_probability_adjustment: float = 0.04
    minimum_team_confidence: float = 0.60
    maximum_snapshot_age_minutes: int = 4_320
    minimum_actionable_facts: int = 1
    minimum_independent_sources: int = 1
    attack_weight: float = 0.35
    defence_weight: float = 0.25
    width_weight: float = 0.20
    set_piece_weight: float = 0.20


@dataclass(frozen=True)
class BaseModelLineageConfig:
    legacy_model_versions: tuple[str, ...] = ("goals_v1",)
    model2026_versions: tuple[str, ...] = ()


def _default_market_lineages() -> dict[str, BaseModelLineageConfig]:
    home = "targethomegoals-2026-08-09-trial-15"
    away = "targetawaygoals-2026-08-09-trial-48"
    total = "targettotalgoals-2026-08-09-trial-53"
    return {
        "TotalGoals": BaseModelLineageConfig(model2026_versions=(total,)),
        "HomeTeamGoals": BaseModelLineageConfig(model2026_versions=(home, away)),
        "AwayTeamGoals": BaseModelLineageConfig(model2026_versions=(away, home)),
    }


@dataclass(frozen=True)
class BotGConfig:
    configuration_version: str = "bot-g-goals-market-intelligence-1.1.0"
    feature_schema_version: str = "bot-g-goals-features-1.0.0"
    training_contract_version: str = "bot-g-training-export-1.1.0"
    model_version: str = "bot-g-market-meta-1.1.0"
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
    market_lineages: dict[str, BaseModelLineageConfig] = field(
        default_factory=_default_market_lineages
    )
    football_intelligence: FootballIntelligenceConfig = field(
        default_factory=FootballIntelligenceConfig
    )
    thresholds: Thresholds = field(default_factory=Thresholds)
    calibration: CalibrationConfig = field(default_factory=CalibrationConfig)
    ranking: RankingConfig = field(default_factory=RankingConfig)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    def validate(self) -> "BotGConfig":
        if self.configuration_version != "bot-g-goals-market-intelligence-1.1.0":
            raise ValueError("Bot G trainer only accepts the live v1.1 configuration identity.")
        if self.feature_schema_version != "bot-g-goals-features-1.0.0":
            raise ValueError("Bot G v1.1 requires the unchanged 1.0 feature-vector schema.")
        if self.training_contract_version != "bot-g-training-export-1.1.0":
            raise ValueError("Bot G v1.1 requires training export contract 1.1.0.")
        if self.model_version != "bot-g-market-meta-1.1.0":
            raise ValueError("Bot G v1.1 trainer must emit the 1.1 meta-model identity.")
        if set(self.market_lineages) != set(self.supported_markets):
            raise ValueError("Bot G must declare exactly one lineage policy per supported market.")
        for market, lineage in self.market_lineages.items():
            for label, values in (
                ("legacy", lineage.legacy_model_versions),
                ("Models 2026", lineage.model2026_versions),
            ):
                normalized = tuple(value.strip() for value in values if value and value.strip())
                if not normalized or len(set(normalized)) != len(values):
                    raise ValueError(f"{market} {label} lineage must be non-empty and unique.")
        intelligence = self.football_intelligence
        if not intelligence.enabled:
            raise ValueError("Bot G live v1.1 requires Football Intelligence enabled.")
        if not intelligence.version.strip():
            raise ValueError("Football Intelligence version is required.")
        bounded = (
            ("weight", intelligence.weight, 0.0, 1.0),
            ("maximum_probability_adjustment", intelligence.maximum_probability_adjustment, 0.0, 0.25),
            ("minimum_team_confidence", intelligence.minimum_team_confidence, 0.0, 1.0),
            ("attack_weight", intelligence.attack_weight, 0.0, 1.0),
            ("defence_weight", intelligence.defence_weight, 0.0, 1.0),
            ("width_weight", intelligence.width_weight, 0.0, 1.0),
            ("set_piece_weight", intelligence.set_piece_weight, 0.0, 1.0),
        )
        for name, value, minimum, maximum in bounded:
            if not minimum <= value <= maximum:
                raise ValueError(f"Football Intelligence {name} is outside [{minimum}, {maximum}].")
        if abs(
            intelligence.attack_weight + intelligence.defence_weight
            + intelligence.width_weight + intelligence.set_piece_weight - 1.0
        ) > 1e-6:
            raise ValueError("Football Intelligence market weights must add up to 1.0.")
        if (
            intelligence.maximum_snapshot_age_minutes < 1
            or intelligence.minimum_actionable_facts < 1
            or intelligence.minimum_independent_sources < 1
        ):
            raise ValueError("Football Intelligence evidence thresholds must be positive.")
        return self
