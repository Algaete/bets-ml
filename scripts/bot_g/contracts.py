from __future__ import annotations

import hashlib
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd

from .config import BotGConfig
from .settlement import settle


REQUIRED_COLUMNS = (
    "CandidateId",
    "QuoteId",
    "FixtureId",
    "FixtureDateUtc",
    "PredictionTimestampUtc",
    "FeatureAsOfUtc",
    "OddsTimestampUtc",
    "OutcomeAvailableUtc",
    "League",
    "HomeTeam",
    "AwayTeam",
    "Bookmaker",
    "MarketType",
    "Selection",
    "Line",
    "OverOdds",
    "UnderOdds",
    "SelectedOdds",
    "LegacyPrediction",
    "LegacyModelVersion",
    "LegacyModelTrainedThroughUtc",
    "Prediction2026",
    "Model2026Version",
    "Model2026TrainedThroughUtc",
    "ContextPrediction",
    "HistoricalMean",
    "HistoricalStd",
    "HistoryCount",
    "DataQualityScore",
    "ActualValue",
)

UTC_COLUMNS = (
    "FixtureDateUtc",
    "PredictionTimestampUtc",
    "FeatureAsOfUtc",
    "OddsTimestampUtc",
    "OutcomeAvailableUtc",
    "LegacyModelTrainedThroughUtc",
    "Model2026TrainedThroughUtc",
)

NUMERIC_COLUMNS = (
    "Line",
    "OverOdds",
    "UnderOdds",
    "SelectedOdds",
    "LegacyPrediction",
    "Prediction2026",
    "ContextPrediction",
    "HistoricalMean",
    "HistoricalStd",
    "HistoryCount",
    "DataQualityScore",
    "ActualValue",
)


@dataclass(frozen=True)
class CandidateDataset:
    rows: pd.DataFrame
    source_path: Path
    sha256: str
    metadata: dict[str, Any]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _load(path: Path) -> pd.DataFrame:
    suffix = path.suffix.lower()
    if suffix in {".jsonl", ".ndjson"}:
        return pd.read_json(path, lines=True)
    if suffix == ".json":
        return pd.read_json(path)
    if suffix == ".csv":
        return pd.read_csv(path)
    raise ValueError("Bot G input must be CSV, JSON, JSONL or NDJSON.")


def _finite(frame: pd.DataFrame, columns: tuple[str, ...]) -> None:
    invalid: list[str] = []
    for column in columns:
        converted = pd.to_numeric(frame[column], errors="coerce")
        if converted.isna().any() or not np.isfinite(converted.to_numpy(dtype=float)).all():
            invalid.append(column)
        frame[column] = converted
    if invalid:
        raise ValueError(f"Required numeric columns contain missing/non-finite values: {', '.join(invalid)}")


def _optional_boolean(frame: pd.DataFrame, column: str) -> None:
    if column not in frame.columns:
        return
    mapping = {
        True: True, False: False, 1: True, 0: False,
        "true": True, "false": False, "1": True, "0": False,
    }
    normalized = frame[column].map(
        lambda value: mapping.get(value.strip().lower(), None)
        if isinstance(value, str)
        else mapping.get(value, None)
    )
    if normalized.isna().any():
        raise ValueError(f"Optional {column} must contain only true/false or 1/0 values.")
    frame[column] = normalized.astype(bool)


def _validate_quote_pairs(frame: pd.DataFrame) -> None:
    quote_columns = [
        "FixtureId", "FixtureDateUtc", "PredictionTimestampUtc", "FeatureAsOfUtc",
        "OddsTimestampUtc", "OutcomeAvailableUtc", "League", "HomeTeam", "AwayTeam",
        "Bookmaker", "MarketType", "Line", "OverOdds", "UnderOdds", "LegacyPrediction",
        "LegacyModelVersion", "LegacyModelTrainedThroughUtc", "Prediction2026",
        "Model2026Version", "Model2026TrainedThroughUtc", "ContextPrediction",
        "HistoricalMean", "HistoricalStd", "HistoryCount", "DataQualityScore", "ActualValue",
    ]
    problems: list[str] = []
    for quote_id, rows in frame.groupby("QuoteId", sort=False):
        if len(rows) != 2 or set(rows["Selection"]) != {"Over", "Under"}:
            problems.append(str(quote_id))
            continue
        for column in quote_columns:
            if rows[column].nunique(dropna=False) != 1:
                problems.append(str(quote_id))
                break
        over = rows.loc[rows["Selection"] == "Over"].iloc[0]
        under = rows.loc[rows["Selection"] == "Under"].iloc[0]
        if not math.isclose(float(over["SelectedOdds"]), float(over["OverOdds"]), abs_tol=1e-9):
            problems.append(str(quote_id))
        if not math.isclose(float(under["SelectedOdds"]), float(under["UnderOdds"]), abs_tol=1e-9):
            problems.append(str(quote_id))
    if problems:
        preview = ", ".join(dict.fromkeys(problems[:10]))
        raise ValueError(
            "Every QuoteId must contain exactly one consistent Over and Under candidate; "
            f"invalid QuoteId values: {preview}"
        )


def _validate_temporal(frame: pd.DataFrame) -> None:
    checks = {
        "FeatureAsOfUtc must not be after PredictionTimestampUtc":
            frame["FeatureAsOfUtc"] > frame["PredictionTimestampUtc"],
        "OddsTimestampUtc must not be after PredictionTimestampUtc":
            frame["OddsTimestampUtc"] > frame["PredictionTimestampUtc"],
        "PredictionTimestampUtc must be before FixtureDateUtc":
            frame["PredictionTimestampUtc"] >= frame["FixtureDateUtc"],
        "OutcomeAvailableUtc must be after PredictionTimestampUtc":
            frame["OutcomeAvailableUtc"] <= frame["PredictionTimestampUtc"],
        "OutcomeAvailableUtc must be after FixtureDateUtc":
            frame["OutcomeAvailableUtc"] <= frame["FixtureDateUtc"],
        "Legacy model cutoff must be before prediction timestamp":
            frame["LegacyModelTrainedThroughUtc"] >= frame["PredictionTimestampUtc"],
        "2026 model cutoff must be before prediction timestamp":
            frame["Model2026TrainedThroughUtc"] >= frame["PredictionTimestampUtc"],
    }
    failed = [message for message, mask in checks.items() if bool(mask.any())]
    if failed:
        raise ValueError("Anti-leakage validation failed: " + "; ".join(failed))


def validate_candidate_frame(frame: pd.DataFrame, config: BotGConfig) -> pd.DataFrame:
    missing = [column for column in REQUIRED_COLUMNS if column not in frame.columns]
    if missing:
        raise ValueError(f"Bot G candidate dataset is missing columns: {', '.join(missing)}")
    frame = frame.copy()
    if frame.empty:
        raise ValueError("Bot G candidate dataset is empty.")

    for column in UTC_COLUMNS:
        frame[column] = pd.to_datetime(frame[column], utc=True, errors="coerce")
        if frame[column].isna().any():
            raise ValueError(f"{column} contains invalid or missing UTC timestamps.")
    _finite(frame, NUMERIC_COLUMNS)
    for column in ("FProbability", "FEdge", "FExpectedValue"):
        if column not in frame.columns:
            continue
        supplied = frame[column].notna()
        converted = pd.to_numeric(frame[column], errors="coerce")
        if (supplied & converted.isna()).any() or not np.isfinite(
            converted.loc[supplied].to_numpy(dtype=float)
        ).all():
            raise ValueError(f"Optional {column} contains a non-finite supplied value.")
        frame[column] = converted
    if "FProbability" in frame.columns:
        supplied = frame["FProbability"].notna()
        if (~frame.loc[supplied, "FProbability"].between(0, 1)).any():
            raise ValueError("Optional FProbability must be in [0,1].")
    _optional_boolean(frame, "FPublished")
    _optional_boolean(frame, "IsSynthetic")
    if "IsSynthetic" in frame.columns and frame["IsSynthetic"].nunique() != 1:
        raise ValueError("IsSynthetic must be constant for the entire candidate universe.")

    for column in (
        "CandidateId", "QuoteId", "FixtureId", "League", "HomeTeam", "AwayTeam",
        "Bookmaker", "MarketType", "Selection", "LegacyModelVersion", "Model2026Version",
    ):
        frame[column] = frame[column].astype("string").str.strip()
        if frame[column].isna().any() or frame[column].eq("").any():
            raise ValueError(f"{column} contains blank identifiers/categories.")

    if frame["CandidateId"].duplicated().any():
        raise ValueError("CandidateId must be unique.")
    unsupported_markets = sorted(set(frame["MarketType"]) - set(config.supported_markets))
    unsupported_sides = sorted(set(frame["Selection"]) - set(config.supported_sides))
    if unsupported_markets or unsupported_sides:
        raise ValueError(
            f"Unsupported markets/sides: markets={unsupported_markets}, sides={unsupported_sides}"
        )
    if (frame[["OverOdds", "UnderOdds", "SelectedOdds"]] <= 1.0).any().any():
        raise ValueError("Both sides and selected decimal odds must exceed 1.0; no-vig is mandatory.")
    if (frame["Line"] < 0).any():
        raise ValueError("Line cannot be negative.")
    if (~frame["Line"].mul(4).round().eq(frame["Line"].mul(4))).any():
        raise ValueError("Only integer, half and quarter Asian lines are supported.")
    if (frame["HistoryCount"] < 0).any() or (~frame["DataQualityScore"].between(0, 1)).any():
        raise ValueError("HistoryCount must be nonnegative and DataQualityScore must be in [0,1].")
    if (~frame["HistoryCount"].round().eq(frame["HistoryCount"])).any():
        raise ValueError("HistoryCount must contain whole counts.")
    if (frame["HistoricalStd"] < 0).any():
        raise ValueError("HistoricalStd cannot be negative.")
    nonnegative = (
        "LegacyPrediction", "Prediction2026", "ContextPrediction", "HistoricalMean", "ActualValue"
    )
    if (frame[list(nonnegative)] < 0).any().any():
        raise ValueError("Predictions, historical means and ActualValue must be nonnegative.")
    if (~frame["ActualValue"].round().eq(frame["ActualValue"])).any():
        raise ValueError("ActualValue must be an integer goal count for the row's market.")

    _validate_quote_pairs(frame)
    _validate_temporal(frame)

    semantic = ["FixtureId", "Bookmaker", "MarketType", "Selection", "Line", "OddsTimestampUtc"]
    if frame.duplicated(semantic).any():
        raise ValueError("Duplicate semantic candidates were found.")

    settlements = [
        settle(row.Selection, float(row.Line), float(row.ActualValue), float(row.SelectedOdds))
        for row in frame.itertuples(index=False)
    ]
    frame["SettlementState"] = [item.state for item in settlements]
    frame["ProfitLoss"] = [item.profit_per_unit for item in settlements]
    frame["TargetPositiveReturn"] = [item.positive_return for item in settlements]
    frame["RawImpliedProbability"] = 1.0 / frame["SelectedOdds"]
    over_raw = 1.0 / frame["OverOdds"]
    under_raw = 1.0 / frame["UnderOdds"]
    margin = over_raw + under_raw
    frame["OddsMargin"] = margin - 1.0
    frame["MarketNoVigProbability"] = np.where(
        frame["Selection"].eq("Over"), over_raw / margin, under_raw / margin
    )
    if not np.allclose(
        frame.groupby("QuoteId")["MarketNoVigProbability"].sum().to_numpy(), 1.0, atol=1e-9
    ):
        raise RuntimeError("No-vig probabilities do not sum to one within quotes.")
    frame["OddsAgeMinutes"] = (
        frame["PredictionTimestampUtc"] - frame["OddsTimestampUtc"]
    ).dt.total_seconds() / 60.0
    frame = frame.sort_values(
        ["PredictionTimestampUtc", "FixtureId", "QuoteId", "Selection"], kind="stable"
    ).reset_index(drop=True)
    return frame


def load_candidate_dataset(path: str | Path, config: BotGConfig) -> CandidateDataset:
    resolved = Path(path).expanduser().resolve()
    if not resolved.is_file():
        raise FileNotFoundError(f"Bot G input not found: {resolved}")
    rows = validate_candidate_frame(_load(resolved), config)
    metadata = {
        "rows": int(len(rows)),
        "fixtures": int(rows["FixtureId"].nunique()),
        "quotes": int(rows["QuoteId"].nunique()),
        "predictionStartUtc": rows["PredictionTimestampUtc"].min().isoformat(),
        "predictionEndUtc": rows["PredictionTimestampUtc"].max().isoformat(),
        "outcomeEndUtc": rows["OutcomeAvailableUtc"].max().isoformat(),
        "markets": sorted(rows["MarketType"].unique().tolist()),
        "bookmakers": sorted(rows["Bookmaker"].unique().tolist()),
        "modelVersions": {
            "legacy": sorted(rows["LegacyModelVersion"].unique().tolist()),
            "model2026": sorted(rows["Model2026Version"].unique().tolist()),
        },
        "declaredSynthetic": bool(rows["IsSynthetic"].all()) if "IsSynthetic" in rows else False,
    }
    return CandidateDataset(rows, resolved, _sha256(resolved), metadata)


def contract_document() -> dict[str, Any]:
    return {
        "requiredColumns": list(REQUIRED_COLUMNS),
        "utcColumns": list(UTC_COLUMNS),
        "candidateUniverse": "Exactly two rows (Over and Under) per immutable QuoteId.",
        "target": "TargetPositiveReturn derived from five-state Asian settlement.",
        "antiLeakage": [
            "FeatureAsOfUtc <= PredictionTimestampUtc",
            "OddsTimestampUtc <= PredictionTimestampUtc",
            "base model trained-through timestamps < PredictionTimestampUtc",
            "training folds require OutcomeAvailableUtc plus configured lag before validation",
            "FixtureId is atomic across every split and bootstrap",
        ],
    }


def write_contract(path: Path) -> None:
    path.write_text(json.dumps(contract_document(), indent=2), encoding="utf-8")
