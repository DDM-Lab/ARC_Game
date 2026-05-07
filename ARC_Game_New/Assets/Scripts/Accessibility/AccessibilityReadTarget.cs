using UnityEngine;
using TMPro;

/// <summary>
/// Attach to any panel/container whose full area should trigger TTS on right-click,
/// and point targetText at the TMP component to read. Used when the TMP rect is
/// smaller than its container (e.g. during a typing effect).
/// </summary>
public class AccessibilityReadTarget : MonoBehaviour
{
    public TMP_Text targetText;
}
