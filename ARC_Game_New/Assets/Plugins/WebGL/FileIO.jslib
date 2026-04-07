mergeInto(LibraryManager.library, {

    // ── Download a text file to the user's computer ──────────────────────────
    // Called from C#: FileIOBridge.DownloadTextFile(filename, content)
    DownloadTextFile: function (filenamePtr, contentPtr) {
        var filename = UTF8ToString(filenamePtr);
        var content  = UTF8ToString(contentPtr);

        var blob = new Blob([content], { type: "application/json" });
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
    OpenFilePicker: function (callbackObjectPtr, callbackMethodPtr) {
        var callbackObject = UTF8ToString(callbackObjectPtr);
        var callbackMethod = UTF8ToString(callbackMethodPtr);

        var input = document.createElement("input");
        input.type   = "file";
        input.accept = ".json,application/json";
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
