#!/usr/bin/env node

// Canonical C/D/E entry point. The legacy C/D filename remains executable so
// existing local commands and documentation do not break.
await import('./compare-bot-c-d-backtest.mjs');
