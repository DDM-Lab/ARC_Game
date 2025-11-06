using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

public class TutorialManager : MonoBehaviour
{ 
    [Header("Tutorial Pages")]
    [SerializeField] private List<GameObject> tutorialPages = new List<GameObject>();
    [SerializeField] private int currentPageIndex = 0;
    
    [Header("Global Skip Button")]
    [SerializeField] private Button globalSkipButton; // Always in same place
    
    [Header("Settings")]
    [SerializeField] private string mainGameSceneName = "MainGame";
    [SerializeField] private string nextButtonName = "NextButton"; // Name to search for
    [SerializeField] private string backButtonName = "BackButton"; // Name to search for
    
    [Header("Optional Progress")]
    [SerializeField] private Text progressText;
    [SerializeField] private Slider progressBar;
    
    // Current page buttons (found dynamically)
    private Button currentNextButton;
    private Button currentBackButton;
    
    void Start()
    {
        // Setup global skip button
        if (globalSkipButton) 
            globalSkipButton.onClick.AddListener(SkipTutorial);
        
        // Show first page
        ShowPage(0);
    }
    
    void ShowPage(int pageIndex)
    {
        // Bounds check
        if (pageIndex < 0 || pageIndex >= tutorialPages.Count) return;
        
        // Clear previous button listeners
        ClearCurrentButtonListeners();
        
        // Hide all pages
        for (int i = 0; i < tutorialPages.Count; i++)
        {
            if (tutorialPages[i] != null)
            {
                tutorialPages[i].SetActive(i == pageIndex);
            }
        }
        
        currentPageIndex = pageIndex;
        
        // Find and setup buttons on the new page
        SetupPageButtons(tutorialPages[pageIndex]);
        
        // Update progress
        UpdateProgressIndicators();
        
        // Page-specific setup
        OnPageShown(pageIndex);
    }
    
    void SetupPageButtons(GameObject currentPage)
    {
        if (currentPage == null) return;
        
        // Find Next button by name (searches children recursively)
        Transform nextTrans = FindChildByName(currentPage.transform, nextButtonName);
        if (nextTrans != null)
        {
            currentNextButton = nextTrans.GetComponent<Button>();
            if (currentNextButton != null)
            {
                currentNextButton.onClick.RemoveAllListeners();
                currentNextButton.onClick.AddListener(NextPage);
                
                // Change text on last page if needed
                bool isLastPage = (currentPageIndex >= tutorialPages.Count - 1);
                Text buttonText = currentNextButton.GetComponentInChildren<Text>();
                TextMeshProUGUI buttonTMP = currentNextButton.GetComponentInChildren<TextMeshProUGUI>();
                
                if (buttonText != null)
                    buttonText.text = isLastPage ? "Start Game" : "Next";
                else if (buttonTMP != null)
                    buttonTMP.text = isLastPage ? "Start Game" : "Next";
            }
        }
        
        // Find Back button by name
        Transform backTrans = FindChildByName(currentPage.transform, backButtonName);
        if (backTrans != null)
        {
            currentBackButton = backTrans.GetComponent<Button>();
            if (currentBackButton != null)
            {
                currentBackButton.onClick.RemoveAllListeners();
                currentBackButton.onClick.AddListener(PreviousPage);
                
                // Hide/disable on first page
                if (currentPageIndex == 0)
                {
                    currentBackButton.gameObject.SetActive(false);
                }
                else
                {
                    currentBackButton.gameObject.SetActive(true);
                }
            }
        }
        
        // Debug info
        Debug.Log($"Page {currentPageIndex}: Next={currentNextButton != null}, Back={currentBackButton != null}");
    }
    
    Transform FindChildByName(Transform parent, string name)
    {
        // Check direct match
        if (parent.name == name)
            return parent;
        
        // Search all children recursively
        foreach (Transform child in parent)
        {
            Transform found = FindChildByName(child, name);
            if (found != null)
                return found;
        }
        
        return null;
    }
    
    void ClearCurrentButtonListeners()
    {
        if (currentNextButton != null)
        {
            currentNextButton.onClick.RemoveAllListeners();
            currentNextButton = null;
        }
        
        if (currentBackButton != null)
        {
            currentBackButton.onClick.RemoveAllListeners();
            currentBackButton = null;
        }
    }
    
    void UpdateProgressIndicators()
    {
        if (progressText)
        {
            progressText.text = $"Step {currentPageIndex + 1} of {tutorialPages.Count}";
        }
        
        if (progressBar)
        {
            progressBar.value = (float)(currentPageIndex + 1) / tutorialPages.Count;
        }
        
        // Hide skip on last page if desired
        if (globalSkipButton)
        {
            bool isLastPage = (currentPageIndex >= tutorialPages.Count - 1);
            globalSkipButton.gameObject.SetActive(!isLastPage);
        }
    }
    
    public void NextPage()
    {
        if (currentPageIndex < tutorialPages.Count - 1)
        {
            ShowPage(currentPageIndex + 1);
        }
        else
        {
            StartGame();
        }
    }
    
    public void PreviousPage()
    {
        if (currentPageIndex > 0)
        {
            ShowPage(currentPageIndex - 1);
        }
    }
    
    public void SkipTutorial()
    {
        StartGame();
    }
    
    void StartGame()
    {
        PlayerPrefs.SetInt("TutorialCompleted", 1);
        PlayerPrefs.Save();
        SceneManager.LoadScene(mainGameSceneName);
    }
    
    void OnPageShown(int pageIndex)
    {
        Debug.Log($"Showing tutorial page {pageIndex + 1}");
        
        // You can add page-specific behavior here
        // Or use a TutorialPage component on each page
    }
    
    void OnDestroy()
    {
        ClearCurrentButtonListeners();
    }
}

// Alternative: Component to put on each page for self-contained button handling
public class TutorialPageNavigation : MonoBehaviour
{
    [Header("Page Navigation Buttons")]
    public Button nextButton;
    public Button backButton;
    
    [Header("Custom Button Labels")]
    public string customNextLabel = "";
    public bool hideBackButton = false;
    
    private TutorialManager manager;
    
    void OnEnable()
    {
        // Find the manager
        if (manager == null)
            manager = GetComponentInParent<TutorialManager>();
        
        // Setup buttons
        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(() => {
                if (manager != null) manager.NextPage();
            });
            
            // Set custom label if specified
            if (!string.IsNullOrEmpty(customNextLabel))
            {
                var text = nextButton.GetComponentInChildren<Text>();
                var tmp = nextButton.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null) text.text = customNextLabel;
                if (tmp != null) tmp.text = customNextLabel;
            }
        }
        
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(() => {
                if (manager != null) manager.PreviousPage();
            });
            
            // Hide if requested
            if (hideBackButton)
                backButton.gameObject.SetActive(false);
        }
    }
    
    void OnDisable()
    {
        // Clean up listeners
        if (nextButton != null)
            nextButton.onClick.RemoveAllListeners();
        if (backButton != null)
            backButton.onClick.RemoveAllListeners();
    }
}