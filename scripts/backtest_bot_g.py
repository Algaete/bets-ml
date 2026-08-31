#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path

from bot_g.backtest import run_walk_forward_backtest, write_backtest_outputs
from bot_g.cli import load_config
from bot_g.contracts import load_candidate_dataset


def parser() -> argparse.ArgumentParser:
    value = argparse.ArgumentParser(
        description="Run a fixture-grouped expanding-window Bot G2026 backtest."
    )
    value.add_argument("--input", required=True, help="Candidate-universe CSV, JSON, JSONL or NDJSON.")
    value.add_argument("--output-dir", default="models/bot-g", help="Backtest output directory.")
    value.add_argument("--config", help="Optional JSON overrides using BotGConfig snake_case names.")
    value.add_argument("--final-test-start", help="Explicit UTC final-test boundary.")
    value.add_argument(
        "--include-final-test", action="store_true",
        help="Explicitly include the otherwise intact final block in walk-forward evaluation.",
    )
    value.add_argument("--quick", action="store_true", help="Reduce ensemble and metric bootstraps.")
    value.add_argument(
        "--synthetic", action="store_true",
        help="Mark supplied input synthetic; reports cannot claim real metrics.",
    )
    return value


def main() -> int:
    args = parser().parse_args()
    config = load_config(args.config)
    dataset = load_candidate_dataset(args.input, config)
    result = run_walk_forward_backtest(
        dataset,
        config,
        include_final_test=args.include_final_test,
        final_test_start=args.final_test_start,
        quick=args.quick,
        synthetic=args.synthetic,
    )
    paths = write_backtest_outputs(result, Path(args.output_dir), config.model_version)
    print(json.dumps({
        "status": "COMPLETE",
        "paths": paths,
        "finalTestIncluded": result.report["finalTestIncluded"],
        "activationAllowed": False,
        "realMetricsGenerated": result.report["realMetricsGenerated"],
    }, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
