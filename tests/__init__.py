# tests/__init__.py
import os
import sys
import pytest
from typing import Dict, Any

# Add the parent directory to the Python path to allow imports from src
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

# Test configuration
TEST_CONFIG: Dict[str, Any] = {
    'OPENAI_API_KEY': 'test_key',
    'DEFAULT_MODEL': 'gpt-3.5-turbo',
    'AUTO_PLAY_DELAY': 0.0,
    'MOVE_HISTORY_LIMIT': 5
}

# Common test fixtures
@pytest.fixture
def mock_config(monkeypatch):
    """Fixture to provide test configuration."""
    for key, value in TEST_CONFIG.items():
        monkeypatch.setenv(key, str(value))
    return TEST_CONFIG

@pytest.fixture
def test_board():
    """Fixture to provide a fresh chess board for tests."""
    import chess
    return chess.Board()

@pytest.fixture
def mock_openai(monkeypatch):
    """Fixture to mock OpenAI API calls."""
    class MockResponse:
        def __init__(self, content):
            self.choices = [type('Choice', (), {'message': type('Message', (), {'content': content})()})]
    
    def mock_create(*args, **kwargs):
        return MockResponse("e4")
    
    monkeypatch.setattr("openai.ChatCompletion.create", mock_create)
    return mock_create

# Test utilities
def assert_valid_move(move: str) -> bool:
    """Helper function to validate chess moves."""
    import re
    return bool(re.match(r'^[a-h][1-8][a-h][1-8]$|^[NBRQK]?[a-h]?[1-8]?x?[a-h][1-8](\=[NBRQ])?[\+#]?$', move))

__all__ = [
    'mock_config',
    'test_board',
    'mock_openai',
    'assert_valid_move',
    'TEST_CONFIG'
]
