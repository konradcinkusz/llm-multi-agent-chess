# utils/simulator.py
"""Utilities for running headless chess simulations.

The functions in this module allow automated games between two agents to
be executed without the Streamlit user interface. They are useful for
testing or for running batches of simulations.
"""

from __future__ import annotations

from typing import Tuple

from ..agents.base import ChessAgent
from ..game.chess_game import ChessGame


def simulate_game(
    white: ChessAgent, black: ChessAgent, max_moves: int = 200
) -> Tuple[ChessGame, str]:
    """Play a single game between two agents.

    Args:
        white: Agent playing White pieces.
        black: Agent playing Black pieces.
        max_moves: Maximum number of ply to play before stopping.

    Returns:
        A tuple of the final :class:`ChessGame` instance and a status
        string describing the outcome.
    """

    game = ChessGame(white, black)

    for _ in range(max_moves):
        success, _ = game.make_move()
        if not success or game.board.is_game_over():
            break

    return game, game.get_game_status()

