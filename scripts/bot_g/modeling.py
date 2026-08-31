from __future__ import annotations

import importlib.metadata
import math
import warnings
from dataclasses import dataclass
from typing import Any

import numpy as np
from scipy.optimize import minimize


def sigmoid(value: np.ndarray) -> np.ndarray:
    return 1.0 / (1.0 + np.exp(-np.clip(value, -40.0, 40.0)))


@dataclass
class LogitResidualModel:
    l2: float = 1.0
    max_iterations: int = 1_000
    mean_: np.ndarray | None = None
    scale_: np.ndarray | None = None
    intercept_: float = 0.0
    coefficient_: np.ndarray | None = None
    converged_: bool = False

    def fit(self, x: np.ndarray, y: np.ndarray, market_offset: np.ndarray) -> "LogitResidualModel":
        x = np.asarray(x, dtype=float)
        y = np.asarray(y, dtype=float)
        offset = np.asarray(market_offset, dtype=float)
        if x.ndim != 2 or len(x) != len(y) or len(y) != len(offset):
            raise ValueError("Logit residual model received inconsistent array shapes.")
        if len(np.unique(y)) < 2:
            raise ValueError("Logit residual model requires both target classes.")
        self.mean_ = np.mean(x, axis=0)
        self.scale_ = np.std(x, axis=0)
        self.scale_ = np.where(self.scale_ > 1e-12, self.scale_, 1.0)
        z = (x - self.mean_) / self.scale_

        def objective(parameters: np.ndarray) -> tuple[float, np.ndarray]:
            intercept = parameters[0]
            coefficient = parameters[1:]
            eta = offset + intercept + z @ coefficient
            probability = sigmoid(eta)
            loss = float(np.sum(np.logaddexp(0.0, eta) - y * eta))
            loss += 0.5 * self.l2 * float(coefficient @ coefficient)
            residual = probability - y
            gradient = np.concatenate(
                ([float(np.sum(residual))], z.T @ residual + self.l2 * coefficient)
            )
            return loss, gradient

        initial = np.zeros(x.shape[1] + 1, dtype=float)
        result = minimize(
            objective,
            initial,
            method="L-BFGS-B",
            jac=True,
            options={"maxiter": self.max_iterations, "ftol": 1e-12},
        )
        if not np.isfinite(result.fun):
            raise RuntimeError("Logit residual optimization returned a non-finite objective.")
        self.intercept_ = float(result.x[0])
        self.coefficient_ = result.x[1:].astype(float)
        self.converged_ = bool(result.success)
        return self

    def adjustment(self, x: np.ndarray) -> np.ndarray:
        if self.mean_ is None or self.scale_ is None or self.coefficient_ is None:
            raise RuntimeError("Logit residual model is not fitted.")
        z = (np.asarray(x, dtype=float) - self.mean_) / self.scale_
        return self.intercept_ + z @ self.coefficient_

    def predict_proba(self, x: np.ndarray, market_offset: np.ndarray) -> np.ndarray:
        residual = np.clip(self.adjustment(x), -4.0, 4.0)
        return sigmoid(np.asarray(market_offset, dtype=float) + residual)

    @staticmethod
    def neutral_probability(market_offset: np.ndarray) -> np.ndarray:
        return sigmoid(np.asarray(market_offset, dtype=float))

    def to_artifact(self, feature_names: list[str] | tuple[str, ...]) -> dict[str, Any]:
        if self.mean_ is None or self.scale_ is None or self.coefficient_ is None:
            raise RuntimeError("Cannot serialize an unfitted model.")
        if len(feature_names) != len(self.coefficient_):
            raise ValueError("Feature names do not match fitted coefficients.")
        return {
            "type": "LogitResidualLogistic",
            "formula": "logit(pFinal)=logit(pMarket)+clip(intercept+sum(coefficient*z),-4,4)",
            "intercept": self.intercept_,
            "converged": self.converged_,
            "features": [
                {
                    "name": name,
                    "mean": float(mean),
                    "scale": float(scale),
                    "coefficient": float(coefficient),
                }
                for name, mean, scale, coefficient in zip(
                    feature_names, self.mean_, self.scale_, self.coefficient_
                )
            ],
        }


def installed_version(distribution: str) -> str | None:
    try:
        return importlib.metadata.version(distribution)
    except importlib.metadata.PackageNotFoundError:
        return None


def available_families() -> dict[str, dict[str, Any]]:
    return {
        "logistic": {"available": True, "version": "scipy-offset-lr-v1"},
        "catboost": {"available": installed_version("catboost") is not None, "version": installed_version("catboost")},
        "xgboost": {"available": installed_version("xgboost") is not None, "version": installed_version("xgboost")},
        "lightgbm": {"available": installed_version("lightgbm") is not None, "version": installed_version("lightgbm")},
    }


def fit_optional_classifier(
    family: str,
    x: np.ndarray,
    y: np.ndarray,
    market_offset: np.ndarray,
    seed: int,
    quick: bool,
) -> Any:
    augmented = np.column_stack([market_offset, x])
    estimators = 35 if quick else 250
    if family == "catboost":
        from catboost import CatBoostClassifier

        model = CatBoostClassifier(
            iterations=estimators,
            depth=5,
            learning_rate=0.04,
            loss_function="Logloss",
            random_seed=seed,
            verbose=False,
            allow_writing_files=False,
            thread_count=1,
        )
    elif family == "xgboost":
        from xgboost import XGBClassifier

        model = XGBClassifier(
            n_estimators=estimators,
            max_depth=4,
            learning_rate=0.04,
            subsample=0.85,
            colsample_bytree=0.85,
            objective="binary:logistic",
            eval_metric="logloss",
            random_state=seed,
            n_jobs=1,
            tree_method="hist",
        )
    elif family == "lightgbm":
        from lightgbm import LGBMClassifier

        model = LGBMClassifier(
            n_estimators=estimators,
            max_depth=5,
            learning_rate=0.04,
            subsample=0.85,
            colsample_bytree=0.85,
            objective="binary",
            random_state=seed,
            n_jobs=1,
            verbosity=-1,
        )
    else:
        raise ValueError(f"Unsupported optional family: {family}")
    model.fit(augmented, y)
    return model


def predict_optional_classifier(model: Any, x: np.ndarray, market_offset: np.ndarray) -> np.ndarray:
    augmented = np.column_stack([market_offset, x])
    with warnings.catch_warnings():
        warnings.filterwarnings(
            "ignore",
            message="X does not have valid feature names.*",
            category=UserWarning,
        )
        probability = np.asarray(model.predict_proba(augmented))[:, 1]
    if not np.isfinite(probability).all():
        raise RuntimeError("Optional classifier returned non-finite probabilities.")
    return probability


def bootstrap_logit_ensemble(
    x: np.ndarray,
    y: np.ndarray,
    offset: np.ndarray,
    fixture_ids: np.ndarray,
    count: int,
    seed: int,
    l2: float,
    max_iterations: int,
) -> list[LogitResidualModel]:
    fixture_ids = np.asarray(fixture_ids).astype(str)
    fixtures = np.unique(fixture_ids)
    if len(fixtures) < 2:
        raise ValueError("Fixture bootstrap requires at least two fixtures.")
    by_fixture = {fixture: np.flatnonzero(fixture_ids == fixture) for fixture in fixtures}
    rng = np.random.default_rng(seed)
    models: list[LogitResidualModel] = []
    attempts = 0
    while len(models) < count and attempts < count * 5:
        attempts += 1
        sampled = rng.choice(fixtures, size=len(fixtures), replace=True)
        indices = np.concatenate([by_fixture[value] for value in sampled])
        if len(np.unique(y[indices])) < 2:
            continue
        models.append(LogitResidualModel(l2, max_iterations).fit(x[indices], y[indices], offset[indices]))
    if not models:
        raise RuntimeError("No valid bootstrap models could be fitted.")
    return models


def ensemble_probabilities(
    models: list[LogitResidualModel],
    x: np.ndarray,
    offset: np.ndarray,
    lower_quantile: float,
) -> dict[str, np.ndarray]:
    matrix = np.column_stack([model.predict_proba(x, offset) for model in models])
    return {
        "mean": np.mean(matrix, axis=1),
        "std": np.std(matrix, axis=1),
        "lower": np.quantile(matrix, lower_quantile, axis=1),
        "upper": np.quantile(matrix, 1.0 - lower_quantile, axis=1),
    }
