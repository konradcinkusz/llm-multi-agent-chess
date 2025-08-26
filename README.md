# README.md
# Chess Agent System with LLM

A sophisticated chess system featuring AI agents powered by Large Language Models (LLMs) playing against each other through a user-friendly Streamlit interface.

## Features

- **Multi-Agent System**: Two LLM-powered chess agents playing against each other
- **Configurable Agents**: Choose different OpenAI models (e.g., GPT-4o) and personalities
- **Interactive UI**: Clean Streamlit interface with real-time game visualization
- **Move History**: Detailed move tracking with automatic notation
- **Auto-Play**: Automatic game progression with configurable delay
- **Game Analysis**: Material balance and position evaluation

## Installation

1. **Clone the repository and enter the directory**:
   ```bash
   git clone https://github.com/konradcinkusz/chess-agent-system-with-llm.git
   cd chess-agent-system-with-llm
   ```
2. **Create and activate a virtual environment**:
   ```bash
   python -m venv venv
   source venv/bin/activate  # On Windows: venv\Scripts\activate
   ```
3. **Install dependencies**:
   ```bash
   pip install -r requirements.txt
   ```
4. **Provide your OpenAI API key**:  
   The application looks for the key in the `OPENAI_API_KEY` environment variable.  
   You can supply it either via a `.env` file or by setting the variable directly:

   - **Using a `.env` file** (recommended for local development):  
     ```bash
     cp .env.example .env
     # Edit .env and set OPENAI_API_KEY=your_api_key
     ```
   - **Setting an environment variable (Windows)**:  
     - PowerShell (current session):  
       ```powershell
       $env:OPENAI_API_KEY="your_api_key"
       ```  
     - Command Prompt:  
       ```cmd
       setx OPENAI_API_KEY "your_api_key"
       ```
5. **Launch the Streamlit interface**:
   ```bash
   streamlit run src/main.py
   ```
6. **(Optional) Run a headless simulation between two agents**:
   See [Simulation](#simulation) for details.

## Usage

1. Configure agents in the sidebar (after launching the app with `streamlit run src/main.py`):
   - Select a model for White and Black players
   - Choose personality types
   - Start a new game

2. Control the game:
   - Use "Make Next Move" for step-by-step progression
   - Toggle "RUN" for automatic play
   - Monitor move history and game status
   - Use "New Game" to reset the board with the currently selected agents
   - Press "RUN" again (now labelled "STOP") to pause automatic play

### Button Reference

- **New Game**: start a fresh match using the models and personalities selected
  in the configuration panel.
- **Make Next Move**: ask the current agent to produce one move and update the
  board.
- **RUN/STOP**: toggle automatic play; when running the label changes to
  "STOP" allowing you to pause the cycle.

### Logging

- Each game writes to a separate log file stored in the `logs/` directory. The
  file name includes a timestamp (e.g., `app_20240101_120000.log`) so that
  sessions are easy to differentiate.
- When a new game starts the logger creates a fresh file and records the start
  time as the first entry. Previous games' logs remain intact for later
  inspection.
- The UI no longer displays application logs. Instead, the right-hand column
  shows **Position Analysis**, which is expanded by default and remains visible
  throughout play.

## Simulation

Run automated games between agents without launching the Streamlit UI.

1. Complete the steps in [Installation](#installation) up to installing dependencies and setting environment variables.
2. Execute a simulation script:
   ```bash
   python - <<'PYTHON'
   from src.agents.random_agent import RandomAgent
   from src.utils import simulate_game

   white = RandomAgent("White")
   black = RandomAgent("Black")
   game, status = simulate_game(white, black, max_moves=20)
   print("Game status:", status)
   for ply, uci, san in game.history:
       print(f"{ply}: {san} ({uci})")
   PYTHON
   ```
3. Swap `RandomAgent` for other agents (e.g., `LLMChessAgent`) to explore different play styles.

## Configuration

Adjust game settings in `src/config.py`:
- Model selection (set `DEFAULT_MODEL` and `ALTERNATIVE_MODELS` via environment variables)
- Agent personalities
- Auto-play delay
- Move history limit
- UI customization

## Project Structure

```
chess-agent-system-with-llm/
├── README.md
├── requirements.txt
├── .env.example
├── src/
│   ├── __init__.py
│   ├── main.py
│   ├── config.py
│   ├── agents/
│   │   ├── __init__.py
│   │   ├── base.py
│   │   ├── llm_agent.py
│   │   └── agent_factory.py
│   ├── game/
│   │   ├── __init__.py
│   │   ├── chess_game.py
│   │   └── board_utils.py
│   ├── ui/
│   │   ├── __init__.py
│   │   ├── app.py
│   │   ├── components.py
│   │   └── styles.py
│   └── utils/
│       ├── __init__.py
│       ├── prompts.py
│       └── validators.py
└── tests/
    ├── __init__.py
    ├── test_agents.py
    ├── test_game.py
    └── test_utils.py
```

## Testing

Run the test suite:
```bash
pytest tests/
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on submitting pull requests, coding standards, and testing. By participating, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Code of Conduct

Please read our [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) to learn about the standards we uphold.


## License

MIT License. See LICENSE file for details.
