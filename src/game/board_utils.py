# game/board_utils.py
from typing import Dict, List, Optional, Tuple
import chess

class BoardUtils:
    """Utility functions for chess board analysis and manipulation."""
    
    def __init__(self):
        """Initialize the board utilities."""
        self.piece_values = {
            chess.PAWN: 1,
            chess.KNIGHT: 3,
            chess.BISHOP: 3,
            chess.ROOK: 5,
            chess.QUEEN: 9,
            chess.KING: 0
        }
    
    def is_valid_move(self, board: chess.Board, move: str) -> bool:
        """
        Check if a move is valid in the current position.
        
        Args:
            board (chess.Board): Current board state
            move (str): Move in standard algebraic notation
            
        Returns:
            bool: True if move is valid
        """
        try:
            legal_moves = [board.san(m) for m in board.legal_moves]
            return move in legal_moves
        except ValueError:
            return False
    
    def get_position_info(self, board: chess.Board) -> Dict:
        """
        Get comprehensive information about the current position.
        
        Args:
            board (chess.Board): Current board state
            
        Returns:
            Dict: Position information including material balance,
                 piece positions, and control metrics
        """
        return {
            'material_balance': self.calculate_material_balance(board),
            'piece_positions': self.get_piece_positions(board),
            'control': self.calculate_square_control(board),
            'evaluation': self.simple_evaluate_position(board),
            'king_safety': self.evaluate_king_safety(board),
            'development': self.evaluate_development(board)
        }
    
    def calculate_material_balance(self, board: chess.Board) -> Dict[str, int]:
        """Calculate the material balance for both sides."""
        white_material = 0
        black_material = 0
        
        for square in chess.SQUARES:
            piece = board.piece_at(square)
            if piece is not None:
                value = self.piece_values.get(piece.piece_type, 0)
                if piece.color:
                    white_material += value
                else:
                    black_material += value
        
        return {
            'white': white_material,
            'black': black_material,
            'advantage': white_material - black_material
        }
    
    def get_piece_positions(self, board: chess.Board) -> Dict[str, List[str]]:
        """Get positions of all pieces on the board."""
        positions = {'white': {}, 'black': {}}
        piece_symbols = {
            chess.PAWN: 'P', chess.KNIGHT: 'N', chess.BISHOP: 'B',
            chess.ROOK: 'R', chess.QUEEN: 'Q', chess.KING: 'K'
        }
        
        for square in chess.SQUARES:
            piece = board.piece_at(square)
            if piece is not None:
                color = 'white' if piece.color else 'black'
                piece_type = piece_symbols[piece.piece_type]
                square_name = chess.square_name(square)
                positions[color].setdefault(piece_type, []).append(square_name)
        
        return positions
    
    def calculate_square_control(self, board: chess.Board) -> Dict[str, int]:
        """Calculate how many squares each side controls."""
        white_control = 0
        black_control = 0
        
        for square in chess.SQUARES:
            if board.is_attacked_by(chess.WHITE, square):
                white_control += 1
            if board.is_attacked_by(chess.BLACK, square):
                black_control += 1
        
        return {
            'white': white_control,
            'black': black_control,
            'advantage': white_control - black_control
        }
    
    def simple_evaluate_position(self, board: chess.Board) -> float:
        """
        Simple position evaluation based on material and square control.
        Positive values favor white, negative favor black.
        """
        material = self.calculate_material_balance(board)
        control = self.calculate_square_control(board)
        
        evaluation = material['advantage'] + (control['advantage'] * 0.1)
        
        if board.is_checkmate():
            evaluation = float('inf') if not board.turn else float('-inf')
        
        return evaluation
    
    def evaluate_king_safety(self, board: chess.Board) -> Dict[str, float]:
        """Evaluate king safety for both sides."""
        safety = {'white': 0.0, 'black': 0.0}
        
        for color in [chess.WHITE, chess.BLACK]:
            king_square = board.king(color)
            if king_square is None:
                continue
            
            # Count defended squares around king
            defenders = 0
            attackers = 0
            
            for square in board.attacks(king_square):
                if board.is_attacked_by(color, square):
                    defenders += 1
                if board.is_attacked_by(not color, square):
                    attackers += 1
            
            safety_score = defenders - attackers
            safety['white' if color else 'black'] = safety_score
        
        return safety
    
    def evaluate_development(self, board: chess.Board) -> Dict[str, int]:
        """Evaluate piece development for both sides."""
        development = {'white': 0, 'black': 0}
        
        # Starting squares for pieces
        starting_squares = {
            chess.WHITE: [
                chess.B1, chess.C1, chess.F1, chess.G1,  # Knights and Bishops
                chess.A2, chess.B2, chess.C2, chess.D2, chess.E2, chess.F2, chess.G2, chess.H2  # Pawns
            ],
            chess.BLACK: [
                chess.B8, chess.C8, chess.F8, chess.G8,  # Knights and Bishops
                chess.A7, chess.B7, chess.C7, chess.D7, chess.E7, chess.F7, chess.G7, chess.H7  # Pawns
            ]
        }
        
        for color in [chess.WHITE, chess.BLACK]:
            # Count pieces that have moved from their starting squares
            for square in starting_squares[color]:
                piece = board.piece_at(square)
                if piece is None or piece.color != color:
                    development['white' if color else 'black'] += 1
        
        return development