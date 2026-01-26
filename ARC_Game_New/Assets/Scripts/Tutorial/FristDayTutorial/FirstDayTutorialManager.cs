using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

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
    public TutorialMessageStream workerAssignmentStream; 
    
    [Header("Context-Aware Feedback Messages")]
    [TextArea(2, 5)]
    public string noBuildingsMessage = "Not building any facilities at the start… an interesting choice. Most leaders prepare early for what's coming next, but this approach might lead to a different outcome. We'll see how it goes.";
    [TextArea(2, 5)]
    public string noKitchenMessage = "Hmm… you might want to consider building a kitchen. Kitchens produce food, and food is essential for keeping clients satisfied and stable.";
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
    private bool day2HighlightsShown = false;
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
        
        // Subscribe to round change
        if (globalClock != null)
        {
            globalClock.OnTimeSegmentChanged += OnRoundChanged;
        }
        
        // Start tutorial immediately on Day 1
        if (globalClock != null && globalClock.GetCurrentDay() == 1 && globalClock.GetCurrentTimeSegment() == 0)
        {
            StartCoroutine(StartTutorialDelayed());
        }
    }

    // Make sure we always make TutorialMessageUI.Instance not null when ShowMessageStream() is called
    IEnumerator StartTutorialDelayed()
    {
        yield return new WaitForEndOfFrame();
        StartTutorial();
    }
    
    void StartTutorial()
    {
        Debug.Log($"Intro messages: {introStream.messages.Count}");
        Debug.Log($"Construction messages: {constructionGuideStream.messages.Count}");
    
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
        Debug.Log($"ShowMessageStream called for: {stream.streamName}");
        Debug.Log($"Stream messages count: {stream.messages.Count}");
        
        if (stream.messages.Count > 0)
        {
            Debug.Log($"First message: {stream.messages[0]}");
        }
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
    
    void OnRoundChanged(int round)
    {
        if (tutorialComplete) return;
        
        if (globalClock.GetCurrentDay() == 1 && round == 1)
        {
            ShowFeedback();
            
            if (globalClock != null)
            {
                globalClock.isWaitingForReport = true; // ADD THIS - force report state
            }
            
            if (globalClock.executeButton != null)
            {
                TextMeshProUGUI buttonText = globalClock.executeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.text = "View Report";
                }
            }
            
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
        
        // loop through worker assignment messages
        foreach (string msg in workerAssignmentStream.messages)
        {
            messages.Add(new TutorialMessage(msg, workerAssignmentStream.agent));
        }

        if (TutorialMessageUI.Instance != null)
        {
            TutorialMessageUI.Instance.ShowMessages(messages, () => {
                HighlightBuiltFacilities();
            });
        }
    }

    void HighlightBuiltFacilities()
    {
        if (abandonedSiteHighlightPrefab == null) return;
        if (day2HighlightsShown) return; // ADD THIS - prevent re-showing
        
        day2HighlightsShown = true; // ADD THIS
        
        // Clear any existing highlights first
        ClearSiteHighlights();
        
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building building in buildings)
        {
            GameObject highlight = Instantiate(abandonedSiteHighlightPrefab, building.transform);
            siteHighlights.Add(highlight);
        }
        
        if (showDebugInfo)
            Debug.Log($"FirstDayTutorial: Highlighted {siteHighlights.Count} built facilities");
        
        highlightsActive = true;
        StartCoroutine(RemoveHighlightsAfterDelay());
    }
    
    string GetContextAwareFeedback()
    {
        // Count buildings NOW instead of relying on events
        Building[] buildings = FindObjectsOfType<Building>();
        
        // Only count operational or under construction buildings
        int buildingCount = 0;
        bool hasKitchen = false;
        
        foreach (Building building in buildings)
        {
            buildingCount++;
            if (building.GetBuildingType() == BuildingType.Kitchen)
            {
                hasKitchen = true;
            }
        }
        
        if (buildingCount == 0)
        {
            return noBuildingsMessage;
        }
        
        if (!hasKitchen)
        {
            return noKitchenMessage;
        }
        
        if (buildingCount > 5)
        {
            return tooManyBuildingsMessage;
        }
        else if (buildingCount < 3)
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
        if (globalClock != null)
        {
            globalClock.OnTimeSegmentChanged -= OnRoundChanged;
        }
        
        ClearSiteHighlights();
    }
}