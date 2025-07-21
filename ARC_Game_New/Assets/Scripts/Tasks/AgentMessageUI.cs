using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Agent Message UI Component
public class AgentMessageUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image agentAvatar;
    public TextMeshProUGUI messageText;
    public Image speechBubble;

    private AgentMessage message;
    private string fullMessage;

    public void Initialize(AgentMessage agentMessage)
    {
        message = agentMessage;
        fullMessage = agentMessage.messageText;

        // Set avatar
        if (agentAvatar != null && agentMessage.agentAvatar != null)
            agentAvatar.sprite = agentMessage.agentAvatar;

        // Initially hide text for typing effect
        if (messageText != null)
            messageText.text = "";
    }

    public IEnumerator PlayTypingEffect(float typingSpeed)
    {
        if (messageText == null || string.IsNullOrEmpty(fullMessage))
            yield break;

        messageText.text = "";

        for (int i = 0; i <= fullMessage.Length; i++)
        {
            messageText.text = fullMessage.Substring(0, i);
            yield return new WaitForSeconds(typingSpeed);
        }
    }

    public void ShowFullMessage()
    {
        if (messageText != null)
            messageText.text = fullMessage;
    }
}

