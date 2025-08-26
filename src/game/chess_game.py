# game/chess_game.py
from typing import List, Tuple, Optional
import chess
import chess.svg
from ..agents.base import ChessAgent
from ..config import Config
from .board_utils import BoardUtils

class ChessGame:
    """Manages the chess game state and player interactions."""
    
    def __init__(self, white_agent: ChessAgent, black_agent: ChessAgent):
        """
        Initialize a new chess game.
        
        Args:
            white_agent (ChessAgent): Agent playing white pieces
            black_agent (ChessAgent): Agent playing black pieces
        """
        self.board = chess.Board()
        self.history: List[Tuple[int, str, str]] = []  # (move_number, color, move)
        self.white_agent = white_agent
        self.black_agent = black_agent
        self.current_agent = white_agent
        self.last_move = None
        self.thinking = False
        self.move_count = 0
        self.board_utils = BoardUtils()
    
    def make_move(self) -> tuple[bool, str]:
        """
        Execute the next move in the game.
        
        Returns:
            tuple[bool, str]: (success, message)
        """
        if self.board.is_game_over():
            return False, "Game is over"
        
        self.thinking = True
        try:
            # Get and validate the move
            move = self.current_agent.get_move(self.board, self.history)
            if not self.board_utils.is_valid_move(self.board, move):
                raise ValueError(f"Invalid move: {move}")
            
            # Execute the move
            self.board.push_san(move)
            
            # Update move count and history
            self.move_count += 1
            move_number = (self.move_count + 1) // 2
            color = "White" if self.move_count % 2 == 1 else "Black"
            self.history.append((move_number, color, move))
            
            # Update last move information
            position_info = self.board_utils.get_position_info(self.board)
            self.last_move = {
                'move_text': f"Move {move_number}: {color} played {move}",
                'evaluation': position_info['evaluation'],
                'material_balance': position_info['material_balance'],
                'is_check': self.board.is_check(),
                'is_checkmate': self.board.is_checkmate()
            }
            
            # Switch current agent
            self.current_agent = self.black_agent if self.current_agent == self.white_agent else self.white_agent
            
            self.thinking = False
            return True, move
        except Exception as e:
            self.thinking = False
            return False, f"Move error: {str(e)}"
    
    def get_board_svg(self) -> str:
        """Get SVG representation of the current board state."""
        last_move = self.board.peek() if self.board.move_stack else None
        return chess.svg.board(
            board=self.board,
            lastmove=last_move,
            check=self.board.king(self.board.turn) if self.board.is_check() else None
        )
    
    def get_game_status(self) -> str:
        """Get the current game status."""
        if self.board.is_checkmate():
            winner = "Black" if self.board.turn else "White"
            return f"Checkmate! {winner} wins!"
        elif self.board.is_stalemate():
            return "Stalemate!"
        elif self.board.is_check():
            return "Check!"
        elif self.board.is_insufficient_material():
            return "Draw (Insufficient material)"
        elif self.board.is_fifty_moves():
            return "Draw (Fifty-move rule)"
        elif self.board.is_repetition():
            return "Draw (Repetition)"
        return "Ongoing"
    
    def get_position_summary(self) -> dict:
        """Get a summary of the current position."""
        return self.board_utils.get_position_info(self.board)
    
    def get_move_history_formatted(self) -> List[dict]:
        """Get formatted move history with additional information."""
        formatted_history = []
        for move_num, color, move in self.history:
            formatted_history.append({
                'move_number': move_num,
                'color': color,
                'move': move,
                'time': None,  # Could be implemented with move timing
                'evaluation': None  # Could be implemented with position evaluation
            })
        return formatted_history
