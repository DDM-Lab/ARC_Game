using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class TutorialMessageStream
{
    public string streamName;
    public TaskOfficer agent;
    [TextArea(3, 10)]
    public List<string> messages = new List<string>();
}

public class FirstDayTutorialManager : MonoBehaviour
{
    [Header("Tutorial Message Streams")]
    public TutorialMessageStream introStream;
    public TutorialMessageStream constructionGuideStream;
    public TutorialMessageStream feedbackStream;
    
    [Header("Context-Aware Feedback Messages")]
    [TextArea(2, 5)]
    public string tooManyBuildingsMessage = "You may have built too many facilities, but it depends on your strategy.";
    [TextArea(2, 5)]
    public string tooFewBuildingsMessage = "You may need more facilities. Feel free to build anytime.";
    [TextArea(2, 5)]
    public string goodStartMessage = "Interesting choice. Let's see how this works out.";
    
    [Header("Highlight Settings")]
    public GameObject abandonedSiteHighlightPrefab;
    public float highlightDuration = 3f;
    
    [Header("System References")]
    public BuildingSystem buildingSystem;
    public GlobalClock globalClock;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    private List<GameObject> siteHighlights = new List<GameObject>();
    private bool tutorialComplete = false;
    private bool highlightsActive = false;
    private int facilitiesBuiltCount = 0;
    
    public static FirstDayTutorialManager Instance { get; private set; }
    
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
        if (buildingSystem == null)
            buildingSystem = FindObjectOfType<BuildingSystem>();
        
        if (globalClock == null)
            globalClock = GlobalClock.Instance;
        
        // Subscribe to building events
        if (buildingSystem != null)
        {
            BuildingSystemUIIntegration.OnBuildingCreated += OnBuildingCreated;
        }
        
        // Subscribe to round change
        if (globalClock != null)
        {
            globalClock.OnTimeSegmentChanged += OnRoundChanged;
        }
        
        // Start tutorial immediately on Day 1
        if (globalClock != null && globalClock.GetCurrentDay() == 1)
        {
            StartTutorial();
        }
    }
    
    void StartTutorial()
    {
        if (showDebugInfo)
            Debug.Log("FirstDayTutorial: Starting tutorial");
        
        // Show intro messages immediately
        ShowMessageStream(introStream, () => {
            // After intro, show construction guide
            ShowMessageStream(constructionGuideStream, () => {
                // After construction guide, show highlights
                ShowAbandonedSiteHighlights();
            });
        });
    }
    
    void ShowMessageStream(TutorialMessageStream stream, System.Action onComplete = null)
    {
        if (TutorialMessageUI.Instance == null)
        {
            Debug.LogWarning("TutorialMessageUI not found!");
            onComplete?.Invoke();
            return;
        }
        
        List<TutorialMessage> messages = new List<TutorialMessage>();
        foreach (string msg in stream.messages)
        {
            messages.Add(new TutorialMessage(msg, stream.agent));
        }
        
        TutorialMessageUI.Instance.ShowMessages(messages, onComplete);
    }
    
    void ShowAbandonedSiteHighlights()
    {
        if (abandonedSiteHighlightPrefab == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("Abandoned site highlight prefab not assigned");
            return;
        }
        
        AbandonedSite[] sites = FindObjectsOfType<AbandonedSite>();
        
        foreach (AbandonedSite site in sites)
        {
            if (site.IsAvailable())
            {
                GameObject highlight = Instantiate(abandonedSiteHighlightPrefab, site.transform);
                siteHighlights.Add(highlight);
            }
        }
        
        highlightsActive = true;
        
        if (showDebugInfo)
            Debug.Log($"FirstDayTutorial: Highlighted {siteHighlights.Count} abandoned sites");
        
        // Start timer to remove highlights
        StartCoroutine(RemoveHighlightsAfterDelay());
    }
    
    IEnumerator RemoveHighlightsAfterDelay()
    {
        yield return new WaitForSecondsRealtime(highlightDuration);
        
        // Remove highlights if player hasn't clicked yet
        if (highlightsActive)
        {
            ClearSiteHighlights();
        }
    }
    
    void ClearSiteHighlights()
    {
        foreach (GameObject highlight in siteHighlights)
        {
            if (highlight != null)
                Destroy(highlight);
        }
        siteHighlights.Clear();
        highlightsActive = false;
        
        if (showDebugInfo)
            Debug.Log("FirstDayTutorial: Highlights cleared");
    }
    
    void OnBuildingCreated(Building building)
    {
        // Remove highlights when player clicks on any site
        if (highlightsActive)
        {
            ClearSiteHighlights();
        }
        
        // Track buildings built
        facilitiesBuiltCount++;
        
        if (showDebugInfo)
            Debug.Log($"FirstDayTutorial: Player built {facilitiesBuiltCount} facilities");
    }
    
    void OnRoundChanged(int round)
    {
        // Only trigger on Day 1, Round 2 (round is 1-indexed in event, but 0-indexed in GlobalClock)
        if (tutorialComplete) return;
        
        if (globalClock.GetCurrentDay() == 1 && round == 1)
        {
            // Show feedback at start of Round 2
            ShowFeedback();
            tutorialComplete = true;
        }
    }
    
    void ShowFeedback()
    {
        if (showDebugInfo)
            Debug.Log("FirstDayTutorial: Showing feedback");
        
        List<TutorialMessage> messages = new List<TutorialMessage>();
        
        // Add custom feedback messages
        foreach (string msg in feedbackStream.messages)
        {
            messages.Add(new TutorialMessage(msg, feedbackStream.agent));
        }
        
        // Add context-aware message based on buildings built
        string contextMessage = GetContextAwareFeedback();
        messages.Add(new TutorialMessage(contextMessage, feedbackStream.agent));
        
        if (TutorialMessageUI.Instance != null)
        {
            TutorialMessageUI.Instance.ShowMessages(messages, null);
        }
    }
    
    string GetContextAwareFeedback()
    {
        if (facilitiesBuiltCount > 5)
        {
            return tooManyBuildingsMessage;
        }
        else if (facilitiesBuiltCount < 3)
        {
            return tooFewBuildingsMessage;
        }
        else
        {
            return goodStartMessage;
        }
    }
    
    void OnDestroy()
    {
        if (buildingSystem != null)
        {
            BuildingSystemUIIntegration.OnBuildingCreated -= OnBuildingCreated;
        }
        
        if (globalClock != null)
        {
            globalClock.OnTimeSegmentChanged -= OnRoundChanged;
        }
        
        ClearSiteHighlights();
    }
}