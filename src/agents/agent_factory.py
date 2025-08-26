# agents/agent_factory.py
from .base import ChessAgent
from .llm_agent import LLMChessAgent
from .random_agent import RandomAgent
from ..config import Config

def create_agent(
    name: str,
    agent_type: str = "llm",
    model: str = Config.DEFAULT_MODEL,
    personality: str = "standard"
) -> ChessAgent:
    """
    Factory function to create chess agents.
    
    Args:
        name (str): Agent name ("White" or "Black")
        agent_type (str): Type of agent to create
        model (str): Model to use for LLM agents
        personality (str): Agent personality type
        
    Returns:
        ChessAgent: Created agent instance
        
    Raises:
        ValueError: If agent_type is unknown
    """
    if agent_type == "llm":
        return LLMChessAgent(name, model, personality)
    if agent_type == "random":
        return RandomAgent(name)
    raise ValueError(f"Unknown agent type: {agent_type}")
