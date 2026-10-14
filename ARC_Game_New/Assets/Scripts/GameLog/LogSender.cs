using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Uploads the game log to save_game_logs.py: a cumulative checkpoint at the end of Days 1-7 and
/// the final upload on Day 8 (see DailyReportManager), plus the debug panel's manual send.
///
/// One upload job runs at a time. A job snapshots the log once, when it is requested (everything
/// logged before the request is in it; DailyReportManager depends on this), and resends those same
/// bytes on every retry, so the server recognises a resend of an upload it already saved and
/// answers "duplicate" instead of saving and processing it again.
///
/// Each attempt
///   • posts the JSON gzipped: in WebGL builds through CoraUpload.jslib (the browser compresses it
///     and reports real upload progress), elsewhere through UnityWebRequest and GZipStream;
///   • is abandoned when nothing has moved for requestTimeout seconds (no upload progress while
///     sending, or no reply once the body is sent), or after maxAttemptSeconds in total. A slow
///     upload that keeps moving is not cut off by requestTimeout;
///   • counts as saved only when the reply is the server's explicit ack (CheckServerAck).
///
/// A failed attempt is retried after the next delay in the job's list (finalRetryDelaysSeconds or
/// checkpointRetryDelaysSeconds). Two failures are not retried, because a resend would fail the same
/// way: 413 (the server's size cap) and an attempt that reached maxAttemptSeconds while still moving
/// (the connection is too slow for the hard limit). A stall, a dropped connection, a server error or
/// an unconfirmed reply is retried, since those are usually temporary.
///
/// A newer job replaces an older one (a checkpoint replaces an earlier checkpoint; the final upload
/// replaces any checkpoint), but nothing replaces a running final upload.
/// </summary>
public class LogSender : MonoBehaviour
{
    [Header("Server Settings")]
    // janus is decommissioned; game logging is handled server-side by the router now.
    // Leave empty (manual log upload disabled) unless a real endpoint is configured.
    [SerializeField] private string serverUrl = "";

    // NOTE: MainScene and TutorialScene serialize their own value for this field, and the build
    // uses that value, not this initializer. Change it in both scenes. Build 1.0.2 shipped with
    // 30s (then a limit on the whole attempt) because only this line was changed.
    [Tooltip("Seconds without any progress before an upload attempt is abandoned: no upload progress while the body is being sent, or no reply once it has been sent. A slow upload that keeps moving is not cut off by this (see maxAttemptSeconds).")]
    [SerializeField] private float requestTimeout = 120f;

    [Tooltip("Hard limit on one upload attempt, even while it is still moving. An attempt that reaches it is not retried: the next one would be just as slow.")]
    [SerializeField] private float maxAttemptSeconds = 600f;

    [Tooltip("Gzip uploads (in the browser in WebGL builds, GZipStream elsewhere). The server must accept gzip before a build with this on is released. If the server refuses a gzipped body the way the pre-gzip save_game_logs.py did, the rest of that upload is sent uncompressed.")]
    [SerializeField] private bool compressUploads = true;

    [Tooltip("Delays before each retry of the end-of-game upload, counted from the end of the failed attempt. 3 entries = 3 retries = 4 attempts.")]
    [SerializeField] private float[] finalRetryDelaysSeconds = { 5f, 15f, 30f };

    [Tooltip("Delays before each retry of a day checkpoint upload (these run in the background during play). A newer checkpoint, or the final upload, replaces a checkpoint that is still being retried.")]
    [SerializeField] private float[] checkpointRetryDelaysSeconds = { 30f, 90f };

    // save_game_logs.py's own hard cap (MAX_BODY_BYTES, and MAX_PAYLOAD_BYTES once decompressed) —
    // kept here only to log how close a payload is getting, not to pre-empt the request client-side
    // (Apache/nginx may enforce a smaller limit in front of the script, which isn't visible here).
    const long ServerMaxBodyBytesKnown = 50L * 1024 * 1024;
    [Tooltip("Only count an upload as successful if the server's reply is an explicit ack — {\"ok\":true,\"bytes\":N} with N equal to the bytes we sent (save_game_logs.py does this). A plain 2xx isn't enough: a server can answer 200 without having saved anything. Turn off only to test against an older server that doesn't send an ack.")]
    [SerializeField] private bool requireServerAck = true;

    // What save_game_logs.py answers with when it has fully saved an upload.
    [Serializable]
    private class ServerAck
    {
        public bool ok;
        public int bytes;          // request body bytes the server received (compressed, if gzipped)
        public int payload_bytes;  // JSON bytes after decompressing (absent from older servers)
        public int messages;       // rows in the upload
        public bool duplicate;     // the server already had this exact upload
        public string file;
        public string message;
    }

    // CoraUpload.jslib's result for one attempt.
    [Serializable]
    private class BrowserResult
    {
        public int state;
        public int status;
        public string error;
        public string response;
        public bool compressed;
        public int bodyBytes;
        public int payloadBytes;
        public int sentBytes;
        public float seconds;
    }

    // Must match CoraUploads' states in CoraUpload.jslib.
    enum BrowserState { Preparing = 0, Sending = 1, Waiting = 2, Done = 3, Failed = 4 }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int CoraUploadStart(string url, byte[] data, int length, int gzip, int stallMs, int hardLimitMs);
    [DllImport("__Internal")] static extern int CoraUploadState(int handle);
    [DllImport("__Internal")] static extern int CoraUploadSentBytes(int handle);
    [DllImport("__Internal")] static extern int CoraUploadTotalBytes(int handle);
    [DllImport("__Internal")] static extern string CoraUploadResult(int handle);
    [DllImport("__Internal")] static extern void CoraUploadRelease(int handle);
    [DllImport("__Internal")] static extern void CoraUploadSetLeaveWarning(int on);
#endif

    public static LogSender Instance { get; private set; }

    public enum SendStatus { Idle, Sending, Success, Failed }
    public SendStatus CurrentStatus { get; private set; } = SendStatus.Idle;
    public string LastStatusMessage { get; private set; } = "";

    // True only after the server has acknowledged the end-of-game ("final") upload. A day
    // checkpoint succeeding does not count: the survey hand-off waits on this, not on CurrentStatus.
    public bool FinalUploadConfirmed { get; private set; }

    // True once the end-of-game upload has used up its attempts (or failed in a way no retry could
    // fix) without being confirmed. Reset when a new end-of-game upload starts.
    public bool FinalUploadGaveUp { get; private set; }

    public bool FinalUploadInProgress => job != null && job.isFinal && !job.finished;

    // The HTTP status of the most recent attempt, and whether its failure is one retrying the
    // identical payload could never fix (413 Payload Too Large, or the hard time limit).
    public long LastHttpResponseCode { get; private set; }
    public bool LastFailureIsUnrecoverable { get; private set; }

    // ── Progress of the current job, for the end-of-game screen ──────────────────────────────
    public enum UploadPhase { Idle, Preparing, Uploading, WaitingForServer, WaitingToRetry, Confirmed, Failed }
    public UploadPhase Phase { get; private set; } = UploadPhase.Idle;
    public string ActiveKind => job != null && !job.finished ? job.kind : null;
    public int Attempt => job != null ? job.attempt : 0;
    public int MaxAttempts => job != null ? job.maxAttempts : 0;
    public long BytesSent { get; private set; }    // request body bytes sent in this attempt
    public long BytesTotal { get; private set; }   // request body size (compressed size, once known)
    public bool LastAttemptCompressed { get; private set; }
    public float SecondsUntilRetry => Phase == UploadPhase.WaitingToRetry ? Mathf.Max(0f, retryAt - Time.realtimeSinceStartup) : 0f;
    public string LastFailureReason { get; private set; } = "";

    public static event Action<SendStatus, string> OnSendComplete;

    class UploadJob
    {
        public string kind;
        public int day;
        public int round;           // > 0 for a per-round checkpoint
        public bool isFinal;
        public byte[] payload;      // the log as UTF-8 JSON, built once and resent unchanged
        public string uploadId;     // the same on every attempt; the server logs it
        public float[] retryDelays;
        public int maxAttempts;
        public int attempt;
        public bool allowGzip;
        public bool finished;       // succeeded, gave up or was replaced
        public bool cancelled;      // replaced by a newer job
        public int browserHandle;   // CoraUpload.jslib handle while a browser upload runs
        public UnityWebRequest request;
    }

    class AttemptResult
    {
        public bool confirmed;
        public bool unrecoverable;
        public bool gzipRejected;
        public bool browserUnavailable;
        public string error = "";   // stalled | no_response | network | hard_limit | timeout | http | not_confirmed | no_url
        public string detail = "";
        public long status;
        public string response = "";
        public bool compressed;
        public long bodyBytes;
        public float seconds;
    }

    UploadJob job;
    float retryAt;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        CancelJob();
        Instance = null;
    }

    /// <summary>The end-of-game upload: the whole log, filed by the server as the session's final data.
    /// No effect while an end-of-game upload is already running.</summary>
    public void SendAllLogs() => StartUpload("final", 0, 0);

    /// <summary>
    /// End-of-day insurance: the whole log so far, filed by the server as that day's checkpoint.
    /// Each checkpoint is cumulative, so the next day's (or the final upload) covers one that failed.
    /// </summary>
    public void SendDayCheckpoint(int day) => StartUpload("checkpoint", day, 0);

    /// <summary>
    /// End-of-ROUND checkpoint: the whole log so far plus a full state checkpoint, filed by the
    /// server per day and round. Makes a participant who closes the tab mid-game lose at most
    /// part of one round, and gives training a full game state at every round. Its status lines
    /// go to the hidden Data stream so 32 uploads a game do not fill the player's log. A newer
    /// checkpoint replaces one still in flight (StartJob), which loses nothing: each is cumulative.
    /// </summary>
    public void SendRoundCheckpoint(int day, int round) => StartUpload("checkpoint", day, round);

    void StartUpload(string uploadKind, int checkpointDay, int checkpointRound = 0)
    {
        bool isFinal = uploadKind == "final";
        if (job != null && !job.finished && job.isFinal)
        {
            if (!isFinal)
                Debug.LogWarning($"[LogSender] End-of-game upload in progress; not starting a {uploadKind} upload.");
            return;
        }

        // A new end-of-game upload supersedes any earlier result, even if this call ends up not
        // posting: an earlier confirmation must not count for it. Done before any early return.
        if (isFinal)
        {
            FinalUploadConfirmed = false;
            FinalUploadGaveUp = false;
        }

        // Headless runs (gym, benchmark, parity harness) must never upload into the human-study
        // log store: they are machine episodes, and 32-round benchmarks reach the day-8 report
        // that auto-sends. Opt in explicitly with ARC_UPLOAD_LOGS=1 if a headless run should
        // upload. Human play (WebGL / desktop player) is never batch mode, so it is unaffected.
        if (Application.isBatchMode && System.Environment.GetEnvironmentVariable("ARC_UPLOAD_LOGS") != "1")
        {
            Debug.Log("[LogSender] Batch mode — not uploading (set ARC_UPLOAD_LOGS=1 to override).");
            return;
        }

        if (!GameLogPanel.DataCollectionEnabled)
        {
            Debug.Log("[LogSender] Data collection disabled (config.json) - skipping send.");
            return;
        }

        if (GameLogPanel.Instance == null)
        {
            Debug.LogError("[LogSender] GameLogPanel not found.");
            return;
        }

        // Snapshot now: the caller relies on everything logged before this call being included.
        string json = GameLogPanel.Instance.GetMessagesAsJson(true, uploadKind, checkpointDay, checkpointRound);
        StartJob(uploadKind, checkpointDay, checkpointRound, json);
    }

    public void SendCurrentRoundLogs()
    {
        if (!GameLogPanel.DataCollectionEnabled)
        {
            Debug.Log("[LogSender] Data collection disabled (config.json) - skipping send.");
            return;
        }

        if (GameLogPanel.Instance == null)
        {
            Debug.LogError("[LogSender] GameLogPanel not found.");
            return;
        }

        if (job != null && !job.finished)
        {
            Debug.LogWarning("[LogSender] Already sending logs, please wait.");
            return;
        }

        StartJob("round", 0, GameLogPanel.Instance.GetMessagesAsJson(false));
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>UploadSelfTest only: points the sender at a test server with short timeouts.</summary>
    public void ConfigureForSelfTest(string url, float stallSeconds, float hardLimitSeconds,
                                     float[] finalDelays, float[] checkpointDelays, bool compress)
    {
        serverUrl = url;
        requestTimeout = stallSeconds;
        maxAttemptSeconds = hardLimitSeconds;
        finalRetryDelaysSeconds = finalDelays;
        checkpointRetryDelaysSeconds = checkpointDelays;
        compressUploads = compress;
    }
#endif

    /// <summary>
    /// Starts uploading `json` as a new job, replacing any job still running. Public so the upload
    /// self-test can drive it without a GameLogPanel; game code goes through SendAllLogs /
    /// SendDayCheckpoint.
    /// </summary>
    public void StartJob(string uploadKind, int checkpointDay, string json) =>
        StartJob(uploadKind, checkpointDay, 0, json);

    public void StartJob(string uploadKind, int checkpointDay, int checkpointRound, string json)
    {
        CancelJob();

        bool isFinal = uploadKind == "final";
        if (isFinal)
        {
            // These describe the newest end-of-game upload only.
            FinalUploadConfirmed = false;
            FinalUploadGaveUp = false;
        }
        float[] delays = isFinal ? finalRetryDelaysSeconds
                       : uploadKind == "checkpoint" ? checkpointRetryDelaysSeconds
                       : null;
        delays = delays ?? new float[0];

        job = new UploadJob
        {
            kind = uploadKind,
            day = checkpointDay,
            round = checkpointRound,
            isFinal = isFinal,
            payload = Encoding.UTF8.GetBytes(json),
            uploadId = Guid.NewGuid().ToString("N").Substring(0, 12),
            retryDelays = delays,
            maxAttempts = 1 + delays.Length,
            allowGzip = compressUploads,
        };
        StartCoroutine(RunJob(job));
    }

    void CancelJob()
    {
        if (job == null || job.finished) return;
        job.finished = true;
        job.cancelled = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        if (job.browserHandle > 0)
        {
            CoraUploadRelease(job.browserHandle);   // abandons the request
            job.browserHandle = 0;
        }
#endif
        if (job.request != null)
        {
            try { job.request.Abort(); } catch (Exception) { }
        }
        if (job.isFinal)
            SetLeaveWarning(false);
        Debug.Log($"[LogSender] {job.kind} upload replaced by a newer upload.");
    }

    IEnumerator RunJob(UploadJob j)
    {
        CurrentStatus = SendStatus.Sending;
        LastStatusMessage = "Sending logs...";
        LastFailureReason = "";
        if (j.isFinal)
            SetLeaveWarning(true);

        while (true)
        {
            j.attempt++;
            var result = new AttemptResult();
            yield return RunAttempt(j, result);
            if (j.cancelled) yield break;

            RecordAttempt(j, result);
            if (result.confirmed)
            {
                Finish(j, true);
                yield break;
            }
            if (result.unrecoverable || j.attempt >= j.maxAttempts)
            {
                Finish(j, false);
                yield break;
            }
            if (result.gzipRejected)
                j.allowGzip = false;

            retryAt = Time.realtimeSinceStartup + j.retryDelays[j.attempt - 1];
            Phase = UploadPhase.WaitingToRetry;
            while (Time.realtimeSinceStartup < retryAt)
            {
                yield return null;
                if (j.cancelled) yield break;
            }
        }
    }

    // config.json's logServerUrl wins, so a deployment can point uploads at its own receiver
    // without a rebuild. RuntimeConfig is what makes that work in the plain human build:
    // WebSocketManager.LoadedConfig is only filled on the LLM connect path, so without it the key
    // was silently ignored offline and uploads always went to the scene's URL.
    string BaseUrl => RuntimeConfig.LogServerUrl
        ?? (!string.IsNullOrEmpty(WebSocketManager.LoadedConfig?.logServerUrl)
            ? WebSocketManager.LoadedConfig.logServerUrl
            : serverUrl);

    // The query string identifies the attempt in the server's upload log and the web server's
    // access log. It always contains "=" (see save_game_logs.py's cli()).
    string AttemptUrl(UploadJob j)
    {
        string url = BaseUrl;
        return url + (url.Contains("?") ? "&" : "?") + "kind=" + j.kind + (j.day > 0 ? "&day=" + j.day : "") + (j.round > 0 ? "&round=" + j.round : "")
               + "&attempt=" + j.attempt + "&uploadId=" + j.uploadId;
    }

    IEnumerator RunAttempt(UploadJob j, AttemptResult result)
    {
        Phase = UploadPhase.Preparing;
        BytesSent = 0;
        BytesTotal = j.payload.Length;

        if (string.IsNullOrEmpty(BaseUrl))
        {
            result.error = "no_url";
            result.unrecoverable = true;
            yield break;
        }

        string url = AttemptUrl(j);
#if UNITY_WEBGL && !UNITY_EDITOR
        yield return RunBrowserAttempt(j, url, result);
        if (!result.browserUnavailable || j.cancelled) yield break;
        Debug.LogWarning("[LogSender] The browser upload could not start; using UnityWebRequest instead.");
#endif
        yield return RunUnityAttempt(j, url, result);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    IEnumerator RunBrowserAttempt(UploadJob j, string url, AttemptResult result)
    {
        int handle = CoraUploadStart(url, j.payload, j.payload.Length, j.allowGzip ? 1 : 0,
                                     Mathf.RoundToInt(requestTimeout * 1000f), Mathf.RoundToInt(maxAttemptSeconds * 1000f));
        if (handle <= 0)
        {
            result.browserUnavailable = true;
            yield break;
        }
        j.browserHandle = handle;

        // Polled every frame. The timeouts themselves run in the browser, so they still fire while
        // the tab is in the background and this loop is not running.
        while (true)
        {
            int state = CoraUploadState(handle);
            if (state < 0 || state >= (int)BrowserState.Done) break;
            BytesSent = CoraUploadSentBytes(handle);
            BytesTotal = CoraUploadTotalBytes(handle);
            Phase = state == (int)BrowserState.Preparing ? UploadPhase.Preparing
                  : state == (int)BrowserState.Sending ? UploadPhase.Uploading
                  : UploadPhase.WaitingForServer;
            yield return null;
            if (j.cancelled) yield break;   // CancelJob has released the handle
        }

        BrowserResult b = null;
        try { b = JsonUtility.FromJson<BrowserResult>(CoraUploadResult(handle)); }
        catch (ArgumentException) { }
        CoraUploadRelease(handle);
        j.browserHandle = 0;

        if (b == null || b.state < 0)
        {
            result.error = "network";
            result.detail = "the browser lost track of the upload";
            yield break;
        }

        result.compressed = b.compressed;
        result.bodyBytes = b.bodyBytes;
        result.seconds = b.seconds;
        BytesSent = b.sentBytes;
        BytesTotal = b.bodyBytes;
        if (b.state == (int)BrowserState.Done)
        {
            Evaluate(j, result, b.status, b.response ?? "");
        }
        else
        {
            result.error = string.IsNullOrEmpty(b.error) ? "network" : b.error;
            result.unrecoverable = result.error == "hard_limit";
        }
    }
#endif

    IEnumerator RunUnityAttempt(UploadJob j, string url, AttemptResult result)
    {
        byte[] body = j.payload;
#if !UNITY_WEBGL || UNITY_EDITOR
        if (j.allowGzip)
        {
            body = Gzip(j.payload);
            result.compressed = true;
        }
#endif
        // UnityWebRequest's upload progress is only relied on off WebGL. In a WebGL build this path
        // is only the fallback for a browser upload that could not start; there it keeps the old
        // single timeout on the whole request.
        bool watchProgress = Application.platform != RuntimePlatform.WebGLPlayer;
        float started = Time.realtimeSinceStartup;
        string stall = null;

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", result.compressed ? "application/gzip" : "application/json");
            request.timeout = Mathf.CeilToInt(watchProgress ? maxAttemptSeconds : requestTimeout);
            j.request = request;
            Phase = UploadPhase.Uploading;
            BytesTotal = body.Length;

            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            ulong lastUp = 0, lastDown = 0;
            float lastActivity = started;
            while (!op.isDone)
            {
                yield return null;
                if (j.cancelled)
                {
                    j.request = null;
                    yield break;
                }
                float now = Time.realtimeSinceStartup;
                ulong up = request.uploadedBytes, down = request.downloadedBytes;
                if (up != lastUp || down != lastDown)
                {
                    lastUp = up;
                    lastDown = down;
                    lastActivity = now;
                }
                bool bodySent = up >= (ulong)body.Length;
                BytesSent = (long)up;
                Phase = bodySent ? UploadPhase.WaitingForServer : UploadPhase.Uploading;
                if (watchProgress && now - lastActivity > requestTimeout)
                {
                    stall = bodySent ? "no_response" : "stalled";
                    request.Abort();
                    break;
                }
            }
            j.request = null;
            result.bodyBytes = body.Length;
            result.seconds = Time.realtimeSinceStartup - started;

            if (stall != null)
            {
                result.error = stall;
            }
            else if (request.result == UnityWebRequest.Result.Success || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Evaluate(j, result, request.responseCode, request.downloadHandler?.text ?? "");
            }
            else
            {
                bool timedOut = (request.error ?? "").IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                result.error = !timedOut ? "network" : watchProgress ? "hard_limit" : "timeout";
                result.detail = request.error ?? "";
                result.unrecoverable = result.error == "hard_limit";
            }
        }
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    static byte[] Gzip(byte[] data)
    {
        using (var output = new System.IO.MemoryStream())
        {
            using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                gzip.Write(data, 0, data.Length);
            return output.ToArray();
        }
    }
#endif

    void Evaluate(UploadJob j, AttemptResult result, long status, string response)
    {
        result.status = status;
        result.response = response;
        if (status >= 200 && status < 300)
        {
            string problem = CheckServerAck(response, result.bodyBytes, j.payload.Length);
            if (problem == null)
            {
                result.confirmed = true;
            }
            else
            {
                result.error = "not_confirmed";
                result.detail = problem;
            }
            return;
        }

        result.error = "http";
        result.detail = $"HTTP {status}: {Truncate(ServerMessage(response), 160)}";
        if (status == 413)
            result.unrecoverable = true;   // the server's size cap: resending cannot help
        else if (status == 400 && result.compressed && LooksLikeGzipRefused(response))
            result.gzipRejected = true;    // a server from before gzip support: resend uncompressed
    }

    // The pre-gzip save_game_logs.py reads a gzipped body as text and fails like this.
    static bool LooksLikeGzipRefused(string response) =>
        response.Contains("not valid UTF-8") || response.Contains("Invalid JSON");

    void RecordAttempt(UploadJob j, AttemptResult result)
    {
        LastHttpResponseCode = result.status;
        LastFailureIsUnrecoverable = result.unrecoverable;
        LastAttemptCompressed = result.compressed;

        // Recorded into the log itself, so payload size, compression and failures over a session —
        // and across participants — can be reviewed from the uploaded data alone. (Rows logged
        // during the final upload are not in it: its payload was snapshotted when it started.)
        string what = $"Upload attempt {j.attempt} of {j.maxAttempts}: kind={j.kind}{(j.day > 0 ? " day " + j.day : "")}{(j.round > 0 ? " round " + j.round : "")}, " +
                      $"payload={j.payload.Length} bytes ({100.0 * j.payload.Length / ServerMaxBodyBytesKnown:F1}% of server cap), " +
                      $"sent={result.bodyBytes} bytes{(result.compressed ? " gzip" : "")} in {result.seconds:F1}s, uploadId={j.uploadId}";

        if (result.confirmed)
        {
            LastFailureReason = "";
            LastStatusMessage = $"Logs sent successfully. Server: {result.response}";
            Debug.Log($"[LogSender] {what} -> confirmed. Server: {result.response}");
            LogStatus(j, true, what + " -> confirmed", null);
            return;
        }

        LastFailureReason = DescribeFailure(result);
        LastStatusMessage = "Failed: " + LastFailureReason;
        Debug.LogWarning($"[LogSender] {what} -> failed: {LastFailureReason}");
        LogStatus(j, false, what + " -> failed: " + LastFailureReason, "Log send failed: " + LastFailureReason);
    }

    /// <summary>A status line in the game log. Day and final uploads are visible (metrics line,
    /// plus `playerText` as an error or action); the per-round checkpoints go to the hidden Data
    /// stream only, so 32 of them a game do not fill the player's log.</summary>
    void LogStatus(UploadJob j, bool ok, string metricsText, string playerText)
    {
        var log = GameLogPanel.Instance;
        if (log == null) return;
        if (j.round > 0)
        {
            log.LogData("upload_status", GameLogPanel.Json("ok", ok, "message", metricsText));
            return;
        }
        log.LogMetricsChange(metricsText);
        if (playerText == null) return;
        if (ok) log.LogPlayerAction(playerText);
        else log.LogError(playerText);
    }

    string DescribeFailure(AttemptResult result)
    {
        switch (result.error)
        {
            case "stalled":
                return $"the upload stopped moving for {requestTimeout:F0} s at {Percent(BytesSent, BytesTotal)}%";
            case "no_response":
                return $"no reply from the server within {requestTimeout:F0} s of the upload finishing";
            case "hard_limit":
                return "the upload took longer than " + (maxAttemptSeconds >= 120f
                    ? $"{maxAttemptSeconds / 60f:F0} minutes"
                    : $"{maxAttemptSeconds:F0} s");
            case "timeout":
                return $"no reply within {requestTimeout:F0} s";
            case "network":
                return "the connection was lost" + (string.IsNullOrEmpty(result.detail) ? "" : $" ({result.detail})");
            case "aborted":
                return "the upload was cancelled";
            case "http":
                return "server error, " + result.detail;
            case "not_confirmed":
                return "the server did not confirm the save (" + result.detail + ")";
            case "no_url":
                return "no log server is configured";
            default:
                return result.error;
        }
    }

    void Finish(UploadJob j, bool success)
    {
        j.finished = true;
        if (j.isFinal)
        {
            SetLeaveWarning(false);
            if (success) FinalUploadConfirmed = true;
            else FinalUploadGaveUp = true;
        }
        Phase = success ? UploadPhase.Confirmed : UploadPhase.Failed;
        CurrentStatus = success ? SendStatus.Success : SendStatus.Failed;

        if (success)
        {
            if (j.round > 0) GameLogPanel.Instance?.LogData("upload_status", GameLogPanel.Json("ok", true, "message", "Logs sent to server successfully"));
            else GameLogPanel.Instance?.LogPlayerAction("Logs sent to server successfully");
        }
        else
        {
            Debug.LogError($"[LogSender] {j.kind} upload not confirmed after {j.attempt} attempt(s): {LastFailureReason}");
            string text = $"Upload ({j.kind}) given up after {j.attempt} attempt(s): {LastFailureReason}";
            if (j.round > 0) GameLogPanel.Instance?.LogData("upload_status", GameLogPanel.Json("ok", false, "message", text));
            else GameLogPanel.Instance?.LogError(text);
        }

        OnSendComplete?.Invoke(CurrentStatus, LastStatusMessage);
    }

    void SetLeaveWarning(bool on)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        CoraUploadSetLeaveWarning(on ? 1 : 0);
#endif
    }

    /// <summary>Returns null if the server's reply is a valid ack for what we sent, else why not.</summary>
    string CheckServerAck(string responseText, long sentBytes, long payloadBytes)
    {
        if (!requireServerAck) return null;

        ServerAck ack;
        try
        {
            ack = JsonUtility.FromJson<ServerAck>(responseText);
        }
        catch (ArgumentException)
        {
            ack = null;   // not JSON at all — e.g. a proxy or login page answering 200
        }

        if (ack == null || !ack.ok)
            return $"reply was not an ok ack ({Truncate(responseText, 120)})";

        if (ack.bytes != sentBytes)
            return $"server received {ack.bytes} bytes but {sentBytes} were sent";

        // Older servers do not report it; when present, it checks the decompression too.
        if (ack.payload_bytes > 0 && ack.payload_bytes != payloadBytes)
            return $"server decoded {ack.payload_bytes} bytes of log but {payloadBytes} were sent";

        return null;
    }

    static string ServerMessage(string responseText)
    {
        try
        {
            ServerAck ack = JsonUtility.FromJson<ServerAck>(responseText);
            if (ack != null && !string.IsNullOrEmpty(ack.message)) return ack.message;
        }
        catch (ArgumentException) { }
        return string.IsNullOrEmpty(responseText) ? "empty reply" : responseText;
    }

    static int Percent(long part, long whole) =>
        whole > 0 ? Mathf.Clamp(Mathf.FloorToInt(100f * part / whole), 0, 100) : 0;

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "empty reply";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
