"""Utility functions for unified logging to Streamlit and log files."""

from __future__ import annotations

import logging
from datetime import datetime
from pathlib import Path

import streamlit as st


# Directory for storing per-game logs
LOG_DIR = Path("logs")
LOG_DIR.mkdir(exist_ok=True)

# Path to the current game log file
LOG_FILE: Path | None = None


def setup_logger() -> logging.Logger:
    """Configure application logger for a new game.

    Each call creates a fresh log file with a timestamped name, ensuring that
    logs from different games are kept separate.
    """

    logger = logging.getLogger("chess_app")

    # Remove existing handlers so a new log file can be configured
    if logger.handlers:
        for handler in logger.handlers[:]:
            logger.removeHandler(handler)
            handler.close()

    logger.setLevel(logging.INFO)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    global LOG_FILE
    LOG_FILE = LOG_DIR / f"app_{timestamp}.log"

    formatter = logging.Formatter(
        "%(asctime)s - %(levelname)s - %(message)s", "%Y-%m-%d %H:%M:%S"
    )

    # File handler
    file_handler = logging.FileHandler(LOG_FILE)
    file_handler.setFormatter(formatter)
    logger.addHandler(file_handler)

    # Stream handler (appears in Streamlit logs)
    stream_handler = logging.StreamHandler()
    stream_handler.setFormatter(formatter)
    logger.addHandler(stream_handler)

    logger.info(f"=== New game started at {timestamp} ===")

    return logger


def log_info(message: str) -> None:
    logger = logging.getLogger("chess_app")
    logger.info(message)
    st.info(message)


def log_success(message: str) -> None:
    logger = logging.getLogger("chess_app")
    logger.info(message)
    st.success(message)


def log_warning(message: str) -> None:
    logger = logging.getLogger("chess_app")
    logger.warning(message)
    st.warning(message)


def log_error(message: str) -> None:
    logger = logging.getLogger("chess_app")
    logger.error(message)
    st.error(message)

