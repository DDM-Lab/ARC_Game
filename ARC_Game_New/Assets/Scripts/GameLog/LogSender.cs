using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class LogSender : MonoBehaviour
{
    [Header("Server Settings")]
    // janus is decommissioned; game logging is handled server-side by the router now.
    // Leave empty (manual log upload disabled) unless a real endpoint is configured.
    [SerializeField] private string serverUrl = "";
    [SerializeField] private float requestTimeout = 30f;
    [Tooltip("Only count an upload as successful if the server's reply is an explicit ack — {\"ok\":true,\"bytes\":N} with N equal to the bytes we sent (save_game_logs.py does this). A plain 2xx isn't enough: a server can answer 200 without having saved anything. Turn off only to test against an older server that doesn't send an ack.")]
    [SerializeField] private bool requireServerAck = true;

    // What save_game_logs.py answers with when it has fully saved an upload.
    [Serializable]
    private class ServerAck
    {
        public bool ok;
        public int bytes;      // request body bytes the server received
        public int messages;   // rows the server saved
        public string file;
        public string message;
    }

    public static LogSender Instance { get; private set; }

    public enum SendStatus { Idle, Sending, Success, Failed }
    public SendStatus CurrentStatus { get; private set; } = SendStatus.Idle;
    public string LastStatusMessage { get; private set; } = "";

    public static event Action<SendStatus, string> OnSendComplete;

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

    /// <summary>The end-of-game upload: the whole log, filed by the server as the session's final data.</summary>
    public void SendAllLogs() => StartUpload("final", 0);

    /// <summary>
    /// End-of-day insurance: the whole log so far, filed by the server as that day's checkpoint.
    /// Each checkpoint is cumulative, so if one fails the next day's covers it — no retry needed.
    /// </summary>
    public void SendDayCheckpoint(int day) => StartUpload("checkpoint", day);

    void StartUpload(string uploadKind, int checkpointDay)
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

        if (CurrentStatus == SendStatus.Sending)
        {
            Debug.LogWarning("[LogSender] Already sending logs, please wait.");
            return;
        }

        string json = GameLogPanel.Instance.GetMessagesAsJson(true, uploadKind, checkpointDay);
        StartCoroutine(PostLogs(json));
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

        if (CurrentStatus == SendStatus.Sending)
        {
            Debug.LogWarning("[LogSender] Already sending logs, please wait.");
            return;
        }

        string json = GameLogPanel.Instance.GetMessagesAsJson(false);
        StartCoroutine(PostLogs(json));
    }

    IEnumerator PostLogs(string jsonPayload)
    {
        CurrentStatus = SendStatus.Sending;
        LastStatusMessage = "Sending logs...";

        string url = !string.IsNullOrEmpty(WebSocketManager.LoadedConfig?.logServerUrl)
            ? WebSocketManager.LoadedConfig.logServerUrl
            : serverUrl;

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        Debug.Log($"[LogSender] Sending {bodyRaw.Length} bytes to {url}");

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = (int)requestTimeout;

            yield return request.SendWebRequest();

            string ackProblem = request.result == UnityWebRequest.Result.Success
                ? CheckServerAck(request.downloadHandler.text, bodyRaw.Length)
                : null;

            if (request.result == UnityWebRequest.Result.Success && ackProblem == null)
            {
                CurrentStatus = SendStatus.Success;
                LastStatusMessage = $"Logs sent successfully. Server: {request.downloadHandler.text}";
                Debug.Log($"[LogSender] {LastStatusMessage}");

                if (GameLogPanel.Instance != null)
                    GameLogPanel.Instance.LogPlayerAction("Logs sent to server successfully");
            }
            else if (ackProblem != null)
            {
                // The request went through (2xx) but the server didn't confirm it saved our data.
                CurrentStatus = SendStatus.Failed;
                LastStatusMessage = $"Failed: server did not confirm the save — {ackProblem}";
                Debug.LogError($"[LogSender] {LastStatusMessage}");

                if (GameLogPanel.Instance != null)
                    GameLogPanel.Instance.LogError($"Log send not confirmed by server: {ackProblem}");
            }
            else
            {
                CurrentStatus = SendStatus.Failed;
                LastStatusMessage = $"Failed: {request.error} (HTTP {request.responseCode})";
                Debug.LogError($"[LogSender] {LastStatusMessage}");

                if (GameLogPanel.Instance != null)
                    GameLogPanel.Instance.LogError($"Log send failed: {request.error}");
            }

            OnSendComplete?.Invoke(CurrentStatus, LastStatusMessage);
        }
    }

    /// <summary>Returns null if the server's reply is a valid ack for what we sent, else why not.</summary>
    string CheckServerAck(string responseText, int sentBytes)
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

        return null;
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "empty reply";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}