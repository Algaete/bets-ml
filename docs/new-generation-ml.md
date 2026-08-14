# Models 2026

## Architecture

- ASP.NET Core MVC page: `/modelos-2026`.
- ASP.NET Core API registry validates all configured bundles and SHA-256 checksums.
- The existing SQL prediction context builds one pre-match feature dictionary per match.
- All 12 contracts are subsets of the same 60 available features.
- One persistent Python worker loads every active native CatBoost CBM once and serves batch
  predictions over stdin/stdout. It does not reload models per request.
- Legacy prediction pages and endpoints remain independent and unchanged.
- The automated run also creates `Bot C · Models 2026` selections with automation suffix
  `-C2026`. Bot A and Bot B continue using the legacy runtime, so their histories remain
  directly comparable and are never overwritten by Bot C.

## Active targets

| Market | Home | Away | Total |
|---|---|---|---|
| Corners | `TargetHomeCorners` | `TargetAwayCorners` | `TargetTotalCorners` |
| Shots | `TargetHomeShots` | `TargetAwayShots` | `TargetTotalShots` |
| Shots on goal | `TargetHomeShotsOnGoal` | `TargetAwayShotsOnGoal` | `TargetTotalShotsOnGoal` |
| Goals | `TargetHomeGoals` | `TargetAwayGoals` | `TargetTotalGoals` |

Every response preserves the continuous clipped prediction and includes a rounded value only
for display. No Over/Under probability is inferred from these point regressions.

## Bot C and supported odds markets

Bot C consumes the same stored upcoming odds as the existing bots and selects the exact target
for each source market:

| Stored odds market | Models 2026 target | Bot Picks page |
|---|---|---|
| `CornersHomeTeam` | `TargetHomeCorners` | Córners |
| `CornersAwayTeam` | `TargetAwayCorners` | Córners |
| `CornersTotal` | `TargetTotalCorners` | Córners |
| `ShotsTotal` | `TargetTotalShots` | Tiros |
| `ShotsOnTargetTotal` | `TargetTotalShotsOnGoal` | Tiros al arco |
| `GoalsTotal` | `TargetTotalGoals` | Goles |

For auditing, the persisted decision JSON includes model generation, immutable model version,
training cutoff, feature set and warnings. Total markets also retain the separate Home + Away
sum as a consensus diagnostic; the direct total target remains the prediction used to choose
Over or Under.

## HTTP API

- `GET /api/ml/models-2026/model-info`: complete 12-model catalog, including signed test MAE.
- `GET /api/ml/models-2026/health`: loads/checks the persistent worker and all active CBMs.
- `POST /api/ml/models-2026/predict`: builds features once and returns every available target.
- `GET|POST /api/ml/home-corners-2026/*` and `/api/ml/corners/home/*`: backwards-compatible
  Home Corners endpoints.

The batch prediction response includes `Predictions` and `FeaturePayloads`. Each payload is
the exact ordered subset sent to that model, so the page can display the same input detail as
the legacy predictor without calculating features in the browser.

## Configuration and rollback

```json
{
  "NewGenerationMl": {
    "ModelsRoot": "../models/football",
    "ActiveVersions": {
      "TargetHomeCorners": "home-corners-2026-08-09-trial-1840",
      "TargetAwayCorners": "targetawaycorners-2026-08-09-trial-53"
    },
    "PythonExecutable": "../.venv-new-generation/bin/python",
    "ScriptPath": "../predict_new_generation.py",
    "TimeoutSeconds": 60
  }
}
```

All 12 targets are configured in `CornersPredictionApi/appsettings.json`. Standard .NET
environment keys can override one version, for example:

```bash
export NewGenerationMl__ActiveVersions__TargetAwayCorners=another-immutable-version
```

`NEW_GENERATION_ML_ACTIVE_VERSION` remains supported for Home Corners. Restart the API after
changing any active version; no request can choose a file path or upload a bundle.

## Local execution

```bash
.venv-new-generation/bin/python -m unittest tests/test_new_generation_runtime.py
dotnet run --project CornersPredictionApi/CornersPredictionApi.csproj --launch-profile http
dotnet run --project CornersPrediction.Web/CornersPrediction.Web.csproj --launch-profile http
```

The API Dockerfile copies the complete `models/football` registry and uses the isolated
new-generation Python environment.

## Verification

All bundles must pass their five official smoke cases at tolerance `1e-8`. Runtime tests also
cover corrupted checksums, missing/unexpected/target features, null handling, negative
clipping, concurrency, all-target discovery and single-load multi-model worker behavior.
