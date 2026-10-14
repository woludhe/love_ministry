// Keeps the screen on while a live stream or meeting is open, so phones don't dim
// and lock mid-session. Uses the Screen Wake Lock API (Chrome/Edge 84+, Safari 16.4+);
// browsers without it just behave as before.
(function () {
    if (!('wakeLock' in navigator)) return;

    let lock = null;

    async function acquire() {
        if (lock || document.visibilityState !== 'visible') return;
        try {
            lock = await navigator.wakeLock.request('screen');
            lock.addEventListener('release', () => { lock = null; });
        } catch {
            // Denied (e.g. low battery mode, or no user interaction yet) — retried below
        }
    }

    // The browser drops the lock whenever the tab is hidden; take it again on return
    document.addEventListener('visibilitychange', acquire);
    // Some browsers only grant it after the user has interacted with the page
    ['click', 'touchend', 'keydown'].forEach(evt =>
        document.addEventListener(evt, acquire, { passive: true }));

    acquire();
})();
