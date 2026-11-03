// Bridge between xterm.js and the WinUI host. Messages are JSON strings: see TerminalProtocol.cs.
(function () {
  const host = window.chrome && window.chrome.webview;
  const send = (m) => host && host.postMessage(JSON.stringify(m));

  const term = new Terminal({
    allowProposedApi: true,
    allowTransparency: true,
    cursorBlink: true,
    scrollback: 10000,
    fontFamily: '"Cascadia Mono", Consolas, monospace',
    fontSize: 14,
    macOptionIsMeta: true,
    rightClickSelectsWord: false,
    windowsPty: { backend: 'conpty' },
  });
  const fit = new FitAddon.FitAddon();
  term.loadAddon(fit);
  term.open(document.getElementById('term'));

  const bytesFromBase64 = (b64) => {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
  };

  let lastCols = 0, lastRows = 0;
  const refit = () => {
    try { fit.fit(); } catch (e) { return; }
    if (term.cols !== lastCols || term.rows !== lastRows) {
      lastCols = term.cols; lastRows = term.rows;
      send({ t: 'size', cols: term.cols, rows: term.rows });
    }
  };

  let pending = null;
  new ResizeObserver(() => {
    if (pending) cancelAnimationFrame(pending);
    pending = requestAnimationFrame(() => { pending = null; refit(); });
  }).observe(document.getElementById('term'));

  term.onData((d) => send({ t: 'in', d }));
  term.onBinary((d) => send({ t: 'bin', d }));

  const copySelection = () => {
    const text = term.getSelection();
    if (text) {
      navigator.clipboard.writeText(text).catch(() => {});
      return true;
    }
    return false;
  };
  const paste = () => navigator.clipboard.readText().then((t) => t && term.paste(t)).catch(() => {});

  // Windows Terminal behaviour: Ctrl+C copies when there is a selection (otherwise it is SIGINT),
  // Ctrl+Shift+C/V always copy/paste, Ctrl+V pastes.
  term.attachCustomKeyEventHandler((e) => {
    if (e.type !== 'keydown' || !e.ctrlKey || e.altKey) return true;
    const k = e.key.toLowerCase();
    if (k === 'c' && (e.shiftKey || term.hasSelection())) {
      copySelection();
      term.clearSelection();
      return false;
    }
    if (k === 'v') {
      paste();
      return false;
    }
    return true;
  });

  document.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (term.hasSelection()) {
      copySelection();
      term.clearSelection();
    } else {
      paste();
    }
  });

  const apply = (m) => {
    if (m.theme) {
      term.options.theme = m.theme;
      document.body.style.background = m.theme.background;
    }
    if (m.fontFamily) term.options.fontFamily = m.fontFamily;
    if (m.fontSize) term.options.fontSize = m.fontSize;
    refit();
  };

  host.addEventListener('message', (e) => {
    const m = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
    switch (m.t) {
      case 'out': term.write(bytesFromBase64(m.d)); break;
      case 'style': apply(m); break;
      case 'focus': term.focus(); break;
      case 'clear': term.reset(); break;
      case 'notice': term.write('\r\n\x1b[2m' + m.d + '\x1b[0m\r\n'); break;
    }
  });

  document.fonts.ready.then(() => {
    refit();
    send({ t: 'ready', cols: term.cols, rows: term.rows });
  });
})();
