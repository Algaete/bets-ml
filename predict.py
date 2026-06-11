import json
import math
import sys
import types
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
MODEL_DIR = BASE_DIR / "newModelsML"
TOTAL_MODEL_PATH = MODEL_DIR / "model_total_corners_filtered_v1.pkl"
HOME_MODEL_PATH = MODEL_DIR / "model_home_corners_filtered_v1.pkl"
AWAY_MODEL_PATH = MODEL_DIR / "model_away_corners_filtered_v1.pkl"
COLUMNS_PATH = MODEL_DIR / "model_columns_filtered_v1.pkl"

ENSEMBLE_MAE = 2.633
CATEGORICAL_COLUMNS = {
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
    "BettingLine": "bettingLine",
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


def load_joblib(path):
    import joblib

    if not path.exists():
        raise FileNotFoundError(f"Required model artifact was not found: {path}")

    return joblib.load(path)


def load_artifacts():
    install_azureml_dataprep_rslex_stub()
    install_xgboost_label_encoder_stub()

    return (
        load_joblib(TOTAL_MODEL_PATH),
        load_joblib(HOME_MODEL_PATH),
        load_joblib(AWAY_MODEL_PATH),
        load_feature_columns(),
    )


def load_feature_columns():
    columns = load_joblib(COLUMNS_PATH)

    if isinstance(columns, tuple):
        columns = list(columns)

    if hasattr(columns, "tolist"):
        columns = columns.tolist()

    if isinstance(columns, dict):
        for key in ("feature_cols", "columns", "features"):
            if key in columns:
                columns = columns[key]
                break

    if not isinstance(columns, list) or not all(isinstance(column, str) for column in columns):
        raise ValueError("model_columns_filtered_v1.pkl must contain a list of feature column names.")

    return columns


def payload_value(payload, column):
    if column in payload:
        return payload[column]

    lookup = {str(key).strip().lower(): value for key, value in payload.items()}
    normalized_column = column.strip().lower()
    if normalized_column in lookup:
        return lookup[normalized_column]

    alias = ALIASES.get(column)
    if alias and alias.strip().lower() in lookup:
        return lookup[alias.strip().lower()]

    lower_first = column[0].lower() + column[1:]
    if lower_first.strip().lower() in lookup:
        return lookup[lower_first.strip().lower()]

    if column.startswith("Home_"):
        lower_home = "home_" + column[len("Home_"):]
        if lower_home.strip().lower() in lookup:
            return lookup[lower_home.strip().lower()]

    if column.startswith("Away_"):
        lower_away = "away_" + column[len("Away_"):]
        if lower_away.strip().lower() in lookup:
            return lookup[lower_away.strip().lower()]

    for prefix in ("Home", "Away", "Expected", "Corners", "Shots", "Total", "Possession", "Goals"):
        if column.startswith(prefix):
            camel = prefix[0].lower() + column[1:]
            if camel.strip().lower() in lookup:
                return lookup[camel.strip().lower()]

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


def normalize_value(column, value):
    if value is None:
        return "Unknown" if column in CATEGORICAL_COLUMNS else 0

    if column == "Season":
        value = normalize_season(value)
        if value is None:
            return "Unknown"

    if column in CATEGORICAL_COLUMNS:
        text = str(value).strip()
        return text if text else "Unknown"

    if isinstance(value, str):
        return value.strip().replace(",", ".")

    return value


def build_dataframe(payload, feature_cols):
    import pandas as pd

    raw_values = {column: payload_value(payload, column) for column in feature_cols}
    defaulted_features = [column for column, value in raw_values.items() if value is None or value == ""]
    row = {column: normalize_value(column, raw_values[column]) for column in feature_cols}
    dataframe = pd.DataFrame([row], columns=feature_cols)

    for column in dataframe.columns:
        if column in CATEGORICAL_COLUMNS:
            dataframe[column] = dataframe[column].fillna("Unknown").astype(str)
        else:
            dataframe[column] = pd.to_numeric(dataframe[column], errors="coerce").fillna(0)

    print(
        json.dumps(
            {
                "debug": "Corners defaulted feature summary.",
                "defaultedFeatureCount": len(defaulted_features),
                "defaultedFeatures": defaulted_features[:20],
                "columnsFile": str(COLUMNS_PATH.name),
            },
            ensure_ascii=False,
        ),
        file=sys.stderr,
    )

    return dataframe


def to_float(value):
    if hasattr(value, "item"):
        value = value.item()

    if isinstance(value, (list, tuple)):
        value = value[0]

    number = float(value)
    if not math.isfinite(number):
        raise ValueError("Model output is not a finite number.")

    return number


def get_betting_line(payload):
    value = payload.get("BettingLine", payload.get("bettingLine"))
    if value is None or value == "":
        return None

    try:
        number = float(str(value).replace(",", "."))
    except ValueError:
        return None

    return number if math.isfinite(number) else None


def confidence_from_distance(distance):
    if distance is None:
        return "N/A"

    if distance < 1.0:
        return "LOW"

    if distance < 1.5:
        return "MEDIUM"

    if distance < 2.0:
        return "HIGH"

    return "VERY_HIGH"


def recommendation(pred_final, betting_line):
    if betting_line is None:
        return "N/A", None, "N/A", "No betting line was provided."

    distance = abs(pred_final - betting_line)
    confidence = confidence_from_distance(distance)
    side = "OVER" if pred_final > betting_line else "UNDER"

    if confidence == "LOW":
        message = "No se recomienda apostar fuerte: la predicción está muy cerca de la línea."
    elif confidence in {"HIGH", "VERY_HIGH"}:
        message = "Señal más fuerte: la predicción está suficientemente alejada de la línea."
    else:
        message = "Señal moderada: revisar contexto y cuota antes de apostar."

    return side, distance, confidence, message


def main():
    payload = parse_payload()
    model_total, model_home, model_away, feature_cols = load_artifacts()
    dataframe = build_dataframe(payload, feature_cols)

    pred_total_direct = to_float(model_total.predict(dataframe)[0])
    pred_home_corners = to_float(model_home.predict(dataframe)[0])
    pred_away_corners = to_float(model_away.predict(dataframe)[0])
    pred_total_combined = pred_home_corners + pred_away_corners
    pred_final = (0.7 * pred_total_direct) + (0.3 * pred_total_combined)

    betting_line = get_betting_line(payload)
    recommended_side, distance_to_line, confidence, message = recommendation(pred_final, betting_line)
    range_low = max(0, pred_final - ENSEMBLE_MAE)
    range_high = pred_final + ENSEMBLE_MAE

    print(
        json.dumps(
            {
                "predTotalDirect": pred_total_direct,
                "predHomeCorners": pred_home_corners,
                "predAwayCorners": pred_away_corners,
                "predTotalCombined": pred_total_combined,
                "predFinal": pred_final,
                "predFinalRounded": round(pred_final, 2),
                "rawTotalCornersPrediction": pred_total_direct,
                "homeCornersPrediction": pred_home_corners,
                "awayCornersPrediction": pred_away_corners,
                "finalCornersPrediction": pred_final,
                "predictedTotalCorners": pred_final,
                "predicted_total_corners": pred_final,
                "rangeLow": range_low,
                "rangeHigh": range_high,
                "probableRangeLow": range_low,
                "probableRangeHigh": range_high,
                "wideRangeLow": range_low,
                "wideRangeHigh": range_high,
                "mae": ENSEMBLE_MAE,
                "rmse": ENSEMBLE_MAE,
                "bettingLine": betting_line,
                "recommendedSide": recommended_side,
                "distanceToLine": distance_to_line,
                "confidence": confidence,
                "message": message,
            }
        )
    )


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
                        "Install the model runtime dependencies before calling /predict."
                    ),
                }
            ),
            file=sys.stderr,
        )
        sys.exit(1)
    except Exception as exception:
        print(json.dumps({"error": str(exception)}), file=sys.stderr)
        sys.exit(1)
