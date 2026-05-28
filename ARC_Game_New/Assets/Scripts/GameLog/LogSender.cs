using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class LogSender : MonoBehaviour
{
    [Header("Server Settings")]
    [SerializeField] private string serverUrl = "http://janus.hss.cmu.edu/cgi-bin/save_game_logs.py";
    [SerializeField] private float requestTimeout = 30f;

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

    public void SendAllLogs()
    {
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

        string json = GameLogPanel.Instance.GetMessagesAsJson(true);
        StartCoroutine(PostLogs(json));
    }

    public void SendCurrentRoundLogs()
    {
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
        Debug.Log($"[LogSender] Sending {jsonPayload.Length} bytes to {serverUrl}");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(serverUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = (int)requestTimeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                CurrentStatus = SendStatus.Success;
                LastStatusMessage = $"Logs sent successfully. Server: {request.downloadHandler.text}";
                Debug.Log($"[LogSender] {LastStatusMessage}");

                if (GameLogPanel.Instance != null)
                    GameLogPanel.Instance.LogPlayerAction("Logs sent to server successfully");
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
}