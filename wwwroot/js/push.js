/* Push alerts on this phone/browser.
   - Bell dropdown row (#pushRow) and Notifications page card (#pushSettings) let the user turn alerts on or off.
   - iPhone: alerts only work in the Home Screen app (iOS 16.4+), so Safari tabs get an "install the app" hint instead.
   - When alerts are already allowed, the subscription is re-linked to whoever is signed in (once per session),
     and it is unlinked on Log out so the next person on a shared phone doesn't get someone else's alerts.
   Config comes from #pushConfig in _NavBar.cshtml (public VAPID key, user id, antiforgery token). */
(function () {
    const cfg = document.getElementById('pushConfig');
    if (!cfg || !cfg.dataset.key) return;

    const isIos = /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    const installed = navigator.standalone === true || window.matchMedia('(display-mode: standalone)').matches;
    const supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    const token = () => (cfg.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';
    const syncKey = 'pushSynced';

    function keyBytes(b64url) {
        const pad = '='.repeat((4 - b64url.length % 4) % 4);
        const raw = atob((b64url + pad).replace(/-/g, '+').replace(/_/g, '/'));
        return Uint8Array.from(raw, c => c.charCodeAt(0));
    }

    function post(url, sub, extra) {
        const json = sub.toJSON();
        return fetch(url + (extra || ''), {
            method: 'POST',
            keepalive: true,
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
            body: JSON.stringify({ endpoint: json.endpoint, p256dh: json.keys && json.keys.p256dh, auth: json.keys && json.keys.auth })
        });
    }

    async function currentSub() {
        const reg = await navigator.serviceWorker.ready;
        return reg.pushManager.getSubscription();
    }

    async function subscribe(welcome) {
        const reg = await navigator.serviceWorker.ready;
        let sub = await reg.pushManager.getSubscription();
        if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(cfg.dataset.key) });
        const res = await post('/Push/Subscribe', sub, welcome ? '?welcome=true' : '');
        if (!res.ok) {
            // The server didn't save it, so don't leave the page claiming alerts are on.
            // Permission stays granted; the next page load signs up again without asking.
            await sub.unsubscribe().catch(function () { });
            throw new Error('Subscribe failed: ' + res.status);
        }
        try { sessionStorage.setItem(syncKey, cfg.dataset.user); } catch (e) { }
        return sub;
    }

    async function unsubscribe() {
        const sub = await currentSub();
        if (sub) {
            try { await post('/Push/Unsubscribe', sub); } catch (e) { }
            await sub.unsubscribe();
        }
        try { sessionStorage.removeItem(syncKey); } catch (e) { }
    }

    // ── UI ──
    const row = document.getElementById('pushRow');
    const rowText = document.getElementById('pushRowText');
    const rowBtn = document.getElementById('pushRowBtn');
    const card = document.getElementById('pushSettings');
    const cardText = document.getElementById('pushSettingsText');
    const cardBtn = document.getElementById('pushSettingsBtn');

    async function render() {
        // iPhone in a Safari tab: alerts need the Home Screen app
        if (!supported || (isIos && !installed)) {
            if (!isIos) return;   // other browsers without push: nothing to offer
            if (row) { rowText.textContent = 'Add this site to your Home Screen to get alerts'; rowBtn.hidden = true; row.hidden = false; }
            if (card) {
                cardText.textContent = 'On iPhone, alerts work in the app: tap Share, then Add to Home Screen, and open it from there.';
                cardBtn.hidden = true; card.hidden = false;
            }
            return;
        }

        const permission = Notification.permission;
        const on = permission === 'granted' && !!(await currentSub());

        if (row) {
            row.hidden = on || permission === 'denied';
            rowText.textContent = 'Get alerts on this device';
            rowBtn.hidden = false;
        }
        if (card) {
            card.hidden = false;
            cardBtn.hidden = false;
            if (permission === 'denied') {
                cardText.textContent = isIos
                    ? 'Alerts are blocked. Turn them on in Settings › Notifications › Apostle Enock.'
                    : 'Alerts are blocked for this site. Allow notifications in your browser settings, then reload.';
                cardBtn.hidden = true;
            } else if (on) {
                cardText.textContent = 'On. You’ll get an alert here when something new arrives.';
                cardBtn.textContent = 'Turn off';
                cardBtn.className = 'btn btn-outline btn-sm';
            } else {
                cardText.textContent = 'Get an alert on this phone or computer when something new arrives.';
                cardBtn.textContent = 'Turn on';
                cardBtn.className = 'btn btn-primary btn-sm';
            }
        }
    }

    async function toggle(btn) {
        btn.disabled = true;
        try {
            const on = Notification.permission === 'granted' && !!(await currentSub());
            if (on) {
                await unsubscribe();
            } else {
                // Must run straight from the tap: iOS only asks for permission inside a user gesture
                const permission = await Notification.requestPermission();
                if (permission === 'granted') await subscribe(true);
            }
        } catch (e) {
            console.warn('[push]', e);
        } finally {
            btn.disabled = false;
            render();
        }
    }

    if (rowBtn) rowBtn.addEventListener('click', function (e) { e.stopPropagation(); toggle(rowBtn); });
    if (cardBtn) cardBtn.addEventListener('click', function () { toggle(cardBtn); });

    // ── Keep the server's record in step with this browser ──
    if (supported && Notification.permission === 'granted') {
        let synced = null;
        try { synced = sessionStorage.getItem(syncKey); } catch (e) { }
        if (synced !== cfg.dataset.user) subscribe(false).catch(function (e) { console.warn('[push]', e); });
    }

    // Log out: unlink this device from the account first, so a shared phone doesn't keep getting their alerts
    document.addEventListener('submit', function (e) {
        const form = e.target;
        if (!supported || !form || !/\/Account\/Logout/i.test(form.getAttribute('action') || '')) return;
        if (form.dataset.pushDone) return;
        e.preventDefault();
        form.dataset.pushDone = '1';
        let sent = false;
        const done = function () { if (!sent) { sent = true; form.submit(); } };
        setTimeout(done, 2500);   // never hold up logging out
        unsubscribe().catch(function () { }).then(done);
    }, true);

    render();
})();
