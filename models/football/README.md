# Versioned football deployment bundles

Only validated immutable inference bundles belong here. Training datasets, Optuna databases,
experiment databases, prediction exports with real targets, caches and temporary files are
excluded.

The Models 2026 registry contains 12 targets, grouped into corners, shots, shots on goal and
goals. Each market has home, away and total models. Every version directory contains one
complete signed bundle under `<market>/<scope>/`:

```text
<model-version>/<market>/<home|away|total>/
  modelo_deployment_full.cbm
  modelo_deployment_full.joblib
  model_metadata.json
  deployment_manifest.json
  deployment_smoke_fixture.json
  reference_evaluation.json
  environment.json
  inference.py
  requirements-inference.txt
  checksums.sha256
```

The API always uses the native CBM. The joblib is retained only as the bundle's declared
fallback and for provenance. Active versions are configured in
`NewGenerationMl:ActiveVersions`; `NewGenerationMl:ActiveVersion` remains the backwards-
compatible Home Corners override. Change a version and restart the API to roll forward or
back.

The signed manifests supplied for shots, shots on goal and goals currently declare
`"market": "corners"`. Their target fields and artifacts are correct. The application derives
the operational market from the target registry, exposes a warning, and does not modify the
signed bundles or their checksums.
