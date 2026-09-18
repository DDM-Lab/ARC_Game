# ARC Game - Disaster Relief Simulation

A Unity-based disaster relief operations game with multi-agent AI support for research and training.

## Documentation

### Configuration Guides

- **[Agent Configuration Guide](AGENT_CONFIG_GUIDE.md)** - Complete reference for configuring the multi-agent AI system
  - How to set up different agent types (auto, choices, coach, manual)
  - Action and observation space configuration
  - LLM provider setup (Anthropic Claude, OpenAI, Ollama)
  - Common configuration patterns and examples
  - Troubleshooting and optimization tips

- **[Choices Agent Guide](CHOICES_AGENT.md)** - Run, understand, and extend the choices agent (the one that proposes selectable action packages)
  - Architecture: change the router, it flows through to the game (no client rebuild)
  - Running the router locally, keys/secrets, REST + WebSocket protocol
  - Repropose / clarify / chat classifier and the reliability layer
  - Building the Unity client (macOS / WebGL) for distribution

### Quick Start

1. **Configure your agents** - Edit or create a config file in `config/`
2. **Set up API keys** - Create a `.env` file with your LLM API keys
3. **Run the game** - Launch Unity and start the Python router:
   ```bash
   python agent_router.py --config config/your_config.json
   ```

See the [Agent Configuration Guide](AGENT_CONFIG_GUIDE.md) for detailed instructions.

## Project Structure

- `config/` - Agent configuration files (JSON)
- `Assets/` - Unity game assets and C# scripts
- `agent_router.py` - Python orchestration layer
- `llm_query.py` - LLM interaction module
- `agent_config.py` - Configuration schema and validation

## Requirements

- Unity 2022.3+
- Python 3.8+
- Required Python packages: `anthropic`, `openai`, `ollama`, `python-dotenv`

## License

[Add your license information here]
