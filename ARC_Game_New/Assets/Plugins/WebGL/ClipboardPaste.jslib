// WebGL clipboard / key-entry bridge for the ARC launcher.
//
// window.prompt() is blocked in newer Chrome/Emscripten builds because the
// Unity C# → jslib call chain is no longer treated as a direct user gesture.
//
// Instead we inject a real HTML <input> overlay directly on the page.
// A real DOM input receives Ctrl+V / Cmd+V natively with no permissions,
// works in iframes, and works on Safari. When the user confirms (Enter or
// the overlay confirm button), the value is sent to Unity via SendMessage
// and the overlay is removed.
//
// ClipboardInit() also hooks document 'paste' as a bonus path for the rare
// browser that delivers the event directly — routes to OnClipboardPasted.
mergeInto(LibraryManager.library, {

  // Show an HTML input overlay; result delivered via OnClipboardPasted.
  // keepOpen=1 means the overlay stays until the user explicitly confirms.
  PromptApiKey: function () {
    if (document.getElementById('__arcKeyOverlay')) return; // already open

    // ── overlay backdrop ────────────────────────────────────────────────
    var overlay = document.createElement('div');
    overlay.id = '__arcKeyOverlay';
    Object.assign(overlay.style, {
      position: 'fixed', inset: '0', background: 'rgba(0,0,0,0.55)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      zIndex: '99999', fontFamily: 'sans-serif'
    });

    // ── card ────────────────────────────────────────────────────────────
    var card = document.createElement('div');
    Object.assign(card.style, {
      background: '#1e2029', borderRadius: '10px', padding: '28px 32px',
      width: '420px', boxShadow: '0 8px 32px rgba(0,0,0,0.6)',
      display: 'flex', flexDirection: 'column', gap: '14px'
    });

    var title = document.createElement('div');
    title.textContent = 'Enter / Paste API Key';
    Object.assign(title.style, {
      color: '#edf0f5', fontSize: '18px', fontWeight: 'bold'
    });

    var hint = document.createElement('div');
    hint.textContent = 'Click the box and press Ctrl+V (or Cmd+V on Mac) to paste.';
    Object.assign(hint.style, { color: '#9aa0ad', fontSize: '13px' });

    var input = document.createElement('input');
    input.type = 'text';
    input.placeholder = 'Paste or type your API key here…';
    Object.assign(input.style, {
      width: '100%', boxSizing: 'border-box',
      padding: '10px 12px', borderRadius: '6px', border: '1px solid #444',
      background: '#0d0e11', color: '#edf0f5', fontSize: '15px', outline: 'none'
    });

    var btnRow = document.createElement('div');
    Object.assign(btnRow.style, { display: 'flex', gap: '10px', justifyContent: 'flex-end' });

    var cancelBtn = document.createElement('button');
    cancelBtn.textContent = 'Cancel';
    Object.assign(cancelBtn.style, {
      padding: '8px 20px', borderRadius: '6px', border: 'none',
      background: '#3a3d47', color: '#ccc', cursor: 'pointer', fontSize: '14px'
    });

    var confirmBtn = document.createElement('button');
    confirmBtn.textContent = 'Confirm';
    Object.assign(confirmBtn.style, {
      padding: '8px 20px', borderRadius: '6px', border: 'none',
      background: '#3472da', color: '#fff', cursor: 'pointer', fontSize: '14px', fontWeight: 'bold'
    });

    // ── helpers ─────────────────────────────────────────────────────────
    function confirm() {
      var val = input.value.trim();
      document.body.removeChild(overlay);
      if (val) SendMessage('[ServerLauncher]', 'OnClipboardPasted', val);
    }
    function cancel() { document.body.removeChild(overlay); }

    confirmBtn.addEventListener('click', confirm);
    cancelBtn.addEventListener('click', cancel);
    input.addEventListener('keydown', function(e) {
      if (e.key === 'Enter') confirm();
      if (e.key === 'Escape') cancel();
    });
    // Clicking outside the card cancels
    overlay.addEventListener('click', function(e) { if (e.target === overlay) cancel(); });

    btnRow.appendChild(cancelBtn);
    btnRow.appendChild(confirmBtn);
    card.appendChild(title);
    card.appendChild(hint);
    card.appendChild(input);
    card.appendChild(btnRow);
    overlay.appendChild(card);
    document.body.appendChild(overlay);

    // Autofocus after a tick so the browser registers it as user-initiated
    setTimeout(function() { input.focus(); }, 50);
  },

  ClipboardInit: function () {
    if (window.__arcPasteHooked) return;
    window.__arcPasteHooked = true;
    document.addEventListener('paste', function (e) {
      try {
        var t = (e.clipboardData || window.clipboardData).getData('text');
        if (t) SendMessage('[ServerLauncher]', 'OnClipboardPasted', t);
      } catch (err) { /* ignore */ }
    }, true);
  }
});
