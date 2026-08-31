from __future__ import annotations

from pathlib import Path

import numpy as np
import pandas as pd


def synthetic_candidate_frame(fixtures: int = 480, seed: int = 20260819) -> pd.DataFrame:
    """Build a deterministic two-sided universe for structural tests only."""
    if fixtures < 120:
        raise ValueError("Synthetic Bot G data needs at least 120 fixtures for temporal folds.")
    rng = np.random.default_rng(seed)
    start = pd.Timestamp("2024-01-01T12:00:00Z")
    markets = ("TotalGoals", "HomeTeamGoals", "AwayTeamGoals")
    bookmakers = ("Betano", "Pinnacle", "SyntheticBook")
    leagues = ("Synthetic-A", "Synthetic-B", "Synthetic-C", "Synthetic-D")
    lines = {
        "TotalGoals": (2.25, 2.50, 2.75, 3.00),
        "HomeTeamGoals": (0.75, 1.00, 1.25, 1.50),
        "AwayTeamGoals": (0.75, 1.00, 1.25, 1.50),
    }
    records: list[dict[str, object]] = []
    for fixture_index in range(fixtures):
        prediction_time = start + pd.Timedelta(days=2 * fixture_index)
        fixture_time = prediction_time + pd.Timedelta(hours=24)
        market = markets[fixture_index % len(markets)]
        bookmaker = bookmakers[(fixture_index // len(markets)) % len(bookmakers)]
        league = leagues[(fixture_index // 7) % len(leagues)]
        line_options = lines[market]
        line = line_options[(fixture_index // (len(markets) * len(bookmakers))) % len(line_options)]

        seasonal = 0.22 * np.sin(fixture_index / 19.0) + 0.10 * np.cos(fixture_index / 7.0)
        home_rate = max(0.25, 1.45 + seasonal + rng.normal(0.0, 0.12))
        away_rate = max(0.20, 1.08 - 0.45 * seasonal + rng.normal(0.0, 0.10))
        actual_home = int(rng.poisson(home_rate))
        actual_away = int(rng.poisson(away_rate))
        true_mean = {
            "TotalGoals": home_rate + away_rate,
            "HomeTeamGoals": home_rate,
            "AwayTeamGoals": away_rate,
        }[market]
        actual = {
            "TotalGoals": actual_home + actual_away,
            "HomeTeamGoals": actual_home,
            "AwayTeamGoals": actual_away,
        }[market]

        # The market is informative but deliberately imperfect.  Neither base model sees outcomes.
        fair_over = float(np.clip(1.0 / (1.0 + np.exp(-(true_mean - line) / 0.72)), 0.12, 0.88))
        quoted_over = float(np.clip(fair_over + rng.normal(0.0, 0.035), 0.12, 0.88))
        overround = 1.045 + 0.01 * ((fixture_index % 5) / 4.0)
        over_odds = 1.0 / (quoted_over * overround)
        under_odds = 1.0 / ((1.0 - quoted_over) * overround)
        legacy = max(0.0, true_mean + 0.16 + rng.normal(0.0, 0.24))
        model_2026 = max(0.0, true_mean - 0.05 + rng.normal(0.0, 0.18))
        historical_mean = max(0.0, true_mean + rng.normal(0.0, 0.16))
        historical_std = float(np.clip(0.80 + rng.normal(0.0, 0.09), 0.45, 1.25))
        context = max(0.0, 0.70 * historical_mean + 0.30 * model_2026)
        quote_id = f"synthetic-q-{fixture_index:05d}"
        fixture_id = f"synthetic-f-{fixture_index:05d}"
        f_side = "Over" if fair_over >= 0.5 else "Under"
        over_intelligence_adjustment = float(0.012 * np.sin(fixture_index / 13.0))
        intelligence_cutoff = prediction_time - pd.Timedelta(hours=2)
        for side, selected_odds in (("Over", over_odds), ("Under", under_odds)):
            records.append({
                "CandidateId": f"{quote_id}-{side.lower()}",
                "QuoteId": quote_id,
                "FixtureId": fixture_id,
                "FixtureDateUtc": fixture_time.isoformat(),
                "PredictionTimestampUtc": prediction_time.isoformat(),
                "FeatureAsOfUtc": prediction_time.isoformat(),
                "OddsTimestampUtc": (prediction_time - pd.Timedelta(minutes=25)).isoformat(),
                "OutcomeAvailableUtc": (fixture_time + pd.Timedelta(hours=4)).isoformat(),
                "League": league,
                "HomeTeam": f"Synthetic Home {fixture_index % 31:02d}",
                "AwayTeam": f"Synthetic Away {fixture_index % 29:02d}",
                "Bookmaker": bookmaker,
                "MarketType": market,
                "Selection": side,
                "Line": line,
                "OverOdds": over_odds,
                "UnderOdds": under_odds,
                "SelectedOdds": selected_odds,
                "LegacyPrediction": legacy,
                "LegacyModelVersion": "synthetic-legacy-v1",
                "LegacyModelTrainedThroughUtc": "2023-01-01T00:00:00+00:00",
                "Prediction2026": model_2026,
                "Model2026Version": "synthetic-2026-v1",
                "Model2026TrainedThroughUtc": "2023-06-01T00:00:00+00:00",
                "ContextPrediction": context,
                "HistoricalMean": historical_mean,
                "HistoricalStd": historical_std,
                "HistoryCount": 12 + fixture_index % 19,
                "DataQualityScore": 0.78 + 0.20 * ((fixture_index % 11) / 10.0),
                "ActualValue": actual,
                "ConfigurationVersion": "bot-g-goals-market-intelligence-1.1.0",
                "FeatureSchemaVersion": "bot-g-goals-features-1.0.0",
                "TrainingContractVersion": "bot-g-training-export-1.1.0",
                "FootballIntelligenceEnabled": True,
                "FootballIntelligenceVersion": "football-intelligence-adjustment-1.0.0",
                "FootballIntelligenceProbabilityAdjustment": (
                    over_intelligence_adjustment if side == "Over"
                    else -over_intelligence_adjustment
                ),
                "FootballIntelligenceHomeEvidenceStatus": "Available",
                "FootballIntelligenceAwayEvidenceStatus": "Available",
                "FootballIntelligenceHomeCutoffUtc": intelligence_cutoff.isoformat(),
                "FootballIntelligenceAwayCutoffUtc": intelligence_cutoff.isoformat(),
                "FPublished": side == f_side and fixture_index % 3 == 0,
                "IsSynthetic": True,
            })
    return pd.DataFrame.from_records(records)


def write_synthetic_candidates(path: Path, fixtures: int = 480, seed: int = 20260819) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    synthetic_candidate_frame(fixtures, seed).to_csv(path, index=False)
    return path
