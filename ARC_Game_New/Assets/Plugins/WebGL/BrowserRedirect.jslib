mergeInto(LibraryManager.library, {

    // ── Redirect the browser to another URL ───────────────────────────────────
    // Called from C#: EndOfGamePanel.RedirectToFollowUpSurvey() via
    //   [DllImport("__Internal")] static extern void RedirectToUrl(string url);
    RedirectToUrl: function (urlPtr) {
        var url = UTF8ToString(urlPtr);
        window.location.href = url;
    }
});
