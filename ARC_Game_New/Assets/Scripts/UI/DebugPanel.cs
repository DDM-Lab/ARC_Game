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

    [Header("Task System Debug")]
    [SerializeField] private TMP_Dropdown taskIdDropdown;
    [SerializeField] private Button spawnTaskButton;

    [Header("Log Sending")]
    [SerializeField] private Button sendLogsButton;
    [SerializeField] private TextMeshProUGUI sendStatusText;

    public static DebugPanel Instance { get; private set; }

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
        if (Input.GetKeyDown(toggleKey))
        {
            SetPanelVisibility(!isPanelVisible);
            Debug.Log($"{toggleKey} key pressed to open Debug Panel.");
        }
    }

    private void InitializeDebugPanel()
    {
        if (taskIdDropdown != null && spawnTaskButton != null)
        {
            UpdateTaskDropdown();
            spawnTaskButton.onClick.AddListener(SpawnSelectedTask);
        }

        if (sendLogsButton != null)
        {
            sendLogsButton.onClick.AddListener(OnSendLogsClicked);
        }

        if (sendStatusText != null)
        {
            sendStatusText.text = "";
        }

        LogSender.OnSendComplete += OnLogSendComplete;
    }

    private void OnDestroy()
    {
        LogSender.OnSendComplete -= OnLogSendComplete;
    }

    void OnSendLogsClicked()
    {
        if (LogSender.Instance == null)
        {
            Debug.LogError("LogSender not found in scene.");
            if (sendStatusText != null)
                sendStatusText.text = "Error: LogSender not in scene";
            return;
        }

        if (sendStatusText != null)
            sendStatusText.text = "Sending logs...";

        if (sendLogsButton != null)
            sendLogsButton.interactable = false;

        LogSender.Instance.SendAllLogs();
    }

    void OnLogSendComplete(LogSender.SendStatus status, string message)
    {
        if (sendLogsButton != null)
            sendLogsButton.interactable = true;

        if (sendStatusText != null)
        {
            if (status == LogSender.SendStatus.Success)
                sendStatusText.text = "Logs sent!";
            else
                sendStatusText.text = $"Failed: {message}";
        }
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

        Debug.Log($"Spawning Task: {selectedTaskId} through Debug Panel.");
    }

    public bool IsUIOpen()
    {
        return isPanelVisible;
    }
}