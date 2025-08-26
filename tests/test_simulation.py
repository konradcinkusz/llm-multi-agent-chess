# tests/test_simulation.py
"""Tests for headless simulation utilities."""

from src.agents.random_agent import RandomAgent
from src.utils.simulator import simulate_game


def test_simulation_runs():
    """A random-vs-random game should progress and return a status."""

    white = RandomAgent("White")
    black = RandomAgent("Black")

    game, status = simulate_game(white, black, max_moves=10)

    # After simulation there should be at least one move in history
    assert len(game.history) > 0
    assert isinstance(status, str) and status
