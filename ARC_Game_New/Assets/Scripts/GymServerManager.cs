using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using GameActions;

/// <summary>
/// TCP-based Gym Server for ARCGameGymEnv communication.
///
/// This server listens for connections from Python gym environments and responds to:
/// - get_game_state: Returns current game state
/// - execute_action: Executes an action and returns result
///
/// Protocol: JSON messages over TCP, one message per line (newline-delimited JSON)
/// </summary>
public class GymServerManager : MonoBehaviour
{
    public static GymServerManager Instance { get; private set; }

    [Header("Server Settings")]
    public bool enableGymServer = false;
    public int gymServerPort = 9876;

    [Header("Status")]
    public bool isListening = false;
    public int connectedClients = 0;

    private TcpListener tcpListener;
    private Thread listenerThread;
    private List<TcpClient> clients = new List<TcpClient>();
    private Queue<Action> mainThreadActions = new Queue<Action>();
    private object actionQueueLock = new object();

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
        // Check for command-line argument
        string[] args = Environment.GetCommandLineArgs();
        bool hasGymServerArg = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-gym-server" || args[i] == "--gym-server")
            {
                enableGymServer = true;
                hasGymServerArg = true;
                Debug.Log("[GymServer] Gym server enabled via command-line");
            }
            if (args[i] == "-gym-port" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int port))
                {
                    gymServerPort = port;
                    Debug.Log($"[GymServer] Port set to {port} via command-line");
                }
            }
        }

        // Auto-enable in batch mode ONLY if explicitly requested OR WebSocketManager is disabled
        if (Application.isBatchMode && !hasGymServerArg)
        {
            // Check if WebSocketManager is active
            if (WebSocketManager.Instance != null && WebSocketManager.Instance.enableWebSocket)
            {
                Debug.Log("[GymServer] WebSocketManager is active, gym server disabled by default");
                Debug.Log("[GymServer] Use -gym-server flag to enable gym server mode");
                enableGymServer = false;
            }
            else
            {
                Debug.Log("[GymServer] Running in batch mode, enabling gym server");
                enableGymServer = true;
            }
        }

        if (enableGymServer)
        {
            StartServer();
        }
    }

    void Update()
    {
        // Execute queued actions on main thread
        lock (actionQueueLock)
        {
            while (mainThreadActions.Count > 0)
            {
                try
                {
                    mainThreadActions.Dequeue()?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] Error executing main thread action: {e}");
                }
            }
        }
    }

    void StartServer()
    {
        try
        {
            tcpListener = new TcpListener(IPAddress.Any, gymServerPort);
            tcpListener.Start();
            isListening = true;

            listenerThread = new Thread(ListenForClients);
            listenerThread.IsBackground = true;
            listenerThread.Start();

            Debug.Log($"[GymServer] ✅ Gym server listening on port {gymServerPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GymServer] Failed to start server: {e}");
            isListening = false;
        }
    }

    void ListenForClients()
    {
        try
        {
            while (isListening)
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                lock (clients)
                {
                    clients.Add(client);
                    connectedClients = clients.Count;
                }

                Debug.Log($"[GymServer] Client connected from {client.Client.RemoteEndPoint}");

                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.IsBackground = true;
                clientThread.Start();
            }
        }
        catch (SocketException e)
        {
            if (isListening)
            {
                Debug.LogError($"[GymServer] Listener error: {e}");
            }
        }
    }

    void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[65536]; // 64KB buffer

        try
        {
            while (client.Connected && isListening)
            {
                if (stream.DataAvailable)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                        // Handle message and send response
                        string response = HandleMessage(message);

                        if (!string.IsNullOrEmpty(response))
                        {
                            byte[] responseBytes = Encoding.UTF8.GetBytes(response + "\n");
                            stream.Write(responseBytes, 0, responseBytes.Length);
                            stream.Flush();
                        }
                    }
                }
                else
                {
                    Thread.Sleep(10); // Small delay to prevent busy-waiting
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GymServer] Client disconnected: {e.Message}");
        }
        finally
        {
            lock (clients)
            {
                clients.Remove(client);
                connectedClients = clients.Count;
            }
            client.Close();
            Debug.Log($"[GymServer] Client disconnected, active clients: {connectedClients}");
        }
    }

    // Build an error response WITHOUT JsonUtility, so it is safe to call from the
    // client thread. JsonUtility.ToJson is NOT thread-safe: serializing an error on
    // the client thread (e.g. a timeout fallback) can run concurrently with a late
    // main-thread ToJson and corrupt Unity's shared native serialization state — a
    // hard native crash. All client-thread responses go through here instead.
    static string ErrorJson(string error)
    {
        return "{\"type\":\"error\",\"error\":\"" + EscapeJson(error ?? "") + "\"}";
    }

    static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    string HandleMessage(string message)
    {
        try
        {
            GymRequest request = JsonUtility.FromJson<GymRequest>(message);

            if (request == null || string.IsNullOrEmpty(request.type))
            {
                return ErrorJson("Invalid request: missing type");
            }

            switch (request.type)
            {
                case "get_game_state":
                    return HandleGetGameState();

                case "execute_action":
                    return HandleExecuteAction(request);

                case "advance_time":
                    return HandleAdvanceTime();

                case "select_task_choice":
                    return HandleSelectTaskChoice(request);

                default:
                    return ErrorJson($"Unknown request type: {request.type}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GymServer] Error handling message: {e}");
            return ErrorJson(e.Message);
        }
    }

    string HandleGetGameState()
    {
        // Must execute on main thread
        string result = null;
        bool completed = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    if (TaskSystem.Instance != null)
                    {
                        GameStatePayload gameState = TaskSystem.Instance.GetCurrentGameState();
                        GymResponse response = new GymResponse
                        {
                            type = "game_state",
                            game_state = JsonUtility.ToJson(gameState)
                        };
                        result = JsonUtility.ToJson(response);
                    }
                    else
                    {
                        result = JsonUtility.ToJson(new GymResponse
                        {
                            type = "error",
                            error = "TaskSystem not found"
                        });
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] Error getting game state: {e}");
                    result = JsonUtility.ToJson(new GymResponse
                    {
                        type = "error",
                        error = e.Message
                    });
                }
                finally
                {
                    completed = true;
                }
            });
        }

        // Wait for main thread to execute (with timeout)
        int timeout = 0;
        while (!completed && timeout < 1000) // 10 second timeout
        {
            Thread.Sleep(10);
            timeout++;
        }

        return result ?? ErrorJson("Timeout waiting for game state");
    }

    string HandleExecuteAction(GymRequest request)
    {
        if (request.action == null)
        {
            return JsonUtility.ToJson(new GymResponse
            {
                type = "error",
                error = "Missing action in request"
            });
        }

        // Must execute on main thread
        string result = null;
        bool completed = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    if (ActionExecutor.Instance != null)
                    {
                        GameAction action = JsonUtility.FromJson<GameAction>(request.action);
                        ActionExecutionResult execResult = ActionExecutor.Instance.ExecuteAction(action);

                        GymResponse response = new GymResponse
                        {
                            type = "action_result",
                            success = execResult.success,
                            error = execResult.success ? null : execResult.error_message
                        };
                        result = JsonUtility.ToJson(response);
                    }
                    else
                    {
                        result = JsonUtility.ToJson(new GymResponse
                        {
                            type = "error",
                            error = "ActionExecutor not found"
                        });
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] Error executing action: {e}");
                    result = JsonUtility.ToJson(new GymResponse
                    {
                        type = "error",
                        error = e.Message
                    });
                }
                finally
                {
                    completed = true;
                }
            });
        }

        // Wait for main thread to execute (with timeout)
        int timeout = 0;
        while (!completed && timeout < 500) // 5 second timeout
        {
            Thread.Sleep(10);
            timeout++;
        }

        return result ?? ErrorJson("Timeout waiting for action execution");
    }

    // Select a choice on an active task (the decision lever for choice tasks)
    // and complete it via the same logic the UI uses.
    string HandleSelectTaskChoice(GymRequest request)
    {
        string result = null;
        bool completed = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    TaskDetailUI ui = FindObjectOfType<TaskDetailUI>();
                    if (ui == null)
                    {
                        result = JsonUtility.ToJson(new GymResponse { type = "error", error = "TaskDetailUI not found" });
                    }
                    else
                    {
                        bool ok = ui.SelectTaskChoiceHeadless(request.taskId, request.choiceId);
                        result = JsonUtility.ToJson(new GymResponse
                        {
                            type = "action_result",
                            success = ok,
                            error = ok ? null : $"Task {request.taskId} or choice {request.choiceId} not found"
                        });
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] Error selecting task choice: {e}");
                    result = JsonUtility.ToJson(new GymResponse { type = "error", error = e.Message });
                }
                finally { completed = true; }
            });
        }

        int timeout = 0;
        while (!completed && timeout < 500) { Thread.Sleep(10); timeout++; }
        return result ?? ErrorJson("Timeout selecting task choice");
    }

    // Advance the simulation by exactly one round (time segment), running the
    // real dynamics decoupled from wall-clock (see GlobalClock.GymAdvanceRound).
    // Blocks until the round completes, then returns the updated game state.
    string HandleAdvanceTime()
    {
        if (GlobalClock.Instance == null)
        {
            return JsonUtility.ToJson(new GymResponse { type = "error", error = "GlobalClock not found" });
        }

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() => GlobalClock.Instance.GymAdvanceRound());
        }

        // Wait for the round to actually start, then finish. With captureDeltaTime
        // the window runs as fast as the CPU renders frames (sub-second), but allow
        // generous headroom in case of slow hardware.
        bool sawRunning = false;
        int ticks = 0;
        while (ticks < 6000) // up to 60s safety cap
        {
            bool running = GlobalClock.Instance.IsSimulationRunning();
            if (running) sawRunning = true;
            else if (sawRunning) break; // started then ended -> round complete
            Thread.Sleep(10);
            ticks++;
        }

        return HandleGetGameState();
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    void OnDestroy()
    {
        StopServer();
    }

    void StopServer()
    {
        isListening = false;

        if (tcpListener != null)
        {
            tcpListener.Stop();
        }

        lock (clients)
        {
            foreach (TcpClient client in clients)
            {
                try { client.Close(); } catch { }
            }
            clients.Clear();
            connectedClients = 0;
        }

        if (listenerThread != null && listenerThread.IsAlive)
        {
            listenerThread.Join(1000); // Wait up to 1 second
        }

        Debug.Log("[GymServer] Server stopped");
    }
}

// ============================================================================
// Message Classes
// ============================================================================

[Serializable]
public class GymRequest
{
    public string type;          // "get_game_state" | "execute_action" | "advance_time" | "select_task_choice"
    public string action;        // JSON string of GameAction (for execute_action)
    public int taskId = -1;      // for select_task_choice
    public int choiceId = -1;    // for select_task_choice
}

[Serializable]
public class GymResponse
{
    public string type;          // "game_state", "action_result", or "error"
    public string game_state;    // JSON string of GameStatePayload
    public bool success;         // Action execution result
    public string error;         // Error message if any
}
