# tests/test_agents.py
import pytest
import chess
from src.agents.base import ChessAgent
from src.agents.llm_agent import LLMChessAgent
from src.config import Config

class MockAgent(ChessAgent):
    """Mock agent for testing."""
    def get_move(self, board, history):
        return list(board.legal_moves)[0].uci()

def test_agent_creation():
    """Test agent initialization."""
    agent = MockAgent("White")
    assert agent.name == "White"

def test_llm_agent_move_generation():
    """Test LLM agent move generation."""
    agent = LLMChessAgent("White")
    board = chess.Board()
    history = []
    
    # Mock OpenAI response
    with pytest.patch('openai.ChatCompletion.create') as mock_create:
        mock_create.return_value.choices[0].message.content = 'e4'
        move = agent.get_move(board, history)
        assert move in [board.san(m) for m in board.legal_moves]

def test_llm_agent_invalid_move_handling():
    """Test LLM agent handling of invalid moves."""
    agent = LLMChessAgent("White")
    board = chess.Board()
    history = []
    
    # Mock OpenAI response with invalid move
    with pytest.patch('openai.ChatCompletion.create') as mock_create:
        mock_create.return_value.choices[0].message.content = 'invalid'
        with pytest.raises(ValueError):
            agent.get_move(board, history)

