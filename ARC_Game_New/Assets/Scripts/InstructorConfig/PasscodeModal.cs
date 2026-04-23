using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Passcode gate modal.  Place in every scene that needs it (TitleScene).
///
/// HIERARCHY:
///   PasscodeModal (this script)
///     └─ ModalPanel (GameObject — the visible panel)
///          ├─ TitleText   (TMP)
///          ├─ PasscodeInput (TMP_InputField, content type: Password)
///          ├─ ErrorText   (TMP, hidden by default)
///          ├─ ConfirmButton (Button)
///          └─ CancelButton  (Button)
/// </summary>
public class PasscodeModal : MonoBehaviour
{
    [Header("UI References")]
    public GameObject        modalPanel;
    public TMP_InputField    passcodeInput;
    public TextMeshProUGUI   errorText;
    public Button            confirmButton;
    public Button            cancelButton;

    [Header("Security")]
    [Tooltip("Change this before distributing to instructors")]
    public string correctPasscode = "instructor123";

    // ─────────────────────────────────────────────────────────────────────────
    private Action _onSuccess;

    void Awake()
    {
        if (modalPanel != null)   modalPanel.SetActive(false);
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);
        if (cancelButton  != null) cancelButton.onClick.AddListener(Hide);
        if (passcodeInput != null)
            passcodeInput.onSubmit.AddListener(_ => OnConfirm());
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Open the modal; invoke onSuccess if the correct passcode is entered.</summary>
    public void Show(Action onSuccess)
    {
        _onSuccess        = onSuccess;
        passcodeInput.text = "";
        errorText.text    = "";
        modalPanel.SetActive(true);
        passcodeInput.Select();
        passcodeInput.ActivateInputField();
    }

    public void Hide()
    {
        modalPanel.SetActive(false);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    void OnConfirm()
    {
        if (passcodeInput.text == correctPasscode)
        {
            Hide();
            _onSuccess?.Invoke();
        }
        else
        {
            errorText.text     = "Incorrect passcode. Please try again.";
            passcodeInput.text = "";
            passcodeInput.Select();
            passcodeInput.ActivateInputField();
        }
    }
}
