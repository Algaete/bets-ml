import json
import math
import sys
import types
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent
MODEL_PATH = BASE_DIR / "models" / "busybeard4d8zjj9" / "model.pkl"
LEGACY_MODEL_PATH = BASE_DIR / "model_total.pkl"
FALLBACK_MODEL_PATH = BASE_DIR / "model.pkl"
COLUMNS_PATH = BASE_DIR / "model_columns_v4.json"

CATEGORICAL_COLUMNS = {
    "League",
    "Season",
    "HomeFormation",
    "AwayFormation",
}
DATE_COLUMNS = {"MatchDate"}
INTEGER_COLUMNS = {
    "big3home",
    "big3away",
    "HomeHasFormation",
    "AwayHasFormation",
}


def install_azureml_dataprep_rslex_stub():
    """Registers a small compatibility stub for Azure ML AutoML models on local Mac runtimes."""
    if "azureml.dataprep.rslex" in sys.modules:
        return

    rslex = types.ModuleType("azureml.dataprep.rslex")
    rslex.PyRsDataflow = type("PyRsDataflow", (), {})
    rslex.StreamInfo = type("StreamInfo", (), {})
    sys.modules["azureml.dataprep.rslex"] = rslex


def get_model_columns(model):
    """Returns the exact feature column order required by the model."""
    if COLUMNS_PATH.exists():
        with COLUMNS_PATH.open("r", encoding="utf-8") as file:
            columns = json.load(file)
    elif hasattr(model, "feature_names_in_"):
        columns = list(model.feature_names_in_)
    else:
        raise FileNotFoundError(
            f"Columns file not found: {COLUMNS_PATH}, and model.feature_names_in_ is not available."
        )

    if not isinstance(columns, list) or not all(isinstance(column, str) for column in columns):
        raise ValueError("Model columns must be a JSON array of column names.")

    return columns


def load_model():
    """Loads the Azure ML AutoML pipeline, preferring the current trained model."""
    install_azureml_dataprep_rslex_stub()

    import joblib

    model_path = next(
        (path for path in (MODEL_PATH, LEGACY_MODEL_PATH, FALLBACK_MODEL_PATH) if path.exists()),
        None,
    )
    if model_path is None:
        raise FileNotFoundError(
            f"Model file not found: {MODEL_PATH}, {LEGACY_MODEL_PATH}, or {FALLBACK_MODEL_PATH}"
        )

    return joblib.load(model_path)


def parse_payload():
    """Reads the JSON feature payload passed by .NET through sys.argv[1]."""
    if len(sys.argv) < 2:
        raise ValueError("Missing JSON payload argument.")

    payload = json.loads(sys.argv[1])
    if not isinstance(payload, dict):
        raise ValueError("Payload must be a JSON object.")

    return payload


def build_dataframe(payload, columns):
    """Builds a one-row DataFrame, fills missing model columns with 0, and preserves column order."""
    import pandas as pd

    row = {column: normalize_value(column, payload.get(column)) for column in columns}
    dataframe = pd.DataFrame([row], columns=columns)

    for column in DATE_COLUMNS.intersection(dataframe.columns):
        dataframe[column] = pd.to_datetime(dataframe[column], errors="coerce")
        dataframe[column] = dataframe[column].fillna(pd.Timestamp("1970-01-01"))

    for column in dataframe.columns:
        if column in DATE_COLUMNS:
            continue

        if column in CATEGORICAL_COLUMNS:
            dataframe[column] = dataframe[column].fillna("UNKNOWN").astype(str)
        elif column in INTEGER_COLUMNS:
            dataframe[column] = pd.to_numeric(dataframe[column], errors="coerce").fillna(0).astype("int8")
        else:
            dataframe[column] = pd.to_numeric(dataframe[column], errors="coerce").fillna(0)

    return dataframe


def normalize_value(column, value):
    """Normalizes JSON values into the dtypes used by the trained model."""
    if value is None:
        return "UNKNOWN" if column in CATEGORICAL_COLUMNS else 0

    if column in CATEGORICAL_COLUMNS or column in DATE_COLUMNS:
        text = str(value).strip()
        return text if text else "UNKNOWN"

    if isinstance(value, str):
        return value.strip().replace(",", ".")

    return value


def to_number(value):
    """Converts model output into a finite Python float for JSON serialization."""
    if hasattr(value, "item"):
        value = value.item()

    if isinstance(value, (list, tuple)):
        value = value[0]

    number = float(value)
    if not math.isfinite(number):
        raise ValueError("Prediction result is not a finite number.")

    return number


def main():
    """Runs the full prediction flow and writes the result JSON to stdout."""
    payload = parse_payload()
    model = load_model()
    columns = get_model_columns(model)
    dataframe = build_dataframe(payload, columns)

    prediction = model.predict(dataframe)
    predicted_total_corners = to_number(prediction[0])

    print(json.dumps({"predicted_total_corners": predicted_total_corners}))


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
                    )
                }
            ),
            file=sys.stderr,
        )
        sys.exit(1)
    except Exception as exception:
        print(json.dumps({"error": str(exception)}), file=sys.stderr)
        sys.exit(1)
