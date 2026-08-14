# Bot D meta-model artifact

Place the optional active LogisticRegression artifact at `active.json`.

Required contract:

- `ModelType`: `LogisticRegression`
- `FeatureSchemaVersion`: `bot-d-features-1.0.0`
- finite intercept, feature means, scales and coefficients
- every named numeric feature must exist in the runtime snapshot

If the artifact is absent or incompatible, Bot D records the reason and uses its explainable rule-based fallback. Override this location with `BOT_D_META_MODEL_ARTIFACT_PATH`.
