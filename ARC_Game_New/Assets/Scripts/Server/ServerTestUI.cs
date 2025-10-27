using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NativeWebSocket;

public class ServerTestUI : MonoBehaviour
{
    [Header("UI References")]
    public Button testButton;
    public TMP_Text resultText;

    [Header("Server Settings")]
    [SerializeField] private string host = "janus.hss.cmu.edu";
    [SerializeField] private int port = 8998;

    private WebSocket websocket;

    void Start()
    {
        if (testButton != null)
        {
            testButton.onClick.AddListener(() => TestServerCommunication());
        }
        if (resultText != null)
        {
            resultText.text = "Press the button to test WebSocket connection.";
        }
    }

    async void TestServerCommunication()
    {
        try
        {
            resultText.text = "Connecting...";
            
            // Create WebSocket connection
            string wsUrl = $"ws://{host}:{port}";
            websocket = new WebSocket(wsUrl);

            // Set up event handlers
            websocket.OnOpen += () =>
            {
                Debug.Log("WebSocket connection opened!");
                resultText.text = "Connected! Sending message...";
                
                // Send test message
                string jsonToSend = "{\"arg\":\"hello\"}";
                websocket.SendText(jsonToSend);
                Debug.Log($"📤 Sent: {jsonToSend}");
            };

            websocket.OnMessage += (bytes) =>
            {
                string response = Encoding.UTF8.GetString(bytes);
                resultText.text = $"Response: {response}";
                Debug.Log($"📩 Received: {response}");
                
                // Close after receiving response
                websocket.Close();
            };

            websocket.OnError += (errorMsg) =>
            {
                resultText.text = $"Error: {errorMsg}";
                Debug.LogError($"WebSocket Error: {errorMsg}");
            };

            websocket.OnClose += (closeCode) =>
            {
                Debug.Log($"WebSocket closed with code: {closeCode}");
            };

            // Connect
            await websocket.Connect();
        }
        catch (Exception e)
        {
            resultText.text = $"Error: {e.Message}";
            Debug.LogError(e);
        }
    }

    void Update()
    {
        // CRITICAL: Dispatch WebSocket messages on main thread
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
        #endif
    }

    async void OnApplicationQuit()
    {
        // Clean up WebSocket on quit
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }
}