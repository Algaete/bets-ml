from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pandas as pd


ABLATIONS = ("market_only", "market_legacy", "market_2026", "market_both", "market_both_context")

# (offline dataframe column, BotGFeatures.ToNumericVector key).  Artifact names are
# deliberately restricted to features the .NET runtime can reproduce exactly.
MARKET_NUMERIC = (
    ("SelectedOdds", "selectedOdds"),
    ("OppositeOdds", "oppositeOdds"),
    ("RawImpliedProbability", "rawImpliedProbability"),
    ("MarketNoVigProbability", "marketNoVigProbability"),
    ("OddsMargin", "oddsMargin"),
    ("OddsAgeMinutes", "oddsAgeMinutes"),
    ("Line", "line"),
)
LEGACY_NUMERIC = (("LegacyPrediction", "legacyPrediction"),)
MODEL_2026_NUMERIC = (("Prediction2026", "prediction2026"),)
BOTH_NUMERIC = (
    ("LegacyMinus2026", "legacyMinus2026"),
    ("AveragePrediction", "averagePrediction"),
    ("PredictionMinusLine", "predictionMinusLine"),
    ("AbsPredictionMinusLine", "absPredictionMinusLine"),
    ("ModelDisagreement", "modelDisagreement"),
)
CONTEXT_NUMERIC = (
    ("ContextPrediction", "contextPrediction"),
    ("ModelVsContextDistance", "modelVsContextDistance"),
    ("ModelVsContextSigma", "modelVsContextSigma"),
    ("ContextAgreementScore", "contextAgreementScore"),
    ("HistoryCount", "historyCount"),
    ("DataQualityScore", "dataQualityScore"),
)


def engineer_features(frame: pd.DataFrame) -> pd.DataFrame:
    result = frame.copy()
    result["OppositeOdds"] = np.where(
        result["Selection"].eq("Over"), result["UnderOdds"], result["OverOdds"]
    )
    result["LegacyMinus2026"] = result["LegacyPrediction"] - result["Prediction2026"]
    result["AveragePrediction"] = (result["LegacyPrediction"] + result["Prediction2026"]) / 2.0
    result["PredictionMinusLine"] = result["AveragePrediction"] - result["Line"]
    result["AbsPredictionMinusLine"] = np.abs(result["PredictionMinusLine"])
    result["ModelDisagreement"] = np.abs(result["LegacyMinus2026"])
    result["ModelVsContextDistance"] = np.abs(
        result["ContextPrediction"] - result["AveragePrediction"]
    )
    sigma = np.maximum(result["HistoricalStd"], 0.25)
    result["ModelVsContextSigma"] = result["ModelVsContextDistance"] / sigma
    result["ContextAgreementScore"] = np.exp(-0.5 * result["ModelVsContextSigma"] ** 2)
    return result


def numeric_columns(ablation: str) -> tuple[tuple[str, str], ...]:
    if ablation not in ABLATIONS:
        raise ValueError(f"Unknown ablation: {ablation!r}")
    columns = list(MARKET_NUMERIC)
    if ablation in {"market_legacy", "market_both", "market_both_context"}:
        columns.extend(LEGACY_NUMERIC)
    if ablation in {"market_2026", "market_both", "market_both_context"}:
        columns.extend(MODEL_2026_NUMERIC)
    if ablation in {"market_both", "market_both_context"}:
        columns.extend(BOTH_NUMERIC)
    if ablation == "market_both_context":
        columns.extend(CONTEXT_NUMERIC)
    return tuple(columns)


@dataclass(frozen=True)
class FeatureEncoder:
    ablation: str
    numeric: tuple[tuple[str, str], ...]
    feature_names: tuple[str, ...]

    @classmethod
    def fit(cls, frame: pd.DataFrame, ablation: str) -> "FeatureEncoder":
        numeric = numeric_columns(ablation)
        names = tuple(runtime_name for _, runtime_name in numeric)
        if len(set(names)) != len(names):
            raise RuntimeError("Bot G runtime feature names must be unique.")
        return cls(ablation, numeric, names)

    def transform(self, frame: pd.DataFrame) -> np.ndarray:
        columns: list[np.ndarray] = []
        for source_name, runtime_name in self.numeric:
            values = pd.to_numeric(frame[source_name], errors="coerce").to_numpy(dtype=float)
            if not np.isfinite(values).all():
                raise ValueError(
                    f"Engineered feature {source_name} ({runtime_name}) contains non-finite values."
                )
            columns.append(values)
        return np.column_stack(columns)

    def to_dict(self) -> dict[str, object]:
        return {
            "ablation": self.ablation,
            "numeric": [
                {"sourceColumn": source, "runtimeName": runtime}
                for source, runtime in self.numeric
            ],
            "featureNames": list(self.feature_names),
            "runtimeContract": "BotGFeatures.ToNumericVector",
        }


def market_logit(frame: pd.DataFrame, clip: float = 1e-6) -> np.ndarray:
    probability = np.clip(frame["MarketNoVigProbability"].to_numpy(dtype=float), clip, 1 - clip)
    return np.log(probability / (1.0 - probability))
