using UnityEngine;
using System;
using System.Collections;
using NativeWebSocket; // Install from: https://github.com/endel/NativeWebSocket
using GameActions;

public class WebSocketManager : MonoBehaviour
{
    public static WebSocketManager Instance { get; private set; }

    [Header("Server Settings")]
    public string serverUrl = "ws://localhost:8000/ws";
    public bool enableWebSocket = true; // Master toggle - set to false to play without server
    public float reconnectDelay = 5f;
    public int maxReconnectAttempts = 3;

    [Header("Headless Mode (for RL training)")]
    public bool headlessMode = false; // Set true for gym environment mode
    public int headlessPort = 9876; // Port for gym environment communication

    [Header("Status")]
    public bool isConnected = false;
    public string connectionStatus = "Not Connected";

    private WebSocket websocket;
    private TaskDetailUI taskDetailUI;
    private int reconnectAttempts = 0;
    private bool isReconnecting = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        taskDetailUI = FindObjectOfType<TaskDetailUI>();

        // Check if running in Unity headless mode (batchmode)
        if (Application.isBatchMode)
        {
            Debug.Log("Running in Unity headless mode (batchmode)");
            headlessMode = true;
            enableWebSocket = true;
            serverUrl = $"ws://localhost:{headlessPort}";
        }

        if (enableWebSocket)
        {
            ConnectToServer();
        }
        else
        {
            connectionStatus = "WebSocket Disabled";
            Debug.Log("WebSocket is disabled. Game will run in offline mode.");
        }
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
        // Dispatch WebSocket messages (required for NativeWebSocket)
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
        #endif
    }

    /// <summary>
    /// Safe connect - won't crash if server unavailable
    /// </summary>
    public async void ConnectToServer()
    {
        if (!enableWebSocket) return;

        try
        {
            connectionStatus = "Connecting...";
            Debug.Log($"Connecting to vLLM server at {serverUrl}...");

            websocket = new WebSocket(serverUrl);

            // Event handler: Connection opened
            websocket.OnOpen += () =>
            {
                isConnected = true;
                reconnectAttempts = 0;
                connectionStatus = "Connected";
                Debug.Log($"✅ Connected to vLLM server at {serverUrl}");
            };

            // Event handler: Message received
            websocket.OnMessage += (bytes) =>
            {
                string message = System.Text.Encoding.UTF8.GetString(bytes);
                OnMessageReceived(message);
            };

            // Event handler: Error occurred
            websocket.OnError += (errorMsg) =>
            {
                isConnected = false;
                connectionStatus = $"Error: {errorMsg}";
                Debug.LogWarning($"⚠️ WebSocket error: {errorMsg}");
            };

            // Event handler: Connection closed
            websocket.OnClose += (closeCode) =>
            {
                isConnected = false;
                connectionStatus = "Disconnected";
                Debug.Log($"WebSocket closed. Code: {closeCode}");

                // Attempt reconnect
                if (enableWebSocket && reconnectAttempts < maxReconnectAttempts && !isReconnecting)
                {
                    StartCoroutine(AttemptReconnect());
                }
            };

            // Connect asynchronously
            await websocket.Connect();

            // Start timeout check
            StartCoroutine(CheckConnectionTimeout());
        }
        catch (Exception ex)
        {
            isConnected = false;
            connectionStatus = $"Failed to connect: {ex.Message}";
            Debug.LogWarning($"⚠️ Cannot connect to vLLM server: {ex.Message}");
            Debug.Log("Game will continue without LLM responses.");
        }
    }

    /// <summary>
    /// Check if connection times out
    /// </summary>
    IEnumerator CheckConnectionTimeout()
    {
        yield return new WaitForSeconds(5f);

        if (!isConnected && websocket != null && websocket.State == WebSocketState.Connecting)
        {
            Debug.LogWarning("⚠️ Connection timeout. Server may be unavailable.");
            connectionStatus = "Connection Timeout";
            CloseWebSocket();
        }
    }

    async void CloseWebSocket()
    {
        if (websocket != null)
        {
            await websocket.Close();
        }
    }

    /// <summary>
    /// Auto-reconnect with exponential backoff
    /// </summary>
    IEnumerator AttemptReconnect()
    {
        isReconnecting = true;
        reconnectAttempts++;

        float delay = reconnectDelay * reconnectAttempts;
        connectionStatus = $"Reconnecting in {delay}s... (Attempt {reconnectAttempts}/{maxReconnectAttempts})";

        Debug.Log($"Attempting to reconnect to vLLM server in {delay} seconds (Attempt {reconnectAttempts}/{maxReconnectAttempts})");

        yield return new WaitForSeconds(delay);

        isReconnecting = false;
        ConnectToServer();
    }

    /// <summary>
    /// Safe send - won't crash if disconnected
    /// </summary>
    public async void SendMessage(string message, int taskId)
    {
        if (!enableWebSocket)
        {
            Debug.Log("WebSocket disabled. Message not sent.");
            return;
        }

        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("⚠️ Cannot send message - not connected to vLLM server.");
            Debug.Log($"Message would have been: '{message}'");
            return; // Graceful failure - no crash!
        }

        try
        {
            // Create JSON payload with task context
            var payload = new MessagePayload
            {
                type = "task_message",
                task_id = taskId,
                message = message,
                timestamp = System.DateTime.UtcNow.ToString("o"),
                task_context = GetTaskContext(taskId)
            };

            string json = JsonUtility.ToJson(payload);
            await websocket.SendText(json);

            Debug.Log($"📤 Sent to vLLM: {message}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"⚠️ Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Get task context to send with messages
    /// </summary>
    TaskContext GetTaskContext(int taskId)
    {
        if (TaskSystem.Instance == null) return null;

        GameTask task = TaskSystem.Instance.GetTaskById(taskId);
        if (task == null) return null;

        TaskContext context = new TaskContext
        {
            taskId = task.taskId,
            taskTitle = task.taskTitle,
            taskDescription = task.description,
            taskType = task.taskType.ToString(),
            affectedFacility = task.affectedFacility,
            roundsRemaining = task.roundsRemaining
        };

        return context;
    }

    /// <summary>
    /// Receive and forward to UI (already on main thread with NativeWebSocket!)
    /// </summary>
    void OnMessageReceived(string data)
    {
        try
        {
            Debug.Log($"📥 Received: {data}");

            // Check if this is a gym environment command
            if (headlessMode)
            {
                HandleGymCommand(data);
                return;
            }

            // Handle new multi-agent router message types
            if (data.Contains("\"choices_proposal\""))
            {
                HandleChoicesProposal(data);
                return;
            }

            if (data.Contains("\"director_turn\""))
            {
                HandleCommanderTurn(data);
                return;
            }

            // Try to parse as action message first
            ActionMessage actionMsg = null;
            try
            {
                actionMsg = JsonUtility.FromJson<ActionMessage>(data);
            }
            catch
            {
                // Not an action message, continue with other parsers
            }

            // Check if this is an action execution request
            if (actionMsg != null && actionMsg.type == "execute_action" && actionMsg.action != null)
            {
                Debug.Log($"🎮 Received action execution request: {actionMsg.action.description}");

                // Execute action
                if (ActionExecutor.Instance != null)
                {
                    ActionExecutionResult result = ActionExecutor.Instance.ExecuteAction(actionMsg.action);

                    // Send result back to server
                    string resultJson = JsonUtility.ToJson(result);
                    SendRawMessage(resultJson);

                    Debug.Log($"📤 Sent action execution result: {(result.success ? "✅ Success" : "❌ Failed")}");
                }
                else
                {
                    Debug.LogError("❌ ActionExecutor not found!");
                }
                return;
            }

            // Parse JSON response (existing handlers)
            var response = JsonUtility.FromJson<LLMResponse>(data);

            // Check if this is a task content generation response
            if (response.success)
            {
                // Task content generation response
                Debug.Log($"✅ Received LLM task content: {response.result.messages.Count} messages, {response.result.choices.Count} choices");

                // Forward to TaskSystem to apply the content
                if (TaskSystem.Instance != null)
                {
                    TaskSystem.Instance.ApplyLLMTaskContent(response.result);
                }
            }
            else if (!string.IsNullOrEmpty(response.error))
            {
                // Error response
                Debug.LogError($"❌ LLM server error: {response.error}");
            }
            else if (!string.IsNullOrEmpty(response.response))
            {
                // Chat message response
                if (taskDetailUI != null)
                {
                    taskDetailUI.OnReceiveLLMResponse(response.response);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to parse message: {ex.Message}");
        }
    }

    /// <summary>
    /// Send raw message string (for action results and agent requests)
    /// </summary>
    public async void SendRawMessage(string message)
    {
        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("⚠️ Cannot send message - not connected");
            return;
        }

        try
        {
            await websocket.SendText(message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"⚠️ Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Manual disconnect
    /// </summary>
    public async void Disconnect()
    {
        enableWebSocket = false;

        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
            connectionStatus = "Manually Disconnected";
            Debug.Log("WebSocket manually disconnected.");
        }
    }

    async void OnDestroy()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }

    /// <summary>
    /// Request LLM to generate task content with choices
    /// </summary>
    public async void RequestTaskContent(int taskId)
    {
        if (!enableWebSocket)
        {
            Debug.Log("WebSocket disabled. Cannot request task content.");
            return;
        }

        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("⚠️ Cannot request task content - not connected to server.");
            return;
        }

        try
        {
            // Get comprehensive game state from TaskSystem
            GameStatePayload gameState = null;
            if (TaskSystem.Instance != null)
            {
                gameState = TaskSystem.Instance.GetCurrentGameState(taskId);
            }

            // Create payload for task content generation
            var payload = new MessagePayload
            {
                type = "generate_task_content",
                task_id = taskId,
                timestamp = System.DateTime.UtcNow.ToString("o"),
                task_context = GetTaskContext(taskId),
                game_state = gameState
            };

            string json = JsonUtility.ToJson(payload);
            await websocket.SendText(json);

            Debug.Log($"📤 Requested LLM task content for task ID: {taskId}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"⚠️ Failed to request task content: {ex.Message}");
        }
    }

    /// <summary>
    /// Check connection status from other scripts
    /// </summary>
    public bool IsConnected()
    {
        return isConnected && websocket != null && websocket.State == WebSocketState.Open;
    }

    // ========================================================================
    // MULTI-AGENT ROUTER MESSAGES
    // ========================================================================

    /// <summary>
    /// Receive a choices_proposal from the agent router.
    /// Routes the proposal to the correct officer tab via AgentConversationUI.
    /// </summary>
    void HandleChoicesProposal(string data)
    {
        try
        {
            var proposal = JsonUtility.FromJson<ChoicesProposalMessage>(data);
            Debug.Log($"[WS] choices_proposal from {proposal.agent_name} "
                     + $"({(proposal.packages != null ? proposal.packages.Length : 0)} packages) "
                     + $"→ {proposal.talkinghead}");

            if (TaskSystem.Instance == null)
            {
                Debug.LogError("[WS] TaskSystem not available — cannot display choices");
                return;
            }

            // Convert choices_proposal packages to LLMTaskContent format
            var llmContent = new LLMTaskContent
            {
                taskId = -1, // Special ID for multi-agent proposals
                messages = new System.Collections.Generic.List<string>(),
                choices = new System.Collections.Generic.List<LLMAgentChoice>()
            };

            // Add agent reasoning as a message
            if (!string.IsNullOrEmpty(proposal.reasoning))
            {
                llmContent.messages.Add(proposal.reasoning);
            }

            // Convert each package to an LLMAgentChoice
            if (proposal.packages != null)
            {
                foreach (var pkg in proposal.packages)
                {
                    llmContent.choices.Add(new LLMAgentChoice
                    {
                        choiceId = pkg.package_index,
                        choiceText = pkg.label,
                        agentReasoning = pkg.description,
                        confidence = pkg.confidence,
                        impacts = new System.Collections.Generic.List<LLMImpact>()
                    });
                }
            }

            // Create or update a special multi-agent task for this officer
            GameTask multiAgentTask = TaskSystem.Instance.GetOrCreateMultiAgentTask(
                proposal.talkinghead,
                proposal.agent_name
            );

            // Store proposal metadata on the task for later reference
            multiAgentTask.multiAgentProposal = proposal;

            // Apply the LLM content to display in the UI
            TaskSystem.Instance.ApplyLLMTaskContent(llmContent);

            Debug.Log($"✅ Displayed {proposal.packages.Length} choice packages in {proposal.talkinghead} tab");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS] Failed to handle choices_proposal: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Receive director_turn signal from router.
    /// For manual director: unlock GUI. For LLM director: router handles it.
    /// </summary>
    void HandleCommanderTurn(string data)
    {
        try
        {
            Debug.Log("[WS] director_turn received - unlocking player GUI.");
            // TODO: notify UI layer that player's turn has started
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS] Failed to handle director_turn: {ex.Message}");
        }
    }

    /// <summary>
    /// Send begin_round to the agent router with current game state.
    /// Call this when the game advances to a new time segment.
    /// </summary>
    public void SendBeginRound(int round, int day, int segment)
    {
        if (!isConnected) return;

        GameStatePayload gameState = null;
        if (TaskSystem.Instance != null)
        {
            gameState = TaskSystem.Instance.GetCurrentGameState();
        }

        if (gameState == null)
        {
            Debug.LogWarning("[WS] SendBeginRound: could not get game state.");
            return;
        }

        var msg = new BeginRoundMessage
        {
            game_state = gameState,
            round = round,
            day = day,
            segment = segment,
            timestamp = System.DateTime.UtcNow.ToString("o"),
        };
        SendRawMessage(JsonUtility.ToJson(msg));
        Debug.Log($"[WS] begin_round sent (round={round}, day={day}, seg={segment})");
    }

    /// <summary>
    /// Send choice_made back to router after player selects a package.
    /// Unity has already executed the actions before calling this.
    /// </summary>
    public void SendChoiceMade(string agentName, int packageIndex,
                               string executionResultsJson, string gameStateJson)
    {
        Debug.Log($"[WS] SendChoiceMade called: isConnected={isConnected}, websocket={(websocket != null ? websocket.State.ToString() : "null")}");

        if (!isConnected)
        {
            Debug.LogWarning("[WS] Cannot send choice_made - not connected!");
            return;
        }

        var msg = $"{{\"type\":\"choice_made\",\"agent_name\":\"{agentName}\","
                + $"\"package_index\":{packageIndex},"
                + $"\"execution_results\":{executionResultsJson},"
                + $"\"game_state\":{gameStateJson},"
                + $"\"timestamp\":\"{System.DateTime.UtcNow:o}\"}}";
        SendRawMessage(msg);
        Debug.Log($"[WS] choice_made sent (agent={agentName}, package={packageIndex})");
    }

    // ========================================================================
    // GYM ENVIRONMENT COMMANDS (for headless RL training)
    // ========================================================================

    /// <summary>
    /// Handle commands from Python Gymnasium environment
    /// </summary>
    void HandleGymCommand(string data)
    {
        try
        {
            var command = JsonUtility.FromJson<GymCommand>(data);

            if (command.command == "reset")
            {
                HandleGymReset();
            }
            else if (command.command == "step")
            {
                HandleGymStep(command.actions);
            }
            else if (command.command == "get_state")
            {
                SendGameState();
            }
            else
            {
                Debug.LogWarning($"Unknown gym command: {command.command}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to handle gym command: {ex.Message}");
        }
    }

    /// <summary>
    /// Reset the game to initial state
    /// </summary>
    void HandleGymReset()
    {
        Debug.Log("🔄 Gym Reset requested");

        // Reset all systems
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.activeTasks.Clear();
            TaskSystem.Instance.completedTasks.Clear();
        }

        if (GlobalClock.Instance != null)
        {
            // Reset clock to day 1, segment 1
            GlobalClock.Instance.ResetToDay1();
        }

        // Note: SatisfactionAndBudget doesn't have a reset method
        // It will be re-initialized when scene reloads or manually set if needed

        // Send initial state back
        SendGameState();
    }

    /// <summary>
    /// Execute action(s) and return new state
    /// </summary>
    void HandleGymStep(string actionsJson)
    {
        Debug.Log($"🎮 Gym Step: {actionsJson}");

        int previousSatisfaction = 0;
        if (SatisfactionAndBudget.Instance != null)
        {
            previousSatisfaction = (int)SatisfactionAndBudget.Instance.GetCurrentSatisfaction();
        }

        // Parse and execute actions
        bool allSuccess = true;
        if (!string.IsNullOrEmpty(actionsJson))
        {
            try
            {
                var actionsList = JsonUtility.FromJson<ActionsList>(actionsJson);

                foreach (var action in actionsList.actions)
                {
                    if (ActionExecutor.Instance != null)
                    {
                        var result = ActionExecutor.Instance.ExecuteAction(action);
                        if (!result.success)
                        {
                            allSuccess = false;
                            Debug.LogWarning($"Action failed: {result.error_message}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to execute actions: {ex.Message}");
                allSuccess = false;
            }
        }

        // Calculate reward (satisfaction delta)
        int currentSatisfaction = 0;
        if (SatisfactionAndBudget.Instance != null)
        {
            currentSatisfaction = (int)SatisfactionAndBudget.Instance.GetCurrentSatisfaction();
        }
        int reward = currentSatisfaction - previousSatisfaction;

        // Check termination conditions
        bool terminated = currentSatisfaction <= 0;
        bool truncated = false;
        if (GlobalClock.Instance != null)
        {
            truncated = GlobalClock.Instance.GetCurrentDay() >= 30;
        }

        // Send response
        SendGymStepResponse(reward, terminated, truncated);
    }

    /// <summary>
    /// Send current game state to gym environment
    /// </summary>
    void SendGameState()
    {
        if (TaskSystem.Instance != null)
        {
            GameStatePayload gameState = TaskSystem.Instance.GetCurrentGameState();
            string json = JsonUtility.ToJson(gameState, prettyPrint: false);

            var response = new GymResetResponse
            {
                type = "reset_response",
                game_state = gameState,
                satisfaction = SatisfactionAndBudget.Instance != null ?
                    (int)SatisfactionAndBudget.Instance.GetCurrentSatisfaction() : 0
            };

            string responseJson = JsonUtility.ToJson(response, prettyPrint: false);
            SendRawMessage(responseJson);

            Debug.Log($"📤 Sent game state ({json.Length} bytes)");
        }
    }

    /// <summary>
    /// Send step response with reward and state
    /// </summary>
    void SendGymStepResponse(int reward, bool terminated, bool truncated)
    {
        GameStatePayload gameState = null;
        if (TaskSystem.Instance != null)
        {
            gameState = TaskSystem.Instance.GetCurrentGameState();
        }

        var response = new GymStepResponse
        {
            type = "step_response",
            game_state = gameState,
            reward = reward,
            terminated = terminated,
            truncated = truncated,
            satisfaction = SatisfactionAndBudget.Instance != null ?
                (int)SatisfactionAndBudget.Instance.GetCurrentSatisfaction() : 0,
            budget = SatisfactionAndBudget.Instance != null ?
                SatisfactionAndBudget.Instance.GetCurrentBudget() : 0,
            day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 0,
            segment = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 0
        };

        string json = JsonUtility.ToJson(response, prettyPrint: false);
        SendRawMessage(json);

        Debug.Log($"📤 Sent step response: reward={reward}, terminated={terminated}, truncated={truncated}");
    }
}

// JSON serialization classes
[System.Serializable]
public class MessagePayload
{
    public string type; // "task_message" or "generate_task_content"
    public int task_id;
    public string message;
    public string timestamp;
    public TaskContext task_context;
    public GameStatePayload game_state; // Full game state for LLM
}

[System.Serializable]
public class AgentDecisionRequest
{
    public string type = "request_agent_decision";
    public GameStatePayload game_state;
    public string goal;
    public string timestamp;
}

[System.Serializable]
public class LLMResponse
{
    public string response; // For chat messages
    public bool success; // For task content generation
    public string error; // For task content generation errors
    public LLMTaskContent result; // For task content generation
    public float inference_time;
    public string timestamp;
}

// Gym environment message classes
[System.Serializable]
public class GymCommand
{
    public string command; // "reset", "step", "get_state"
    public string actions; // JSON string of actions list
}

[System.Serializable]
public class ActionsList
{
    public GameAction[] actions;
}

[System.Serializable]
public class GymResetResponse
{
    public string type = "reset_response";
    public GameStatePayload game_state;
    public int satisfaction;
}

[System.Serializable]
public class GymStepResponse
{
    public string type = "step_response";
    public GameStatePayload game_state;
    public int reward;
    public bool terminated;
    public bool truncated;
    public int satisfaction;
    public int budget;
    public int day;
    public int segment;
}

// -- Multi-Agent Router message classes --------------------------------------

[System.Serializable]
public class BeginRoundMessage
{
    public string type = "begin_round";
    public GameStatePayload game_state;
    public int round;
    public int day;
    public int segment;
    public string timestamp;
}

[System.Serializable]
public class ActionPackage
{
    public int package_index;
    public string label;
    public string description;
    public float confidence;
    public int[] action_indices;
}

[System.Serializable]
public class ChoicesProposalMessage
{
    public string type;
    public string agent_name;
    public string talkinghead;
    public string reasoning;
    public ActionPackage[] packages;
    public GameAction[] available_actions; // Full action objects from router
}
