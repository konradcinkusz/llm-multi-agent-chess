# agents/random_agent.py
"""Simple agent that selects random legal moves.

This agent is useful for simulations or testing where a full LLM-based
agent is unnecessary. It adheres to the :class:`ChessAgent` interface,
returning moves in standard algebraic notation.
"""

from __future__ import annotations

import random
from typing import List, Tuple

import chess

from .base import ChessAgent


class RandomAgent(ChessAgent):
    """Chess agent that plays random legal moves."""

    def get_move(
        self, board: chess.Board, history: List[Tuple[int, str, str]]
    ) -> str:
        """Select a random legal move from the current board state."""
        legal_moves = list(board.legal_moves)
        if not legal_moves:
            raise ValueError("No legal moves available")

        move = random.choice(legal_moves)
        return board.san(move)

