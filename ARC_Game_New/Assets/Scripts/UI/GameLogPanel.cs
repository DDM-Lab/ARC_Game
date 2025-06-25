using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameLogPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private TextMeshProUGUI logText;

    [Header("Settings")]
    [SerializeField] private Color normalTextColor = Color.white;
    [SerializeField] private Color highlightTextColor = Color.yellow;
    [SerializeField] private bool autoScrollToBottom = true;

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;
    [SerializeField] private KeyCode clearLogKey = KeyCode.F5;

    private Queue<string> messageQueue = new Queue<string>();
    private bool isDisplayingMessage = false;

    // Singleton implementation
    public static GameLogPanel Instance { get; private set; }
    
    // Static property to check if text is currently being displayed
    public static bool IsDisplayingText { get; private set; } = false;

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

    private void Start()
    {
        // Initialize log panel
        logText.text = "";

        // Check required references
        if (scrollRect == null)
            Debug.LogError("GameLogPanel: scrollRect reference missing!");

        if (contentRect == null)
            Debug.LogError("GameLogPanel: contentRect reference missing!");

        if (logText == null)
            Debug.LogError("GameLogPanel: logText reference missing!");
    }

    private void Update()
    {
        // Debug functionality
        if (debugMode)
        {
            // Clear log
            if (Input.GetKeyDown(clearLogKey))
            {
                ClearLog();
            }
        }
    }

    /// <summary>
    /// Add a new message to the log
    /// </summary>
    public void AddMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        messageQueue.Enqueue(message);

        if (!isDisplayingMessage)
        {
            StartCoroutine(DisplayNextMessage());
        }
    }

    /// <summary>
    /// Coroutine to display messages instantly
    /// </summary>
    private IEnumerator DisplayNextMessage()
    {
        isDisplayingMessage = true;
        IsDisplayingText = true;

        while (messageQueue.Count > 0)
        {
            string message = messageQueue.Dequeue();
            bool isFormattedMessage = message.StartsWith("[FORMATTED]");
        
            if (isFormattedMessage)
            {
                message = message.Substring("[FORMATTED]".Length);
            }

            // Show timestamp in debug mode
            if (debugMode)
            {
                string timestamp = System.DateTime.Now.ToString("[HH:mm:ss] ");
                logText.text += "<color=#AAAAAA>" + timestamp + "</color>";
            }

            // Display entire message at once
            logText.text += message;

            // Auto scroll to bottom
            if (autoScrollToBottom && scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }

            // Add line break after message
            logText.text += "\n\n";

            // Force scroll to bottom
            if (autoScrollToBottom && scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }

            yield return null; // Wait one frame between messages
        }

        isDisplayingMessage = false;
        IsDisplayingText = false;
    }

    /// <summary>
    /// Clear all log content
    /// </summary>
    public void ClearLog()
    {
        logText.text = "";
        messageQueue.Clear();
        isDisplayingMessage = false;
        IsDisplayingText = false;
    }

    /// <summary>
    /// Add a highlighted message
    /// </summary>
    public void AddHighlightMessage(string message)
    {
        AddMessage("<color=#" + ColorUtility.ToHtmlStringRGB(highlightTextColor) + ">" + message + "</color>");
    }

    /// <summary>
    /// Add a colored message
    /// </summary>
    public void AddColoredMessage(string message, Color color)
    {
        AddMessage("<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + message + "</color>");
    }

    /// <summary>
    /// Add a message with a tag
    /// </summary>
    public void AddTaggedMessage(string tag, string message)
    {
        AddMessage("[" + tag + "] " + message);
    }

    /// <summary>
    /// Add debug information (only shows if debug mode is enabled)
    /// </summary>
    public void AddDebugMessage(string message)
    {
        if (debugMode)
        {
            AddMessage("<color=#AAAAAA>[DEBUG] " + message + "</color>");
        }
    }
    
    /// <summary>
    /// Add a formatted message (same as regular message now)
    /// </summary>
    public void AddFormattedMessage(string message)
    {
        AddMessage(message);
    }

    /// <summary>
    /// Get current message queue count
    /// </summary>
    public int GetQueueCount()
    {
        return messageQueue.Count;
    }

    private void OnApplicationQuit()
    {
        // Stop all coroutines
        StopAllCoroutines();

        // Clear queue
        messageQueue.Clear();
        isDisplayingMessage = false;
        IsDisplayingText = false;

        Debug.Log("GameLogPanel: Application quit cleanup completed");
    }
}