# Agent Conversation System Design

**Date:** 2026-03-17
**Status:** Approved Design
**Author:** Design collaboration with user

## Overview

Add conversational messaging capabilities between subagents and the director, allowing agents to communicate, ask questions, and refine decisions through dialogue. Each agent maintains their own isolated conversation history with the director (future: with other agents), and all conversations are logged for trajectory analysis.

## Requirements

### Functional Requirements

1. **Private Conversations**: Each agent has an isolated conversation thread with the director
2. **Episode-Level History**: Conversation history persists across all rounds within an episode
3. **Auto Agent Summaries**: Auto agents post conversational summaries after executing actions
4. **Choice Agent Interaction**: Choice agents can converse with director while making decisions
5. **Choice Reproposal**: Choice agents can generate new choices based on director feedback
6. **Full Logging**: All conversational messages logged to episode JSONL files
7. **Unity Integration**: Messages display in existing TaskOfficer-based conversation UI

### Non-Functional Requirements

1. **Future-Proof**: Architecture supports agent-to-agent conversations (not implemented now)
2. **Privacy**: Agents only see their own conversations, not others'
3. **Scalability**: Message queue can handle multiple concurrent conversations

## Architecture

### Message Queue Design

**Option Selected**: Nested conversation threads with per-agent isolation

```python
class MessageQueue:
    def __init__(self):
        # Structure: {agent_name: {partner_name: [messages]}}
        self.agent_conversations = {}
```

**Rationale**:
- Natural extension to multi-agent conversations (just add more partners)
- Easy visibility control (agent only sees their own conversations)
- Simple LLM context building (pass agent's conversations to prompt)

### Data Structures

**Message Format**:
```python
{
    "id": "uuid",
    "from": "agent_name",
    "to": "partner_name",
    "content": "message text",
    "type": "message_type",  # See MESSAGE_TYPES below
    "round": 5,
    "timestamp": 1234567890.123
}
```

**Message Types**:
- `action_summary`: Auto agent posts summary of actions taken
- `question`: Agent asks director a question
- `choice_proposal`: Choice agent proposes choices to director
- `choice_revision`: Choice agent repropose choices after feedback
- `feedback`: Director provides feedback on agent's proposal
- `response`: General conversational response
- `approval`: Director approves action/choice
- `rejection`: Director rejects action/choice

**Conversation Storage**:
```python
self.agent_conversations = {
    "Construction Officer": {
        "Director": [
            {"from": "Construction Officer", "to": "Director", "content": "Built clinic", "round": 1},
            {"from": "Director", "to": "Construction Officer", "content": "Good work", "round": 1}
        ]
    },
    "Logistics Officer": {
        "Director": [
            {"from": "Logistics Officer", "to": "Director", "content": "Assigned workers", "round": 2}
        ]
    }
}
```

## Component Design

### Python Components

#### 1. MessageQueue Class (`message_queue.py` - new file)

```python
class MessageQueue:
    def __init__(self):
        self.agent_conversations = {}

    def send_message(self, from_agent, to_agent, content, msg_type, round_num):
        """Send message and store in both participants' threads"""

    def get_conversation(self, agent_name, partner_name):
        """Get conversation history between two agents"""

    def get_all_conversations_for(self, agent_name):
        """Get all conversation threads for an agent (future use)"""

    def clear_all(self):
        """Clear all conversations (episode boundaries)"""
```

#### 2. AgentRouter Updates (`agent_router.py`)

**Add MessageQueue**:
```python
class AgentRouter:
    def __init__(self, config, log_path):
        self.message_queue = MessageQueue()
        # ... existing init
```

**Auto Agent Flow**:
```python
async def _run_auto(self, agent, game_state, all_actions):
    # Get conversation context
    conversation = self.message_queue.get_conversation(agent.subagent_name, "Director")

    # Query LLM with conversation history
    raw = query_llm(game_state, filtered_actions, agent, conversation)

    # Execute actions
    results = await self._execute_validated_actions(...)

    # Post summary message
    await self._post_auto_summary(agent, results)
```

**Choice Agent Flow**:
```python
async def _run_choices(self, agent, game_state, all_actions):
    # Get conversation context
    conversation = self.message_queue.get_conversation(agent.subagent_name, "Director")

    # Generate choices with conversation context
    choices = query_llm_for_choices(game_state, filtered_actions, agent, conversation)

    # Send to Unity
    await self._send({
        "type": "choices_proposal",
        "agent_name": agent.subagent_name,
        "talkinghead_endpoint": agent.talkinghead_endpoint,
        "choices": choices
    })
```

**New Message Handlers**:
```python
async def _handle_director_message(self, msg: dict):
    """Handle message from director to an agent"""
    to_agent_name = msg.get("to_agent")
    content = msg.get("content")

    self.message_queue.send_message(
        from_agent="Director",
        to_agent=to_agent_name,
        content=content,
        msg_type="response",
        round_num=self.round_num
    )

async def _handle_request_reproposal(self, msg: dict):
    """Handle director requesting agent to repropose choices"""
    agent_name = msg.get("agent_name")
    feedback = msg.get("feedback")

    agent = self._get_agent_by_name(agent_name)
    await self._repropose_choices(agent, feedback)
```

#### 3. LLM Query Updates (`llm_query.py`)

**Update function signature to include conversation**:
```python
def query_llm(game_state, actions, agent_config, conversation=None):
    """
    Query LLM with conversation context

    Args:
        game_state: Current game state dict
        actions: Available actions list
        agent_config: AgentConfig object
        conversation: List of messages between agent and director
    """

    # Build prompt with conversation history
    prompt_parts = []

    if conversation:
        prompt_parts.append("Your conversation with the director:")
        for msg in conversation:
            role = "You" if msg["from"] == agent_config.subagent_name else "Director"
            prompt_parts.append(f"[Round {msg['round']}] {role}: {msg['content']}")

    prompt_parts.append(f"\nCurrent game state: {format_state(game_state)}")
    prompt_parts.append(f"\nAvailable actions: {format_actions(actions)}")

    # ... rest of LLM query
```

### Unity Components

#### 1. WebSocketManager Updates (`WebSocketManager.cs`)

**New message handlers**:
```csharp
void HandleMessage(string message) {
    JObject msg = JObject.Parse(message);
    string msgType = msg["type"].ToString();

    switch (msgType) {
        case "agent_message":
            HandleAgentMessage(msg);
            break;
        case "choices_proposal":
            HandleChoicesProposal(msg);  // Update to include conversation context
            break;
        // ... existing cases
    }
}

void HandleAgentMessage(JObject msg) {
    string agentName = msg["agent_name"].ToString();
    string endpoint = msg["talkinghead_endpoint"].ToString();
    string content = msg["content"].ToString();
    string messageType = msg["message_type"].ToString();

    TaskOfficer officer = (TaskOfficer)Enum.Parse(typeof(TaskOfficer), endpoint);

    if (AgentConversationUI.Instance != null) {
        AgentConversationUI.Instance.AddAgentMessage(officer, content, messageType);
    }
}
```

#### 2. AgentConversationUI Updates (`AgentConversationUI.cs`)

**Add send message functionality**:
```csharp
public void OnSendPlayerMessage() {
    if (string.IsNullOrEmpty(playerInputField.text)) return;

    string message = playerInputField.text;
    string agentName = GetCurrentAgentName(currentSelectedAgent);

    var msg = new {
        type = "director_message",
        to_agent = agentName,
        content = message,
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    WebSocketManager.Instance.SendMessage(JsonUtility.ToJson(msg));
    AddPlayerMessage(message);
    playerInputField.text = "";
}

string GetCurrentAgentName(TaskOfficer officer) {
    if (AgentConfigLoader.Instance != null && AgentConfigLoader.Instance.IsLoaded) {
        foreach (var agent in AgentConfigLoader.Instance.Config.agents) {
            if (agent.talkinghead_endpoint == officer.ToString()) {
                return agent.subagent_name;
            }
        }
    }
    return officer.ToString();
}
```

**Add reproposal request button** (optional UI enhancement):
```csharp
public void OnRequestReproposal() {
    string agentName = GetCurrentAgentName(currentSelectedAgent);
    string feedback = feedbackInputField.text;

    var msg = new {
        type = "request_reproposal",
        agent_name = agentName,
        feedback = feedback,
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    WebSocketManager.Instance.SendMessage(JsonUtility.ToJson(msg));
}
```

## WebSocket Protocol

### Python → Unity

**Agent Message**:
```json
{
    "type": "agent_message",
    "agent_name": "Construction Officer",
    "talkinghead_endpoint": "DisasterOfficer",
    "content": "I built a clinic at (5,3) for $400",
    "message_type": "action_summary",
    "round": 5,
    "timestamp": 1234567890.123
}
```

**Choices Proposal** (enhanced):
```json
{
    "type": "choices_proposal",
    "agent_name": "Logistics Officer",
    "talkinghead_endpoint": "WorkforceService",
    "choices": [
        {"index": 0, "description": "Assign 3 workers to clinic"},
        {"index": 1, "description": "Assign 3 workers to motel"}
    ],
    "message": "Should I prioritize the clinic or motel?",
    "is_revision": false,
    "round": 5
}
```

### Unity → Python

**Director Message**:
```json
{
    "type": "director_message",
    "to_agent": "Construction Officer",
    "content": "Good work on the clinic. Check budget first next time.",
    "timestamp": 1234567890.456
}
```

**Request Reproposal**:
```json
{
    "type": "request_reproposal",
    "agent_name": "Logistics Officer",
    "feedback": "Can you propose choices for housing instead?",
    "timestamp": 1234567890.789
}
```

**Choice Made** (existing, unchanged):
```json
{
    "type": "choice_made",
    "agent_name": "Logistics Officer",
    "choice_index": 1,
    "timestamp": 1234567890.999
}
```

## Logging

### Episode JSONL Format

All conversational messages are logged alongside existing events:

```jsonl
{"event_type": "round_start", "round": 1, "day": 1, "segment": 0}
{"event_type": "subagent_turn", "round": 1, "agent": "Construction Officer", "actions": [...]}
{"event_type": "conversation_message", "round": 1, "from": "Construction Officer", "to": "Director", "content": "Built clinic at (5,3)", "message_type": "action_summary", "message_id": "uuid", "timestamp": 1234567890.1}
{"event_type": "conversation_message", "round": 1, "from": "Director", "to": "Construction Officer", "content": "Good work", "message_type": "response", "message_id": "uuid", "timestamp": 1234567890.2}
{"event_type": "choices_proposal", "round": 2, "agent": "Logistics Officer", "choices": [...]}
{"event_type": "conversation_message", "round": 2, "from": "Logistics Officer", "to": "Director", "content": "Should I prioritize clinic?", "message_type": "question", "message_id": "uuid", "timestamp": 1234567890.3}
{"event_type": "conversation_message", "round": 2, "from": "Director", "to": "Logistics Officer", "content": "Yes, prioritize clinic", "message_type": "feedback", "message_id": "uuid", "timestamp": 1234567890.4}
{"event_type": "choice_made", "round": 2, "agent": "Logistics Officer", "choice_index": 0}
```

## Lifecycle Management

### Episode Boundaries

```python
def start_new_episode(self, episode_id):
    self.round_num = 0
    self.message_queue.clear_all()  # Clear all conversation histories

    # Clear each agent's conversation_history field
    for agent in self.config.agents:
        agent.conversation_history = []
```

### Conversation History Retention

- **Scope**: Episode-level (cleared each new episode)
- **Storage**: In-memory during episode, logged to JSONL for persistence
- **Access**: Each agent only sees their own conversations

## Future Extensions

### Agent-to-Agent Conversations

The nested dict structure supports agent-to-agent conversations with minimal changes:

```python
# Current: Only director conversations
self.agent_conversations = {
    "Construction Officer": {
        "Director": [messages]
    }
}

# Future: Add agent-to-agent
self.agent_conversations = {
    "Construction Officer": {
        "Director": [messages],
        "Logistics Officer": [messages]  # Agent-to-agent conversation
    }
}
```

**Required changes**:
1. Update `send_message` to support agent-to-agent routing
2. Add visibility rules (which agents can talk to each other)
3. Update Unity UI to show agent-to-agent conversations
4. Update LLM prompt to include multiple conversation threads

### Async Real-Time Conversations

Future evolution to async/parallel agent execution:

```python
# Agents run in parallel coroutines
async def agent_loop(agent):
    while True:
        # Check for new messages
        messages = await message_queue.get_messages_for(agent.name)
        if messages:
            await agent.handle_messages(messages)

        # Check if can act
        if agent.can_act(game_state):
            actions = await agent.decide_actions()
            await execute_actions(actions)

        # Enter standby
        await agent.set_mode("STANDBY")
        await message_queue.wait_for_event(agent.name)
```

## Implementation Files

### New Files
- `message_queue.py`: MessageQueue class implementation

### Modified Files
- `agent_router.py`: Add MessageQueue, conversation handlers, update agent flow
- `llm_query.py`: Add conversation parameter to LLM queries
- `WebSocketManager.cs`: Add agent_message and director_message handlers
- `AgentConversationUI.cs`: Add send message functionality

### Configuration
- No changes to `agents_config.json` schema
- Uses existing `talkinghead_endpoint` field to route messages to Unity TaskOfficer

## Testing Considerations

1. **Unit Tests**:
   - MessageQueue isolation (agents only see their own conversations)
   - Message routing correctness
   - Conversation history persistence

2. **Integration Tests**:
   - WebSocket message flow (Python ↔ Unity)
   - Choice reproposal with feedback
   - JSONL logging completeness

3. **Manual Tests**:
   - Auto agent summaries appear in correct conversation UI
   - Choice agent can be conversed with during decision-making
   - Director messages trigger appropriate agent responses
   - Reproposal generates different choices

## Open Questions

None - design approved.

## References

- Existing codebase: `agent_router.py`, `AgentConversationUI.cs`
- Unity TaskOfficer enum: `Scripts/Tasks/TaskSystem.cs:61`
- WebSocket protocol: `WebSocketManager.cs`, `agent_router.py`
