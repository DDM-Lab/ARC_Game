using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using NativeWebSocket; // Install from: https://github.com/endel/NativeWebSocket
using GameActions;

[System.Serializable]
public class AppConfig
{
    public string wsUrl;
    public string mapConfigUrl;
    public string logServerUrl;
}

public class WebSocketManager : MonoBehaviour
{
    public static WebSocketManager Instance { get; private set; }
    public static AppConfig LoadedConfig { get; private set; }

    [Header("Server Settings")]
    public string serverUrl = "ws://localhost:9876/ws";
    public bool enableWebSocket = true; // Master toggle - set to false to play without server
    public float reconnectDelay = 5f;
    public int maxReconnectAttempts = 3;

    [Header("Session Identity")]
    // The router validates this against its keys file / ARC_API_KEYS env.
    // Defaults to the dev key the router uses when no keys file is supplied.
    public string apiKey = "dev-local-key";
    // Config name (without .json) the router should load for this session.
    public string configName = "openai_multi_agent_config_local";

    [Header("Headless Mode (for RL training)")]
    public bool headlessMode = false; // Set true for gym environment mode
    public int headlessPort = 9876; // Port for gym environment communication

    [Header("Status")]
    public bool isConnected = false;
    // Flips to true after the first successful connect of this play session.
    // Used to suppress re-sending game_start on transient reconnects.
    private bool gameStartSentThisSession = false;
    // Server-assigned session id from hello_ack. Empty until the handshake
    // completes; reset on each fresh connection.
    private string sessionId = "";
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

    /// <summary>Drop cached references to scene objects (e.g. after a gym in-process
    /// reset reloads MainScene). This manager is DontDestroyOnLoad and survives the
    /// reload, so its cached TaskDetailUI would otherwise dangle at the destroyed old
    /// instance. Re-resolved lazily on next use / next Start.</summary>
    public void ClearSceneRefs()
    {
        taskDetailUI = null;
    }

    void Start()
    {
        taskDetailUI = FindObjectOfType<TaskDetailUI>();

        // Pull server URL, API key, and config name from PlayerPrefs if a
        // previous launcher screen saved them. Inspector values act as
        // defaults the first time a user runs the game.
        if (PlayerPrefs.HasKey("arc_server_url"))
            serverUrl = PlayerPrefs.GetString("arc_server_url");
        if (PlayerPrefs.HasKey("arc_api_key"))
            apiKey = PlayerPrefs.GetString("arc_api_key");
        if (PlayerPrefs.HasKey("arc_config_name"))
            configName = PlayerPrefs.GetString("arc_config_name");

        // Headless / gym training mode: auto-connect immediately.
        if (Application.isBatchMode)
        {
            Debug.Log("Running in Unity headless mode (batchmode)");
            headlessMode = true;
            enableWebSocket = true;
            serverUrl = $"ws://localhost:{headlessPort}";
            ConnectToServer();
            return;
        }

        // Editor / standalone: defer to the ServerLauncherUI. The launcher
        // pulls the config catalog from the router, lets the user pick one,
        // then invokes ConnectToServer() with the chosen settings.
        if (enableWebSocket)
        {
            connectionStatus = "Awaiting launcher";
            Debug.Log("[WS] Awaiting launcher to call ConnectToServer()…");
        }
        else
        {
            connectionStatus = "WebSocket Disabled";
            Debug.Log("WebSocket is disabled. Game will run in offline mode.");
        }
    }

    IEnumerator LoadConfigThenConnect()
    {
        string configPath = Application.streamingAssetsPath + "/config.json";
        using (UnityWebRequest req = UnityWebRequest.Get(configPath))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var config = JsonUtility.FromJson<AppConfig>(req.downloadHandler.text);
                if (config != null)
                {
                    LoadedConfig = config;
                    if (!string.IsNullOrEmpty(config.wsUrl))
                    {
                        serverUrl = config.wsUrl;
                        Debug.Log($"[WebSocket] URL loaded from config.json: {serverUrl}");
                    }
                }
            }
            else
            {
                Debug.Log($"[WebSocket] config.json not found, using Inspector value: {serverUrl}");
            }
        }

        ConnectToServer();
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
                sessionId = "";
                connectionStatus = "Authenticating...";
                Debug.Log($"✅ Connected to router at {serverUrl}");

                // Multi-tenant hello handshake. Router will reply with
                // hello_ack (success) or hello_error (rejection) before any
                // gameplay traffic flows.
                string hello = "{\"type\":\"hello\""
                               + ",\"api_key\":\"" + EscapeJson(apiKey) + "\""
                               + ",\"player_id\":\"" + EscapeJson(GetOrCreatePlayerId()) + "\""
                               + ",\"config\":\"" + EscapeJson(configName) + "\"}";
                SendRawMessage(hello);
                Debug.Log($"[WS] hello sent (config={configName})");
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

            // Multi-tenant handshake — must run before any gameplay traffic.
            if (data.Contains("\"hello_ack\""))
            {
                HandleHelloAck(data);
                return;
            }
            if (data.Contains("\"hello_error\""))
            {
                HandleHelloError(data);
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

            // Handle agent conversational messages with embedded choices
            if (data.Contains("\"agent_message_with_choices\""))
            {
                HandleAgentMessageWithChoices(data);
                return;
            }

            // Handle agent conversational messages
            if (data.Contains("\"agent_message\""))
            {
                HandleAgentMessage(data);
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

            // Check if this is a task-choice answer (a router officer selecting a
            // choice on one of its jurisdiction's tasks). Mirrors the gym-TCP
            // HandleSelectTaskChoice path: resolve to TaskDetailUI.SelectTaskChoiceHeadless
            // and reply with an ActionExecutionResult (no `type`) so the router's
            // action-result handler resolves the officer's pending choice future.
            if (actionMsg != null && actionMsg.type == "select_task_choice")
            {
                TaskChoiceMessage choiceMsg = null;
                try { choiceMsg = JsonUtility.FromJson<TaskChoiceMessage>(data); }
                catch { }

                var choiceResult = new ActionExecutionResult
                {
                    action_id = choiceMsg != null
                        ? $"choice_{choiceMsg.taskId}_{choiceMsg.choiceId}" : "choice_unknown",
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                if (choiceMsg == null)
                {
                    choiceResult.success = false;
                    choiceResult.error_message = "Malformed select_task_choice message";
                }
                else
                {
                    if (taskDetailUI == null)
                        taskDetailUI = FindObjectOfType<TaskDetailUI>();

                    if (taskDetailUI == null)
                    {
                        choiceResult.success = false;
                        choiceResult.error_message = "TaskDetailUI not available";
                    }
                    else
                    {
                        string failReason;
                        bool ok = taskDetailUI.SelectTaskChoiceHeadless(
                            choiceMsg.taskId, choiceMsg.choiceId, out failReason);
                        choiceResult.success = ok;
                        choiceResult.error_message = ok ? null : failReason;
                        Debug.Log($"🗳️ select_task_choice task {choiceMsg.taskId} " +
                                  $"choice {choiceMsg.choiceId}: " +
                                  (ok ? "✅ Success" : "❌ " + failReason));
                    }
                }

                SendRawMessage(JsonUtility.ToJson(choiceResult));
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
    /// Minimal JSON string escaper for the hello frame. NativeWebSocket sends
    /// raw text so we have to hand-build the payload here; bigger payloads
    /// elsewhere already use a real serializer.
    /// </summary>
    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    /// <summary>
    /// Persistent per-browser player id. In WebGL, PlayerPrefs is backed by the
    /// browser's IndexedDB (idbfs), so this UUID survives reloads and new game
    /// sessions exactly like a localStorage id — it lets us differentiate
    /// individual players who share one API key. Generated once, then reused.
    /// </summary>
    private string GetOrCreatePlayerId()
    {
        string id = PlayerPrefs.GetString("arc_player_id", "");
        if (string.IsNullOrEmpty(id))
        {
            id = System.Guid.NewGuid().ToString();
            PlayerPrefs.SetString("arc_player_id", id);
            PlayerPrefs.Save();
        }
        return id;
    }

    /// <summary>
    /// Handshake success. Stash the server-assigned session id and now
    /// (and only now) send game_start so the router clears any leftover
    /// in-memory state from a previous session.
    /// </summary>
    private void HandleHelloAck(string data)
    {
        // Extract session_id without pulling in a JSON dependency — the
        // payload is tiny and we only need one field.
        int idx = data.IndexOf("\"session_id\"");
        if (idx >= 0)
        {
            int colon = data.IndexOf(':', idx);
            int firstQuote = data.IndexOf('"', colon + 1);
            int secondQuote = data.IndexOf('"', firstQuote + 1);
            if (firstQuote > 0 && secondQuote > firstQuote)
                sessionId = data.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }
        connectionStatus = "Connected";
        Debug.Log($"[WS] hello_ack received (session={sessionId})");

        if (!gameStartSentThisSession)
        {
            SendRawMessage("{\"type\":\"game_start\",\"timestamp\":\""
                           + System.DateTime.UtcNow.ToString("o") + "\"}");
            gameStartSentThisSession = true;
            Debug.Log("[WS] game_start sent");
        }
    }

    /// <summary>
    /// Handshake rejection (bad key, unknown config, etc). Surface in the
    /// status string and let the server close us; reconnect would just be
    /// rejected again with the same credentials.
    /// </summary>
    private void HandleHelloError(string data)
    {
        connectionStatus = "Auth failed";
        Debug.LogWarning($"[WS] hello_error: {data}");
        enableWebSocket = false; // suppress auto-reconnect on credential errors
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
                string cleanReasoning = TaskSystem.Instance.ConvertSiteNamesToFriendly(proposal.reasoning);
                llmContent.messages.Add(proposal.reasoning);
            }

            // Convert each package to an LLMAgentChoice
            if (proposal.packages != null)
            {
                foreach (var pkg in proposal.packages)
                {
                    // Friendly site names for the human-visible label; our detailed
                    // description (package + action list) for the agent-facing reasoning.
                    string cleanLabel = TaskSystem.Instance.ConvertSiteNamesToFriendly(pkg.label);
                    string detailedDescription = FormatPackageDescription(pkg, proposal.available_actions);

                    llmContent.choices.Add(new LLMAgentChoice
                    {
                        choiceId = pkg.package_index,
                        choiceText = cleanLabel,
                        agentReasoning = detailedDescription,
                        confidence = pkg.confidence,
                        impacts = new System.Collections.Generic.List<LLMImpact>()
                    });
                    //llmContent.choices.Add(new LLMAgentChoice
                    //{
                    //    choiceId = pkg.package_index,
                    //    choiceText = pkg.label,
                    //    agentReasoning = pkg.description,
                    //    confidence = pkg.confidence,
                    //    impacts = new System.Collections.Generic.List<LLMImpact>()
                    //});
                }
            }

            // Before the existing multi-agent task gets cleared, snapshot its
            // current reasoning + choices into the per-officer chat history so
            // prior proposals stay visible across reproposals.
            if (AgentConversationUI.Instance != null
                && System.Enum.TryParse(proposal.talkinghead, out TaskOfficer archiveOfficer))
            {
                AgentConversationUI.Instance.ArchiveExistingProposal(archiveOfficer);
            }

            // Create or update a special multi-agent task for this officer
            GameTask multiAgentTask = TaskSystem.Instance.GetOrCreateMultiAgentTask(
                proposal.talkinghead,
                proposal.agent_name
            );

            // Store proposal metadata on the task for later reference
            multiAgentTask.multiAgentProposal = proposal;

            // Apply the LLM content to display in the UI. Pass the exact officer task
            // (not by id): all officers' proposals share taskId == -1, so an id lookup
            // would cross-wire proposals between officers in multi-agent scenarios.
            TaskSystem.Instance.ApplyLLMTaskContent(multiAgentTask, llmContent);

            // If the user is currently viewing this officer's tab, re-render so the
            // newly proposed/reproposed choices appear immediately. Without this,
            // the task data updates but the panel only refreshes on next tab switch.
            if (AgentConversationUI.Instance != null
                && System.Enum.TryParse(proposal.talkinghead, out TaskOfficer officerEnum))
            {
                // Proposal arrived: this officer is done generating.
                AgentConversationUI.Instance.SetOfficerGenerating(officerEnum, false);
                AgentConversationUI.Instance.OnChoicesProposalApplied(officerEnum);
            }

            // The proposal also renders in the task-detail panel (opened from the Task
            // Center). If that panel is currently showing this officer's proposal,
            // re-render it in place so reproposed options appear immediately instead of
            // only after close/reopen.
            if (taskDetailUI != null
                && System.Enum.TryParse(proposal.talkinghead, out TaskOfficer detailOfficer))
            {
                taskDetailUI.RefreshProposalIfShowing(detailOfficer);
            }

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
            // Round over: every officer has finished, so clear any waiting bubbles
            // (including officers that ended their turn without sending a frame).
            if (AgentConversationUI.Instance != null)
                AgentConversationUI.Instance.ClearAllGenerating();
            // TODO: notify UI layer that player's turn has started
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS] Failed to handle director_turn: {ex.Message}");
        }
    }

    void HandleAgentMessage(string data)
    {
        try
        {
            var msg = JsonUtility.FromJson<AgentConversationMessage>(data);
            Debug.Log($"[WS] agent_message received from {msg.agent_name}: {msg.content}");

            // Parse talkinghead_endpoint to TaskOfficer enum
            TaskOfficer officer;
            if (System.Enum.TryParse(msg.talkinghead_endpoint, out officer))
            {
                // Forward to conversation UI
                if (AgentConversationUI.Instance != null)
                {
                    // Response arrived: drop this officer's waiting bubble before
                    // the message is appended so the message lands at the bottom.
                    AgentConversationUI.Instance.SetOfficerGenerating(officer, false);
                    AgentConversationUI.Instance.AddAgentMessage(officer, msg.content, msg.message_type);
                }
                else
                {
                    Debug.LogWarning("[WS] AgentConversationUI not found, cannot display message");
                }
            }
            else
            {
                Debug.LogError($"[WS] Invalid talkinghead_endpoint: {msg.talkinghead_endpoint}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS] Failed to handle agent_message: {ex.Message}");
        }
    }

    void HandleAgentMessageWithChoices(string data)
    {
        try
        {
            var msg = JsonUtility.FromJson<AgentMessageWithChoices>(data);
            Debug.Log($"[WS] agent_message_with_choices received from {msg.agent_name}: {msg.content}");

            // Parse talkinghead_endpoint to TaskOfficer enum
            TaskOfficer officer;
            if (System.Enum.TryParse(msg.talkinghead_endpoint, out officer))
            {
                // Forward to conversation UI with embedded choices
                if (AgentConversationUI.Instance != null)
                {
                    AgentConversationUI.Instance.SetOfficerGenerating(officer, false);
                    AgentConversationUI.Instance.AddAgentMessageWithChoices(
                        officer,
                        msg.content,
                        msg.message_type,
                        msg.reasoning,
                        msg.packages,
                        msg.available_actions
                    );
                }
                else
                {
                    Debug.LogWarning("[WS] AgentConversationUI not found, cannot display message with choices");
                }
            }
            else
            {
                Debug.LogError($"[WS] Invalid talkinghead_endpoint: {msg.talkinghead_endpoint}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS] Failed to handle agent_message_with_choices: {ex.Message}");
        }
    }

    /// <summary>
    /// Send begin_round to the agent router with current game state.
    /// Call this when the game advances to a new time segment.
    /// </summary>
    /// <summary>
    /// Send begin_round to the router. Returns true only if the frame was
    /// actually sent (connected AND game state serializable). Callers that
    /// retry until success (the human first-proposal one-shot) rely on this
    /// bool so they don't flip their "done" flag before the state is ready.
    /// </summary>
    public bool SendBeginRound(int round, int day, int segment)
    {
        if (!isConnected) return false;

        GameStatePayload gameState = null;
        if (TaskSystem.Instance != null)
        {
            gameState = TaskSystem.Instance.GetCurrentGameState();
        }

        if (gameState == null)
        {
            Debug.LogWarning("[WS] SendBeginRound: could not get game state.");
            return false;
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
        // All continuous officers are dispatched at once on begin_round; show a
        // waiting bubble on each until its proposal/message arrives (or director_turn).
        if (AgentConversationUI.Instance != null)
            AgentConversationUI.Instance.MarkRoundGenerating();
        return true;
    }

    /// <summary>
    /// True once the game_start frame has been sent for this session (right
    /// after hello_ack). The router clears its queue and resets its round
    /// counter on game_start, so a begin_round sent before it would be
    /// discarded — the human first-proposal must wait for this.
    /// </summary>
    public bool HasSentGameStart() => gameStartSentThisSession;

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
                + $"\"click_seq\":{GuiInteractionRecorder.LastClickSeq},"
                + $"\"timestamp\":\"{System.DateTime.UtcNow:o}\"}}";
        SendRawMessage(msg);
        Debug.Log($"[WS] choice_made sent (agent={agentName}, package={packageIndex})");
    }

    /// <summary>
    /// Send director message to an agent.
    /// Called when player sends a conversational message to an agent.
    /// </summary>
    public void SendDirectorMessage(string toAgent, string content)
    {
        if (!isConnected)
        {
            Debug.LogWarning("[WS] Cannot send director_message - not connected!");
            return;
        }

        var msg = new DirectorMessage
        {
            to_agent = toAgent,
            content = content,
            click_seq = GuiInteractionRecorder.LastClickSeq,
            timestamp = (System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds
        };

        SendRawMessage(JsonUtility.ToJson(msg));
        Debug.Log($"[WS] director_message sent to {toAgent}: {content}");
    }

    // ── Action / interaction logging (per-actor unified log) ─────────
    // These mirror human UI interactions to the router so they land, actor-tagged,
    // in the per-session JSONL. Guarded by isConnected so the headless/gym build
    // (which has no human UI) never emits them.

    /// <summary>
    /// Send a semantic human UI interaction (Tier-1 ui_interaction): opening an
    /// agent's conversation, switching officers, selecting/switching a choice
    /// package, clicking confirm, opening metrics, etc. The raw click coords are
    /// sent separately by GuiInteractionRecorder; clickSeq joins the two.
    /// </summary>
    public void SendClientEvent(string category, string name, string detail, long clickSeq)
    {
        if (!isConnected) return;
        string msg = "{\"type\":\"client_event\",\"actor_kind\":\"human\","
            + $"\"category\":\"{EscapeJson(category)}\",\"name\":\"{EscapeJson(name)}\","
            + $"\"payload\":{{\"detail\":\"{EscapeJson(detail ?? string.Empty)}\"}},"
            + $"\"click_seq\":{clickSeq},"
            + $"\"timestamp\":\"{System.DateTime.UtcNow:o}\"}}";
        SendRawMessage(msg);
    }

    /// <summary>
    /// Send one raw mouse click (Tier-2 every-click trace) with screen/normalized
    /// coordinates and the UI element hit. hitName == null means the click landed
    /// on no UI element (coords-only record).
    /// </summary>
    public void SendGuiEvent(long clickSeq, int button,
        float sx, float sy, float sw, float sh, float nx, float ny,
        string canvasName, float clx, float cly,
        string hitName, string hitType, string hitPath)
    {
        if (!isConnected) return;
        string hitJson = hitName == null ? "null"
            : $"{{\"name\":\"{EscapeJson(hitName)}\",\"type\":\"{EscapeJson(hitType)}\",\"path\":\"{EscapeJson(hitPath)}\"}}";
        string canvasJson = canvasName == null ? "null"
            : $"{{\"name\":\"{EscapeJson(canvasName)}\",\"local_x\":{F(clx)},\"local_y\":{F(cly)}}}";
        string payload = $"{{\"button\":{button},"
            + $"\"screen\":{{\"x\":{F(sx)},\"y\":{F(sy)},\"w\":{F(sw)},\"h\":{F(sh)}}},"
            + $"\"normalized\":{{\"x\":{F(nx)},\"y\":{F(ny)}}},"
            + $"\"canvas\":{canvasJson},\"hit\":{hitJson}}}";
        string msg = $"{{\"type\":\"gui_event\",\"click_seq\":{clickSeq},"
            + $"\"payload\":{payload},"
            + $"\"timestamp\":\"{System.DateTime.UtcNow:o}\"}}";
        SendRawMessage(msg);
    }

    // Invariant-culture float formatting so coords never serialize with a comma
    // decimal separator on non-US locales (which would corrupt the JSON).
    // (EscapeJson already exists above and is reused here.)
    private static string F(float v) =>
        v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Send request to agent to repropose choices with feedback.
    /// Called when director wants agent to generate new choices.
    /// </summary>
    public void SendRequestReproposal(string agentName, string feedback)
    {
        if (!isConnected)
        {
            Debug.LogWarning("[WS] Cannot send request_reproposal - not connected!");
            return;
        }

        var msg = $"{{\"type\":\"request_reproposal\",\"agent_name\":\"{agentName}\","
                + $"\"feedback\":\"{feedback}\","
                + $"\"timestamp\":{(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds}}}";

        SendRawMessage(msg);
        Debug.Log($"[WS] request_reproposal sent to {agentName}");
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
                            Debug.LogWarning($"Action failed: {result.error_message}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to execute actions: {ex.Message}");
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

    /// <summary>
    /// Format package description combining LLM description + action list
    /// </summary>
    string FormatPackageDescription(ActionPackage package, GameAction[] availableActions)
    {
        // The grounded description already reads "$cost · <engine action summary>\nWhy: ..."
        // — the action summary is built server-side from the engine's OWN action list, so we
        // do NOT append a separate "Actions:" block here. It duplicated the summary and could
        // show engine numbers that contradicted the model's prose. Use the description directly.
        if (!string.IsNullOrEmpty(package.description))
            return package.description.TrimEnd();

        // Fallback only: no description was supplied — list the engine actions so the
        // card is never blank.
        System.Text.StringBuilder desc = new System.Text.StringBuilder();
        if (package.action_indices != null && package.action_indices.Length > 0 && availableActions != null)
        {
            desc.AppendLine("Actions:");
            foreach (int idx in package.action_indices)
            {
                if (idx >= 0 && idx < availableActions.Length)
                {
                    string actionName = availableActions[idx].description;
                    if (string.IsNullOrEmpty(actionName))
                        actionName = availableActions[idx].action_id;
                    desc.AppendLine($"• {actionName}");
                }
            }
        }

        return desc.ToString().TrimEnd();
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

[System.Serializable]
public class AgentConversationMessage
{
    public string type = "agent_message";
    public string agent_name;
    public string talkinghead_endpoint;
    public string content;
    public string message_type;
    public int round;
    public double timestamp;
}

[System.Serializable]
public class AgentMessageWithChoices
{
    public string type = "agent_message_with_choices";
    public string agent_name;
    public string talkinghead_endpoint;
    public string content;
    public string message_type;
    public int round;
    public double timestamp;
    public string reasoning;
    public ActionPackage[] packages;
    public GameAction[] available_actions;
}

[System.Serializable]
public class DirectorMessage
{
    public string type = "director_message";
    public string to_agent;
    public string content;
    public long click_seq = -1;
    public double timestamp;
}
