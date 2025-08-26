# utils/validators.py
from typing import List, Optional, Tuple
import chess
from ..config import Config

class MoveValidator:
    """Validator for chess moves and game states."""
    
    @staticmethod
    def validate_move(move: str, board: chess.Board) -> Tuple[bool, Optional[str]]:
        """
        Validate a chess move.
        
        Args:
            move (str): Move in standard algebraic notation
            board (chess.Board): Current board state
            
        Returns:
            Tuple[bool, Optional[str]]: (is_valid, error_message)
        """
        try:
            # Check if move is in list of legal moves
            legal_moves = [board.san(m) for m in board.legal_moves]
            if move not in legal_moves:
                return False, f"Move '{move}' is not in list of legal moves: {', '.join(legal_moves)}"
            
            # Try to execute the move
            board.push_san(move)
            board.pop()  # Undo the move
            
            return True, None
        except ValueError as e:
            return False, str(e)
        except Exception as e:
            return False, f"Unexpected error: {str(e)}"

class GameStateValidator:
    """Validator for chess game states."""
    
    @staticmethod
    def validate_history(history: List[Tuple[int, str, str]]) -> Tuple[bool, Optional[str]]:
        """
        Validate move history format and consistency.
        
        Args:
            history (List[Tuple[int, str, str]]): Game move history
            
        Returns:
            Tuple[bool, Optional[str]]: (is_valid, error_message)
        """
        try:
            if not history:
                return True, None
            
            # Check move numbers are sequential
            move_numbers = [move[0] for move in history]
            expected_numbers = list(range(1, (len(move_numbers) + 3) // 2 + 1))
            if move_numbers != expected_numbers * 2:
                return False, "Move numbers are not sequential"
            
            # Check color alternation
            colors = [move[1] for move in history]
            for i in range(len(colors) - 1):
                if colors[i] == colors[i + 1]:
                    return False, f"Colors do not alternate properly at move {i + 1}"
            
            return True, None
        except Exception as e:
            return False, f"History validation error: {str(e)}"

