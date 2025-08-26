# utils/prompts.py
from typing import Dict, Optional
from ..config import Config

class ChessPrompts:
    """Chess game prompts for LLM agents."""
    
    @staticmethod
    def get_base_prompt() -> str:
        """Get the base prompt template."""
        return """You are a chess master analyzing the following position.
                Your task is to select the {style} legal move for {player_color}.
                
                Current board state:
                {ascii_board}
                
                Recent moves:
                {history}
                
                Strictly available legal moves (you must choose one of these exactly):
                {legal_moves}
                
                Material count:
                {material_balance}
                
                Additional information:
                - You are playing as {player_color}
                - Current turn number: {turn_number}
                - Captured pieces: {captured_pieces}
                
                Important:
                1. You MUST respond with ONLY a move from the legal moves list
                2. Do not add any explanation or additional text
                3. The move must exactly match one from the legal moves list
                4. If a piece was captured, it cannot be used anymore"""

    @staticmethod
    def get_personality_prompts() -> Dict[str, str]:
        """Get prompts for different agent personalities."""
        base_prompt = ChessPrompts.get_base_prompt()
        personalities = {
            "standard": {
                "style": "best",
                "description": "Analyze the position considering all factors and choose the objectively strongest move."
            },
            "aggressive": {
                "style": "most attacking",
                "description": "Focus on attacking chances, tactical opportunities, and creating threats."
            },
            "defensive": {
                "style": "safest",
                "description": "Prioritize piece safety, pawn structure, and king protection."
            }
        }
        
        return {
            personality: base_prompt + f"\n\nStrategy focus: {data['description']}"
            for personality, data in personalities.items()
        }

