using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerMessageUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image speechBubble;
    public TextMeshProUGUI messageText;
    
    public void Initialize(PlayerMessage message)
    {
        if (messageText != null)
            messageText.text = message.messageText;
    }
}