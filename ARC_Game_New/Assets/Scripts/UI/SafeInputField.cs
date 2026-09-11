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
/// The guard is deliberately narrow: the base method RUNS, and only its one documented
/// blow-up is caught. Pre-empting it instead also skipped the selection and caret bookkeeping
/// OnPointerDown performs, which left the field accepting keystrokes while never refreshing
/// its display.
/// </summary>
public class SafeInputField : TMP_InputField
{
    public override void OnPointerDown(PointerEventData eventData)
    {
        // RUN THE REAL THING, and only catch the one failure. An earlier version of this
        // pre-empted the base method whenever the label had no line, which also skipped the
        // selection and caret bookkeeping OnPointerDown does. The field then accepted
        // keystrokes while never refreshing its display -- typing "registered" but showed
        // nothing. Letting the base method run and catching its one documented blow-up keeps
        // every other side effect intact.
        try
        {
            base.OnPointerDown(eventData);
        }
        catch (System.IndexOutOfRangeException)
        {
            // FindNearestLine returned -1 (no laid-out line, i.e. an empty field) and
            // FindNearestCharacterOnLine indexed lineInfo[-1]. Nothing to put a caret in
            // front of; just focus the field so typing works.
            ActivateInputField();
        }
    }
}
