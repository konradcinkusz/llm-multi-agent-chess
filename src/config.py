# config.py
import os
from dataclasses import dataclass
from typing import Dict, Any, Tuple

@dataclass
class Config:
    """Configuration settings for the chess application."""
    
    # API and Model Settings
    OPENAI_API_KEY: str = os.getenv("OPENAI_API_KEY", "")
    DEFAULT_MODEL: str = os.getenv("DEFAULT_MODEL", "gpt-4o-mini")
    ALTERNATIVE_MODELS: Tuple[str, ...] = tuple(
        model.strip()
        for model in os.getenv(
            "ALTERNATIVE_MODELS",
            "gpt-4o-mini,gpt-4o,gpt-3.5-turbo,gpt-4"
        ).split(",")
    )
    
    # Agent Settings
    AGENT_PERSONALITIES: Tuple[str, ...] = ("standard", "aggressive", "defensive")
    MAX_RETRIES: int = 3
    TEMPERATURE: float = 0.3
    
    # Game Settings
    AUTO_PLAY_DELAY: float = 1.0
    MOVE_HISTORY_LIMIT: int = 5  # Number of recent moves to show in prompt
    
    # UI Settings
    BOARD_SIZE: str = "large"
    HISTORY_HEIGHT: int = 200  # pixels
    
    @classmethod
    def validate(cls) -> bool:
        """Validate the configuration settings."""
        if not cls.OPENAI_API_KEY:
            raise ValueError("OPENAI_API_KEY must be set")
        return True

