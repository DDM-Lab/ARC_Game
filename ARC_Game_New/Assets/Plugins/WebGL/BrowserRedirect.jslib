mergeInto(LibraryManager.library, {

    // ── Redirect the browser to another URL ───────────────────────────────────
    // Called from C#: EndOfGamePanel.RedirectToFollowUpSurvey() via
    //   [DllImport("__Internal")] static extern void RedirectToUrl(string url);
    RedirectToUrl: function (urlPtr) {
        var url = UTF8ToString(urlPtr);
        window.location.href = url;
    },

    // ── The participant's time zone, straight from the browser ────────────────
    // Called from C#: GameLogPanel.LogClientTimeZone() via
    //   [DllImport("__Internal")] static extern string GetBrowserTimeInfo();
    // Asked of the browser rather than .NET, since TimeZoneInfo.Local isn't reliable in WebGL.
    // Returns e.g. "zone=America/New_York, utcOffset=-04:00, local=2026-09-23 15:22:16".
    // (ES5 syntax on purpose — this file goes through Emscripten's older JS processing.)
    GetBrowserTimeInfo: function () {
        var zone = "";
        try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone || ""; } catch (e) {}

        var now = new Date();
        var offsetMinutes = -now.getTimezoneOffset();   // getTimezoneOffset() is minutes WEST of UTC
        var absMinutes = Math.abs(offsetMinutes);
        var pad = function (n) { return (n < 10 ? "0" : "") + n; };
        var offset = (offsetMinutes >= 0 ? "+" : "-") + pad(Math.floor(absMinutes / 60)) + ":" + pad(absMinutes % 60);
        var local = now.getFullYear() + "-" + pad(now.getMonth() + 1) + "-" + pad(now.getDate()) + " " +
                    pad(now.getHours()) + ":" + pad(now.getMinutes()) + ":" + pad(now.getSeconds());

        var info = "zone=" + (zone || "unknown") + ", utcOffset=" + offset + ", local=" + local;
        var size = lengthBytesUTF8(info) + 1;
        var buffer = _malloc(size);
        stringToUTF8(info, buffer, size);
        return buffer;
    }
});
