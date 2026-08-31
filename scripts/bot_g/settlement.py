from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Iterable

import numpy as np


SETTLEMENT_STATES = ("Win", "HalfWin", "Push", "HalfLoss", "Loss")


@dataclass(frozen=True)
class Settlement:
    state: str
    profit_per_unit: float
    component_states: tuple[str, ...]

    @property
    def positive_return(self) -> int:
        return int(self.profit_per_unit > 0)


def _fraction(line: float) -> float:
    return round(line - math.floor(line), 2)


def is_quarter_line(line: float) -> bool:
    return _fraction(line) in {0.25, 0.75}


def requires_ordinal_distribution(line: float) -> bool:
    return _fraction(line) in {0.0, 0.25, 0.75}


def component_lines(line: float) -> tuple[float, ...]:
    fraction = _fraction(line)
    floor = math.floor(line)
    if fraction == 0.25:
        return float(floor), float(floor) + 0.5
    if fraction == 0.75:
        return float(floor) + 0.5, float(floor) + 1.0
    if fraction not in {0.0, 0.5}:
        raise ValueError(f"Unsupported Asian line increment: {line}")
    return (float(line),)


def settle(selection: str, line: float, actual: float, odds: float) -> Settlement:
    if selection not in {"Over", "Under"}:
        raise ValueError(f"Unsupported selection: {selection!r}")
    if not all(math.isfinite(value) for value in (line, actual, odds)) or odds <= 1:
        raise ValueError("Line, actual and decimal odds must be finite; odds must exceed 1.")

    states: list[str] = []
    profits: list[float] = []
    for component in component_lines(line):
        delta = actual - component if selection == "Over" else component - actual
        if delta > 1e-12:
            states.append("Win")
            profits.append(odds - 1.0)
        elif delta < -1e-12:
            states.append("Loss")
            profits.append(-1.0)
        else:
            states.append("Push")
            profits.append(0.0)

    profit = float(np.mean(profits))
    if len(states) == 1:
        state = states[0]
    elif states.count("Win") == 1 and states.count("Push") == 1:
        state = "HalfWin"
    elif states.count("Loss") == 1 and states.count("Push") == 1:
        state = "HalfLoss"
    elif all(value == "Win" for value in states):
        state = "Win"
    elif all(value == "Loss" for value in states):
        state = "Loss"
    elif all(value == "Push" for value in states):
        state = "Push"
    else:  # Defensive: opposing Win/Loss components net to a push.
        state = "Push"
    return Settlement(state, profit, tuple(states))


def state_profit(state: str, odds: float) -> float:
    if state == "Win":
        return odds - 1.0
    if state == "HalfWin":
        return (odds - 1.0) / 2.0
    if state == "Push":
        return 0.0
    if state == "HalfLoss":
        return -0.5
    if state == "Loss":
        return -1.0
    raise ValueError(f"Unknown settlement state: {state!r}")


def normalize_distribution(probabilities: dict[str, float]) -> dict[str, float]:
    values = {state: max(0.0, float(probabilities.get(state, 0.0))) for state in SETTLEMENT_STATES}
    total = sum(values.values())
    if total <= 0:
        raise ValueError("Settlement distribution has no probability mass.")
    return {state: value / total for state, value in values.items()}


def tilt_distribution_to_positive_probability(
    base: dict[str, float], positive_probability: float
) -> dict[str, float]:
    """Preserve within-group state ratios while matching P(positive return)."""
    base = normalize_distribution(base)
    positive_probability = float(np.clip(positive_probability, 0.0, 1.0))
    positive = ("Win", "HalfWin")
    nonpositive = ("Push", "HalfLoss", "Loss")
    positive_mass = sum(base[state] for state in positive)
    nonpositive_mass = sum(base[state] for state in nonpositive)
    result: dict[str, float] = {}
    for state in positive:
        ratio = base[state] / positive_mass if positive_mass > 0 else (1.0 if state == "Win" else 0.0)
        result[state] = positive_probability * ratio
    for state in nonpositive:
        ratio = base[state] / nonpositive_mass if nonpositive_mass > 0 else (1.0 if state == "Loss" else 0.0)
        result[state] = (1.0 - positive_probability) * ratio
    return normalize_distribution(result)


def expected_profit(distribution: dict[str, float], odds: float) -> float:
    normalized = normalize_distribution(distribution)
    return float(sum(normalized[state] * state_profit(state, odds) for state in SETTLEMENT_STATES))


def distribution_from_states(states: Iterable[str], alpha: float = 0.5) -> dict[str, float]:
    counts = {state: float(alpha) for state in SETTLEMENT_STATES}
    for state in states:
        if state not in counts:
            raise ValueError(f"Unknown settlement state: {state!r}")
        counts[state] += 1.0
    return normalize_distribution(counts)
