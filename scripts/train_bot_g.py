#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path

from bot_g.cli import load_config, parse_families
from bot_g.contracts import load_candidate_dataset
from bot_g.pipeline import train_bot_g, write_training_outputs
from bot_g.preflight import build_preflight_report, require_training_ready
from bot_g.selftest import run_self_test


def parser() -> argparse.ArgumentParser:
    value = argparse.ArgumentParser(
        description="Train the leakage-safe, market-anchored Bot G2026 offline artifact."
    )
    mode = value.add_mutually_exclusive_group(required=True)
    mode.add_argument("--input", help="Candidate-universe CSV, JSON, JSONL or NDJSON.")
    mode.add_argument("--self-test", action="store_true", help="Run the synthetic structural test.")
    value.add_argument("--output-dir", default="models/bot-g", help="Versioned artifact directory.")
    value.add_argument(
        "--preflight-only", action="store_true",
        help="Validate identity, lineage, temporal cutoffs and intelligence without training or writing files.",
    )
    value.add_argument("--config", help="Optional JSON overrides using BotGConfig snake_case names.")
    value.add_argument("--final-test-start", help="Explicit UTC boundary; otherwise temporal fraction is used.")
    value.add_argument(
        "--evaluate-final-test", action="store_true",
        help="Explicit one-time permission to score the intact final test block.",
    )
    value.add_argument(
        "--families", default="logistic,catboost,xgboost,lightgbm",
        help="Comma-separated OOF comparisons; unavailable optional libraries are recorded.",
    )
    value.add_argument("--quick", action="store_true", help="Reduce optional models and metric bootstraps.")
    value.add_argument(
        "--synthetic", action="store_true",
        help="Mark supplied input synthetic; this permanently disables activation and real-metric claims.",
    )
    value.add_argument(
        "--activate", action="store_true",
        help="Reserved and fail-closed: automatic active.json creation is disabled for Bot G v1.1.",
    )
    return value


def main() -> int:
    args = parser().parse_args()
    if args.self_test:
        print(json.dumps(run_self_test(), indent=2, ensure_ascii=False))
        return 0
    config = load_config(args.config)
    dataset = load_candidate_dataset(args.input, config)
    preflight = build_preflight_report(dataset, config)
    if args.preflight_only:
        print(json.dumps(preflight, indent=2, ensure_ascii=False))
        return 0 if preflight["trainingReady"] else 2
    require_training_ready(preflight)
    result = train_bot_g(
        dataset,
        config,
        evaluate_final_test=args.evaluate_final_test,
        final_test_start=args.final_test_start,
        families=parse_families(args.families),
        quick=args.quick,
        synthetic=args.synthetic,
        repository_root=Path(__file__).resolve().parents[1],
    )
    paths = write_training_outputs(result, Path(args.output_dir), config, activate=args.activate)
    print(json.dumps({
        "status": "COMPLETE",
        "preflight": preflight,
        "paths": paths,
        "finalTest": result.report["finalTest"]["status"],
        "activationAllowed": result.report["activationAllowed"],
        "activeJsonWritten": bool(args.activate and result.report["activationAllowed"]),
        "realMetricsGenerated": result.report["realMetricsGenerated"],
    }, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
