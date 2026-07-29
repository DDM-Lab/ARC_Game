using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;
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

    // Whether enabled frame captures should also return base64 in the response
    // (set via configure_render). Path-only by default — cheaper over TCP.
    private bool renderIncludeBase64 = false;

    // reset_game handshake: cleared when a reset is requested, flipped true by the
    // reset coroutine once MainScene has fully reloaded to a fresh Day 1. Written on
    // the main thread, polled on the client thread → volatile.
    private volatile bool resetComplete = false;

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

            // Cap the frame rate up front so the headless loop sleeps (~1% CPU) instead
            // of busy-spinning at ~50-67% of a core before the first round runs. The
            // gym round itself (GlobalClock.GymAdvanceRound) uncaps for speed and
            // EndSimulation restores this cap afterwards.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 10;

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

                // Single-client enforcement. This server is designed for exactly one gym
                // client (one env : one Unity process). A SECOND concurrent connection is
                // never legitimate — it means a wrapper double-spawn / port mix-up landed
                // two envs on the same Unity. It is also actively dangerous: every request
                // is parsed with JsonUtility.FromJson on its own client thread, and Unity's
                // JsonUtility is NOT thread-safe (the same reason ErrorJson is hand-rolled).
                // Two client threads serializing concurrently can corrupt Unity's native
                // serialization state and hard-crash the process mid-response — which looks
                // exactly like the observed silent death (log ends mid-state-serialization,
                // the gym's env.step() then blocks forever on a dead socket). So reject the
                // extra connection loudly instead of letting it crash the server.
                bool alreadyServing;
                lock (clients)
                {
                    PruneDeadClients();
                    alreadyServing = clients.Count > 0;
                    if (!alreadyServing)
                    {
                        clients.Add(client);
                        connectedClients = clients.Count;
                    }
                }

                if (alreadyServing)
                {
                    string who = SafeRemoteEndpoint(client);
                    Debug.LogWarning($"[GymServer] REJECTING second client from {who}: this server " +
                                     $"already has an active client. This is almost certainly a wrapper " +
                                     $"double-spawn (two envs → one Unity). Close it; keeping the first.");
                    try { client.Close(); } catch { }
                    continue;
                }

                Debug.Log($"[GymServer] Client connected from {SafeRemoteEndpoint(client)}");

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

    // Drop any clients whose socket is no longer connected, so a client that
    // disconnected ungracefully (half-open) does not permanently block a legitimate
    // reconnect under single-client enforcement. Caller must hold lock(clients).
    void PruneDeadClients()
    {
        for (int i = clients.Count - 1; i >= 0; i--)
        {
            TcpClient c = clients[i];
            if (c == null || !c.Connected)
            {
                try { c?.Close(); } catch { }
                clients.RemoveAt(i);
            }
        }
        connectedClients = clients.Count;
    }

    static string SafeRemoteEndpoint(TcpClient client)
    {
        try { return client.Client.RemoteEndPoint?.ToString() ?? "unknown"; }
        catch { return "unknown"; }
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
                        string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        // Newline framing. The protocol is newline-delimited JSON, but TCP
                        // does not preserve message boundaries: one stream.Read can return
                        // several coalesced messages ("{...}\n{...}"). Parsing the raw chunk
                        // as a single JSON object then throws "JSON parse error: Invalid
                        // value" (seen at startup when the wrapper sends its handshake + first
                        // command back-to-back). Split on newlines and dispatch each message.
                        // If the chunk carries no newline at all, fall back to the original
                        // whole-chunk behavior — strictly no worse than before, and never
                        // buffers/blocks waiting for a delimiter a client might not send.
                        string[] messages = chunk.IndexOf('\n') >= 0
                            ? chunk.Split('\n')
                            : new[] { chunk };

                        foreach (string raw in messages)
                        {
                            string message = raw.Trim();
                            if (message.Length == 0) continue; // skip blank/keepalive lines

                            string response = HandleMessage(message);
                            if (!string.IsNullOrEmpty(response))
                            {
                                byte[] responseBytes = Encoding.UTF8.GetBytes(response + "\n");
                                stream.Write(responseBytes, 0, responseBytes.Length);
                                stream.Flush();
                            }
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

                case "configure_render":
                    return HandleConfigureRender(request);

                case "pathfind_matrix":
                    return HandlePathfindMatrix();

                case "map_grid":
                    return HandleMapGrid();

                case "capture_frame":
                    return HandleCaptureFrame();

                case "reset_game":
                    return HandleResetGame();

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
                        // Ensure the configured initial budget/satisfaction has been applied
                        // before the first observation, so it never reports the stale default.
                        SatisfactionAndBudget.Instance?.EnsureConfigApplied();
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
                        string failReason;
                        bool ok = ui.SelectTaskChoiceHeadless(request.taskId, request.choiceId, request.stableId, out failReason);
                        result = JsonUtility.ToJson(new GymResponse
                        {
                            type = "action_result",
                            success = ok,
                            error = ok ? null : failReason
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

    // Configure (or disable) camera frame capture. Runs on the main thread because
    // it touches Unity GPU objects (RenderTexture). Default is Off, so this is only
    // ever invoked when the gym client explicitly opts in.
    string HandleConfigureRender(GymRequest request)
    {
        string result = null;
        bool completed = false;

        renderIncludeBase64 = request.renderIncludeBase64;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    GymCameraCapture cap = EnsureCaptureComponent();
                    GymCameraCapture.CaptureMode m = ParseCaptureMode(request.renderMode);
                    cap.Configure(m, request.renderWidth, request.renderHeight, request.renderDir);
                    result = JsonUtility.ToJson(new GymResponse { type = "action_result", success = true });
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] Error configuring render: {e}");
                    result = JsonUtility.ToJson(new GymResponse { type = "error", error = e.Message });
                }
                finally { completed = true; }
            });
        }

        int timeout = 0;
        while (!completed && timeout < 500) { Thread.Sleep(10); timeout++; }
        return result ?? ErrorJson("Timeout configuring render");
    }

    // ── Debug / analysis: pathfinding distance matrix ───────────────────────────
    // Computes the real A*-over-roads delivery estimate (road distance + seconds +
    // flood-blocked) between every pair of routing-relevant nodes: available build
    // sites, communities, the motel, and any built facilities. Used offline to
    // quantify how much site LOCATION matters (road distance -> delivery rounds) and
    // to annotate the synthetic map. Not used on the hot RL path.
    string HandlePathfindMatrix()
    {
        string result = null;
        bool completed = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try { result = BuildPathfindMatrixJson(); }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] pathfind_matrix failed: {e}");
                    result = ErrorJson(e.Message);
                }
                finally { completed = true; }
            });
        }

        // Up to ~30s: an N-node matrix is N*(N-1) A* runs (N~20 -> ~380 paths).
        int timeout = 0;
        while (!completed && timeout < 3000) { Thread.Sleep(10); timeout++; }
        return result ?? ErrorJson("Timeout computing pathfind matrix");
    }

    string BuildPathfindMatrixJson()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;

        PathfindingSystem pf = FindObjectOfType<PathfindingSystem>();
        if (pf == null) return ErrorJson("PathfindingSystem not found");

        // Build the node list: sites (buildable), prebuilt buildings (communities +
        // motel), and any built facilities. Each node = (id, kind, name, position).
        var ids = new List<string>();
        var kinds = new List<string>();
        var names = new List<string>();
        var siteIds = new List<int>();
        var positions = new List<Vector3>();

        foreach (AbandonedSite site in FindObjectsOfType<AbandonedSite>())
        {
            if (!site.IsAvailable()) continue;
            ids.Add($"site:{site.GetId()}");
            kinds.Add("site");
            names.Add(site.name);
            siteIds.Add(site.GetId());
            positions.Add(site.transform.position);
        }
        foreach (PrebuiltBuilding pb in FindObjectsOfType<PrebuiltBuilding>())
        {
            ids.Add($"prebuilt:{pb.name}");
            kinds.Add(pb.GetPrebuiltType().ToString());
            names.Add(pb.name);
            siteIds.Add(-1);
            positions.Add(pb.transform.position);
        }
        foreach (Building b in FindObjectsOfType<Building>())
        {
            ids.Add($"building:{b.name}");
            kinds.Add(b.GetBuildingType().ToString());
            names.Add(b.name);
            siteIds.Add(b.GetOriginalSiteId());
            positions.Add(b.transform.position);
        }

        int n = positions.Count;
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"pathfind_matrix\",\"nodes\":[");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(',');
            Vector3 p = positions[i];
            sb.Append("{\"idx\":").Append(i)
              .Append(",\"id\":\"").Append(EscapeJson(ids[i]))
              .Append("\",\"kind\":\"").Append(EscapeJson(kinds[i]))
              .Append("\",\"name\":\"").Append(EscapeJson(names[i]))
              .Append("\",\"site_id\":").Append(siteIds[i])
              .Append(",\"x\":").Append(p.x.ToString("F3", ci))
              .Append(",\"y\":").Append(p.y.ToString("F3", ci))
              .Append('}');
        }
        sb.Append("],\"edges\":[");
        bool first = true;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                DeliveryTimeEstimate est = pf.EstimateDeliveryTime(positions[i], positions[j]);
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"i\":").Append(i).Append(",\"j\":").Append(j)
                  .Append(",\"dist\":").Append(est.totalDistance.ToString("F3", ci))
                  .Append(",\"sec\":").Append(est.estimatedTimeSeconds.ToString("F3", ci))
                  .Append(",\"tiles\":").Append(est.roadTileCount)
                  .Append(",\"path\":").Append(est.pathExists ? "true" : "false")
                  .Append(",\"flood\":").Append(est.isFloodBlocked ? "true" : "false")
                  .Append('}');
            }
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // ── map_grid: export the static terrain as a tile-accurate char grid ──────
    // Lets the synthetic renderer draw grass / rivers / roads / forests / flood
    // on the REAL tile lattice (not invented decoration), plus the world-rect so
    // world-space facility/site positions overlay on the same axes.
    string HandleMapGrid()
    {
        string result = null;
        bool completed = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try { result = BuildMapGridJson(); }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] map_grid failed: {e}");
                    result = ErrorJson(e.Message);
                }
                finally { completed = true; }
            });
        }

        int timeout = 0;
        while (!completed && timeout < 1000) { Thread.Sleep(10); timeout++; }
        return result ?? ErrorJson("Timeout building map grid");
    }

    string BuildMapGridJson()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;

        FloodSystem fs = FindObjectOfType<FloodSystem>();
        if (fs == null || fs.groundTilemap == null)
            return ErrorJson("FloodSystem / groundTilemap not found");
        RoadTilemapManager rm = FindObjectOfType<RoadTilemapManager>();

        var ground = fs.groundTilemap;                                  // grass + river
        var flood  = fs.floodTilemap;                                   // dynamic water
        var block  = fs.terrainBlockingTilemap;                         // forest / mountain
        var river  = fs.riverRuleTile;
        var road   = rm != null ? rm.roadTilemap : null;

        // groundTilemap spans the whole playable map; road/blocking/flood share the
        // same Grid so their cell coords align and GetTile(c) is valid at any cell.
        BoundsInt b = ground.cellBounds;
        int xMin = b.xMin, yMin = b.yMin, xMax = b.xMax, yMax = b.yMax;
        int W = xMax - xMin, H = yMax - yMin;

        Vector3 wMin = ground.CellToWorld(new Vector3Int(xMin, yMin, 0)); // lower-left corner
        Vector3 wMax = ground.CellToWorld(new Vector3Int(xMax, yMax, 0)); // upper-right corner
        Vector3 cs = ground.cellSize;

        var sb = new StringBuilder();
        sb.Append("{\"type\":\"map_grid\"");
        sb.Append(",\"bounds\":{\"xMin\":").Append(xMin).Append(",\"yMin\":").Append(yMin)
          .Append(",\"width\":").Append(W).Append(",\"height\":").Append(H).Append('}');
        sb.Append(",\"cellSize\":{\"x\":").Append(cs.x.ToString("F3", ci))
          .Append(",\"y\":").Append(cs.y.ToString("F3", ci)).Append('}');
        sb.Append(",\"worldRect\":{\"left\":").Append(wMin.x.ToString("F3", ci))
          .Append(",\"bottom\":").Append(wMin.y.ToString("F3", ci))
          .Append(",\"right\":").Append(wMax.x.ToString("F3", ci))
          .Append(",\"top\":").Append(wMax.y.ToString("F3", ci)).Append('}');
        sb.Append(",\"legend\":{\"g\":\"ground\",\"r\":\"river\",\"R\":\"road\",")
          .Append("\"b\":\"blocking\",\"f\":\"flood\",\".\":\"empty\"}");

        // rows top -> bottom (row 0 = y=yMax-1) so it maps directly onto an
        // origin='upper' imshow with extent = worldRect.
        sb.Append(",\"rows\":[");
        for (int y = yMax - 1; y >= yMin; y--)
        {
            if (y < yMax - 1) sb.Append(',');
            var row = new StringBuilder(W);
            for (int x = xMin; x < xMax; x++)
            {
                var c = new Vector3Int(x, y, 0);
                char code;
                if (flood != null && flood.GetTile(c) != null) code = 'f';
                else if (road != null && road.GetTile(c) != null) code = 'R';
                else if (block != null && block.GetTile(c) != null) code = 'b';
                else
                {
                    var gt = ground.GetTile(c);
                    if (gt == null) code = '.';
                    else if (river != null && gt == river) code = 'r';
                    else code = 'g';
                }
                row.Append(code);
            }
            sb.Append('"').Append(row).Append('"');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // ── capture_frame: grab the CURRENT camera view without advancing time ────
    // Lets a client fetch a decision-time frame (the synthetic renderer reads
    // game_state at decision time; this gives the real-image arm the same timing).
    // Requires render capture to be configured (frame_capture != off); returns an
    // error otherwise. PerStep mode always captures, so this never hits the
    // PerGameTime time-gate.
    string HandleCaptureFrame()
    {
        string framePath = null, frameB64 = null;
        bool captured = false, completed = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    GymCameraCapture cap = GymCameraCapture.Instance;
                    if (cap != null && cap.mode != GymCameraCapture.CaptureMode.Off)
                    {
                        int day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 0;
                        int seg = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 0;
                        if (cap.CaptureFrame(day, seg, true))
                        {
                            framePath = cap.LastFramePath;
                            frameB64 = cap.LastFrameBase64;
                            captured = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GymServer] capture_frame failed: {e.Message}");
                }
                finally { completed = true; }
            });
        }

        int timeout = 0;
        while (!completed && timeout < 1000) { Thread.Sleep(10); timeout++; }
        if (!captured)
            return ErrorJson("No frame captured (render capture not configured?)");

        var sb = new StringBuilder();
        sb.Append("{\"type\":\"frame\",\"frame_path\":")
          .Append(framePath != null ? "\"" + EscapeJson(framePath) + "\"" : "null")
          .Append(",\"frame_base64\":")
          .Append(frameB64 != null ? "\"" + frameB64 + "\"" : "null")
          .Append('}');
        return sb.ToString();
    }

    // Find or create the capture component. The MainScene has no GymCameraCapture
    // by default, so create one lazily on the GymServerManager GameObject; it stays
    // dormant (mode Off) until Configure() turns it on.
    GymCameraCapture EnsureCaptureComponent()
    {
        if (GymCameraCapture.Instance != null) return GymCameraCapture.Instance;
        var go = new GameObject("GymCameraCapture");
        return go.AddComponent<GymCameraCapture>();
    }

    static GymCameraCapture.CaptureMode ParseCaptureMode(string s)
    {
        switch ((s ?? "off").ToLowerInvariant())
        {
            case "step": return GymCameraCapture.CaptureMode.PerStep;
            case "game_time": return GymCameraCapture.CaptureMode.PerGameTime;
            default: return GymCameraCapture.CaptureMode.Off;
        }
    }

    // Capture a frame (no-op unless enabled) on the main thread, then return the
    // standard game_state response augmented with frame_path/frame_base64. This is
    // a thin wrapper over HandleGetGameState used at the end of an advance.
    string HandleGetGameStateWithFrame()
    {
        // First capture (main thread, gated by mode); then build the state response
        // and splice in the frame fields. Capture is enqueued and waited on like the
        // other main-thread ops so GPU work never runs on the client thread.
        string framePath = null;
        string frameB64 = null;
        bool captured = false;
        bool completed = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    GymCameraCapture cap = GymCameraCapture.Instance;
                    if (cap != null && cap.mode != GymCameraCapture.CaptureMode.Off)
                    {
                        int day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 0;
                        int seg = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 0;
                        if (cap.CaptureFrame(day, seg, renderIncludeBase64))
                        {
                            framePath = cap.LastFramePath;
                            frameB64 = cap.LastFrameBase64;
                            captured = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GymServer] Frame capture failed (continuing): {e.Message}");
                }
                finally { completed = true; }
            });
        }

        int timeout = 0;
        while (!completed && timeout < 1000) { Thread.Sleep(10); timeout++; }

        // Build the normal game_state response, then re-serialize with frame fields.
        string stateJson = HandleGetGameState();
        if (!captured) return stateJson;

        try
        {
            GymResponse resp = JsonUtility.FromJson<GymResponse>(stateJson);
            if (resp != null && resp.type == "game_state")
            {
                resp.frame_path = framePath;
                resp.frame_base64 = frameB64;
                return JsonUtility.ToJson(resp);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GymServer] Could not attach frame fields: {e.Message}");
        }
        return stateJson;
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

        // Finite-horizon guard. Once the last round of finalDay has run, the episode is
        // over. GlobalClock.GymAdvanceRound()/StartSimulation() have NO day cap, so a
        // wrapper that keeps calling advance_time past the terminal marches the game into
        // "overtime" (Day finalDay+1, +2, ...) indefinitely. Each overtime round is a
        // clean Start/Stop, so Unity never deadlocks — but a wrapper whose terminal check
        // is `day > finalDay` (strict) silently overruns exactly one round into Day
        // finalDay+1 before it stops stepping, and with no reset wired Unity then idles
        // between rounds at GYM_IDLE_FPS. That is the "mid-Day-9 freeze" observed on the
        // cluster: not a hang, just Unity correctly waiting for a command that never comes.
        //
        // Fix: refuse to advance once terminal. Return the current state re-tagged
        // type="game_over" (the embedded game_state still carries sessionInfo.isGameOver,
        // so this is backward compatible) — an unambiguous, idempotent terminal the
        // wrapper resets on regardless of how it detects the horizon.
        if (IsFiniteHorizonOver(out int goDay, out int goSeg, out int goFinalDay))
        {
            Debug.Log($"[GymServer] advance_time at finite-horizon terminal " +
                      $"(Day {goDay} R{goSeg + 1}, finalDay {goFinalDay}); returning game_over " +
                      $"WITHOUT advancing the clock (no overtime).");
            return GameOverJson();
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

        // Capture a frame (no-op unless render capture was enabled) and return the
        // post-advance state. Falls through to a plain game_state when capture is Off.
        return HandleGetGameStateWithFrame();
    }

    // Finite-horizon terminal test. Mirrors TaskSystem.GetSessionInfo's isGameOver
    // (currentDay > finalDay || (currentDay == finalDay && currentRound >= 4)) so the
    // gym server can refuse to advance the clock past the last round of finalDay.
    // currentRound here is GlobalClock.GetCurrentTimeSegment() (0-based; it reads 4 only
    // after the day's 4th round has completed) — identical to what GetSessionInfo uses.
    bool IsFiniteHorizonOver(out int day, out int seg, out int finalDay)
    {
        day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 0;
        seg = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 0;
        finalDay = DailyReportManager.Instance != null ? DailyReportManager.Instance.finalDay : 8;
        return day > finalDay || (day == finalDay && seg >= 4);
    }

    // Terminal response: the full current game_state payload, re-tagged type="game_over".
    // Backward compatible — the embedded game_state field is unchanged (and already
    // carries sessionInfo.isGameOver == true); only the envelope type differs from
    // "game_state". Runs the FromJson/ToJson on the client thread only AFTER
    // HandleGetGameState() (which marshals to and blocks on the main thread) has fully
    // returned, so there is no concurrent-JsonUtility hazard — same pattern as
    // HandleGetGameStateWithFrame's frame-field splice.
    string GameOverJson()
    {
        string stateJson = HandleGetGameState();
        try
        {
            GymResponse resp = JsonUtility.FromJson<GymResponse>(stateJson);
            if (resp != null && resp.type == "game_state")
            {
                resp.type = "game_over";
                return JsonUtility.ToJson(resp);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GymServer] GameOverJson re-tag failed ({e.Message}); returning plain state.");
        }
        return stateJson;
    }

    // Reset the game to a fresh Day-1 state IN-PROCESS, without restarting Unity. RL
    // loops that recycle one Unity process across many episodes
    // (restart_unity_each_episode=False) need this; the benchmark spawns a fresh
    // process per episode and never hits it.
    //
    // All game-state singletons are scene-placed in MainScene AND DontDestroyOnLoad, so
    // a plain LoadScene would reload the scene but the fresh copies would self-destruct
    // against the surviving old singletons (their Awake sees Instance != null). So we
    // (1) destroy the old game-state DontDestroyOnLoad objects first — keeping only the
    // gym/network/logging infra, which owns this coroutine + the live TCP socket and
    // must survive — then (2) LoadSceneAsync(MainScene, Single) to rebuild the
    // game-state singletons fresh at Day 1. Infra created via RuntimeInitializeOnLoad
    // (RewardMetricsTracker/GymCameraCapture/ServerLauncherUI/GuiInteractionRecorder) is
    // NOT recreated by a scene reload, so it is preserved; RewardMetricsTracker is
    // additionally zeroed, else its cumulative accumulators leak across episodes.
    string HandleResetGame()
    {
        resetComplete = false;

        lock (actionQueueLock)
        {
            mainThreadActions.Enqueue(() =>
            {
                try
                {
                    StartCoroutine(ResetRoutine());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GymServer] reset_game failed to start: {e}");
                    resetComplete = true; // unblock the client thread
                }
            });
        }

        // Scene reload + a few settle frames is well under a second, but allow generous
        // headroom on slow/headless hardware (same 60s-class cap style as advance_time).
        int ticks = 0;
        while (!resetComplete && ticks < 3000) // up to 30s safety cap
        {
            Thread.Sleep(10);
            ticks++;
        }

        if (!resetComplete)
            return ErrorJson("reset_game timed out waiting for scene reload");

        return "{\"type\":\"reset_done\"}";
    }

    // GameObjects that must survive a reset: this manager (coroutine + TCP socket),
    // network/logging infra, and GameConfigLoader (KEEP its cached config so reset is
    // fast and needs no network re-fetch). Resolved via FindObjectOfType so absent
    // infra (e.g. GuiInteractionRecorder is skipped in batch mode; GymCameraCapture
    // only exists once render capture is configured) is simply not added — null-safe.
    HashSet<GameObject> BuildResetPreserveSet()
    {
        var keep = new HashSet<GameObject>();
        keep.Add(gameObject); // this GymServerManager
        KeepIfPresent<WebSocketManager>(keep);
        KeepIfPresent<GameConfigLoader>(keep);
        KeepIfPresent<GymCameraCapture>(keep);
        KeepIfPresent<ServerLauncherUI>(keep);
        KeepIfPresent<GuiInteractionRecorder>(keep);
        KeepIfPresent<RewardMetricsTracker>(keep);
        return keep;
    }

    void KeepIfPresent<T>(HashSet<GameObject> keep) where T : Component
    {
        var obj = FindObjectOfType<T>();
        if (obj != null) keep.Add(obj.gameObject);
    }

    IEnumerator ResetRoutine()
    {
        Scene active = SceneManager.GetActiveScene();
        int buildIndex = active.buildIndex;

        // Preserve infra; zero the reward accumulators that survive the reload; drop
        // cached scene refs on preserved infra so they re-resolve after the reload.
        HashSet<GameObject> keep = BuildResetPreserveSet();
        RewardMetricsTracker.Instance?.ResetForNewEpisode();
        WebSocketManager.Instance?.ClearSceneRefs();

        // Destroy every DontDestroyOnLoad game-state root not in the keep set. These
        // live in Unity's dedicated DontDestroyOnLoad scene; enumerate it via a temp
        // object placed there.
        var probe = new GameObject("[ResetProbe]");
        DontDestroyOnLoad(probe);
        keep.Add(probe);
        Scene ddol = probe.scene;
        var destroyedNames = new List<string>();
        var keptNames = new List<string>();
        foreach (GameObject root in ddol.GetRootGameObjects())
        {
            if (!keep.Contains(root))
            {
                destroyedNames.Add(root.name);
                Destroy(root);
            }
            else if (root != probe)
            {
                keptNames.Add(root.name);
            }
        }
        Destroy(probe);
        Debug.Log($"[GymServer] reset teardown — destroyed DDOL roots: [{string.Join(", ", destroyedNames)}]; " +
                  $"kept: [{string.Join(", ", keptNames)}]");

        // Let end-of-frame destruction actually run so the reloaded scene's fresh
        // singletons see Instance == null (Unity reports destroyed objects as null in
        // the == overload) and claim the slot instead of self-destructing.
        yield return null;

        // Rebuild MainScene from scratch → fresh Day-1 game-state singletons.
        AsyncOperation op = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
        while (op != null && !op.isDone)
            yield return null;

        // Give the reloaded scene's Awake/Start chain a couple frames to re-establish
        // Day 1 (GlobalClock.InitializeTimeSystem) and re-apply cached config
        // (SatisfactionAndBudget.EnsureConfigApplied on its fresh ConfigApplied=false).
        yield return null;
        yield return null;

        // ActionExecutor persists across the reload (it shares the preserved
        // DontDestroyOnLoad "WebSocketManager" GameObject), so its serialized system
        // refs still point at the destroyed old scene. Re-point them at the fresh
        // BuildingSystem/WorkerSystem/DeliverySystem now that the reload has settled.
        ActionExecutor.Instance?.ReresolveSceneRefs();

        int day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : -1;
        Debug.Log($"[GymServer] reset_game complete — reloaded MainScene (build {buildIndex}); now Day {day}.");
        resetComplete = true;
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
    public string type;          // "get_game_state" | "execute_action" | "advance_time" | "select_task_choice" | "configure_render"
    public string action;        // JSON string of GameAction (for execute_action)
    public int taskId = -1;      // for select_task_choice
    public int choiceId = -1;    // for select_task_choice
    public string stableId;      // for select_task_choice: stable cross-regeneration task id (optional fallback)
    // ── configure_render fields (camera frame capture; default off) ──
    public string renderMode;        // "off" | "step" | "game_time"
    public int renderWidth = 0;      // 0 => keep component default
    public int renderHeight = 0;     // 0 => keep component default
    public string renderDir;         // output directory for PNGs (null => default)
    public bool renderIncludeBase64; // also return base64 PNG in the response
}

[Serializable]
public class GymResponse
{
    public string type;          // "game_state", "action_result", or "error"
    public string game_state;    // JSON string of GameStatePayload
    public bool success;         // Action execution result
    public string error;         // Error message if any
    // ── Frame capture results (populated only when render capture is enabled) ──
    public string frame_path;    // absolute path to the PNG written this step (or null)
    public string frame_base64;  // base64 PNG if requested (or null)
}
