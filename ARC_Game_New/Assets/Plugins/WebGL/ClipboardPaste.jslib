// WebGL clipboard / key-entry bridge for the ARC launcher.
//
// Unity WebGL can't reliably paste into a TMP_InputField: the keyboard capture
// that lets you type also swallows Cmd/Ctrl+V, and navigator.clipboard.readText()
// is permission-blocked on Safari. The reliable workaround is a native browser
// prompt() dialog — a real OS text box with full paste + typing support and no
// permission restrictions — whose value is returned straight to Unity.
//
// PromptApiKey() returns the entered string (empty if cancelled).
// ClipboardInit() additionally hooks the document 'paste' event as a bonus path
// for browsers that do deliver it; it routes text to OnClipboardPasted.
mergeInto(LibraryManager.library, {
  PromptApiKey: function () {
    var s = window.prompt('Paste or type your API key:', '');
    if (s === null) s = '';
    var size = lengthBytesUTF8(s) + 1;
    var buffer = _malloc(size);
    stringToUTF8(s, buffer, size);
    return buffer;
  },

  ClipboardInit: function () {
    if (window.__arcPasteHooked) return;
    window.__arcPasteHooked = true;
    document.addEventListener('paste', function (e) {
      try {
        var t = (e.clipboardData || window.clipboardData).getData('text');
        if (t) SendMessage('[ServerLauncher]', 'OnClipboardPasted', t);
      } catch (err) { /* ignore — prompt() is the reliable path */ }
    }, true);
  }
});
