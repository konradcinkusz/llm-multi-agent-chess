# src/main.py
import os
import sys

# Add the parent directory to PYTHONPATH
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import streamlit as st
from src.ui.app import run_app
from dotenv import load_dotenv

def initialize():
    # Ensure environment variables are set
    if not os.getenv("OPENAI_API_KEY"):
        st.error("Please set the OPENAI_API_KEY environment variable")
        st.stop()

if __name__ == "__main__":
    load_dotenv()
    initialize()
    run_app()

