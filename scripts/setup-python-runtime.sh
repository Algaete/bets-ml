#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

python3 -m venv .venv
.venv/bin/python -m pip install --upgrade pip

.venv/bin/python -m pip install \
  joblib==1.2.0 \
  numpy==1.23.5 \
  pandas==1.5.3 \
  scipy==1.10.1 \
  scikit-learn==1.5.1 \
  sklearn-pandas==1.7.0 \
  lightgbm==4.6.0 \
  xgboost==2.1.4 \
  dill==0.3.9

.venv/bin/python -m pip install \
  azureml-core==1.61.0.post1 \
  azureml-telemetry==1.61.0 \
  applicationinsights==0.11.10

.venv/bin/python -m pip install \
  azureml-automl-runtime==1.61.0 \
  azureml-training-tabular==1.61.0 \
  azureml-automl-core==1.61.0.post1 \
  azureml-dataprep==5.4.2 \
  azureml-dataset-runtime==1.61.0 \
  --no-deps

.venv/bin/python -m pip install \
  wheel \
  Cython==3.2.4 \
  statsmodels==0.13.5 \
  pmdarima==1.8.5 \
  --no-build-isolation

.venv/bin/python -m pip install \
  gensim==4.3.2 \
  smart-open==6.4.0 \
  skl2onnx==1.15.0 \
  onnxconverter-common==1.13.0 \
  onnxmltools==1.12.0 \
  onnx==1.17.0 \
  onnxruntime==1.17.3 \
  pyarrow==14.0.2 \
  psutil==5.9.3 \
  boto3==1.41.0 \
  botocore==1.41.0 \
  arch==5.6.0

echo "Python runtime ready at $ROOT_DIR/.venv/bin/python"
