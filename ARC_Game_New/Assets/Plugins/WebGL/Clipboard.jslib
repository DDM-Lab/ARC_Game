mergeInto(LibraryManager.library, {

    // ── Read clipboard text and send it back to Unity ────────────────────────
    // Uses the async Clipboard API (requires a secure context / user gesture).
    // On success:  gameObject.SendMessage(callbackObject, onSuccess, text)
    // On failure:  gameObject.SendMessage(callbackObject, onFailure, errorMessage)
    ReadClipboardText: function (callbackObjectPtr, onSuccessPtr, onFailurePtr) {
        var callbackObject = UTF8ToString(callbackObjectPtr);
        var onSuccess      = UTF8ToString(onSuccessPtr);
        var onFailure      = UTF8ToString(onFailurePtr);

        navigator.clipboard.readText().then(function (text) {
            unityInstance.SendMessage(callbackObject, onSuccess, text);
        }).catch(function (err) {
            unityInstance.SendMessage(callbackObject, onFailure, err.toString());
        });
    }
});
