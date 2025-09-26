using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System;
using System.IO;
using System.Text;


public enum LogMessageType
{
    Normal,
    Debug,
    Error
}

public enum LogCategory
{
    All,
    Buildings,    // Facility status, construction, damage, operational changes
    Resources,    // Food production/consumption, resource transfers, storage changes
    Workers,      // Assignment, training, workforce changes
    Tasks,        // Generation, completion, expiration, task-related deliveries
    Environment,  // Weather, floods, external conditions
    Vehicles,     // Delivery status, damage, route changes
    Metrics,      // Satisfaction, budget, performance indicators
    Player        // Direct player interactions (clicks, UI actions, decisions)
}

[System.Serializable]
public class LogMessage
{
    public string id;
    public string content;
    public LogMessageType messageType;
    public LogCategory category;
    public int day;
    public int round;
    public float timestamp;
    public DateTime realTime;
    
    public LogMessage(string content, LogMessageType type, LogCategory category)
    {
        this.id = System.Guid.NewGuid().ToString();
        this.content = content;
        this.messageType = type;
        this.category = category;
        this.timestamp = Time.time;
        this.realTime = DateTime.Now;
        
        // Get current game time
        if (GlobalClock.Instance != null)
        {
            this.day = GlobalClock.Instance.GetCurrentDay();
            this.round = GlobalClock.Instance.GetCurrentTimeSegment() + 1;
        }
        else
        {
            this.day = 1;
            this.round = 1;
        }
    }
}

[System.Serializable]
public class LogExportData
{
    public List<LogMessage> messages;
    public string exportTime;
    public int totalMessages;
    public string gameVersion;
    
    public LogExportData(List<LogMessage> messages)
    {
        this.messages = messages;
        this.exportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        this.totalMessages = messages.Count;
        this.gameVersion = Application.version;
    }
}

public class GameLogPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private TextMeshProUGUI logText;

    [Header("Filter UI")]
    [SerializeField] private TMP_Dropdown messageTypeDropdown;
    [SerializeField] private TMP_Dropdown categoryDropdown;
    [SerializeField] private TMP_Dropdown timePeriodDropdown;

    [Header("Colors")]
    [SerializeField] private Color normalTextColor = Color.white;
    [SerializeField] private Color debugTextColor = Color.cyan;
    [SerializeField] private Color errorTextColor = Color.red;
    [SerializeField] private bool enableDebugMessages = true;

    [Header("Settings")]
    [SerializeField] private bool autoScrollToBottom = true;
    [SerializeField] private int maxDisplayedMessages = 100;

    [Header("Export")]
    [SerializeField] private Button exportCurrentButton;
    [SerializeField] private Button exportAllButton;

    // Message storage
    private List<LogMessage> allMessages = new List<LogMessage>();
    private Queue<string> displayQueue = new Queue<string>();
    private bool isDisplayingMessage = false;

    // Filtering
    private LogMessageType currentTypeFilter = LogMessageType.Normal;
    private LogCategory currentCategoryFilter = LogCategory.All;
    private int currentTimePeriodFilter = 0; // 0=Current, 1=Today, 2=All

    // Singleton
    public static GameLogPanel Instance { get; private set; }
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
        InitializeUI();
        SetupDropdowns();
        RefreshDisplay();

        // Add welcome message
        LogPlayerAction("Game started");
    }

    void InitializeUI()
    {
        if (logText == null)
            Debug.LogError("GameLogPanel: logText reference missing!");

        // Setup export buttons
        if (exportCurrentButton != null)
            exportCurrentButton.onClick.AddListener(() => ExportMessages(false));

        if (exportAllButton != null)
            exportAllButton.onClick.AddListener(() => ExportMessages(true));
    }

    void SetupDropdowns()
    {
        // Message Type Dropdown
        if (messageTypeDropdown != null)
        {
            messageTypeDropdown.ClearOptions();
            messageTypeDropdown.AddOptions(new List<string> { "All", "Normal", "Debug", "Error" });
            messageTypeDropdown.onValueChanged.AddListener(OnMessageTypeFilterChanged);
        }

        // Category Dropdown
        if (categoryDropdown != null)
        {
            categoryDropdown.ClearOptions();
            var categoryNames = System.Enum.GetNames(typeof(LogCategory)).ToList();
            categoryDropdown.AddOptions(categoryNames);
            categoryDropdown.onValueChanged.AddListener(OnCategoryFilterChanged);
        }

        // Time Period Dropdown
        if (timePeriodDropdown != null)
        {
            timePeriodDropdown.ClearOptions();
            timePeriodDropdown.AddOptions(new List<string> { "Current Round", "Today", "All Time" });
            timePeriodDropdown.onValueChanged.AddListener(OnTimePeriodFilterChanged);
        }
    }

    #region Public Logging Methods

    // Buildings category
    public void LogBuildingStatus(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Buildings);

    // Resources category
    public void LogResourceChange(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Resources);

    // Workers category
    public void LogWorkerAction(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Workers);

    // Tasks category
    public void LogTaskEvent(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Tasks);

    // Environment category
    public void LogEnvironmentChange(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Environment);

    // Vehicles category
    public void LogVehicleEvent(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Vehicles);

    // Metrics category
    public void LogMetricsChange(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Metrics);

    // Player category
    public void LogPlayerAction(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Player);

    // Debug messages
    public void LogDebug(string message) => AddLogMessage(message, LogMessageType.Debug, LogCategory.Player);

    // Error messages
    public void LogError(string message) => AddLogMessage(message, LogMessageType.Error, LogCategory.Player);

    #endregion

    void AddLogMessage(string content, LogMessageType type, LogCategory category)
    {
        // Skip debug messages if disabled
        if (type == LogMessageType.Debug && !enableDebugMessages)
            return;

        LogMessage message = new LogMessage(content, type, category);
        allMessages.Add(message);

        // If message passes current filters, add to display queue
        if (PassesCurrentFilters(message))
        {
            string formattedMessage = FormatMessageForDisplay(message);
            displayQueue.Enqueue(formattedMessage);

            if (!isDisplayingMessage)
            {
                StartCoroutine(DisplayNextMessage());
            }
        }
    }

    bool PassesCurrentFilters(LogMessage message)
    {
        // Type filter
        int typeDropdownValue = messageTypeDropdown != null ? messageTypeDropdown.value : 0;
        if (typeDropdownValue != 0) // Not "All"
        {
            LogMessageType expectedType = LogMessageType.Normal;
            switch (typeDropdownValue)
            {
                case 1: expectedType = LogMessageType.Normal; break;
                case 2: expectedType = LogMessageType.Debug; break;
                case 3: expectedType = LogMessageType.Error; break;
            }

            if (message.messageType != expectedType) return false;
        }

        // Category filter
        if (currentCategoryFilter != LogCategory.All && message.category != currentCategoryFilter)
            return false;

        // Time period filter
        if (GlobalClock.Instance != null)
        {
            int currentDay = GlobalClock.Instance.GetCurrentDay();
            int currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + 1;

            switch (currentTimePeriodFilter)
            {
                case 0: // Current Round
                    if (message.day != currentDay || message.round != currentRound) return false;
                    break;
                case 1: // Today
                    if (message.day != currentDay) return false;
                    break;
                case 2: // All Time
                    break;
            }
        }

        return true;
    }

    string FormatMessageForDisplay(LogMessage message)
    {
        Color messageColor = GetMessageColor(message.messageType);
        string timeStamp = $"[Day {message.day}, Round {message.round}]";
        string categoryTag = $"[{message.category}]";

        string colorHex = ColorUtility.ToHtmlStringRGB(messageColor);
        return $"<color=#AAAAAA>{timeStamp}</color> <color=#{colorHex}>{categoryTag} {message.content}</color>";
    }

    Color GetMessageColor(LogMessageType type)
    {
        switch (type)
        {
            case LogMessageType.Debug: return debugTextColor;
            case LogMessageType.Error: return errorTextColor;
            default: return normalTextColor;
        }
    }

    IEnumerator DisplayNextMessage()
    {
        isDisplayingMessage = true;
        IsDisplayingText = true;

        while (displayQueue.Count > 0)
        {
            string message = displayQueue.Dequeue();
            logText.text += message + "\n";

            // Limit displayed messages for performance
            string[] lines = logText.text.Split('\n');
            if (lines.Length > maxDisplayedMessages)
            {
                logText.text = string.Join("\n", lines.Skip(lines.Length - maxDisplayedMessages));
            }

            // Force TextMeshPro to recalculate
            logText.ForceMeshUpdate();

            // Update content height to match text
            if (contentRect != null)
            {
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, logText.preferredHeight + 20);
            }

            if (autoScrollToBottom && scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }

            yield return null;
        }

        isDisplayingMessage = false;
        IsDisplayingText = false;
    }

    #region Filter Event Handlers

    void OnMessageTypeFilterChanged(int value)
    {
        // Update the current filter based on dropdown value
        switch (value)
        {
            case 0: currentTypeFilter = LogMessageType.Normal; break; // "All" 
            case 1: currentTypeFilter = LogMessageType.Normal; break;
            case 2: currentTypeFilter = LogMessageType.Debug; break;
            case 3: currentTypeFilter = LogMessageType.Error; break;
        }
        RefreshDisplay();
    }

    void OnCategoryFilterChanged(int value)
    {
        currentCategoryFilter = (LogCategory)value;
        RefreshDisplay();
    }

    void OnTimePeriodFilterChanged(int value)
    {
        currentTimePeriodFilter = value;
        RefreshDisplay();
    }

    #endregion

    void RefreshDisplay()
    {
        logText.text = "";
        displayQueue.Clear();

        var filteredMessages = allMessages.Where(PassesCurrentFilters).ToList();

        foreach (var message in filteredMessages.TakeLast(maxDisplayedMessages))
        {
            string formattedMessage = FormatMessageForDisplay(message);
            logText.text += formattedMessage + "\n";
        }

        // Force TextMeshPro to update its mesh
        logText.ForceMeshUpdate();

        // Update content rect height based on text's preferred height
        if (contentRect != null)
        {
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, logText.preferredHeight + 20);
        }

        if (autoScrollToBottom && scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    #region Export Methods

    public void ExportMessages(bool exportAll = false)
    {
        List<LogMessage> messagesToExport;

        if (exportAll)
        {
            messagesToExport = allMessages.ToList();
        }
        else
        {
            // Export current round and day only
            if (GlobalClock.Instance != null)
            {
                int currentDay = GlobalClock.Instance.GetCurrentDay();
                int currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + 1;

                messagesToExport = allMessages.Where(m =>
                    m.day == currentDay && m.round == currentRound).ToList();
            }
            else
            {
                messagesToExport = allMessages.ToList();
            }
        }

        LogExportData exportData = new LogExportData(messagesToExport);
        string json = JsonUtility.ToJson(exportData, true);

        // TBA: For now, just log the JSON. Later you can save to file or send to API
        Debug.Log("=== LOG EXPORT ===");
        Debug.Log(json);

        LogPlayerAction($"Exported {messagesToExport.Count} log messages");
    }

    public string GetMessagesAsJson(bool exportAll = false)
    {
        List<LogMessage> messagesToExport = exportAll ?
            allMessages.ToList() :
            allMessages.Where(m => GlobalClock.Instance != null &&
                m.day == GlobalClock.Instance.GetCurrentDay() &&
                m.round == GlobalClock.Instance.GetCurrentTimeSegment() + 1).ToList();

        LogExportData exportData = new LogExportData(messagesToExport);
        return JsonUtility.ToJson(exportData, true);
    }

    public void ExportToCSV(bool exportAll = false)
    {
        List<LogMessage> messagesToExport = GetMessagesToExport(exportAll);
        
        string fileName = exportAll ? 
            $"game_logs_all_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv" : 
            $"game_logs_current_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        
        string filePath = Path.Combine(Application.persistentDataPath, fileName);
        
        try
        {
            List<string> csvLines = new List<string>();
            
            // Add header
            csvLines.Add("ID,Timestamp,RealTime,Day,Round,Category,MessageType,Content");
            
            // Process each message
            foreach (LogMessage message in messagesToExport)
            {
                string cleanContent = CleanContentForCSV(message.content);
                
                string csvLine = string.Join(",", new string[]
                {
                    QuoteAndEscape(message.id ?? ""),
                    message.timestamp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    QuoteAndEscape(message.realTime.ToString("yyyy-MM-dd HH:mm:ss")),
                    message.day.ToString(),
                    message.round.ToString(),
                    message.category.ToString(),
                    message.messageType.ToString(),
                    QuoteAndEscape(cleanContent)
                });
                
                csvLines.Add(csvLine);
            }
            
            // Write all lines at once
            File.WriteAllLines(filePath, csvLines, System.Text.Encoding.UTF8);
            
            Debug.Log($"CSV exported successfully to: {filePath}");
            LogPlayerAction($"Exported {messagesToExport.Count} messages to CSV: {fileName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to export CSV: {e.Message}");
            LogError($"CSV export failed: {e.Message}");
        }
    }
    
    private string CleanContentForCSV(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "";
        
        // Remove ALL problematic characters
        content = content.Replace("\r\n", " ")    // Windows line breaks
                        .Replace("\r", " ")        // Mac line breaks  
                        .Replace("\n", " ")        // Unix line breaks
                        .Replace("\t", " ")        // Tab characters (main culprit!)
                        .Replace("\"", "\"\"");    // Escape quotes
        
        // Clean up multiple spaces
        while (content.Contains("  "))
        {
            content = content.Replace("  ", " ");
        }
        
        return content.Trim();
    }

    private string QuoteAndEscape(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "\"\"";
        
        // Always quote fields to prevent CSV parsing issues
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
    
    private List<LogMessage> GetMessagesToExport(bool exportAll)
    {
        if (exportAll)
        {
            return allMessages.ToList();
        }

        if (GlobalClock.Instance != null)
        {
            int currentDay = GlobalClock.Instance.GetCurrentDay();
            int currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + 1;

            return allMessages.Where(m => m.day == currentDay && m.round == currentRound).ToList();
        }

        return allMessages.ToList();
    }

    private string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "\"\"";
        
        // Remove ALL types of line breaks and extra whitespace
        field = field.Replace("\r\n", " ")
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Replace("\t", " ");
        
        // Collapse multiple spaces into single space
        while (field.Contains("  "))
        {
            field = field.Replace("  ", " ");
        }
        
        field = field.Trim();
        
        // Escape quotes by doubling them
        field = field.Replace("\"", "\"\"");
        
        // Always wrap in quotes
        return "\"" + field + "\"";
    }

    #endregion

    public void ClearLog()
    {
        allMessages.Clear();
        displayQueue.Clear();
        logText.text = "";
        isDisplayingMessage = false;
        IsDisplayingText = false;

        LogPlayerAction("Game log cleared");
    }

    [ContextMenu("Test Log Message")]
    void TestLogMessage()
    {
        // Buildings - facility status, construction, damage, operations
        LogBuildingStatus("Kitchen started food production");
        LogBuildingStatus("Shelter damaged by flood");

        // Resources - production, consumption, transfers, storage
        LogResourceChange("Produced 10 food packs");
        LogResourceChange("Consumed 5 food packs");

        // Workers - assignment, training, workforce changes  
        LogWorkerAction("Assigned 2 trained workers to Kitchen");
        LogWorkerAction("Worker training completed");

        // Tasks - generation, completion, expiration, deliveries
        LogTaskEvent("Emergency food task completed");
        LogTaskEvent("Population transport task generated");

        // Environment - weather, floods, external conditions
        LogEnvironmentChange("Weather changed to Rainy");
        LogEnvironmentChange("Flood expanded to 5 tiles");

        // Vehicles - delivery status, damage, route changes
        LogVehicleEvent("Vehicle completed food delivery");
        LogVehicleEvent("Vehicle damaged by flood");

        // Metrics - satisfaction, budget, performance indicators
        LogMetricsChange("Satisfaction increased by 10");
        LogMetricsChange("Budget decreased by $500");

        // Player - direct interactions, clicks, UI actions, decisions
        LogPlayerAction("Player opened task center");
        LogPlayerAction("Player selected emergency response");

        // Debug and Error
        LogDebug("Pathfinding calculation completed");
        LogError("Failed to create delivery task");
    }

    [ContextMenu("Export Current to CSV")]
    void TestExportCurrentCSV()
    {
        ExportToCSV(false);
    }

    [ContextMenu("Export All to CSV")]  
    void TestExportAllCSV()
    {
        ExportToCSV(true);
    }
}