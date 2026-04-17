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

    string HandleMessage(string message)
    {
        try
        {
            GymRequest request = JsonUtility.FromJson<GymRequest>(message);

            if (request == null || string.IsNullOrEmpty(request.type))
            {
                return JsonUtility.ToJson(new GymResponse
                {
                    type = "error",
                    error = "Invalid request: missing type"
                });
            }

            switch (request.type)
            {
                case "get_game_state":
                    return HandleGetGameState();

                case "execute_action":
                    return HandleExecuteAction(request);

                default:
                    return JsonUtility.ToJson(new GymResponse
                    {
                        type = "error",
                        error = $"Unknown request type: {request.type}"
                    });
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GymServer] Error handling message: {e}");
            return JsonUtility.ToJson(new GymResponse
            {
                type = "error",
                error = e.Message
            });
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

        return result ?? JsonUtility.ToJson(new GymResponse
        {
            type = "error",
            error = "Timeout waiting for game state"
        });
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

        return result ?? JsonUtility.ToJson(new GymResponse
        {
            type = "error",
            error = "Timeout waiting for action execution"
        });
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
    public string type;          // "get_game_state" or "execute_action"
    public string action;        // JSON string of GameAction (for execute_action)
}

[Serializable]
public class GymResponse
{
    public string type;          // "game_state", "action_result", or "error"
    public string game_state;    // JSON string of GameStatePayload
    public bool success;         // Action execution result
    public string error;         // Error message if any
}
