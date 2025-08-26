# src/__init__.py
from .agents import ChessAgent, LLMChessAgent, create_agent
from .game import ChessGame, BoardUtils
from .ui import run_app, ChessUI, apply_styles
from .utils import ChessPrompts, MoveValidator, GameStateValidator
from .config import Config

__version__ = "1.0.0"
__author__ = "Your Name"
__description__ = "Chess Multi-Agent System with LLM-powered players"

__all__ = [
    # Agents
    'ChessAgent',
    'LLMChessAgent',
    'create_agent',
    
    # Game
    'ChessGame',
    'BoardUtils',
    
    # UI
    'run_app',
    'ChessUI',
    'apply_styles',
    
    # Utils
    'ChessPrompts',
    'MoveValidator',
    'GameStateValidator',
    
    # Config
    'Config',
    
    # Version info
    '__version__',
    '__author__',
    '__description__'
]