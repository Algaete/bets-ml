from __future__ import annotations

import importlib.util
import io
import json
import math
import sys
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from unittest.mock import patch

import pandas as pd


sys.dont_write_bytecode = True


ROOT = Path(__file__).resolve().parents[1]
BUNDLE = (
    ROOT
    / "models"
    / "football"
    / "home-corners-2026-08-09-trial-1840"
    / "corners"
    / "home"
)
BUNDLE_MANIFESTS = sorted(
    (ROOT / "models" / "football").glob("*/*/*/deployment_manifest.json")
)
SPEC = importlib.util.spec_from_file_location(
    "new_generation_runtime", ROOT / "predict_new_generation.py"
)
runtime = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = runtime
SPEC.loader.exec_module(runtime)


class FakeModel:
    def __init__(self, raw: float = 5.73):
        self.raw = raw

    def predict(self, _values):
        clipped = max(0.0, self.raw)
        return pd.DataFrame(
            {
                "prediction_raw": [self.raw],
                "prediction_clipped": [clipped],
                "prediction_rounded": [int(round(clipped))],
            }
        )


class HomeCorners2026RuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest_path = BUNDLE / "deployment_manifest.json"
        cls.package = runtime.load_package(cls.manifest_path)
        cls.model = runtime._load_model(cls.package)
        cls.fixture = json.loads(
            (BUNDLE / "deployment_smoke_fixture.json").read_text(encoding="utf-8")
        )

    def test_bundle_loads_native_cbm_with_exact_schema(self):
        self.assertEqual(".cbm", self.package.model_path.suffix)
        self.assertEqual(60, len(self.package.metadata["features"]))
        self.assertEqual(
            self.package.metadata["features"], list(self.model.features)
        )

    def test_all_five_official_parity_cases(self):
        tolerance = float(self.fixture["tolerance"])
        self.assertEqual(5, len(self.fixture["cases"]))
        for case in self.fixture["cases"]:
            with self.subTest(case=case["case_id"]):
                result = runtime.predict_with_model(
                    self.package, self.model, case["features"]
                )
                expected = case["expected"]
                self.assertLessEqual(
                    abs(result["predictionRaw"] - expected["prediction_raw"]), tolerance
                )
                self.assertLessEqual(
                    abs(result["predictionClipped"] - expected["prediction_clipped"]),
                    tolerance,
                )
                self.assertEqual(
                    expected["prediction_rounded"], result["predictionRounded"]
                )

    def test_all_twelve_bundles_load_and_pass_official_parity(self):
        self.assertEqual(12, len(BUNDLE_MANIFESTS))
        targets = set()
        for manifest_path in BUNDLE_MANIFESTS:
            package = runtime.load_package(manifest_path)
            model = runtime._load_model(package)
            fixture = json.loads(
                (manifest_path.parent / "deployment_smoke_fixture.json").read_text(
                    encoding="utf-8"
                )
            )
            targets.add(package.target)
            self.assertEqual(5, len(fixture["cases"]))
            for case in fixture["cases"]:
                with self.subTest(target=package.target, case=case["case_id"]):
                    result = runtime.predict_with_model(
                        package, model, case["features"]
                    )
                    expected = case["expected"]
                    self.assertLessEqual(
                        abs(result["predictionRaw"] - expected["prediction_raw"]),
                        fixture["tolerance"],
                    )
                    self.assertLessEqual(
                        abs(
                            result["predictionClipped"]
                            - expected["prediction_clipped"]
                        ),
                        fixture["tolerance"],
                    )
                    self.assertEqual(
                        expected["prediction_rounded"], result["predictionRounded"]
                    )
        self.assertEqual(runtime.SUPPORTED_TARGETS, targets)

    def test_bad_checksum_is_rejected(self):
        real_sha256 = runtime._sha256

        def tampered(path: Path) -> str:
            return "0" * 64 if path.name == "model_metadata.json" else real_sha256(path)

        with patch.object(runtime, "_sha256", side_effect=tampered):
            with self.assertRaisesRegex(runtime.ModelPackageError, "Checksum mismatch"):
                runtime.load_package(self.manifest_path)

    def test_missing_feature_is_rejected(self):
        features = dict(self.fixture["cases"][0]["features"])
        features.pop("HomeAvgCornersFor5")
        with self.assertRaisesRegex(runtime.ModelPackageError, "Required features"):
            runtime.predict_with_model(self.package, FakeModel(), features)

    def test_target_feature_is_rejected(self):
        features = dict(self.fixture["cases"][0]["features"])
        features["TargetHomeCorners"] = 9
        with self.assertRaisesRegex(runtime.ModelPackageError, "Target columns"):
            runtime.predict_with_model(self.package, FakeModel(), features)

    def test_unexpected_feature_is_rejected(self):
        features = dict(self.fixture["cases"][0]["features"])
        features["MadeUpFeature"] = 9
        with self.assertRaisesRegex(runtime.ModelPackageError, "Unexpected inference"):
            runtime.predict_with_model(self.package, FakeModel(), features)

    def test_unknown_category_and_null_numeric_use_official_runtime(self):
        features = dict(self.fixture["cases"][0]["features"])
        features["HomeFormationStyle"] = None
        features["HomeAvgHomeCornersFor5"] = None
        result = runtime.predict_with_model(self.package, self.model, features)
        self.assertTrue(math.isfinite(result["predictionClipped"]))

    def test_negative_prediction_is_clipped(self):
        features = dict(self.fixture["cases"][0]["features"])
        result = runtime.predict_with_model(self.package, FakeModel(-1.25), features)
        self.assertEqual(-1.25, result["predictionRaw"])
        self.assertEqual(0.0, result["predictionClipped"])

    def test_two_concurrent_predictions_are_stable(self):
        cases = self.fixture["cases"][:2]

        def execute(case):
            return runtime.predict_with_model(
                self.package, self.model, case["features"]
            )["predictionRaw"]

        with ThreadPoolExecutor(max_workers=2) as pool:
            actual = list(pool.map(execute, cases))
        expected = [case["expected"]["prediction_raw"] for case in cases]
        for left, right in zip(actual, expected, strict=True):
            self.assertLessEqual(abs(left - right), self.fixture["tolerance"])

    def test_worker_loads_model_once(self):
        request = {
            "id": "predict-1",
            "action": "predict",
            "payload": self.fixture["cases"][0]["features"],
        }
        worker_input = json.dumps({"id": "health-1", "action": "health"}) + "\n"
        worker_input += json.dumps(request) + "\n"
        output = io.StringIO()
        with (
            patch.object(runtime, "_load_model", return_value=FakeModel()) as load_model,
            patch.object(sys, "stdin", io.StringIO(worker_input)),
            patch.object(sys, "stdout", output),
        ):
            self.assertEqual(0, runtime.serve(self.manifest_path))
        responses = [json.loads(line) for line in output.getvalue().splitlines()]
        self.assertEqual("ready", responses[0]["event"])
        self.assertEqual("health-1", responses[1]["id"])
        self.assertEqual("predict-1", responses[2]["id"])
        load_model.assert_called_once()

    def test_registry_worker_loads_each_model_once_and_predicts_many(self):
        manifest_paths = BUNDLE_MANIFESTS[:2]
        packages = [runtime.load_package(path) for path in manifest_paths]
        payloads = {}
        for package in packages:
            fixture = json.loads(
                (package.bundle_dir / "deployment_smoke_fixture.json").read_text(
                    encoding="utf-8"
                )
            )
            payloads[package.target] = fixture["cases"][0]["features"]
        worker_input = json.dumps(
            {"id": "many-1", "action": "predict_many", "payload": payloads}
        ) + "\n"
        output = io.StringIO()
        with (
            patch.object(runtime, "_load_model", return_value=FakeModel()) as load_model,
            patch.object(sys, "stdin", io.StringIO(worker_input)),
            patch.object(sys, "stdout", output),
        ):
            self.assertEqual(0, runtime.serve(manifest_paths))
        responses = [json.loads(line) for line in output.getvalue().splitlines()]
        self.assertEqual("ready", responses[0]["event"])
        self.assertEqual("many-1", responses[1]["id"])
        self.assertEqual(2, len(responses[1]["result"]["predictions"]))
        self.assertEqual(2, load_model.call_count)


if __name__ == "__main__":
    unittest.main()
