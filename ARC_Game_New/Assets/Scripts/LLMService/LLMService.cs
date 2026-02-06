using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeWebSocket;

public class LLMService : MonoBehaviour
{
    public static LLMService Instance { get; private set; }
    
    [Header("Configuration")]
    public string serverUrl = "";
    public bool enableLLM = true;
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    
    private WebSocket websocket;
    private TaskCompletionSource<string> responseTask;
    
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
        }
    }
    
    void Start()
    {
        if (enableLLM)
        {
            ConnectToServer();
        }
    }
    
    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
        #endif
    }
    
    async void ConnectToServer()
    {
        
    }
    
    void OnDestroy()
    {
        if (websocket != null)
        {
            websocket.Close();
        }
    }
    
    /// <summary>
    /// Generate messages for a task (initial load)
    /// </summary>
    public async Task<List<AgentMessage>> GenerateMessagesAsync(GameTask task)
    {
        return new List<AgentMessage>();
    }
    
    /// <summary>
    /// Generate reply based on player message (future feature)
    /// </summary>
    public async Task<List<AgentMessage>> GenerateReplyAsync(GameTask task, string playerMessage)
    {
        return new List<AgentMessage>();
    }
    
    /// <summary>
    /// Send data to LLM and wait for response
    /// </summary>
    async Task<string> SendAndWaitForResponse(string jsonData)
    {
        return "";
    }
    
    /// <summary>
    /// Prepare task data as JSON for LLM
    /// </summary>
    string PrepareTaskDataForLLM(GameTask task)
    {
        return "";
    }
    
    /// <summary>
    /// Parse LLM response into AgentMessage list
    /// </summary>
    List<AgentMessage> ParseLLMResponse(string jsonResponse, GameTask task)
    {
        return new List<AgentMessage>();
    }
    
    void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[LLMService] {message}");
        }
    }
}