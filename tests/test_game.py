# tests/test_game.py
import pytest
import chess
from src.game.chess_game import ChessGame
from src.agents.base import ChessAgent

class TestAgent(ChessAgent):
    """Test agent that makes predetermined moves."""
    def __init__(self, name, moves):
        super().__init__(name)
        self.moves = moves
        self.move_index = 0
    
    def get_move(self, board, history):
        move = self.moves[self.move_index]
        self.move_index = (self.move_index + 1) % len(self.moves)
        return move

def test_game_initialization():
    """Test game initialization."""
    white_agent = TestAgent("White", ["e4"])
    black_agent = TestAgent("Black", ["e5"])
    game = ChessGame(white_agent, black_agent)
    
    assert game.board.turn == chess.WHITE
    assert len(game.history) == 0
    assert game.current_agent == white_agent

def test_game_move_making():
    """Test making moves in the game."""
    white_agent = TestAgent("White", ["e4"])
    black_agent = TestAgent("Black", ["e5"])
    game = ChessGame(white_agent, black_agent)
    
    success, move = game.make_move()
    assert success
    assert move == "e4"
    assert len(game.history) == 1
    assert game.current_agent == black_agent

def test_game_status():
    """Test game status reporting."""
    white_agent = TestAgent("White", ["e4", "Qh5", "Qxf7"])
    black_agent = TestAgent("Black", ["e5", "Nc6"])
    game = ChessGame(white_agent, black_agent)
    
    # Play fool's mate
    game.make_move()  # e4
    game.make_move()  # e5
    game.make_move()  # Qh5
    game.make_move()  # Nc6
    game.make_move()  # Qxf7#
    
    assert "Checkmate" in game.get_game_status()
