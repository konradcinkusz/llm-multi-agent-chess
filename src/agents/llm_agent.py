# agents/llm_agent.py
from typing import List, Tuple, Optional, Dict
import chess
from openai import OpenAI
from .base import ChessAgent
from ..config import Config
from ..utils.logger import log_info, log_success, log_error, log_warning

class LLMChessAgent(ChessAgent):
    """Chess agent powered by OpenAI's language models."""
    
    def __init__(self, name: str, model: str = Config.DEFAULT_MODEL, personality: str = "standard"):
        """
        Initialize an LLM-powered chess agent.
        
        Args:
            name (str): Agent name ("White" or "Black")
            model (str): OpenAI model to use
            personality (str): Agent's playing style
        """
        super().__init__(name)
        self.personality = personality
        self.model = model
        self.client = OpenAI()
    
    def calculate_material_balance(self, board: chess.Board) -> str:
        """Calculate the material balance of the current position."""
        piece_values = {
            chess.PAWN: 1,
            chess.KNIGHT: 3,
            chess.BISHOP: 3,
            chess.ROOK: 5,
            chess.QUEEN: 9
        }
        
        white_material = 0
        black_material = 0
        
        for square in chess.SQUARES:
            piece = board.piece_at(square)
            if piece is not None:
                value = piece_values.get(piece.piece_type, 0)
                if piece.color == chess.WHITE:
                    white_material += value
                else:
                    black_material += value
        
        return f"White: {white_material}, Black: {black_material}"
    
    def get_captured_pieces(self, board: chess.Board) -> str:
        """Calculate captured pieces by comparing with initial setup."""
        initial_pieces = {
            'P': 8, 'N': 2, 'B': 2, 'R': 2, 'Q': 1, 'K': 1,
            'p': 8, 'n': 2, 'b': 2, 'r': 2, 'q': 1, 'k': 1
        }
        
        current_pieces = {'P': 0, 'N': 0, 'B': 0, 'R': 0, 'Q': 0, 'K': 0,
                         'p': 0, 'n': 0, 'b': 0, 'r': 0, 'q': 0, 'k': 0}
        
        for square in chess.SQUARES:
            piece = board.piece_at(square)
            if piece:
                current_pieces[piece.symbol()] += 1
        
        captured_white = []
        captured_black = []
        
        for piece in 'PNBRQK':
            diff = initial_pieces[piece] - current_pieces[piece]
            if diff > 0:
                captured_white.extend([piece] * diff)
            diff = initial_pieces[piece.lower()] - current_pieces[piece.lower()]
            if diff > 0:
                captured_black.extend([piece] * diff)
        
        return f"White captured: {' '.join(captured_black) or 'none'}, Black captured: {' '.join(captured_white) or 'none'}"
    
    def get_move(self, board: chess.Board, history: List[Tuple[int, str, str]]) -> str:
        """Get the next move from the LLM agent."""
        legal_moves = [board.san(move) for move in board.legal_moves]
        
        if not legal_moves:
            raise ValueError("No legal moves available")
        
        player_color = "White" if board.turn else "Black"
        turn_number = (len(history) // 2) + 1
        
        # Format recent moves
        recent_moves = "\n".join([
            f"Move {move_num}: {color} played {move}" 
            for move_num, color, move in history[-Config.MOVE_HISTORY_LIMIT:]
        ])
        
        # Get captured pieces
        captured_pieces = self.get_captured_pieces(board)
        
        # Multiple attempts to get a valid move
        for attempt in range(Config.MAX_RETRIES):
            try:
                log_info(f"Attempt {attempt + 1}/{Config.MAX_RETRIES} to generate move")
                
                # Log the API call attempt
                log_info("Making OpenAI API call...")
                
                response = self.client.chat.completions.create(
                    model=self.model,
                    messages=[
                        {
                            "role": "system",
                            "content": self.get_prompt_template().format(
                                ascii_board=str(board),
                                history=recent_moves or "No moves played yet",
                                legal_moves=", ".join(legal_moves),
                                player_color=player_color,
                                material_balance=self.calculate_material_balance(board),
                                turn_number=turn_number,
                                captured_pieces=captured_pieces
                            )
                        }
                    ],
                    temperature=Config.TEMPERATURE,
                    max_tokens=10
                )
                
                # Log successful API response
                log_success("API call successful")
                
                move = response.choices[0].message.content.strip()
                log_info(f"Generated move: {move}")
                
                if move in legal_moves:
                    log_success(f"Move '{move}' is valid")
                    return move
                else:
                    log_error(f"Generated move '{move}' is not in legal moves: {legal_moves}")
                    raise ValueError(f"Move '{move}' not in legal moves list")
                
            except Exception as e:
                log_error(
                    f"Error during attempt {attempt + 1}:\n"
                    f"Type: {type(e).__name__}\n"
                    f"Message: {str(e)}\n"
                    f"Legal moves were: {', '.join(legal_moves)}"
                )
                
                if attempt == Config.MAX_RETRIES - 1:
                    log_warning(
                        f"Failed all {Config.MAX_RETRIES} attempts to generate valid move.\n"
                        f"Last error: {type(e).__name__} - {str(e)}\n"
                        f"Falling back to first legal move: {legal_moves[0]}"
                    )
                    return legal_moves[0]
                
                log_info(f"Retrying... ({attempt + 1}/{Config.MAX_RETRIES})")
                continue
            
    def get_prompt_template(self) -> str:
        """Get the appropriate prompt template based on personality."""
        base_template = """You are a chess master analyzing the following position.
        Your task is to select the {style} legal move.
        
        Current board state:
        {ascii_board}
        
        Recent moves:
        {history}
        
        Strictly available legal moves (you must choose one of these exactly):
        {legal_moves}
        
        Material count:
        {material_balance}
        
        Additional information:
        - Current turn number: {turn_number}
        - Captured pieces: {captured_pieces}
        
        Important:
        1. You MUST respond with ONLY a move from the legal moves list
        2. Do not add any explanation or additional text
        3. The move must exactly match one from the legal moves list
        4. If a piece was captured, it cannot be used anymore"""
        
        styles = {
            "standard": "best",
            "aggressive": "most attacking",
            "defensive": "safest"
        }
        
        return base_template.replace("{style}", styles[self.personality])
