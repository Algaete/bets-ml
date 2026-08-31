from __future__ import annotations

import hashlib
import json
from dataclasses import asdict
from typing import Any

from .config import BotGConfig
from .contracts import CandidateDataset


def build_preflight_report(dataset: CandidateDataset, config: BotGConfig) -> dict[str, Any]:
    """Return a deterministic, side-effect-free readiness report for one immutable export."""
    config.validate()
    rows = dataset.rows
    reasons: list[str] = []
    warnings: list[str] = []
    markets = set(rows["MarketType"].astype(str))
    missing_markets = sorted(set(config.supported_markets) - markets)
    if missing_markets:
        reasons.append("MISSING_SUPPORTED_MARKETS:" + ",".join(missing_markets))
    if len(rows) < config.minimum_training_rows:
        reasons.append(
            f"INSUFFICIENT_ROWS:{len(rows)}<{config.minimum_training_rows}"
        )

    fixture_count = int(rows["FixtureId"].nunique())
    usable_intelligence = int(rows["FootballIntelligenceEvidenceUsable"].sum())
    if usable_intelligence == 0:
        reasons.append("NO_USABLE_FOOTBALL_INTELLIGENCE_EVIDENCE")
    unusable_intelligence = int(len(rows) - usable_intelligence)
    if unusable_intelligence:
        warnings.append(
            "Rows without usable Football Intelligence remain auditable but must abstain at decision time."
        )

    declared_synthetic = bool(dataset.metadata.get("declaredSynthetic", False))
    if declared_synthetic:
        warnings.append("Synthetic input is structural-only and can never be promoted.")
    if fixture_count < config.promotion_minimum_fixtures:
        warnings.append(
            f"Promotion sample is below {config.promotion_minimum_fixtures} independent fixtures."
        )

    config_payload = json.dumps(
        asdict(config), sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")
    rows_by_market = {
        str(market): {
            "rows": int(len(group)),
            "fixtures": int(group["FixtureId"].nunique()),
            "usableFootballIntelligenceRows": int(
                group["FootballIntelligenceEvidenceUsable"].sum()
            ),
            "legacyModelLineages": sorted(
                group["LegacyModelVersion"].astype(str).unique().tolist()
            ),
            "model2026Lineages": sorted(
                group["Model2026Version"].astype(str).unique().tolist()
            ),
        }
        for market, group in rows.groupby("MarketType", sort=True)
    }
    training_ready = not reasons
    data_promotion_ready = bool(
        training_ready
        and not declared_synthetic
        and fixture_count >= config.promotion_minimum_fixtures
    )
    return {
        "status": "PASS" if training_ready else "BLOCKED",
        "trainingReady": training_ready,
        "dataPromotionReviewReady": data_promotion_ready,
        "publicationEnabled": False,
        "automaticActivationEnabled": False,
        "finalTestStillRequired": True,
        "reasons": reasons,
        "warnings": warnings,
        "identity": {
            "configurationVersion": config.configuration_version,
            "featureSchemaVersion": config.feature_schema_version,
            "trainingContractVersion": config.training_contract_version,
            "metaModelVersion": config.model_version,
            "footballIntelligenceVersion": config.football_intelligence.version,
        },
        "dataset": {
            "sha256": dataset.sha256,
            "configSha256": hashlib.sha256(config_payload).hexdigest(),
            "rows": int(len(rows)),
            "fixtures": fixture_count,
            "quotes": int(rows["QuoteId"].nunique()),
            "declaredSynthetic": declared_synthetic,
            "usableFootballIntelligenceRows": usable_intelligence,
            "unusableFootballIntelligenceRows": unusable_intelligence,
            "predictionStartUtc": rows["PredictionTimestampUtc"].min().isoformat(),
            "predictionEndUtc": rows["PredictionTimestampUtc"].max().isoformat(),
            "outcomeKnowledgeEndUtc": rows["OutcomeAvailableUtc"].max().isoformat(),
            "markets": rows_by_market,
        },
    }


def require_training_ready(report: dict[str, Any]) -> None:
    if report.get("trainingReady"):
        return
    reasons = report.get("reasons") or ["UNKNOWN_PREFLIGHT_FAILURE"]
    raise ValueError("Bot G preflight blocked training: " + "; ".join(map(str, reasons)))
