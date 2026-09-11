using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// A <see cref="TMP_InputField"/> that survives being clicked when its text has no laid-out
/// line.
///
/// THE BUG IT FIXES. TMP_InputField.OnPointerDown asks TMP_TextUtilities for the caret index
/// under the pointer. That path is:
///
///     GetCursorIndexFromPosition -> FindNearestLine -> FindNearestCharacterOnLine
///
/// FindNearestLine starts at `closest = -1` and only improves that inside a loop over
/// `textInfo.lineCount`. With zero lines the loop never runs, it returns -1, and
/// FindNearestCharacterOnLine immediately does `textInfo.lineInfo[-1]` --
/// IndexOutOfRangeException, thrown before the field is ever activated. The player clicks the
/// box, nothing happens, and the only trace is an exception in the Player log:
///
///     IndexOutOfRangeException: Index was outside the bounds of the array.
///       at TMPro.TMP_TextUtilities.FindNearestCharacterOnLine
///       at TMPro.TMP_InputField.OnPointerDown
///
/// Zero lines is the normal state of an EMPTY field, so "you cannot type in this box" is the
/// symptom. ForceMeshUpdate does not help: empty text still generates no lines, which is why
/// the earlier fix at the construction site (BUG_REPORTS A12) did not hold.
///
/// The guard is deliberately narrow -- it only skips the caret lookup, which is meaningless
/// with no text anyway, and still activates the field so typing works.
/// </summary>
public class SafeInputField : TMP_InputField
{
    public override void OnPointerDown(PointerEventData eventData)
    {
        TMP_TextInfo info = textComponent != null ? textComponent.textInfo : null;
        if (info == null || info.lineCount == 0 || info.lineInfo == null)
        {
            // Nothing to put a caret in front of. Focus the field and let the caret sit at 0.
            ActivateInputField();
            caretPosition = 0;
            return;
        }
        base.OnPointerDown(eventData);
    }
}
