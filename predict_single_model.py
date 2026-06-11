import json
import math
import sys
import types
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
DEFAULT_MODEL_DIR = BASE_DIR / "newModelsML"

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
}

DEBUG_COLUMNS = [
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

MODEL_REGISTRY = {
    "corners-total": {
        "market": "corners",
        "target": "TotalCorners",
        "model_file": "model_total_corners_filtered_v1.pkl",
        "columns_file": "model_columns_filtered_v1.pkl",
    },
    "corners-home": {
        "market": "corners",
        "target": "HomeCorners",
        "model_file": "model_home_corners_filtered_v1.pkl",
        "columns_file": "model_columns_filtered_v1.pkl",
    },
    "corners-away": {
        "market": "corners",
        "target": "AwayCorners",
        "model_file": "model_away_corners_filtered_v1.pkl",
        "columns_file": "model_columns_filtered_v1.pkl",
    },
    "shots-total": {
        "market": "shots",
        "target": "TotalShots",
        "model_file": "artifacts_shots_v3/model_total_shots_catboost_v3.pkl",
        "columns_file": "artifacts_shots_v3/model_columns_shots_v3.pkl",
    },
    "shots-home": {
        "market": "shots",
        "target": "HomeShots",
        "model_file": "artifacts_shots_v3/model_home_shots_catboost_v3.pkl",
        "columns_file": "artifacts_shots_v3/model_columns_shots_v3.pkl",
    },
    "shots-away": {
        "market": "shots",
        "target": "AwayShots",
        "model_file": "artifacts_shots_v3/model_away_shots_catboost_v3.pkl",
        "columns_file": "artifacts_shots_v3/model_columns_shots_v3.pkl",
    },
    "sog-total": {
        "market": "sog",
        "target": "TotalShotsOnGoal",
        "model_file": "sog_v1/model_total_sog_filtered_v1.pkl",
        "columns_file": "sog_v1/model_columns_shots_sog_filtered_v1.pkl",
    },
    "sog-home": {
        "market": "sog",
        "target": "HomeShotsOnGoal",
        "model_file": "sog_v1/model_home_sog_filtered_v1.pkl",
        "columns_file": "sog_v1/model_columns_shots_sog_filtered_v1.pkl",
    },
    "sog-away": {
        "market": "sog",
        "target": "AwayShotsOnGoal",
        "model_file": "sog_v1/model_away_sog_filtered_v1.pkl",
        "columns_file": "sog_v1/model_columns_shots_sog_filtered_v1.pkl",
    },
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


def load_columns(model_dir, columns_file):
    columns_artifact = load_joblib(model_dir / columns_file)

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
        raise ValueError(f"{columns_file} must contain a feature column list.")

    categorical_cols = set(categorical_cols or CATEGORICAL_FALLBACK)
    numeric_cols = set(numeric_cols or [column for column in feature_cols if column not in categorical_cols])

    return feature_cols, categorical_cols, numeric_cols


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
    missing_features = [column for column, value in raw_values.items() if value is None or value == ""]
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

    return dataframe, missing_features


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


def main():
    wrapper = parse_payload()
    model_key = str(wrapper.get("modelKey") or wrapper.get("model_key") or "").strip().lower()
    features = wrapper.get("features") if isinstance(wrapper.get("features"), dict) else wrapper

    if model_key not in MODEL_REGISTRY:
        valid_keys = ", ".join(sorted(MODEL_REGISTRY))
        raise ValueError(f"Unknown modelKey '{model_key}'. Valid values: {valid_keys}")

    model_dir = resolve_model_dir()
    metadata = MODEL_REGISTRY[model_key]

    install_azureml_dataprep_rslex_stub()
    install_xgboost_label_encoder_stub()

    feature_cols, categorical_cols, numeric_cols = load_columns(model_dir, metadata["columns_file"])
    input_df, missing_features = build_model_input(features, feature_cols, categorical_cols, numeric_cols)
    model = load_joblib(model_dir / metadata["model_file"])
    prediction = to_float(model.predict(input_df)[0])

    response = {
        "modelKey": model_key,
        "market": metadata["market"],
        "target": metadata["target"],
        "modelFile": metadata["model_file"],
        "columnsFile": metadata["columns_file"],
        "prediction": prediction,
        "featureCount": len(feature_cols),
        "missingFeatureCount": len(missing_features),
        "missingFeatures": missing_features[:50],
        "debugValues": build_debug_values(input_df, DEBUG_COLUMNS),
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
                        "Install the model runtime dependencies before calling the debug model endpoint."
                    ),
                }
            ),
            file=sys.stderr,
        )
        sys.exit(2)
    except Exception as exception:
        print(json.dumps({"error_type": "prediction_failed", "error": str(exception)}), file=sys.stderr)
        sys.exit(1)
