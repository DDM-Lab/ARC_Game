using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

public class StorylineManager : MonoBehaviour
{
    [FormerlySerializedAs("mayorSprite")]
    [Header("Character Sprites")]
    [SerializeField] private Sprite disasterOfficerSprite;
    [SerializeField] private Sprite workforceOfficerSprite;
    [SerializeField] private Sprite healthcareOfficerSprite;
    
    [Header("Timing Settings")]
    [SerializeField] private float initialDelay = 2.0f; // Time before first dialogue appears
    [SerializeField] private bool playOnStart = true;
    
    private void Start()
    {
        if (playOnStart)
        {
            StartCoroutine(PlayIntroductionSequence());
        }
    }
    
    // Method to manually trigger the introduction sequence
    public void TriggerIntroduction()
    {
        StartCoroutine(PlayIntroductionSequence());
    }
    
    private IEnumerator PlayIntroductionSequence()
    {
        yield return new WaitForSeconds(initialDelay);
        
        DialogueManager.Instance.ShowDialogueWithTypingEffect(
            "Disaster Officer",
            disasterOfficerSprite,
            "Welcome to the ARK Simulation! As the new emergency coordinator, you'll be responsible for managing our city's response to various disasters.",
            0.04f,
            () => StartCoroutine(ShowSecondDialogue())
        );
    }
    
    private IEnumerator ShowSecondDialogue()
    {
        yield return new WaitForSeconds(0.5f);
        
        DialogueManager.Instance.ShowDialogueWithTypingEffect(
            "Workforce Officer",
            workforceOfficerSprite,
            "Our meteorological data indicates a severe flood risk in the coming days. We need to prepare the city immediately to minimize damage and casualties.",
            0.04f,
            () => StartCoroutine(ShowThirdDialogue())
        );
    }
    
    private IEnumerator ShowThirdDialogue()
    {
        yield return new WaitForSeconds(0.5f);
        
        DialogueManager.Instance.ShowDialogueWithTypingEffect(
            "Healthcare Officer",
            healthcareOfficerSprite,
            "To get started, use the bottom toolbar to assign workers to critical tasks. Click on 'Facilities' to build shelters and 'Tasks' to manage emergency operations. Good luck!",
            0.04f,
            () => {
                // This callback runs when the player closes the final dialogue
                Debug.Log("Introduction sequence completed!");
                // TODO: Trigger a tutorial highlight of the UI elements here
                HighlightUIElements();
            }
        );
    }
    
    private void HighlightUIElements()
    {
        // This method should highlight key UI elements after the dialogue
        Debug.Log("Highlighting key UI elements for the tutorial");
    }
}
