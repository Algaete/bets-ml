import json
import math
import sys
import types
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
DEFAULT_MODEL_DIR = BASE_DIR / "newModelsML"
ACTIVE_MODELS_FILE = "active_models.json"

EXPECTED_MARKET_FILES = {
    "shots": {
        "version": "shots_v3_catboost",
        "directory": "artifacts_shots_v3",
        "columns": "model_columns_shots_v3.pkl",
        "total": "model_total_shots_catboost_v3.pkl",
        "home": "model_home_shots_catboost_v3.pkl",
        "away": "model_away_shots_catboost_v3.pkl",
    },
    "sog": {
        "version": "sog_v1",
        "directory": "sog_v1",
        "columns": "model_columns_shots_sog_filtered_v1.pkl",
        "total": "model_total_sog_filtered_v1.pkl",
        "home": "model_home_sog_filtered_v1.pkl",
        "away": "model_away_sog_filtered_v1.pkl",
    },
    "goals": {
        "version": "goals_v1",
        "directory": "goals_v1",
        "columns": "model_columns_goals_v1.pkl",
        "total": "model_total_goals_v1.pkl",
        "home": "model_home_goals_v1.pkl",
        "away": "model_away_goals_v1.pkl",
    },
}

CATEGORICAL_FALLBACK = {
    "League",
    "Season",
    "HomeTeam",
    "AwayTeam",
    "HomeFormation",
    "AwayFormation",
}

ALIASES = {
    "Big3Diff": "big3Diff",
    "IsKnockout": "isKnockout",
    "HomeHasFormation": "homeHasFormation",
    "AwayHasFormation": "awayHasFormation",
    "HomeFormation": "homeFormation",
    "AwayFormation": "awayFormation",
    "HomeTeam": "homeTeam",
    "AwayTeam": "awayTeam",
    "League": "league",
    "Season": "season",
    "HomeIsCountry": "homeIsCountry",
    "AwayIsCountry": "awayIsCountry",
    "CountryDiff": "countryDiff",
}

SHOTS_DEBUG_COLUMNS = [
    "Home_AvgShotsForLast5",
    "Away_AvgShotsForLast5",
    "Home_AvgShotsAgainstLast5",
    "Away_AvgShotsAgainstLast5",
    "Home_AvgTotalShotsLast5",
    "Away_AvgTotalShotsLast5",
    "HomeShotsPowerLast5",
    "AwayShotsPowerLast5",
    "ExpectedTotalShotsPowerLast5",
    "ShotsDiffLast5",
    "TotalStdShotsLast5",
    "TotalRangeShotsLast5",
    "Home_HomeAvgTotalShotsLast10",
    "Away_AwayAvgTotalShotsLast10",
]

GOALS_DEBUG_COLUMNS = [
    "Home_AvgGoalsForLast3",
    "Away_AvgGoalsForLast3",
    "Home_AvgGoalsAgainstLast3",
    "Away_AvgGoalsAgainstLast3",
    "Home_AvgGoalsForLast5",
    "Away_AvgGoalsForLast5",
    "Home_AvgGoalsAgainstLast5",
    "Away_AvgGoalsAgainstLast5",
    "HomeGoalsPowerLast5",
    "AwayGoalsPowerLast5",
    "ExpectedTotalGoalsPowerLast5",
    "GoalsForDiffLast5",
    "GoalsAgainstDiffLast5",
    "Home_AvgShotsForLast5",
    "Away_AvgShotsForLast5",
    "Home_AvgShotsOnGoalForLast5",
    "Away_AvgShotsOnGoalForLast5",
    "ExpectedTotalShotsOnGoalPowerLast5",
    "PossessionDiffLast5",
]

ACCURACY_BY_MARKET = {
    "shots": [
        (0.0, 0.6350),
        (0.5, 0.6434),
        (0.75, 0.6477),
        (1.0, 0.6520),
        (1.25, 0.6564),
        (1.5, 0.6606),
        (2.0, 0.6694),
        (3.0, 0.6862),
        (4.0, 0.7026),
    ],
    "sog": [
        (0.0, 0.7680),
        (0.5, 0.7791),
        (0.75, 0.7849),
        (1.0, 0.7900),
        (1.25, 0.7971),
        (1.5, 0.8045),
        (2.0, 0.8250),
        (3.0, 0.8728),
        (4.0, 0.9381),
    ],
    "goals": [
        (0.0, 0.5500),
        (0.25, 0.5730),
        (0.5, 0.6286),
        (0.75, 0.6496),
        (1.0, 0.6000),
    ],
}


def install_azureml_dataprep_rslex_stub():
    if "azureml.dataprep.rslex" in sys.modules:
        return

    rslex = types.ModuleType("azureml.dataprep.rslex")
    rslex.PyRsDataflow = type("PyRsDataflow", (), {})
    rslex.StreamInfo = type("StreamInfo", (), {})
    sys.modules["azureml.dataprep.rslex"] = rslex


def install_xgboost_label_encoder_stub():
    try:
        import xgboost.compat
        from sklearn.preprocessing import LabelEncoder

        if not hasattr(xgboost.compat, "XGBoostLabelEncoder"):
            xgboost.compat.XGBoostLabelEncoder = LabelEncoder
    except ModuleNotFoundError:
        return


def parse_payload():
    if len(sys.argv) < 2:
        raise ValueError("Missing JSON payload argument.")

    payload = json.loads(sys.argv[1])
    if not isinstance(payload, dict):
        raise ValueError("Payload must be a JSON object.")

    return payload


def resolve_model_dir():
    if len(sys.argv) >= 3 and sys.argv[2].strip():
        return Path(sys.argv[2]).expanduser().resolve()

    return DEFAULT_MODEL_DIR


def load_joblib(path):
    import joblib

    if not path.exists():
        raise FileNotFoundError(f"Required model artifact was not found: {path}")

    return joblib.load(path)


def found_files(directory):
    if not directory.exists() or not directory.is_dir():
        return []

    return sorted(path.name for path in directory.iterdir() if path.is_file())


def require_configured_file(path, label, configured_file):
    if path.exists():
        return path

    directory = path.parent
    raise FileNotFoundError(
        json.dumps(
            {
                "error": "Configured model artifact was not found.",
                "label": label,
                "missingFile": configured_file,
                "searchedPath": str(path),
                "currentDirectory": str(Path.cwd()),
                "foundFiles": found_files(directory),
            },
            ensure_ascii=False,
        )
    )


def load_columns(columns_path):
    columns_artifact = load_joblib(columns_path)

    if isinstance(columns_artifact, dict):
        feature_cols = columns_artifact.get("feature_cols")
        categorical_cols = columns_artifact.get("categorical_cols", CATEGORICAL_FALLBACK)
        numeric_cols = columns_artifact.get("numeric_cols")
    else:
        feature_cols = columns_artifact
        categorical_cols = CATEGORICAL_FALLBACK
        numeric_cols = None

    if hasattr(feature_cols, "tolist"):
        feature_cols = feature_cols.tolist()

    if not isinstance(feature_cols, list) or not all(isinstance(column, str) for column in feature_cols):
        raise ValueError(f"{columns_path} must contain a list named feature_cols.")

    categorical_cols = set(categorical_cols or CATEGORICAL_FALLBACK)
    numeric_cols = set(numeric_cols or [column for column in feature_cols if column not in categorical_cols])

    return feature_cols, categorical_cols, numeric_cols


def load_active_models_config(model_dir):
    config_path = model_dir / ACTIVE_MODELS_FILE
    require_configured_file(config_path, "active model config", ACTIVE_MODELS_FILE)

    with config_path.open("r", encoding="utf-8") as handle:
        config = json.load(handle)

    if not isinstance(config, dict):
        raise ValueError(f"{config_path} must be a JSON object.")

    for market in ("shots", "sog", "goals"):
        if market not in config or not isinstance(config[market], dict):
            raise ValueError(f"{config_path} must include an object named '{market}'.")

    return config_path, config


def configured_market_paths(model_dir, config, market):
    market_config = config[market]
    directory_name = market_config.get("directory")
    expected = EXPECTED_MARKET_FILES[market]

    if not isinstance(directory_name, str) or not directory_name.strip():
        raise ValueError(f"active_models.json market '{market}' must define a non-empty directory.")

    validate_market_config(market, market_config, directory_name, expected)

    market_dir = (model_dir / directory_name).resolve()
    paths = {
        "version": str(market_config.get("version") or directory_name),
        "directory": market_dir,
        "columns": require_configured_file(market_dir / str(market_config.get("columns")), f"{market} columns", str(market_config.get("columns"))),
        "total": require_configured_file(market_dir / str(market_config.get("total")), f"{market} total model", str(market_config.get("total"))),
        "home": require_configured_file(market_dir / str(market_config.get("home")), f"{market} home model", str(market_config.get("home"))),
        "away": require_configured_file(market_dir / str(market_config.get("away")), f"{market} away model", str(market_config.get("away"))),
        "direct_weight": float(market_config.get("directWeight", 0.8)),
        "combined_weight": float(market_config.get("combinedWeight", 0.2)),
    }

    return paths


def validate_market_config(market, market_config, directory_name, expected):
    configured = {
        "version": str(market_config.get("version") or directory_name),
        "directory": str(directory_name),
        "columns": str(market_config.get("columns")),
        "total": str(market_config.get("total")),
        "home": str(market_config.get("home")),
        "away": str(market_config.get("away")),
    }

    mismatches = [
        f"{key}: expected '{expected[key]}', got '{configured[key]}'"
        for key in expected
        if configured[key] != expected[key]
    ]

    if market == "shots":
        sog_files = [
            f"{key}='{value}'"
            for key, value in configured.items()
            if key in {"columns", "total", "home", "away"} and "sog" in value.lower()
        ]
        if sog_files:
            mismatches.append("shots config must not reference SOG artifacts: " + ", ".join(sog_files))

    if market == "goals":
        non_goal_files = [
            f"{key}='{value}'"
            for key, value in configured.items()
            if key in {"columns", "total", "home", "away"} and "goals" not in value.lower()
        ]
        if non_goal_files:
            mismatches.append("goals config must reference only goals artifacts: " + ", ".join(non_goal_files))

    if mismatches:
        raise ValueError(
            "Invalid active_models.json configuration for "
            f"market '{market}'. " + "; ".join(mismatches)
        )


def build_active_model_debug(model_dir, config_path, shots_paths, sog_paths, goals_paths):
    return {
        "modelDir": str(model_dir),
        "activeModelConfigPath": str(config_path),
        "shotsVersion": shots_paths["version"],
        "shotsModelDirectory": str(shots_paths["directory"]),
        "shotsTotalModelPath": str(shots_paths["total"]),
        "shotsHomeModelPath": str(shots_paths["home"]),
        "shotsAwayModelPath": str(shots_paths["away"]),
        "shotsColumnsPath": str(shots_paths["columns"]),
        "sogVersion": sog_paths["version"],
        "sogModelDirectory": str(sog_paths["directory"]),
        "sogTotalModelPath": str(sog_paths["total"]),
        "sogHomeModelPath": str(sog_paths["home"]),
        "sogAwayModelPath": str(sog_paths["away"]),
        "sogColumnsPath": str(sog_paths["columns"]),
        "goalsVersion": goals_paths["version"],
        "goalsModelDirectory": str(goals_paths["directory"]),
        "goalsTotalModelPath": str(goals_paths["total"]),
        "goalsHomeModelPath": str(goals_paths["home"]),
        "goalsAwayModelPath": str(goals_paths["away"]),
        "goalsColumnsPath": str(goals_paths["columns"]),
    }


def load_artifacts(model_dir):
    install_azureml_dataprep_rslex_stub()
    install_xgboost_label_encoder_stub()

    config_path, active_config = load_active_models_config(model_dir)
    shots_paths = configured_market_paths(model_dir, active_config, "shots")
    sog_paths = configured_market_paths(model_dir, active_config, "sog")
    goals_paths = configured_market_paths(model_dir, active_config, "goals")
    active_model_debug = build_active_model_debug(model_dir, config_path, shots_paths, sog_paths, goals_paths)
    log_debug("Active ML model configuration loaded.", active_model_debug)

    shots_feature_cols, shots_categorical_cols, shots_numeric_cols = load_columns(shots_paths["columns"])
    sog_feature_cols, sog_categorical_cols, sog_numeric_cols = load_columns(sog_paths["columns"])
    goals_feature_cols, goals_categorical_cols, goals_numeric_cols = load_columns(goals_paths["columns"])

    return {
        "shots_columns": shots_feature_cols,
        "shots_categorical": shots_categorical_cols,
        "shots_numeric": shots_numeric_cols,
        "shots_direct_weight": shots_paths["direct_weight"],
        "shots_combined_weight": shots_paths["combined_weight"],
        "sog_columns": sog_feature_cols,
        "sog_categorical": sog_categorical_cols,
        "sog_numeric": sog_numeric_cols,
        "sog_direct_weight": sog_paths["direct_weight"],
        "sog_combined_weight": sog_paths["combined_weight"],
        "goals_columns": goals_feature_cols,
        "goals_categorical": goals_categorical_cols,
        "goals_numeric": goals_numeric_cols,
        "goals_direct_weight": goals_paths["direct_weight"],
        "goals_combined_weight": goals_paths["combined_weight"],
        "active_model_debug": active_model_debug,
        "shots_total": load_joblib(shots_paths["total"]),
        "shots_home": load_joblib(shots_paths["home"]),
        "shots_away": load_joblib(shots_paths["away"]),
        "sog_total": load_joblib(sog_paths["total"]),
        "sog_home": load_joblib(sog_paths["home"]),
        "sog_away": load_joblib(sog_paths["away"]),
        "goals_total": load_joblib(goals_paths["total"]),
        "goals_home": load_joblib(goals_paths["home"]),
        "goals_away": load_joblib(goals_paths["away"]),
    }


def normalize_key(key):
    return str(key).strip().lower()


def build_payload_lookup(payload):
    return {normalize_key(key): value for key, value in payload.items()}


def payload_value(payload, column):
    if column in payload:
        return payload[column]

    lookup = build_payload_lookup(payload)
    if normalize_key(column) in lookup:
        return lookup[normalize_key(column)]

    alias = ALIASES.get(column)
    if alias and normalize_key(alias) in lookup:
        return lookup[normalize_key(alias)]

    lower_first = column[0].lower() + column[1:]
    if normalize_key(lower_first) in lookup:
        return lookup[normalize_key(lower_first)]

    if column.startswith("Home_"):
        lower_home = "home_" + column[len("Home_"):]
        if normalize_key(lower_home) in lookup:
            return lookup[normalize_key(lower_home)]

    if column.startswith("Away_"):
        lower_away = "away_" + column[len("Away_"):]
        if normalize_key(lower_away) in lookup:
            return lookup[normalize_key(lower_away)]

    for prefix in ("Home", "Away", "Expected", "Corners", "Shots", "Total", "Possession", "Goals"):
        if column.startswith(prefix):
            camel = prefix[0].lower() + column[1:]
            if normalize_key(camel) in lookup:
                return lookup[normalize_key(camel)]

    return None


def normalize_season(value):
    if value is None:
        return None

    text = str(value).strip()
    if not text:
        return None

    if "-" in text:
        parts = [part.strip() for part in text.split("-") if part.strip()]
        if parts:
            return parts[-1]

    return text


def normalize_value(column, value, categorical_cols):
    if value is None:
        return "Unknown" if column in categorical_cols else 0

    if column == "Season":
        value = normalize_season(value)
        if value is None:
            return "Unknown"

    if column in categorical_cols:
        text = str(value).strip()
        return text if text else "Unknown"

    if isinstance(value, str):
        return value.strip().replace(",", ".")

    return value


def build_model_input(payload, feature_cols, categorical_cols, numeric_cols):
    import pandas as pd

    raw_values = {column: payload_value(payload, column) for column in feature_cols}
    defaulted_features = [column for column, value in raw_values.items() if value is None or value == ""]
    row = {
        column: normalize_value(column, raw_values[column], categorical_cols)
        for column in feature_cols
    }
    dataframe = pd.DataFrame([row], columns=feature_cols)

    for column in feature_cols:
        if column in categorical_cols:
            dataframe[column] = dataframe[column].fillna("Unknown").astype(str)
        elif column in numeric_cols:
            dataframe[column] = pd.to_numeric(dataframe[column], errors="coerce").fillna(0)
        else:
            dataframe[column] = pd.to_numeric(dataframe[column], errors="coerce").fillna(0)

    return dataframe, defaulted_features


def log_debug(message, payload):
    print(json.dumps({"debug": message, **payload}, ensure_ascii=False), file=sys.stderr)


def build_debug_values(dataframe, columns):
    values = {}
    for column in columns:
        if column in dataframe.columns:
            value = dataframe.iloc[0][column]
            if hasattr(value, "item"):
                value = value.item()
            values[column] = value
    return values


def to_float(value):
    if hasattr(value, "item"):
        value = value.item()

    if isinstance(value, (list, tuple)):
        value = value[0]

    number = float(value)
    if not math.isfinite(number):
        raise ValueError("Model output is not a finite number.")

    return number


def parse_line(payload, *names):
    for name in names:
        number = parse_number_value(payload_value(payload, name))

        if math.isfinite(number) and number >= 0:
            return number

    return None


def parse_number_value(value):
    if value is None or value == "":
        return math.nan

    try:
        number = float(str(value).replace(",", "."))
    except (TypeError, ValueError):
        return math.nan

    return number if math.isfinite(number) else math.nan


def payload_number(payload, *columns):
    for column in columns:
        number = parse_number_value(payload_value(payload, column))
        if math.isfinite(number):
            return number

    return None


def confidence_from_distance(distance, market_type):
    if distance is None:
        return None

    if market_type == "shots":
        if distance < 2.0:
            return "NO_BET"
        if distance < 3.0:
            return "LOW"
        if distance < 4.0:
            return "MEDIUM"
        return "HIGH"

    if market_type == "goals":
        if distance < 0.35:
            return "NO_BET"
        if distance < 0.5:
            return "LOW"
        if distance < 0.75:
            return "MEDIUM"
        return "HIGH"

    if distance < 1.0:
        return "LOW"
    if distance < 2.0:
        return "MEDIUM"
    if distance < 3.0:
        return "HIGH"
    return "VERY_HIGH"


def historical_accuracy(distance, market_type):
    if distance is None:
        return None

    selected = None
    for threshold, accuracy in ACCURACY_BY_MARKET[market_type]:
        if distance >= threshold:
            selected = accuracy

    return selected


def apply_line_metrics(market, line, market_type):
    market["line"] = line

    if line is None:
        market["recommendation"] = None
        market["distance"] = None
        market["confidence"] = None
        market["historicalAccuracy"] = None
        return market

    prediction = market["prediction"]
    distance = abs(prediction - line)
    market["recommendation"] = "OVER" if prediction > line else "UNDER"
    market["distance"] = distance
    market["confidence"] = confidence_from_distance(distance, market_type)
    market["historicalAccuracy"] = historical_accuracy(distance, market_type)
    return market


def predict_market(total_model, home_model, away_model, input_df, line, market_type, payload=None, direct_weight=0.8, combined_weight=0.2):
    total_direct = to_float(total_model.predict(input_df)[0])
    home_prediction = to_float(home_model.predict(input_df)[0])
    away_prediction = to_float(away_model.predict(input_df)[0])
    combined_home_away = home_prediction + away_prediction
    direct_weight_adjusted = False
    direct_weight_reason = None
    feature_prior = shots_feature_prior(payload) if market_type == "shots" and payload is not None else None

    if market_type == "shots":
        reference_values = [combined_home_away]
        if feature_prior is not None:
            reference_values.append(feature_prior)

        reference_value = max(value for value in reference_values if value is not None)
        if reference_value > 0 and total_direct < (reference_value * 0.5):
            direct_weight_reason = "total shots direct model was below 50% of the home/away or recent-context reference"
            log_debug(
                "Shots sanity warning: total-direct output is inconsistent with context, but configured weights were kept.",
                {
                    "totalShotsDirect": total_direct,
                    "homeAwayCombined": combined_home_away,
                    "recentShotsContext": feature_prior,
                    "referenceValue": reference_value,
                    "directWeight": direct_weight,
                    "combinedWeight": combined_weight,
                },
            )

    final_prediction = (direct_weight * total_direct) + (combined_weight * combined_home_away)

    market = {
        "prediction": final_prediction,
        "rawPrediction": final_prediction,
        "sanityAdjusted": direct_weight_adjusted,
        "sanityReason": direct_weight_reason,
        "featurePrior": feature_prior,
        "directWeight": direct_weight,
        "combinedWeight": combined_weight,
        "directWeightAdjusted": direct_weight_adjusted,
        "directWeightReason": direct_weight_reason,
        "homePrediction": home_prediction,
        "awayPrediction": away_prediction,
        "totalDirectPrediction": total_direct,
        "combinedHomeAwayPrediction": combined_home_away,
        "finalPrediction": final_prediction,
    }

    return apply_line_metrics(market, line, market_type)


def shots_feature_prior(payload):
    values = []
    for column in (
        "ExpectedTotalShotsPowerLast5",
        "Home_AvgTotalShotsLast5",
        "Away_AvgTotalShotsLast5",
        "Home_AvgTotalShotsLast3",
        "Away_AvgTotalShotsLast3",
        "Home_HomeAvgTotalShotsLast10",
        "Away_AwayAvgTotalShotsLast10",
    ):
        number = payload_number(payload, column)
        if number is not None and number > 0:
            values.append(number)

    if not values:
        return None

    return sum(values) / len(values)


def main():
    payload = parse_payload()
    model_dir = resolve_model_dir()
    artifacts = load_artifacts(model_dir)
    shots_df, shots_defaulted_features = build_model_input(
        payload,
        artifacts["shots_columns"],
        artifacts["shots_categorical"],
        artifacts["shots_numeric"],
    )
    sog_df, sog_defaulted_features = build_model_input(
        payload,
        artifacts["sog_columns"],
        artifacts["sog_categorical"],
        artifacts["sog_numeric"],
    )
    goals_df, goals_defaulted_features = build_model_input(
        payload,
        artifacts["goals_columns"],
        artifacts["goals_categorical"],
        artifacts["goals_numeric"],
    )

    debug_values = build_debug_values(shots_df, SHOTS_DEBUG_COLUMNS)
    goals_debug_values = build_debug_values(goals_df, GOALS_DEBUG_COLUMNS)
    log_debug("Shots v2 mapped feature values before prediction.", debug_values)
    log_debug("Goals mapped feature values before prediction.", goals_debug_values)
    log_debug(
        "Shots v2 defaulted feature summary.",
        {
            "defaultedFeatureCount": len(shots_defaulted_features),
            "defaultedFeatures": shots_defaulted_features[:20],
        },
    )
    log_debug(
        "SOG defaulted feature summary.",
        {
            "defaultedFeatureCount": len(sog_defaulted_features),
            "defaultedFeatures": sog_defaulted_features[:20],
        },
    )
    log_debug(
        "Goals defaulted feature summary.",
        {
            "defaultedFeatureCount": len(goals_defaulted_features),
            "defaultedFeatures": goals_defaulted_features[:20],
        },
    )

    shots_line = parse_line(payload, "shotsLine", "ShotsLine", "TotalShotsLine")
    sog_line = parse_line(payload, "sogLine", "SogLine", "SOGLine", "shotsOnGoalLine", "ShotsOnGoalLine")
    goals_line = parse_line(payload, "goalsLine", "GoalsLine", "totalGoalsLine", "TotalGoalsLine")

    shots = predict_market(
        artifacts["shots_total"],
        artifacts["shots_home"],
        artifacts["shots_away"],
        shots_df,
        shots_line,
        "shots",
        payload,
        artifacts["shots_direct_weight"],
        artifacts["shots_combined_weight"],
    )
    sog = predict_market(
        artifacts["sog_total"],
        artifacts["sog_home"],
        artifacts["sog_away"],
        sog_df,
        sog_line,
        "sog",
        None,
        artifacts["sog_direct_weight"],
        artifacts["sog_combined_weight"],
    )
    goals = predict_market(
        artifacts["goals_total"],
        artifacts["goals_home"],
        artifacts["goals_away"],
        goals_df,
        goals_line,
        "goals",
        None,
        artifacts["goals_direct_weight"],
        artifacts["goals_combined_weight"],
    )
    prediction_comparison = {
        "shotsTotalDirect": shots["totalDirectPrediction"],
        "shotsHome": shots["homePrediction"],
        "shotsAway": shots["awayPrediction"],
        "shotsCombined": shots["combinedHomeAwayPrediction"],
        "shotsFinal": shots["finalPrediction"],
        "sogTotalDirect": sog["totalDirectPrediction"],
        "sogHome": sog["homePrediction"],
        "sogAway": sog["awayPrediction"],
        "sogCombined": sog["combinedHomeAwayPrediction"],
        "sogFinal": sog["finalPrediction"],
        "goalsTotalDirect": goals["totalDirectPrediction"],
        "goalsHome": goals["homePrediction"],
        "goalsAway": goals["awayPrediction"],
        "goalsCombined": goals["combinedHomeAwayPrediction"],
        "goalsFinal": goals["finalPrediction"],
    }
    log_debug("Prediction comparison.", prediction_comparison)

    shots_sog_gap = abs(shots["finalPrediction"] - sog["finalPrediction"])
    shots_sog_anomaly = (
        shots["finalPrediction"] <= 12
        and sog["finalPrediction"] <= 12
        and shots_sog_gap <= 2
    )
    if shots_sog_anomaly:
        log_debug(
            "Shots/SOG anomaly: final shots is too close to final SOG.",
            {
                "shotsFinal": shots["finalPrediction"],
                "sogFinal": sog["finalPrediction"],
                "gap": shots_sog_gap,
                "message": "Check active model paths, payload completeness and Python runtime versions.",
            },
        )

    response = {
        "match": {
            "league": payload.get("League", payload.get("league")),
            "season": payload.get("Season", payload.get("season")),
            "homeTeam": payload.get("HomeTeam", payload.get("homeTeam")),
            "awayTeam": payload.get("AwayTeam", payload.get("awayTeam")),
        },
        "markets": {
            "shots": shots,
            "sog": sog,
            "goals": goals,
        },
        "shots": shots,
        "sog": sog,
        "goals": goals,
        "predictedShots": shots["prediction"],
        "predictedShotsOnGoal": sog["prediction"],
        "predictedGoals": goals["prediction"],
        "predicted_shots_on_goal": sog["prediction"],
        "rawTotalShotsPrediction": shots["totalDirectPrediction"],
        "homeShotsPrediction": shots["homePrediction"],
        "awayShotsPrediction": shots["awayPrediction"],
        "finalShotsPrediction": shots["finalPrediction"],
        "rawTotalSogPrediction": sog["totalDirectPrediction"],
        "homeSogPrediction": sog["homePrediction"],
        "awaySogPrediction": sog["awayPrediction"],
        "finalSogPrediction": sog["finalPrediction"],
        "rawTotalGoalsPrediction": goals["totalDirectPrediction"],
        "homeGoalsPrediction": goals["homePrediction"],
        "awayGoalsPrediction": goals["awayPrediction"],
        "finalGoalsPrediction": goals["finalPrediction"],
        "debug": {
            **artifacts["active_model_debug"],
            "mappedShotsValues": debug_values,
            "mappedGoalsValues": goals_debug_values,
            **prediction_comparison,
            "shotsSogGap": shots_sog_gap,
            "shotsSogAnomaly": shots_sog_anomaly,
            "shotsSogAnomalyMessage": (
                "Final shots is too close to final SOG. Check active model paths, payload completeness and Python runtime versions."
                if shots_sog_anomaly
                else None
            ),
            "shotsFeatureCount": len(artifacts["shots_columns"]),
            "shotsDefaultedFeatureCount": len(shots_defaulted_features),
            "shotsDefaultedFeatures": shots_defaulted_features[:20],
            "sogFeatureCount": len(artifacts["sog_columns"]),
            "sogDefaultedFeatureCount": len(sog_defaulted_features),
            "sogDefaultedFeatures": sog_defaulted_features[:20],
            "goalsFeatureCount": len(artifacts["goals_columns"]),
            "goalsDefaultedFeatureCount": len(goals_defaulted_features),
            "goalsDefaultedFeatures": goals_defaulted_features[:20],
            "goalsDirectWeight": artifacts["goals_direct_weight"],
            "goalsCombinedWeight": artifacts["goals_combined_weight"],
            "runtimeNote": "If total-direct shots is much lower than context while mapped values are present, check Python package versions against the training environment.",
        },
    }

    print(json.dumps(response))


if __name__ == "__main__":
    try:
        main()
    except ModuleNotFoundError as exception:
        print(
            json.dumps(
                {
                    "error_type": "missing_dependency",
                    "error": (
                        f"Missing Python dependency: {exception.name}. "
                        "Install the shots/SOG model runtime dependencies before calling /predict/shots-on-goal."
                    ),
                }
            ),
            file=sys.stderr,
        )
        sys.exit(1)
    except Exception as exception:
        print(json.dumps({"error": str(exception)}), file=sys.stderr)
        sys.exit(1)
