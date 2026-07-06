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

    // Whether enabled frame captures should also return base64 in the response
    // (set via configure_render). Path-only by default — cheaper over TCP.
    private bool renderIncludeBase64 = false;

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

                case "configure_render":
                    return HandleConfigureRender(request);

                case "pathfind_matrix":
                    return HandlePathfindMatrix();

                case "map_grid":
                    return HandleMapGrid();

                case "capture_frame":
                    return HandleCaptureFrame();

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
                        bool ok = ui.SelectTaskChoiceHeadless(request.taskId, request.choiceId, out failReason);
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
