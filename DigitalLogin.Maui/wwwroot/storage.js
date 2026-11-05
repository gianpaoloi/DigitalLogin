// Small browser helpers for the Blazor page. Every call is wrapped so that a missing or blocked
// localStorage (private window, policy, cleared site data) degrades to "not remembered" instead
// of an error in the UI.
window.digitalLoginStore = {
    get: (key) => {
        try { return localStorage.getItem(key); } catch { return null; }
    },

    set: (key, value) => {
        try { localStorage.setItem(key, value); } catch { /* not remembered */ }
    },

    copy: async (text) => {
        const value = text ?? '';
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(value);
            return;
        }

        // Fallback for contexts without the async clipboard API.
        const textarea = document.createElement('textarea');
        textarea.value = value;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        textarea.remove();
    }
};
