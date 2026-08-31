from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .config import (
    BaseModelLineageConfig,
    BotGConfig,
    CalibrationConfig,
    FootballIntelligenceConfig,
    Thresholds,
)


def load_config(path: str | Path | None) -> BotGConfig:
    if path is None:
        return BotGConfig().validate()
    resolved = Path(path).expanduser().resolve()
    values: dict[str, Any] = json.loads(resolved.read_text(encoding="utf-8"))
    if not isinstance(values, dict):
        raise ValueError("Bot G configuration JSON must contain one object.")
    if "thresholds" in values:
        values["thresholds"] = Thresholds(**values["thresholds"])
    if "calibration" in values:
        values["calibration"] = CalibrationConfig(**values["calibration"])
    if "football_intelligence" in values:
        values["football_intelligence"] = FootballIntelligenceConfig(
            **values["football_intelligence"]
        )
    if "market_lineages" in values:
        values["market_lineages"] = {
            market: BaseModelLineageConfig(
                legacy_model_versions=tuple(lineage.get("legacy_model_versions", ("goals_v1",))),
                model2026_versions=tuple(lineage.get("model2026_versions", ())),
            )
            for market, lineage in values["market_lineages"].items()
        }
    for name in ("supported_markets", "supported_sides"):
        if name in values:
            values[name] = tuple(values[name])
    return BotGConfig(**values).validate()


def parse_families(value: str) -> tuple[str, ...]:
    families = tuple(item.strip().lower() for item in value.split(",") if item.strip())
    supported = {"logistic", "catboost", "xgboost", "lightgbm"}
    unknown = sorted(set(families) - supported)
    if unknown:
        raise ValueError(f"Unsupported comparison families: {', '.join(unknown)}")
    if "logistic" not in families:
        families = ("logistic", *families)
    return families
