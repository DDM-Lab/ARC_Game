// ── Log uploads for LogSender.cs (WebGL builds) ─────────────────────────────────────────────────
//
// What this adds over UnityWebRequest:
//   • gzip in the browser (CompressionStream): native code, does not block the game's frame loop.
//     Sent as Content-Type: application/gzip (NOT Content-Encoding: gzip, which a proxy may try to
//     decode); save_game_logs.py recognises the gzip bytes itself. Falls back to the plain JSON
//     when the browser has no CompressionStream or compression fails.
//   • Real upload progress (XMLHttpRequest upload events).
//   • Timeouts that measure the right thing: an attempt is abandoned when nothing has moved for
//     stallMs (no upload progress, or no reply once the body is sent) or when it has run for
//     hardLimitMs in total. A slow upload that keeps moving is not cut off. These run on browser
//     timers, which keep going while the tab is in the background (Unity's frame loop does not).
//   • A "Leave site?" prompt while the end-of-game upload runs.
//
// C# polls each upload's state every frame instead of being called back. A SendMessage callback
// would need either a GameObject name (LogSender sits on a generically named object) or the
// page's unityInstance global (not every WebGL template defines it). Polling only returns numbers
// and _malloc'd strings, as BrowserRedirect.jslib already does.
//
// ES5 syntax on purpose (see BrowserRedirect.jslib). Newer browser APIs are reached with bracket
// notation and the result JSON uses quoted keys, so a minifier cannot rename them.

var CoraUploadLibrary = {

    $CoraUploads: {
        nextId: 1,
        jobs: {},
        leaveWarning: false,

        // Job states. Must match LogSender.BrowserState.
        PREPARING: 0,   // copying / compressing the payload
        SENDING: 1,     // request body going out
        WAITING: 2,     // body sent, waiting for the server's reply
        DONE: 3,        // a reply arrived (any HTTP status)
        FAILED: 4,      // no reply: see job.error

        canCompress: function () {
            return typeof window["CompressionStream"] === "function" &&
                   typeof window["Response"] === "function" &&
                   typeof Blob !== "undefined" &&
                   typeof Blob.prototype["stream"] === "function";
        },

        onBeforeUnload: function (e) {
            e.preventDefault();
            e.returnValue = "";   // the browser shows its own "Leave site?" text
            return "";
        },

        stopTimer: function (job) {
            if (job.timer) {
                clearInterval(job.timer);
                job.timer = null;
            }
        },

        fail: function (job, reason) {
            if (job.state >= CoraUploads.DONE) return;
            job.state = CoraUploads.FAILED;
            job.error = reason;
            CoraUploads.stopTimer(job);
            var xhr = job.xhr;
            job.xhr = null;
            if (xhr) {
                try { xhr.abort(); } catch (e) { }
            }
        },

        check: function (job) {
            if (job.state >= CoraUploads.DONE) {
                CoraUploads.stopTimer(job);
                return;
            }
            var now = Date.now();
            if (now - job.started > job.hardLimitMs) {
                CoraUploads.fail(job, "hard_limit");
            } else if (job.state !== CoraUploads.PREPARING && now - job.lastActivity > job.stallMs) {
                CoraUploads.fail(job, job.state === CoraUploads.SENDING ? "stalled" : "no_response");
            }
        },

        send: function (job, body, compressed) {
            var xhr = new XMLHttpRequest();
            job.xhr = xhr;
            job.compressed = compressed;
            job.total = body.length;
            job.state = CoraUploads.SENDING;
            job.lastActivity = Date.now();

            xhr.open("POST", job.url, true);
            xhr.setRequestHeader("Content-Type", compressed ? "application/gzip" : "application/json");

            xhr.upload.onprogress = function (e) {
                if (e.loaded > job.sent) {
                    job.sent = e.loaded;
                    job.lastActivity = Date.now();
                }
            };
            xhr.upload.onload = function () {
                job.sent = job.total;
                if (job.state === CoraUploads.SENDING) job.state = CoraUploads.WAITING;
                job.lastActivity = Date.now();
            };
            xhr.onreadystatechange = function () {
                if (xhr.readyState >= 2 && job.state === CoraUploads.SENDING) {
                    // The server answered (possibly before reading the whole body, e.g. a 413).
                    job.state = CoraUploads.WAITING;
                }
                job.lastActivity = Date.now();
            };
            xhr.onprogress = function () {
                job.lastActivity = Date.now();   // reply bytes arriving
            };
            xhr.onload = function () {
                if (job.state >= CoraUploads.DONE) return;
                job.status = xhr.status;
                job.response = xhr.responseText || "";
                job.state = CoraUploads.DONE;
                job.xhr = null;
                CoraUploads.stopTimer(job);
            };
            xhr.onerror = function () { CoraUploads.fail(job, "network"); };
            xhr.onabort = function () { CoraUploads.fail(job, "aborted"); };
            xhr.ontimeout = function () { CoraUploads.fail(job, "hard_limit"); };

            xhr.send(body);
        }
    },

    // 1 if uploads can be gzipped in this browser, else 0.
    CoraUploadCanCompress__deps: ["$CoraUploads"],
    CoraUploadCanCompress: function () {
        return CoraUploads.canCompress() ? 1 : 0;
    },

    // Starts one upload attempt of `length` bytes of UTF-8 JSON at dataPtr. Returns a handle > 0,
    // or 0 if the attempt could not be started (C# then falls back to UnityWebRequest).
    CoraUploadStart__deps: ["$CoraUploads"],
    CoraUploadStart: function (urlPtr, dataPtr, length, gzip, stallMs, hardLimitMs) {
        try {
            var id = CoraUploads.nextId++;
            var job = {
                url: UTF8ToString(urlPtr),
                state: CoraUploads.PREPARING,
                sent: 0,
                total: length,
                payloadBytes: length,
                compressed: false,
                status: 0,
                response: "",
                error: "",
                started: Date.now(),
                lastActivity: Date.now(),
                stallMs: stallMs,
                hardLimitMs: hardLimitMs,
                xhr: null,
                timer: null
            };
            // Copy out of the heap now: once this returns, C# may move or reuse that memory.
            var bytes = HEAPU8.slice(dataPtr, dataPtr + length);
            CoraUploads.jobs[id] = job;
            job.timer = setInterval(function () { CoraUploads.check(job); }, 1000);

            if (gzip && CoraUploads.canCompress()) {
                var stream = new Blob([bytes])["stream"]()["pipeThrough"](new window["CompressionStream"]("gzip"));
                new window["Response"](stream)["arrayBuffer"]().then(function (buffer) {
                    if (job.state !== CoraUploads.PREPARING) return;   // abandoned while compressing
                    CoraUploads.send(job, new Uint8Array(buffer), true);
                }, function () {
                    if (job.state !== CoraUploads.PREPARING) return;
                    CoraUploads.send(job, bytes, false);   // compression failed: send it as it is
                });
            } else {
                CoraUploads.send(job, bytes, false);
            }
            return id;
        } catch (e) {
            return 0;
        }
    },

    // State of an upload (CoraUploads.PREPARING .. FAILED), or -1 for an unknown handle.
    CoraUploadState__deps: ["$CoraUploads"],
    CoraUploadState: function (id) {
        var job = CoraUploads.jobs[id];
        return job ? job.state : -1;
    },

    // Request body bytes sent so far (compressed bytes, if compressed).
    CoraUploadSentBytes__deps: ["$CoraUploads"],
    CoraUploadSentBytes: function (id) {
        var job = CoraUploads.jobs[id];
        return job ? job.sent : 0;
    },

    // Request body size (the compressed size once compression has finished).
    CoraUploadTotalBytes__deps: ["$CoraUploads"],
    CoraUploadTotalBytes: function (id) {
        var job = CoraUploads.jobs[id];
        return job ? job.total : 0;
    },

    // The outcome as JSON; read it once the state is DONE or FAILED.
    CoraUploadResult__deps: ["$CoraUploads"],
    CoraUploadResult: function (id) {
        var job = CoraUploads.jobs[id];
        var result = JSON.stringify(job ? {
            "state": job.state,
            "status": job.status,
            "error": job.error,
            "response": job.response,
            "compressed": job.compressed,
            "bodyBytes": job.total,
            "payloadBytes": job.payloadBytes,
            "sentBytes": job.sent,
            "seconds": (Date.now() - job.started) / 1000
        } : { "state": -1, "error": "unknown_handle" });
        var size = lengthBytesUTF8(result) + 1;
        var buffer = _malloc(size);
        stringToUTF8(result, buffer, size);
        return buffer;
    },

    // Abandons an upload (no effect once it has finished).
    CoraUploadAbort__deps: ["$CoraUploads"],
    CoraUploadAbort: function (id) {
        var job = CoraUploads.jobs[id];
        if (job) CoraUploads.fail(job, "aborted");
    },

    // Forgets an upload, abandoning it first if it is still running.
    CoraUploadRelease__deps: ["$CoraUploads"],
    CoraUploadRelease: function (id) {
        var job = CoraUploads.jobs[id];
        if (!job) return;
        CoraUploads.fail(job, "aborted");
        delete CoraUploads.jobs[id];
    },

    // 1: ask "Leave site?" before the page is closed or reloaded. 0: stop asking.
    CoraUploadSetLeaveWarning__deps: ["$CoraUploads"],
    CoraUploadSetLeaveWarning: function (on) {
        if (on && !CoraUploads.leaveWarning) {
            window.addEventListener("beforeunload", CoraUploads.onBeforeUnload);
        } else if (!on && CoraUploads.leaveWarning) {
            window.removeEventListener("beforeunload", CoraUploads.onBeforeUnload);
        }
        CoraUploads.leaveWarning = !!on;
    }
};

mergeInto(LibraryManager.library, CoraUploadLibrary);
