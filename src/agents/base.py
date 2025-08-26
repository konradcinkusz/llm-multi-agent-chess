# agents/base.py
from abc import ABC, abstractmethod
from typing import List, Tuple
import chess

class ChessAgent(ABC):
    """Abstract base class for chess agents."""
    
    def __init__(self, name: str):
        """
        Initialize a chess agent.
        
        Args:
            name (str): Name of the agent (e.g., "White" or "Black")
        """
        self.name = name
    
    @abstractmethod
    def get_move(self, board: chess.Board, history: List[Tuple[int, str, str]]) -> str:
        """
        Get the next move from the agent.
        
        Args:
            board (chess.Board): Current board state
            history (List[Tuple[int, str, str]]): List of (move_number, color, move)
            
        Returns:
            str: Selected move in standard algebraic notation
            
        Raises:
            ValueError: If no valid move can be generated
        """
        pass

