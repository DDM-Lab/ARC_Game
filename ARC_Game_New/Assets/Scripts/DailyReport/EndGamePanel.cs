using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EndGamePanel : MonoBehaviour
{
    [Header("UI References")]
    public GameObject endGamePanel;
    public Button viewReportButton;
    
    public static EndGamePanel Instance { get; private set; }
    
    void Awake()
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
    
    void Start()
    {
        if (viewReportButton != null)
        {
            viewReportButton.onClick.AddListener(OnViewReportClicked);
        }
        
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// Show end game panel
    /// </summary>
    public void ShowEndGamePanel()
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(true);
            
            Debug.Log("End game panel displayed");
            GameLogPanel.Instance?.LogUIInteraction("End game panel displayed");
        }
    }
    
    /// <summary>
    /// Hide end game panel
    /// </summary>
    public void HideEndGamePanel()
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// Handle view report button click
    /// </summary>
    void OnViewReportClicked()
    {
        HideEndGamePanel();

        DailyReportManager mgr = DailyReportManager.Instance;

        // Instance can be null if its scene was unloaded; fall back to searching the scene
        if (mgr == null)
        {
            mgr = FindObjectOfType<DailyReportManager>();
            Debug.LogWarning("[EndGamePanel] DailyReportManager.Instance was null — found via FindObjectOfType");
        }

        if (mgr == null)
        {
            Debug.LogError("[EndGamePanel] DailyReportManager not found in scene. Cannot open report.");
            return;
        }

        if (!mgr.gameObject.activeInHierarchy)
        {
            Debug.LogError("[EndGamePanel] DailyReportManager GameObject is inactive. Cannot start coroutine.");
            mgr.gameObject.SetActive(true);
        }

        Debug.Log("[EndGamePanel] Calling ShowDailyReport(forceShow: true)");
        mgr.ShowDailyReport(forceShow: true);

        GameLogPanel.Instance?.LogUIInteraction("Opening final daily report from end game panel");
    }
}
