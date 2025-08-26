# tests/test_utils.py
import chess
from src.utils.validators import MoveValidator, GameStateValidator
from src.utils.prompts import ChessPrompts

def test_move_validator():
    """Test move validation."""
    board = chess.Board()
    
    # Test valid move
    is_valid, error = MoveValidator.validate_move("e4", board)
    assert is_valid
    assert error is None
    
    # Test invalid move
    is_valid, error = MoveValidator.validate_move("e8", board)
    assert not is_valid
    assert error is not None

def test_game_state_validator():
    """Test game state validation."""
    # Test valid history
    history = [
        (1, "White", "e4"),
        (1, "Black", "e5"),
        (2, "White", "Nf3"),
        (2, "Black", "Nc6")
    ]
    is_valid, error = GameStateValidator.validate_history(history)
    assert is_valid
    assert error is None
    
    # Test invalid history (wrong color order)
    invalid_history = [
        (1, "White", "e4"),
        (1, "White", "e5")  # Should be Black
    ]
    is_valid, error = GameStateValidator.validate_history(invalid_history)
    assert not is_valid
    assert error is not None

def test_chess_prompts():
    """Test chess prompt generation."""
    # Test base prompt
    base_prompt = ChessPrompts.get_base_prompt()
    assert isinstance(base_prompt, str)
    assert "{style}" in base_prompt
    assert "{player_color}" in base_prompt
    
    # Test personality prompts
    personality_prompts = ChessPrompts.get_personality_prompts()
    assert "standard" in personality_prompts
    assert "aggressive" in personality_prompts
    assert "defensive" in personality_prompts
