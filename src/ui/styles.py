# ui/styles.py
import streamlit as st

def apply_styles():
    """Apply custom styles to the UI."""
    st.markdown(
        """
        <style>
        .status-box {
            padding: 1rem;
            color: white;
            border-radius: 0.5rem;
            margin-bottom: 1rem;
            text-align: center;
            font-weight: 500;
        }
        .info-box {
            padding: 1rem;
            background-color: #f0f2f6;
            border-radius: 0.5rem;
            margin-bottom: 1rem;
            border-left: 4px solid #1f77b4;
        }
        .thinking-box {
            padding: 0.5rem;
            background-color: #fff3cd;
            border-radius: 0.5rem;
            margin-bottom: 1rem;
            text-align: center;
            animation: pulse 2s infinite;
        }
        .history-box {
            height: 300px;
            overflow-y: auto;
            padding: 1rem;
            background-color: #f8f9fa;
            border-radius: 0.5rem;
            border: 1px solid #dee2e6;
        }
        .history-item {
            padding: 0.5rem;
            border-bottom: 1px solid #dee2e6;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .history-item:first-child {
            font-weight: 600;
            background-color: #e9ecef;
            border-radius: 0.3rem;
        }
        .move-number { color: #6c757d; flex: 0 0 80px; }
        .move-color { color: #1f77b4; flex: 0 0 60px; }
        .move-text { font-family: monospace; flex-grow: 1; text-align: right; }
        @keyframes pulse {
            0% { opacity: 1; }
            50% { opacity: 0.6; }
            100% { opacity: 1; }
        }
        .analysis-box {
            background-color: #f8f9fa;
            padding: 1rem;
            border-radius: 0.5rem;
            margin-top: 1rem;
        }
        .stButton > button { width: 100%; font-weight: 500; }
        #MainMenu {visibility: hidden;}
        footer {visibility: hidden;}
        </style>
        """,
        unsafe_allow_html=True,
    )
