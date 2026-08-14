from __future__ import annotations

import json
import os
import unittest
import urllib.error
import urllib.request


API_KEY = os.environ.get("HOME_CORNERS_API_KEY")
BASE_URL = os.environ.get("HOME_CORNERS_API_URL", "http://localhost:5070").rstrip("/")


@unittest.skipUnless(API_KEY, "Set HOME_CORNERS_API_KEY to run live endpoint integration tests.")
class HomeCorners2026EndpointIntegrationTests(unittest.TestCase):
    def request_json(self, path: str, payload: dict | None = None):
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            BASE_URL + path,
            data=data,
            method="GET" if payload is None else "POST",
            headers={
                "Accept": "application/json",
                "Content-Type": "application/json",
                "X-Internal-Api-Key": API_KEY or "",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=90) as response:
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            return error.code, json.loads(error.read())

    def test_model_info_and_legacy_alias(self):
        for path in (
            "/api/ml/home-corners-2026/model-info",
            "/api/ml/corners/home/model-info",
        ):
            status, body = self.request_json(path)
            self.assertEqual(200, status)
            self.assertTrue(body["Ready"])
            self.assertEqual(60, body["FeatureCount"])
            self.assertEqual("home-corners-2026-08-09-trial-1840", body["ModelVersion"])

    def test_health_reports_loaded_model(self):
        status, body = self.request_json("/api/ml/home-corners-2026/health")
        self.assertEqual(200, status)
        self.assertTrue(body["Loaded"])
        self.assertEqual("healthy", body["Status"])

    def test_models_2026_catalog_reports_all_twelve_models(self):
        status, body = self.request_json("/api/ml/models-2026/model-info")
        self.assertEqual(200, status)
        self.assertTrue(body["Ready"])
        self.assertEqual(12, body["ReadyModels"])
        self.assertEqual(12, body["TotalModels"])
        self.assertEqual(12, len(body["Models"]))
        self.assertAlmostEqual(
            2.1858147832254478,
            next(model for model in body["Models"] if model["Target"] == "TargetHomeCorners")["TestMae"],
        )
        self.assertEqual(
            {
                "TargetHomeCorners",
                "TargetAwayCorners",
                "TargetTotalCorners",
                "TargetHomeShots",
                "TargetAwayShots",
                "TargetTotalShots",
                "TargetHomeShotsOnGoal",
                "TargetAwayShotsOnGoal",
                "TargetTotalShotsOnGoal",
                "TargetHomeGoals",
                "TargetAwayGoals",
                "TargetTotalGoals",
            },
            {model["Target"] for model in body["Models"]},
        )

    def test_target_field_is_rejected_by_endpoint(self):
        status, body = self.request_json(
            "/api/ml/home-corners-2026/predict",
            {
                "league": "Premier Division (Ireland)",
                "season": "2026",
                "matchDate": "2026-08-10",
                "homeTeam": "Waterford",
                "awayTeam": "Shelbourne",
                "isKnockout": False,
                "TargetHomeCorners": 4,
            },
        )
        self.assertEqual(400, status)
        self.assertIn("Target fields are forbidden", body["error"])

    def test_missing_match_fields_are_rejected(self):
        status, body = self.request_json(
            "/api/ml/home-corners-2026/predict",
            {
                "league": "",
                "matchDate": "2026-08-10",
                "homeTeam": "Waterford",
                "awayTeam": "Shelbourne",
            },
        )
        self.assertEqual(400, status)
        self.assertIn("League, HomeTeam and AwayTeam are required", body["error"])

    def test_prediction_uses_historical_feature_builder_and_cbm(self):
        status, body = self.request_json(
            "/api/ml/home-corners-2026/predict",
            {
                "league": "Premier Division (Ireland)",
                "season": "2026",
                "matchDate": "2026-08-10",
                "homeTeam": "Waterford",
                "awayTeam": "Shelbourne",
                "homeFormation": None,
                "awayFormation": None,
                "isKnockout": False,
            },
        )
        self.assertEqual(200, status)
        self.assertEqual("TargetHomeCorners", body["target"])
        self.assertGreaterEqual(body["predictionClipped"], 0)
        self.assertEqual("home-corners-2026-08-09-trial-1840", body["modelVersion"])
        self.assertEqual("2026-08-07", body["trainedThrough"])

    def test_batch_prediction_returns_all_models_and_exact_payloads(self):
        status, body = self.request_json(
            "/api/ml/models-2026/predict",
            {
                "league": "Premier Division (Ireland)",
                "season": "2026",
                "matchDate": "2026-08-10",
                "homeTeam": "Waterford",
                "awayTeam": "Shelbourne",
                "homeFormation": None,
                "awayFormation": None,
                "isKnockout": False,
            },
        )
        self.assertEqual(200, status)
        self.assertEqual(12, len(body["Predictions"]))
        self.assertEqual(12, len(body["FeaturePayloads"]))
        for prediction in body["Predictions"]:
            target = prediction["target"]
            self.assertGreaterEqual(prediction["predictionClipped"], 0)
            self.assertIn(target, body["FeaturePayloads"])
            self.assertGreater(len(body["FeaturePayloads"][target]), 0)
            self.assertFalse(
                any(name.startswith("Target") for name in body["FeaturePayloads"][target])
            )


if __name__ == "__main__":
    unittest.main()
