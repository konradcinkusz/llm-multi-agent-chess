# ui/app.py
"""Streamlit user interface for the chess agent system."""

from __future__ import annotations

import os
import time

import streamlit as st

from ..agents.agent_factory import create_agent
from ..config import Config
from ..game.chess_game import ChessGame
from ..utils.logger import log_error, setup_logger
from .components import ChessUI
from .styles import apply_styles


def run_app() -> None:
    """Main application entry point."""
    st.set_page_config(layout="wide")
    apply_styles()

    if "game" not in st.session_state:
        st.session_state.game = None
    if "running" not in st.session_state:
        st.session_state.running = False

    st.title("Chess Agent Control Center")

    if st.session_state.game is None:
        initialize_new_game(
            os.getenv("DEFAULT_WHITE_MODEL", Config.DEFAULT_MODEL),
            os.getenv("DEFAULT_WHITE_PERSONALITY", Config.AGENT_PERSONALITIES[0]),
            os.getenv("DEFAULT_BLACK_MODEL", Config.DEFAULT_MODEL),
            os.getenv("DEFAULT_BLACK_PERSONALITY", Config.AGENT_PERSONALITIES[0]),
        )

    game: ChessGame = st.session_state.game

    if st.session_state.running:
        board_col, analysis_col, info_col = st.columns(3)

        with analysis_col:
            ChessUI.render_position_analysis(game)

        with board_col:
            ChessUI.render_game_board(game, board_width=300)
            ChessUI.render_game_controls(game)

        with info_col:
            ChessUI.render_agent_configuration_text(game)
            ChessUI.render_game_info(game)
    else:
        board_col, config_col, info_col = st.columns(3)

        with config_col:
            render_configuration()

        with board_col:
            ChessUI.render_game_board(game, board_width=300)
            ChessUI.render_game_controls(game)

        with info_col:
            ChessUI.render_game_info(game)
            ChessUI.render_position_analysis(game)

    handle_auto_play()


def render_configuration() -> None:
    """Render agent configuration on the main page."""
    st.header("Agent Configuration")

    default_white = os.getenv("DEFAULT_WHITE_MODEL", Config.DEFAULT_MODEL)
    white_model = st.selectbox(
        "White Model",
        Config.ALTERNATIVE_MODELS,
        index=(
            Config.ALTERNATIVE_MODELS.index(default_white)
            if default_white in Config.ALTERNATIVE_MODELS
            else 0
        ),
        key="white_model",
    )
    white_personality = st.selectbox(
        "White Personality", Config.AGENT_PERSONALITIES, key="white_personality"
    )

    st.markdown("---")

    default_black = os.getenv("DEFAULT_BLACK_MODEL", Config.DEFAULT_MODEL)
    black_model = st.selectbox(
        "Black Model",
        Config.ALTERNATIVE_MODELS,
        index=(
            Config.ALTERNATIVE_MODELS.index(default_black)
            if default_black in Config.ALTERNATIVE_MODELS
            else 0
        ),
        key="black_model",
    )
    black_personality = st.selectbox(
        "Black Personality", Config.AGENT_PERSONALITIES, key="black_personality"
    )

    if st.button("New Game", use_container_width=True):
        initialize_new_game(
            white_model, white_personality, black_model, black_personality
        )
        st.rerun()


def initialize_new_game(
    white_model: str, white_personality: str, black_model: str, black_personality: str
) -> None:
    """Initialize a new game with specified configurations."""

    setup_logger()
    white_agent = create_agent("White", "llm", white_model, white_personality)
    black_agent = create_agent("Black", "llm", black_model, black_personality)
    st.session_state.game = ChessGame(white_agent, black_agent)
    st.session_state.running = False


def handle_auto_play() -> None:
    """Handle auto-play functionality."""

    if st.session_state.running and not st.session_state.game.board.is_game_over():
        time.sleep(Config.AUTO_PLAY_DELAY)
        success, move = st.session_state.game.make_move()
        if success:
            st.rerun()
        else:
            log_error(f"Auto-play failed: {move}")
            st.session_state.running = False

