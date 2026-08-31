# Bot G artifact slot

This directory intentionally contains no `active.json`.

G2026 fails closed with `ModelUnavailable` until the offline pipeline produces a
non-synthetic, temporally valid artifact that passes the explicit final holdout and
promotion gates. Do not copy a final-refit base model or a synthetic self-test
artifact here.

Bot G v1.1 has no automatic activation path. Export and preflight do not write this
directory. A future `active.json` must be the result of a separately reviewed,
manual deployment of an immutable real artifact whose configuration, training
contract, per-market lineages and Football Intelligence settings match runtime.

See [`../../docs/bot-g-goals-market-anchored.md`](../../docs/bot-g-goals-market-anchored.md)
and [`../../scripts/bot_g/README.md`](../../scripts/bot_g/README.md).
