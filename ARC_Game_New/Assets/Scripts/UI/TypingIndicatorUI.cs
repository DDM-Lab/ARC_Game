using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Animated "typing…" indicator shown in an officer's conversation panel while
/// that officer's LLM is generating a response, so the director knows inference
/// is in flight and nothing is stuck.
///
/// Driven onto an existing agent-message bubble (see AgentConversationUI.
/// CreateTypingIndicator): it pulses a short dot sequence by cycling TMP's
/// maxVisibleCharacters over a fixed string. We never mutate .text per frame —
/// that would re-run layout inside the conversation's VerticalLayoutGroup every
/// tick and fight the ScrollRect, the same hazard AgentMessageUI.PlayTypingEffect
/// calls out.
/// </summary>
public class TypingIndicatorUI : MonoBehaviour
{
    private const string Dots = "●●●";

    private TextMeshProUGUI text;
    private float cycleSeconds = 0.35f;

    /// <summary>Bind to a TMP label and start pulsing. Safe to call once.</summary>
    public void Begin(TextMeshProUGUI target, float cycleSeconds = 0.35f)
    {
        text = target;
        this.cycleSeconds = Mathf.Max(0.05f, cycleSeconds);
        if (text != null)
        {
            text.text = Dots;
            text.ForceMeshUpdate();
        }
        StopAllCoroutines();
        StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        if (text == null) yield break;
        text.ForceMeshUpdate();
        int total = Mathf.Max(1, text.textInfo.characterCount);
        var wait = new WaitForSecondsRealtime(cycleSeconds);
        while (true)
        {
            for (int shown = 1; shown <= total; shown++)
            {
                text.maxVisibleCharacters = shown;
                yield return wait;
            }
            // brief hold at full before restarting the pulse
            yield return wait;
        }
    }
}
