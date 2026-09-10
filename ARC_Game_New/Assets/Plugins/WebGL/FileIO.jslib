mergeInto(LibraryManager.library, {

    // ── Download a text file to the user's computer ──────────────────────────
    // Called from C#: FileIOBridge.DownloadTextFile(filename, content)
    DownloadTextFile: function (filenamePtr, contentPtr) {
        var filename = UTF8ToString(filenamePtr);
        var content  = UTF8ToString(contentPtr);

        // octet-stream, not application/json: a .cora download was being handed to the
        // browser as JSON, and some browsers helpfully rename it to .json on save.
        var blob = new Blob([content], { type: "application/octet-stream" });
        var url  = URL.createObjectURL(blob);

        var a = document.createElement("a");
        a.href     = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    // ── Open a file picker and send the text content back to Unity ──────────
    // When the user picks a file, calls back:
    //   gameObject.SendMessage(callbackObject, callbackMethod, fileContent)
    // `acceptPtr` is the <input accept=...> filter, e.g. ".cora" or ".json,application/json".
    // It used to be hardcoded to JSON, which made a .cora file UNPICKABLE in the browser --
    // the picker simply greyed it out and the user had no way to tell why.
    OpenFilePicker: function (callbackObjectPtr, callbackMethodPtr, acceptPtr) {
        var callbackObject = UTF8ToString(callbackObjectPtr);
        var callbackMethod = UTF8ToString(callbackMethodPtr);
        var accept         = acceptPtr ? UTF8ToString(acceptPtr) : "";

        var input = document.createElement("input");
        input.type   = "file";
        if (accept) input.accept = accept;
        input.style.display = "none";

        input.onchange = function (e) {
            var file   = e.target.files[0];
            if (!file) return;

            var reader = new FileReader();
            reader.onload = function (evt) {
                var text = evt.target.result;
                // Send to Unity MonoBehaviour on the named GameObject
                unityInstance.SendMessage(callbackObject, callbackMethod, text);
            };
            reader.readAsText(file);
            document.body.removeChild(input);
        };

        document.body.appendChild(input);
        input.click();
    }
});
