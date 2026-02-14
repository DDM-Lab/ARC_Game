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
    Buildings,
    Resources,
    Workers,
    Tasks,
    Environment,
    Vehicles,
    Metrics,
    Player
}

[System.Serializable]
public class LogMessage
{
    public string content;
    public string messageType;
    public string category;
    public int day;
    public int round;
    public float timestamp;
    public string realTime;

    public LogMessage(string content, LogMessageType type, LogCategory category)
    {
        this.content = content;
        this.messageType = type.ToString();
        this.category = category.ToString();
        this.timestamp = Time.time;
        this.realTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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
    public string sessionId;
    public string playerName;
    public string gameVersion;
    public string exportTime;
    public int totalMessages;
    public List<LogMessage> messages;

    public LogExportData(List<LogMessage> messages)
    {
        this.sessionId = PlayerSession.SessionId;
        this.playerName = PlayerSession.PlayerName;
        this.gameVersion = Application.version;
        this.exportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        this.totalMessages = messages.Count;
        this.messages = messages;
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

    private List<LogMessage> allMessages = new List<LogMessage>();
    private Queue<string> displayQueue = new Queue<string>();
    private bool isDisplayingMessage = false;

    private LogMessageType currentTypeFilter = LogMessageType.Normal;
    private LogCategory currentCategoryFilter = LogCategory.All;
    private int currentTimePeriodFilter = 0;

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
        LogPlayerAction("Game started");
    }

    void InitializeUI()
    {
        if (logText == null)
            Debug.LogError("GameLogPanel: logText reference missing!");

        if (exportCurrentButton != null)
            exportCurrentButton.onClick.AddListener(() => ExportMessages(false));

        if (exportAllButton != null)
            exportAllButton.onClick.AddListener(() => ExportMessages(true));
    }

    void SetupDropdowns()
    {
        if (messageTypeDropdown != null)
        {
            messageTypeDropdown.ClearOptions();
            messageTypeDropdown.AddOptions(new List<string> { "All", "Normal", "Debug", "Error" });
            messageTypeDropdown.onValueChanged.AddListener(OnMessageTypeFilterChanged);
        }

        if (categoryDropdown != null)
        {
            categoryDropdown.ClearOptions();
            var categoryNames = System.Enum.GetNames(typeof(LogCategory)).ToList();
            categoryDropdown.AddOptions(categoryNames);
            categoryDropdown.onValueChanged.AddListener(OnCategoryFilterChanged);
        }

        if (timePeriodDropdown != null)
        {
            timePeriodDropdown.ClearOptions();
            timePeriodDropdown.AddOptions(new List<string> { "Current Round", "Today", "All Time" });
            timePeriodDropdown.onValueChanged.AddListener(OnTimePeriodFilterChanged);
        }
    }

    #region Public Logging Methods

    public void LogBuildingStatus(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Buildings);
    public void LogResourceChange(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Resources);
    public void LogWorkerAction(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Workers);
    public void LogTaskEvent(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Tasks);
    public void LogEnvironmentChange(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Environment);
    public void LogVehicleEvent(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Vehicles);
    public void LogMetricsChange(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Metrics);
    public void LogPlayerAction(string message) => AddLogMessage(message, LogMessageType.Normal, LogCategory.Player);
    public void LogDebug(string message) => AddLogMessage(message, LogMessageType.Debug, LogCategory.Player);
    public void LogError(string message) => AddLogMessage(message, LogMessageType.Error, LogCategory.Player);

    #endregion

    void AddLogMessage(string content, LogMessageType type, LogCategory category)
    {
        if (type == LogMessageType.Debug && !enableDebugMessages)
            return;

        LogMessage message = new LogMessage(content, type, category);
        allMessages.Add(message);

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
        int typeDropdownValue = messageTypeDropdown != null ? messageTypeDropdown.value : 0;
        if (typeDropdownValue != 0)
        {
            LogMessageType expectedType = LogMessageType.Normal;
            switch (typeDropdownValue)
            {
                case 1: expectedType = LogMessageType.Normal; break;
                case 2: expectedType = LogMessageType.Debug; break;
                case 3: expectedType = LogMessageType.Error; break;
            }

            LogMessageType msgType = (LogMessageType)System.Enum.Parse(typeof(LogMessageType), message.messageType);
            if (msgType != expectedType) return false;
        }

        LogCategory msgCategory = (LogCategory)System.Enum.Parse(typeof(LogCategory), message.category);
        if (currentCategoryFilter != LogCategory.All && msgCategory != currentCategoryFilter)
            return false;

        if (GlobalClock.Instance != null)
        {
            int currentDay = GlobalClock.Instance.GetCurrentDay();
            int currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + 1;

            switch (currentTimePeriodFilter)
            {
                case 0:
                    if (message.day != currentDay || message.round != currentRound) return false;
                    break;
                case 1:
                    if (message.day != currentDay) return false;
                    break;
                case 2:
                    break;
            }
        }

        return true;
    }

    string FormatMessageForDisplay(LogMessage message)
    {
        LogMessageType msgType = (LogMessageType)System.Enum.Parse(typeof(LogMessageType), message.messageType);
        Color messageColor = GetMessageColor(msgType);
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

            string[] lines = logText.text.Split('\n');
            if (lines.Length > maxDisplayedMessages)
            {
                logText.text = string.Join("\n", lines.Skip(lines.Length - maxDisplayedMessages));
            }

            logText.ForceMeshUpdate();

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
        switch (value)
        {
            case 0: currentTypeFilter = LogMessageType.Normal; break;
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

        logText.ForceMeshUpdate();

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

    public LogExportData GetExportData(bool exportAll = false)
    {
        List<LogMessage> messagesToExport = GetMessagesToExport(exportAll);
        return new LogExportData(messagesToExport);
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

            csvLines.Add("Timestamp,RealTime,Day,Round,Category,MessageType,Content");

            foreach (LogMessage message in messagesToExport)
            {
                string cleanContent = CleanContentForCSV(message.content);

                string csvLine = string.Join(",", new string[]
                {
                    message.timestamp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    QuoteAndEscape(message.realTime),
                    message.day.ToString(),
                    message.round.ToString(),
                    message.category,
                    message.messageType,
                    QuoteAndEscape(cleanContent)
                });

                csvLines.Add(csvLine);
            }

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

        content = content.Replace("\r\n", " ")
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Replace("\t", " ")
                        .Replace("\"", "\"\"");

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
        LogBuildingStatus("Kitchen started food production");
        LogBuildingStatus("Shelter damaged by flood");
        LogResourceChange("Produced 10 food packs");
        LogResourceChange("Consumed 5 food packs");
        LogWorkerAction("Assigned 2 trained workers to Kitchen");
        LogWorkerAction("Worker training completed");
        LogTaskEvent("Emergency food task completed");
        LogTaskEvent("Population transport task generated");
        LogEnvironmentChange("Weather changed to Rainy");
        LogEnvironmentChange("Flood expanded to 5 tiles");
        LogVehicleEvent("Vehicle completed food delivery");
        LogVehicleEvent("Vehicle damaged by flood");
        LogMetricsChange("Satisfaction increased by 10");
        LogMetricsChange("Budget decreased by $500");
        LogPlayerAction("Player opened task center");
        LogPlayerAction("Player selected emergency response");
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