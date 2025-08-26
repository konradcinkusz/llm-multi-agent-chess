# ui/components.py
import streamlit as st
from cairosvg import svg2png
import chess
import textwrap

from ..utils.logger import log_error, log_success

class ChessUI:
    """UI components for the chess game."""
    
    @staticmethod
    def render_game_board(game, board_width: int = 350) -> None:
        """Render the chess board."""
        st.subheader("Game Board")
        svg_board = game.get_board_svg()
        st.image(svg2png(bytestring=svg_board.encode()), width=board_width)
    
    @staticmethod
    def render_position_analysis(game) -> None:
        """Render position analysis information."""
        position_info = game.get_position_summary()

        with st.expander("Position Analysis", expanded=True):
            col1, col2 = st.columns(2)

            with col1:
                st.markdown("### Material Balance")
                st.write(f"White: {position_info['material_balance']['white']}")
                st.write(f"Black: {position_info['material_balance']['black']}")

            with col2:
                st.markdown("### Square Control")
                st.write(f"White: {position_info['control']['white']} squares")
                st.write(f"Black: {position_info['control']['black']} squares")

            st.markdown("### King Safety")
            st.write(f"White King: {position_info['king_safety']['white']}")
            st.write(f"Black King: {position_info['king_safety']['black']}")

    @staticmethod
    def render_agent_configuration_text(game) -> None:
        """Render agent configuration as read-only text."""

        st.subheader("Agent Configuration")
        st.write(
            f"White Model: {game.white_agent.model} — {game.white_agent.personality}"
        )
        st.write(
            f"Black Model: {game.black_agent.model} — {game.black_agent.personality}"
        )
    
    @staticmethod
    def render_game_controls(game) -> None:
        """Render game control buttons."""
        st.subheader("Game Controls")
        
        # Game status
        status = game.get_game_status()
        status_color = ("green" if "wins" in status else 
                       "blue" if status == "Ongoing" else "orange")
        
        st.markdown(f"""
            <div class="status-box" style="background-color: {status_color};">
                <strong>Status:</strong> {status}
            </div>
        """, unsafe_allow_html=True)
        
        # Control buttons
        col1, col2 = st.columns(2)

        with col1:
            if not st.session_state.running:
                if st.button(
                    "Make Next Move",
                    key="next-move",
                    use_container_width=True,
                ):
                    success, move = game.make_move()
                    if success:
                        log_success(f"Move made: {move}")
                        st.rerun()
                    else:
                        log_error(f"Move failed: {move}")

        with col2:
            run_button = st.button(
                "RUN" if not st.session_state.running else "STOP",
                key="run-toggle",
                type="primary" if not st.session_state.running else "secondary",
                use_container_width=True,
            )

            if run_button:
                st.session_state.running = not st.session_state.running
                st.rerun()
    
    @staticmethod
    def render_game_info(game) -> None:
        """Render game information."""
        # Current player
        st.markdown(f"""
            <div class="info-box">
                <strong>Current Player:</strong> {game.current_agent.name}
            </div>
        """, unsafe_allow_html=True)
        
        # Thinking indicator
        if game.thinking:
            st.markdown("""
                <div class="thinking-box">
                    🤔 Thinking...
                </div>
            """, unsafe_allow_html=True)
        
        # Last move
        if game.last_move:
            st.markdown(f"""
                <div class="info-box">
                    <strong>Last Move:</strong> {game.last_move['move_text']}
                </div>
            """, unsafe_allow_html=True)
        
        # Move history
        st.subheader("Move History")
        history = game.get_move_history_formatted()
        
        # Move history rendering with fixed height for compact display
        history_html = textwrap.dedent(
            """
            <div style="height: 160px; overflow-y: auto; background-color: #f8f9fa; border: 1px solid #dee2e6; border-radius: 5px; padding: 10px;">
            """
        )
        for move in reversed(history):  # Newest moves first
            history_html += textwrap.dedent(
                f"""
                <div style="padding: 8px; margin: 4px 0; background-color: {'#ffffff' if move['move_number'] % 2 == 0 else '#f8f9fa'}; border-radius: 4px; display: flex; justify-content: space-between; align-items: center;">
                    <span style="color: #6c757d; min-width: 80px;">Move {move['move_number']}</span>
                    <span style="color: {'#0d6efd' if move['color'] == 'White' else '#212529'}; min-width: 60px;">{move['color']}</span>
                    <span style="font-family: monospace;">{move['move']}</span>
                </div>
                """
            )
        history_html += "</div>"

        st.markdown(history_html, unsafe_allow_html=True)
