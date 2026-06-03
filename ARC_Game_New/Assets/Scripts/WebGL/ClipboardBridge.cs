using System;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;

/// <summary>
/// Bridges the browser Clipboard API to Unity TMP_InputFields.
///
/// USAGE
///   1. Add this component to any persistent GameObject (e.g. the same one that
///      holds FileIOBridge).
///   2. Call ClipboardBridge.Instance.PasteInto(myTMPInputField) from a button's
///      onClick, or wire PasteIntoTarget() in the Inspector after assigning
///      targetInputField.
///
/// WHY THIS IS NEEDED
///   Unity WebGL intercepts keyboard events before the browser sees them, so the
///   standard Ctrl+V shortcut never reaches the browser clipboard handler and
///   TMP_InputField receives nothing.  The only reliable fix is to call the async
///   navigator.clipboard.readText() API from JavaScript on a user-gesture event
///   (button click), then push the result back into the input field via SendMessage.
/// </summary>
public class ClipboardBridge : MonoBehaviour
{
    public static ClipboardBridge Instance { get; private set; }

    [Tooltip("Optional: assign a TMP_InputField here and call PasteIntoTarget() from a button.")]
    public TMP_InputField targetInputField;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void ReadClipboardText(string callbackObject, string onSuccess, string onFailure);
#endif

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Read the clipboard and paste the result into the given input field.</summary>
    public void PasteInto(TMP_InputField field)
    {
        if (field == null) { Debug.LogWarning("[ClipboardBridge] PasteInto called with null field."); return; }
        targetInputField = field;
        RequestClipboard();
    }

    /// <summary>Inspector-friendly version: reads clipboard into the assigned targetInputField.</summary>
    public void PasteIntoTarget()
    {
        PasteInto(targetInputField);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    void RequestClipboard()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ReadClipboardText(gameObject.name, nameof(OnClipboardSuccess), nameof(OnClipboardFailure));
#else
        // Editor / Standalone: use the Unity clipboard directly
        string text = GUIUtility.systemCopyBuffer;
        ApplyText(text);
#endif
    }

    // Called by jslib on success (must be public, single string param)
    public void OnClipboardSuccess(string text)
    {
        ApplyText(text);
    }

    // Called by jslib on failure (must be public, single string param)
    public void OnClipboardFailure(string error)
    {
        Debug.LogWarning($"[ClipboardBridge] Clipboard read failed: {error}");
    }

    void ApplyText(string text)
    {
        if (targetInputField == null) return;
        targetInputField.text = text;
        targetInputField.caretPosition = text.Length;
        targetInputField.ForceMeshUpdate();
    }
}
