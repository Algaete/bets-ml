import json
import math
import sys
import types
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
DEFAULT_MODEL_PATH = BASE_DIR / "MLModels" / "OverUnder" / "model.pkl"

MODEL_COLUMNS = [
    "League",
    "big3home",
    "big3away",
    "Big3Diff",
    "HomeFormation",
    "AwayFormation",
    "HomeHasFormation",
    "AwayHasFormation",
    "IsKnockout",
    "Home_AvgCornersForLast3",
    "Home_AvgCornersAgainstLast3",
    "Home_AvgShotsForLast3",
    "Home_AvgShotsAgainstLast3",
    "Home_AvgShotsOnGoalForLast3",
    "Home_AvgShotsOnGoalAgainstLast3",
    "Home_AvgPossessionForLast3",
    "Home_AvgCornersForLast5",
    "Home_AvgCornersAgainstLast5",
    "Home_AvgShotsForLast5",
    "Home_AvgShotsAgainstLast5",
    "Home_AvgShotsOnGoalForLast5",
    "Home_AvgShotsOnGoalAgainstLast5",
    "Home_AvgPossessionForLast5",
    "Home_StdCornersForLast5",
    "Home_RangeCornersForLast5",
    "Away_AvgCornersForLast3",
    "Away_AvgCornersAgainstLast3",
    "Away_AvgShotsForLast3",
    "Away_AvgShotsAgainstLast3",
    "Away_AvgShotsOnGoalForLast3",
    "Away_AvgShotsOnGoalAgainstLast3",
    "Away_AvgPossessionForLast3",
    "Away_AvgCornersForLast5",
    "Away_AvgCornersAgainstLast5",
    "Away_AvgShotsForLast5",
    "Away_AvgShotsAgainstLast5",
    "Away_AvgShotsOnGoalForLast5",
    "Away_AvgShotsOnGoalAgainstLast5",
    "Away_AvgPossessionForLast5",
    "Away_StdCornersForLast5",
    "Away_RangeCornersForLast5",
    "CornersDiffLast5",
    "CornersPowerHomeLast5",
    "CornersPowerAwayLast5",
    "ExpectedTotalCornersPowerLast5",
    "ShotsDiffLast5",
    "ShotsOnGoalDiffLast5",
    "PossessionDiffLast5",
    "TotalStdCornersLast5",
    "TotalRangeCornersLast5",
    "BettingLine",
    "TotalCornersPredProxy",
    "DistanceToLine",
    "AbsDistanceToLine",
]

CATEGORICAL_COLUMNS = {
    "League",
    "HomeFormation",
    "AwayFormation",
}

INTEGER_COLUMNS = {
    "big3home",
    "big3away",
    "Big3Diff",
    "HomeHasFormation",
    "AwayHasFormation",
    "IsKnockout",
    "Home_RangeCornersForLast5",
    "Away_RangeCornersForLast5",
    "TotalRangeCornersLast5",
}


def install_azureml_dataprep_rslex_stub():
    """Registers a small compatibility stub for Azure ML AutoML models on local Mac runtimes."""
    if "azureml.dataprep.rslex" in sys.modules:
        return

    rslex = types.ModuleType("azureml.dataprep.rslex")
    rslex.PyRsDataflow = type("PyRsDataflow", (), {})
    rslex.StreamInfo = type("StreamInfo", (), {})
    sys.modules["azureml.dataprep.rslex"] = rslex


def install_xgboost_label_encoder_stub():
    """Restores the legacy XGBoostLabelEncoder symbol used by older pickled pipelines."""
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


def resolve_model_path():
    if len(sys.argv) >= 3 and sys.argv[2].strip():
        return Path(sys.argv[2]).expanduser().resolve()

    return DEFAULT_MODEL_PATH


def load_model(model_path):
    install_azureml_dataprep_rslex_stub()
    install_xgboost_label_encoder_stub()

    import joblib

    if not model_path.exists():
        raise FileNotFoundError(f"Over/Under model file not found: {model_path}")

    return joblib.load(model_path)


def normalize_value(column, value):
    if value is None:
        return "UNKNOWN" if column in CATEGORICAL_COLUMNS else 0

    if column in CATEGORICAL_COLUMNS:
        text = str(value).strip()
        return text if text else "UNKNOWN"

    if isinstance(value, str):
        return value.strip().replace(",", ".")

    return value


def build_dataframe(payload):
    import pandas as pd

    row = {column: normalize_value(column, payload.get(column)) for column in MODEL_COLUMNS}
    dataframe = pd.DataFrame([row], columns=MODEL_COLUMNS)

    for column in dataframe.columns:
        if column in CATEGORICAL_COLUMNS:
            dataframe[column] = dataframe[column].fillna("UNKNOWN").astype(str)
        elif column in INTEGER_COLUMNS:
            dataframe[column] = pd.to_numeric(dataframe[column], errors="coerce").fillna(0).astype("int8")
        else:
            dataframe[column] = pd.to_numeric(dataframe[column], errors="coerce").fillna(0)

    return dataframe


def to_float(value):
    if hasattr(value, "item"):
        value = value.item()

    number = float(value)
    if not math.isfinite(number):
        raise ValueError("Model output is not a finite number.")

    return number


def to_class(value):
    if hasattr(value, "item"):
        value = value.item()

    if isinstance(value, bool):
        return 1 if value else 0

    text = str(value).strip().upper()
    if text in {"1", "1.0", "OVER", "TRUE"}:
        return 1

    if text in {"0", "0.0", "UNDER", "FALSE"}:
        return 0

    return int(float(value))


def normalize_probability(value):
    number = to_float(value)
    if number < 0 or number > 1:
        raise ValueError("Model probability is outside the expected 0..1 range.")

    return number


def get_probability_by_class(model, probabilities, target_class):
    classes = getattr(model, "classes_", None)
    if classes is None:
        steps = getattr(model, "steps", None)
        if steps:
            classes = getattr(steps[-1][1], "classes_", None)

    if classes is None:
        classes = [0, 1]

    for index, class_value in enumerate(classes):
        if to_class(class_value) == target_class:
            return normalize_probability(probabilities[index])

    return None


def calculate_confidence(over_probability, under_probability, abs_distance_to_line):
    if over_probability is not None or under_probability is not None:
        max_probability = max(over_probability or 0, under_probability or 0)
        if max_probability >= 0.70 and abs_distance_to_line >= 1.0:
            return "HIGH"
        if max_probability >= 0.60 and abs_distance_to_line >= 0.5:
            return "MEDIUM"
        return "LOW"

    if abs_distance_to_line >= 1.5:
        return "HIGH"
    if abs_distance_to_line >= 0.75:
        return "MEDIUM"
    return "LOW"


def main():
    payload = parse_payload()
    model = load_model(resolve_model_path())
    dataframe = build_dataframe(payload)

    predicted_class = to_class(model.predict(dataframe)[0])
    over_probability = None
    under_probability = None

    if hasattr(model, "predict_proba"):
        probabilities = model.predict_proba(dataframe)[0]
        under_probability = get_probability_by_class(model, probabilities, 0)
        over_probability = get_probability_by_class(model, probabilities, 1)

    betting_line = to_float(payload.get("BettingLine", 0))
    distance_to_line = to_float(payload.get("DistanceToLine", 0))
    abs_distance_to_line = to_float(payload.get("AbsDistanceToLine", abs(distance_to_line)))

    print(json.dumps({
        "bettingLine": betting_line,
        "prediction": "OVER" if predicted_class == 1 else "UNDER",
        "predictedClass": predicted_class,
        "overProbability": over_probability,
        "underProbability": under_probability,
        "confidence": calculate_confidence(over_probability, under_probability, abs_distance_to_line),
        "distanceToLine": distance_to_line,
        "absDistanceToLine": abs_distance_to_line
    }))


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
                        "Install the Over/Under model runtime dependencies before calling /predict/over-under."
                    )
                }
            ),
            file=sys.stderr,
        )
        sys.exit(1)
    except Exception as exception:
        print(json.dumps({"error": str(exception)}), file=sys.stderr)
        sys.exit(1)
