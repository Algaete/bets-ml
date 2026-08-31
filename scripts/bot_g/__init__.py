"""Offline, leakage-safe training and backtesting for Bot G2026."""

from .config import BotGConfig
from .contracts import CandidateDataset, load_candidate_dataset
from .pipeline import train_bot_g

__all__ = ["BotGConfig", "CandidateDataset", "load_candidate_dataset", "train_bot_g"]
