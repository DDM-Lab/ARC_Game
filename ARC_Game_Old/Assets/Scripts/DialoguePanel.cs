using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class DialoguePanel : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image agentImage;
    [SerializeField] private TextMeshProUGUI agentNameText;
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private Button closeButton;
    
    [Header("Animation Settings")]
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private float fadeOutDuration = 0.3f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    private CanvasGroup _canvasGroup;
    private Coroutine _typingCoroutine;
    
    public event Action OnDialogueClosed;
    public Action OnCompleteCallback { get; set; }

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            
        // Set up close button
        closeButton.onClick.AddListener(CloseDialogue);
    }

    private void OnDestroy()
    {
        closeButton.onClick.RemoveListener(CloseDialogue);
    }

    /// <summary>
    /// Display the dialogue with the specified parameters
    /// </summary>
    public void Display(string agentName, Sprite agentImage, string dialogueText)
    {
        // Stop any ongoing typing effect
        if (_typingCoroutine != null)
        {
            StopCoroutine(_typingCoroutine);
            _typingCoroutine = null;
        }

        // Set the dialogue content
        this.agentNameText.text = agentName;
        this.agentImage.sprite = agentImage;
        this.dialogueText.text = dialogueText;
        
        // Show the panel with fade-in animation
        StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Display dialogue with a typing effect
    /// </summary>
    public void DisplayWithTypingEffect(string agentName, Sprite agentImage, string dialogueText, float typingSpeed = 0.05f)
    {
        // Set the agent info
        this.agentNameText.text = agentName;
        this.agentImage.sprite = agentImage;
        this.dialogueText.text = string.Empty;
        
        // Show the panel with fade-in animation
        StartCoroutine(FadeIn());
        
        // Start the typing effect
        _typingCoroutine = StartCoroutine(TypeText(dialogueText, typingSpeed));
    }

    /// <summary>
    /// Close the dialogue panel
    /// </summary>
    public void CloseDialogue()
    {
        StartCoroutine(FadeOut());
    }

    /// <summary>
    /// Fade in animation
    /// </summary>
    private IEnumerator FadeIn()
    {
        gameObject.SetActive(true);
        _canvasGroup.alpha = 0f;
        
        float time = 0;
        while (time < fadeInDuration)
        {
            _canvasGroup.alpha = fadeCurve.Evaluate(time / fadeInDuration);
            time += Time.deltaTime;
            yield return null;
        }
        
        _canvasGroup.alpha = 1f;
    }

    /// <summary>
    /// Fade out animation
    /// </summary>
    private IEnumerator FadeOut()
    {
        float time = 0;
        float startAlpha = _canvasGroup.alpha;
        
        while (time < fadeOutDuration)
        {
            _canvasGroup.alpha = startAlpha * (1 - fadeCurve.Evaluate(time / fadeOutDuration));
            time += Time.deltaTime;
            yield return null;
        }
        
        _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
        
        // Notify that dialogue has been closed
        OnDialogueClosed?.Invoke();
    }

    /// <summary>
    /// Type text character by character
    /// </summary>
    private IEnumerator TypeText(string text, float typingSpeed)
    {
        dialogueText.text = string.Empty;
        
        foreach (char c in text)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }
        
        _typingCoroutine = null;
    }
}