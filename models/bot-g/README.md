# Bot G artifact slot

This directory intentionally contains no `active.json`.

G2026 fails closed with `ModelUnavailable` until the offline pipeline produces a
non-synthetic, temporally valid artifact that passes the explicit final holdout and
promotion gates. Do not copy a final-refit base model or a synthetic self-test
artifact here.

See [`../../docs/bot-g-goals-market-anchored.md`](../../docs/bot-g-goals-market-anchored.md)
and [`../../scripts/bot_g/README.md`](../../scripts/bot_g/README.md).
