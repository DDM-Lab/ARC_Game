using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DebugPanel : MonoBehaviour
{
    [Header("Debug Panel Settings")]
    [SerializeField] private GameObject debugPanelUI;
    [SerializeField] private bool startVisible = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;

    [Header("Game Log Debug Section")]
    [SerializeField] private TMP_InputField messageInputField;
    [SerializeField] private TMP_Dropdown colorDropdown;
    [SerializeField] private TMP_Dropdown messageTypeDropdown;
    [SerializeField] private Button addMessageButton;
    [SerializeField] private Button clearLogButton;

    [Header("Task System Debug")]
    [SerializeField] private TMP_Dropdown taskIdDropdown;
    [SerializeField] private Button spawnTaskButton;


    // Singleton
    public static DebugPanel Instance { get; private set; }

    // Debug colors
    private readonly Dictionary<string, Color> debugColors = new Dictionary<string, Color>
    {
        { "White", Color.white },
        { "Red", Color.red },
        { "Green", Color.green },
        { "Blue", Color.blue },
        { "Yellow", Color.yellow },
        { "Cyan", Color.cyan },
        { "Magenta", Color.magenta },
        { "Orange", new Color(1f, 0.5f, 0f) },
        { "Purple", new Color(0.5f, 0f, 1f) },
        { "Gray", Color.gray }
    };

    // Message types
    private readonly string[] messageTypes = { "Normal", "Highlighted", "Tagged", "Debug", "Formatted" };

    private bool isPanelVisible = false;

    private void Awake()
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

    private void Start()
    {
        InitializeDebugPanel();
        SetPanelVisibility(startVisible);

    }

    void Update()
    {
        // Toggle debug panel with F1 key
        if (Input.GetKeyDown(toggleKey))
        {
            SetPanelVisibility(!isPanelVisible);
            Debug.Log("F1 key pressed!");
        }
    }

    private void InitializeDebugPanel()
    {

        // Initialize color dropdown
        if (colorDropdown != null)
        {
            colorDropdown.ClearOptions();
            colorDropdown.AddOptions(new List<string>(debugColors.Keys));
            colorDropdown.value = 0;
        }

        if (taskIdDropdown != null && spawnTaskButton != null)
        {
            UpdateTaskDropdown();
            spawnTaskButton.onClick.AddListener(SpawnSelectedTask);
        }

        // Initialize message type dropdown
        if (messageTypeDropdown != null)
        {
            messageTypeDropdown.ClearOptions();
            messageTypeDropdown.AddOptions(new List<string>(messageTypes));
            messageTypeDropdown.value = 0;
        }

        // Setup button listeners
        if (addMessageButton != null)
            addMessageButton.onClick.AddListener(AddDebugMessage);

        if (clearLogButton != null)
            clearLogButton.onClick.AddListener(ClearGameLog);

        // Set default message
        if (messageInputField != null)
            messageInputField.text = "Test message";
    }

    public void SetPanelVisibility(bool visible)
    {
        isPanelVisible = visible;

        if (debugPanelUI != null)
        {
            debugPanelUI.SetActive(visible);
        }

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void UpdateTaskDropdown()
    {
        TaskSystem taskSystem = FindObjectOfType<TaskSystem>();
        if (taskSystem?.taskDatabase == null) return;
        
        List<string> taskIds = taskSystem.taskDatabase.GetAllTaskIds();
        taskIdDropdown.ClearOptions();
        taskIdDropdown.AddOptions(taskIds);
    }

    void SpawnSelectedTask()
    {
        TaskSystem taskSystem = FindObjectOfType<TaskSystem>();
        if (taskSystem?.taskDatabase == null) return;
        
        string selectedTaskId = taskIdDropdown.options[taskIdDropdown.value].text;
        TaskData taskData = taskSystem.taskDatabase.GetTaskById(selectedTaskId);
        
        if (taskData != null)
        {
            taskSystem.CreateTaskFromDatabase(taskData);
            LogSuccess($"Spawned task: {selectedTaskId}");
        }
        else
        {
            LogError($"Task not found: {selectedTaskId}");
        }
    }

    private void AddDebugMessage()
    {
        if (GameLogPanel.Instance == null)
        {
            Debug.LogError("GameLogPanel.Instance is null!");
            return;
        }

        // Get message text
        string message = "Test message"; // Default fallback
        if (messageInputField != null && !string.IsNullOrEmpty(messageInputField.text))
        {
            message = messageInputField.text;
        }

        // Get selected color
        Color selectedColor = Color.white; // Default
        if (colorDropdown != null && colorDropdown.value < colorDropdown.options.Count)
        {
            string colorName = colorDropdown.options[colorDropdown.value].text;
            if (debugColors.ContainsKey(colorName))
            {
                selectedColor = debugColors[colorName];
            }
        }

        // Get selected message type
        string messageType = "Normal"; // Default
        if (messageTypeDropdown != null && messageTypeDropdown.value < messageTypeDropdown.options.Count)
        {
            messageType = messageTypeDropdown.options[messageTypeDropdown.value].text;
        }

        // Add message based on type
        try
        {
            switch (messageType)
            {
                case "Normal":
                    if (selectedColor == Color.white)
                        GameLogPanel.Instance.AddMessage(message);
                    else
                        GameLogPanel.Instance.AddColoredMessage(message, selectedColor);
                    break;

                case "Highlighted":
                    GameLogPanel.Instance.AddHighlightMessage(message);
                    break;

                case "Tagged":
                    GameLogPanel.Instance.AddTaggedMessage("DEBUG", message);
                    break;

                case "Debug":
                    GameLogPanel.Instance.AddDebugMessage(message);
                    break;

                case "Formatted":
                    if (selectedColor == Color.white)
                        GameLogPanel.Instance.AddFormattedMessage(message);
                    else
                        GameLogPanel.Instance.AddFormattedMessage(
                            $"<color=#{ColorUtility.ToHtmlStringRGB(selectedColor)}>{message}</color>");
                    break;

                default:
                    GameLogPanel.Instance.AddMessage(message);
                    break;
            }

            Debug.Log($"Added message: '{message}' with type: {messageType}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error adding message: {e.Message}");
        }
    }

    private void ClearGameLog()
    {
        if (GameLogPanel.Instance != null)
        {
            GameLogPanel.Instance.ClearLog();
        }
    }

    public bool IsUIOpen()
    {
        return isPanelVisible;
    }

    // Simple public methods for external use
    public void LogError(string error)
    {
        if (GameLogPanel.Instance != null)
        {
            GameLogPanel.Instance.AddColoredMessage($"[ERROR] {error}", Color.red);
        }
    }

    public void LogWarning(string warning)
    {
        if (GameLogPanel.Instance != null)
        {
            GameLogPanel.Instance.AddColoredMessage($"[WARNING] {warning}", Color.yellow);
        }
    }

    public void LogSuccess(string success)
    {
        if (GameLogPanel.Instance != null)
        {
            GameLogPanel.Instance.AddColoredMessage($"[SUCCESS] {success}", Color.green);
        }
    }
    

}