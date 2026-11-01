// terminal.js - minimal helpers for the crowdsec-cli terminal page.
// Loaded from Pages/_Host.cshtml alongside threatmap.js.
window.csTerminal = window.csTerminal || {};

// Scroll the terminal output element to its bottom so the latest line is
// always visible after a re-render. Called from ConsolePage.razor via IJSRuntime.
window.csTerminal.scrollToEnd = function (el) {
    if (!el) return;
    try { el.scrollTop = el.scrollHeight; } catch (_) { /* ignore */ }
};
