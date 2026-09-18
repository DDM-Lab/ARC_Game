using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime self-check for the launcher's input fields, so "can you actually see what you
/// type" can be ANSWERED rather than guessed at from a screenshot.
///
/// Opt-in only: pass -corauitest on the command line. It never runs for a player.
///
///     "Build/Client/macOS/ARC_Game.app/Contents/MacOS/<exe>" -corauitest -logFile out.log
///
/// It writes a value into each TMP_InputField the way a keystroke does and then reports what
/// the VISIBLE label ended up holding. A field whose `text` updates while its
/// textComponent stays empty is the exact failure this exists to catch: input is accepted and
/// stored, and nothing is drawn.
/// </summary>
public static class UiSelfTest
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        string[] args = Environment.GetCommandLineArgs();
        bool wanted = Environment.GetEnvironmentVariable("CORA_UI_TEST") == "1";
        foreach (string a in args)
            if (a == "-corauitest") { wanted = true; break; }
        if (!wanted)
        {
            // One line so a missed trigger is diagnosable instead of silent.
            Debug.Log($"[UiSelfTest] not armed (args: {string.Join(" ", args)})");
            return;
        }

        var go = new GameObject("[UiSelfTest]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
    }

    class Runner : MonoBehaviour
    {
        IEnumerator Start()
        {
            // Let the launcher build its UI and LoadPrefs run.
            yield return new WaitForSecondsRealtime(2.5f);

            var fields = UnityEngine.Object.FindObjectsOfType<TMP_InputField>(true);
            Debug.Log($"[UiSelfTest] found {fields.Length} TMP_InputField(s)");

            foreach (var f in fields)
            {
                string owner = f.transform.parent != null ? f.transform.parent.name : "?";
                bool hasLabel = f.textComponent != null;
                string before = hasLabel ? f.textComponent.text : "<null textComponent>";

                const string probe = "PROBE123";
                f.text = probe;
                f.ForceLabelUpdate();
                Canvas.ForceUpdateCanvases();

                // Let a full frame pass so TMP actually regenerates the mesh; reading
                // characterCount in the same frame reports 0 even when rendering is fine,
                // which made an earlier run of this test lie.
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;

                string shown = hasLabel ? f.textComponent.text : "<null textComponent>";
                int chars = hasLabel && f.textComponent.textInfo != null
                          ? f.textComponent.textInfo.characterCount : -1;
                bool active = f.gameObject.activeInHierarchy;
                string font = hasLabel && f.textComponent.font != null
                            ? f.textComponent.font.name : "<no font>";
                Vector2 size = hasLabel ? f.textComponent.rectTransform.rect.size : Vector2.zero;
                // A password field draws asteriskChar, NOT the typed text. If the font has no
                // glyph for it, every keystroke is stored and renders as nothing.
                bool hasAsterisk = hasLabel && f.textComponent.font != null
                                 && f.textComponent.font.HasCharacter(f.asteriskChar);
                bool visible = hasLabel && active && chars > 0;

                Debug.Log($"[UiSelfTest] field under '{owner}': active={active} enabled={f.enabled} "
                        + $"interactable={f.interactable} targetGraphic={(f.targetGraphic != null)} "
                        + $"contentType={f.contentType} font={font} size={size} "
                        + $"asterisk='{f.asteriskChar}' fontHasAsterisk={hasAsterisk} "
                        + $"labelBefore='{before}' value='{f.text}' label='{shown}' chars={chars} "
                        + $"=> {(visible ? "VISIBLE" : "NOT VISIBLE")}");

                f.text = "";
                f.ForceLabelUpdate();
            }

            Debug.Log("[UiSelfTest] done");
        }
    }
}
