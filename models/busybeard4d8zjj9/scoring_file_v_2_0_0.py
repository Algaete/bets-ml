# ---------------------------------------------------------
# Copyright (c) Microsoft Corporation. All rights reserved.
# ---------------------------------------------------------
import json
import logging
import os
import pickle
import numpy as np
import pandas as pd
import joblib

import azureml.automl.core
from azureml.automl.core.shared import logging_utilities, log_server
from azureml.telemetry import INSTRUMENTATION_KEY

from inference_schema.schema_decorators import input_schema, output_schema
from inference_schema.parameter_types.numpy_parameter_type import NumpyParameterType
from inference_schema.parameter_types.pandas_parameter_type import PandasParameterType
from inference_schema.parameter_types.standard_py_parameter_type import StandardPythonParameterType

data_sample = PandasParameterType(pd.DataFrame({"League": pd.Series(["example_value"], dtype="object"), "Season": pd.Series(["example_value"], dtype="object"), "MatchDate": pd.Series(["2000-1-1"], dtype="datetime64[ns]"), "big3home": pd.Series([0], dtype="int8"), "big3away": pd.Series([0], dtype="int8"), "Home_AvgCornersForLast3": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersAgainstLast3": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersForLast3_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersAgainstLast3_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsForLast3": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsAgainstLast3": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsForLast3_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsAgainstLast3_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgShotsLast3": pd.Series(["example_value"], dtype="object"), "Home_AvgShotsOnGoalLast3": pd.Series(["example_value"], dtype="object"), "Home_AvgPossessionLast3": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersForLast5": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersAgainstLast5": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersForLast5_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersAgainstLast5_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsForLast5": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsAgainstLast5": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsForLast5_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsAgainstLast5_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgShotsLast5": pd.Series(["example_value"], dtype="object"), "Home_AvgShotsOnGoalLast5": pd.Series(["example_value"], dtype="object"), "Home_AvgPossessionLast5": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersForLast10": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersAgainstLast10": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersForLast10_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgCornersAgainstLast10_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsForLast10": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsAgainstLast10": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsForLast10_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgGoalsAgainstLast10_HomeOnly": pd.Series(["example_value"], dtype="object"), "Home_AvgShotsLast10": pd.Series(["example_value"], dtype="object"), "Home_AvgShotsOnGoalLast10": pd.Series(["example_value"], dtype="object"), "Home_AvgPossessionLast10": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersForLast3": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersAgainstLast3": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersForLast3_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersAgainstLast3_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsForLast3": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsAgainstLast3": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsForLast3_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsAgainstLast3_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgShotsLast3": pd.Series(["example_value"], dtype="object"), "Away_AvgShotsOnGoalLast3": pd.Series(["example_value"], dtype="object"), "Away_AvgPossessionLast3": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersForLast5": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersAgainstLast5": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersForLast5_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersAgainstLast5_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsForLast5": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsAgainstLast5": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsForLast5_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsAgainstLast5_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgShotsLast5": pd.Series(["example_value"], dtype="object"), "Away_AvgShotsOnGoalLast5": pd.Series(["example_value"], dtype="object"), "Away_AvgPossessionLast5": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersForLast10": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersAgainstLast10": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersForLast10_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgCornersAgainstLast10_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsForLast10": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsAgainstLast10": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsForLast10_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgGoalsAgainstLast10_AwayOnly": pd.Series(["example_value"], dtype="object"), "Away_AvgShotsLast10": pd.Series(["example_value"], dtype="object"), "Away_AvgShotsOnGoalLast10": pd.Series(["example_value"], dtype="object"), "Away_AvgPossessionLast10": pd.Series(["example_value"], dtype="object"), "HomeFormation": pd.Series(["example_value"], dtype="object"), "AwayFormation": pd.Series(["example_value"], dtype="object"), "HomeHasFormation": pd.Series([0], dtype="int8"), "AwayHasFormation": pd.Series([0], dtype="int8")}))
input_sample = StandardPythonParameterType({'data': data_sample})

result_sample = NumpyParameterType(np.array([0]))
output_sample = StandardPythonParameterType({'Results':result_sample})
sample_global_parameters = StandardPythonParameterType(1.0)

try:
    log_server.enable_telemetry(INSTRUMENTATION_KEY)
    log_server.set_verbosity('INFO')
    logger = logging.getLogger('azureml.automl.core.scoring_script_v2')
except:
    pass


def get_model_root(model_root: str):
    root_contents = os.listdir(model_root)
    logger.info(f"List model root dir: {os.listdir(model_root)}")
    if len(root_contents) == 1:
        root_file_path = os.path.join(model_root, root_contents[0])
        return root_file_path if os.path.isdir(root_file_path) else model_root
    else:
        raise Exception("Unexpected. root must contain a model file or a mlflow model directory")


def init():
    global model
    # This name is model.id of model that we want to deploy deserialize the model file back
    # into a sklearn model
    model_root = get_model_root(os.getenv('AZUREML_MODEL_DIR'))
    model_path = os.path.join(model_root, 'model.pkl')
    path = os.path.normpath(model_path)
    path_split = path.split(os.sep)
    log_server.update_custom_dimensions({'model_name': path_split[-3], 'model_version': path_split[-2]})
    try:
        logger.info("Loading model from path.")
        model = joblib.load(model_path)
        logger.info("Loading successful.")
    except Exception as e:
        logging_utilities.log_traceback(e, logger)
        raise

@input_schema('Inputs', input_sample)
@input_schema('GlobalParameters', sample_global_parameters, convert_to_provided_type=False)
@output_schema(output_sample)
def run(Inputs, GlobalParameters=1.0):
    data = Inputs['data']
    result = model.predict(data)
    return {'Results':result.tolist()}
