# Bot G2026 offline ML pipeline

This package trains and audits the Goals meta-model without SQL or service-side writes. The
deployment model is always a neutral market-offset logistic regression:

`logit(pFinal) = logit(pMarketNoVig) + intercept + Σ coefficient × standardizedFeature`

With a zero intercept and zero coefficients it returns the no-vig market probability exactly.
CatBoost, XGBoost and LightGBM are OOF comparison families only; each is reported as unavailable
when its pinned optional package is absent.

## Safety contract

The input is an immutable CSV, JSON array, JSONL or NDJSON candidate universe conforming to
[`input.schema.json`](input.schema.json). Every `QuoteId` must have exactly two consistent rows:
one `Over` and one `Under`, including both decimal odds. The loader rejects missing/non-finite
values, duplicate candidates, unsupported markets/lines, future features/odds, base-model cutoffs
at or after prediction time, predictions at or after kickoff, and outcomes available at or before
prediction time.

The live identity is configuration `bot-g-goals-market-intelligence-1.1.0`, unchanged numeric
feature schema `bot-g-goals-features-1.0.0`, training/export contract
`bot-g-training-export-1.1.0`, and meta-model `bot-g-market-meta-1.1.0`. Every real row must carry
the exact per-market base-model lineage and reproducible Football Intelligence version,
selected-side adjustment, evidence statuses and immutable cutoffs. v1.0 rows are rejected rather
than relabeled. Missing/unusable intelligence produces an abstention, never a favorable default.

`FixtureId` is atomic in final holdout, expanding-window folds and bootstraps. Training rows must
have `OutcomeAvailableUtc` strictly before each validation knowledge cutoff after embargo and
outcome lag. The final test is excluded unless an explicit flag is supplied.

`ActualValue` is the resolved total for the row's market: match total, home-team goals, or away-team
goals. It is used only for labels and five-state Asian settlement (`Win`, `HalfWin`, `Push`,
`HalfLoss`, `Loss`), never as a feature. `FPublished` is optional and enables paired shared/G-only/
F-only economics and predictive metrics on identical candidate signatures. Supplying the optional
`FProbability`, `FEdge` and `FExpectedValue` fields also reports shared mean G-minus-F differences;
their absence is reported explicitly rather than imputed. `IsSynthetic=true` is also optional, must
be constant for the whole file, and permanently disables deployability even if the CLI flag is
omitted.

The minimum trustworthy export needs historical snapshots of both sides, immutable odds timestamps,
feature/model cutoffs, outcome availability and resolved outcomes. A current-state quote table that
overwrites odds is not sufficient for honest training or backtesting.

The exporter must use the same definitions as the runtime feature builder. `LegacyPrediction` and
`Prediction2026` are for the row's market; `ContextPrediction` is the pre-match historical context;
`HistoricalStd` is the runtime overall-last-20 standard deviation used to form
`modelVsContextSigma`; `HistoryCount` and `DataQualityScore` are their as-of-snapshot values. Model
trained-through timestamps describe the exact artifacts that produced those predictions. Values
reconstructed after the match are invalid even when their columns look complete.

## Reproducible commands

Create an environment with CPython 3.10 and install the core lock. Install the optional lock only
when tree-model comparisons are desired.

```bash
python -m pip install -r scripts/bot_g/requirements.lock.txt
python -m pip install -r scripts/bot_g/requirements-optional.lock.txt
python scripts/train_bot_g.py --self-test
```

Export and preflight without touching the final test:

```bash
BOT_G_SQL_CONNECTION_STRING='...' dotnet run \
  --project tools/BotGTrainingExport/BotGTrainingExport.csproj -- \
  --output /secure/path/goals-candidates.jsonl \
  --as-of 2026-08-31T23:59:59Z

python scripts/train_bot_g.py \
  --input /secure/path/goals-candidates.jsonl \
  --preflight-only
```

The standalone exporter calls the existing stored procedure with resolved outcomes only, writes
an immutable JSONL plus SHA-256 manifest, accepts its connection only via environment variable,
and never logs it. Preflight performs no training and writes no artifact.

Train without touching the final test after preflight passes:

```bash
python scripts/train_bot_g.py \
  --input /absolute/path/goals-candidates.jsonl \
  --output-dir models/bot-g
```

One explicitly authorized final evaluation:

```bash
python scripts/train_bot_g.py \
  --input /absolute/path/goals-candidates.jsonl \
  --output-dir models/bot-g \
  --evaluate-final-test
```

Walk-forward backtest (the final block remains excluded by default):

```bash
python scripts/backtest_bot_g.py \
  --input /absolute/path/goals-candidates.jsonl \
  --output-dir models/bot-g
```

Automatic activation is intentionally disabled: `--activate` fails and never creates
`active.json`. After an independent final-test review, an approved versioned artifact can only be
promoted by a separate, human-reviewed deployment step. Backtests never activate.
Versioned artifacts, reports, OOF exports and final-test exports are immutable. Re-running a
version must fail; use new `model_version` and `configuration_version` values instead of
overwriting experiment evidence.

## Evaluation and artifacts

The trainer runs the five same-row ablations `market_only`, `market_legacy`, `market_2026`,
`market_both`, and `market_both_context`. It fits calibration only from temporal OOF predictions,
chooses monotonic Platt or Beta calibration, then applies effective-sample shrinkage through global,
market, side and bookmaker levels. Platt and identity profiles are serialized into the runtime's
canonical Beta formula without changing their mathematics.

Uncertainty comes from fixture-cluster bootstrap models; performance intervals bootstrap by fixture.
OOD uses training median/MAD and 1st/99th percentiles. Quarter and integer lines require a five-state,
line-specific settlement profile with adequate evidence or the decision is `Abstain`. Reports include
Brier, log loss, ECE, calibration slope/intercept, AUC, EV buckets, coverage, yield, drawdown, profit
factor, streaks, paired F comparisons, drift PSI and a promotion scorecard.
The promotion gate counts independent fixtures (never candidate rows), requires at least 200,
an explicitly evaluated final block and at least two chronological OOS walk-forward windows.

The runtime JSON emits these canonical top-level fields:

- `modelVersion`, `configurationVersion`, `featureSchemaVersion`,
  `trainingContractVersion`, `trainedThroughUtc`
- exact `training.marketLineages[]` and the full `footballIntelligence` runtime contract
- `model` with `type=LogitResidualLogistic`, intercept and standardized coefficients
- `ensemble[]` with a stable `name`, intercept and the same feature contract
- `calibration[]`, `oodFeatureStats[]`, and line-specific `settlementProfiles[]`

Rich experiment metadata also records seed, Python/package versions, Git state, full configuration,
dataset SHA-256 and the locked final-test boundary.

Synthetic self-test reports are structural checks only. They never represent real performance,
never make an artifact deployable and never create `active.json`. Without sufficient real input the
pipeline intentionally produces no real metrics and does not activate a model.
