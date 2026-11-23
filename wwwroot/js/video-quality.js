// Video quality for the meeting room and the Live page: how sharp the video we receive
// from the LiveKit media server is. Lower quality means less to decode, so a phone stays
// cooler and uses less data.
//
// Choices: auto (default), high, medium, low. Saved per device.
// Auto is medium on a phone and high on a computer, and steps down one level by itself
// when the phone shows strain: the browser reports heavy CPU load (Compute Pressure API),
// LiveKit says our camera encoder can't keep up, or the battery is low. It never steps
// back up by itself; the viewer can pick a higher level from the menu.
//
// Usage:
//   const vq = VideoQuality(LivekitClient, { room, toast: msg => ..., onChange: () => ... });
//   vq.apply(publication)        // a subscribed remote video publication
//   vq.applyAll(room)            // all of a room's remote video
//   vq.stepDown(reason)          // auto mode only
//   vq.openMenu(buttonElement)   // the choice menu, next to the button
function VideoQuality(LK, opts = {}) {
    const KEY = 'video.quality', AUTO_KEY = 'video.autoLevel';
    const LEVELS = ['low', 'medium', 'high'];
    const NAMES = { auto: 'Auto', high: 'High', medium: 'Medium', low: 'Low (data saver)' };
    const toast = opts.toast || (() => { });

    const isMobile = (() => {
        if (navigator.userAgentData && typeof navigator.userAgentData.mobile === 'boolean' && navigator.userAgentData.mobile) return true;
        if (/Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent)) return true;
        // iPadOS reports itself as a Mac
        if (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1) return true;
        return !!(window.matchMedia && matchMedia('(pointer:coarse) and (max-width: 900px)').matches);
    })();

    // Lite mode (leaf button in the site menu): Auto starts at Low. Pages with the site layout set
    // window.LITE; the meeting room has its own page, so read the "lite" cookie (or Data Saver) there.
    const lite = typeof window.LITE === 'boolean' ? window.LITE : (() => {
        const m = document.cookie.match(/(?:^|;\s*)lite=([01])/);
        return m ? m[1] === '1' : !!(navigator.connection && navigator.connection.saveData);
    })();
    const startLevel = () => lite ? 'low' : isMobile ? 'medium' : 'high';

    function read(store, key) { try { return store.getItem(key); } catch { return null; } }
    function write(store, key, v) { try { v == null ? store.removeItem(key) : store.setItem(key, v); } catch { } }

    let choice = read(localStorage, KEY);
    if (!(choice in NAMES)) choice = 'auto';
    // Auto's current level; a step-down lasts for this browser tab, so a reload stays cool
    let autoLevel = read(sessionStorage, AUTO_KEY);
    if (!LEVELS.includes(autoLevel)) autoLevel = startLevel();

    let room = opts.room || null, lastStep = 0;
    const level = () => choice === 'auto' ? autoLevel : choice;

    function apply(pub) {
        if (!pub || pub.kind !== 'video' || !pub.isSubscribed) return;
        let l = level();
        // A shared screen at 180p is unreadable
        if (l === 'low' && pub.source === LK.Track.Source.ScreenShare) l = 'medium';
        try {
            pub.setVideoQuality(l === 'low' ? LK.VideoQuality.LOW : l === 'medium' ? LK.VideoQuality.MEDIUM : LK.VideoQuality.HIGH);
            pub.setVideoFPS(l === 'low' ? 15 : 30);
        } catch (e) { console.warn('Could not set video quality', e); }
    }
    function applyAll(r) {
        room = r || room;
        room?.remoteParticipants.forEach(p => p.trackPublications.forEach(apply));
    }
    function changed() { applyAll(); opts.onChange?.(); }

    function set(c) {
        if (!(c in NAMES)) return;
        choice = c;
        write(localStorage, KEY, c === 'auto' ? null : c);
        // Picking Auto again starts it fresh at the device's normal level
        if (c === 'auto') { autoLevel = startLevel(); write(sessionStorage, AUTO_KEY, null); }
        changed();
    }

    // Auto only: one level down, at most once a minute
    function stepDown(reason) {
        if (choice !== 'auto') return false;
        const i = LEVELS.indexOf(autoLevel);
        if (i <= 0 || Date.now() - lastStep < 60000) return false;
        lastStep = Date.now();
        autoLevel = LEVELS[i - 1];
        write(sessionStorage, AUTO_KEY, autoLevel);
        console.log('Video quality lowered to', autoLevel, '—', reason);
        toast('Video quality lowered to keep your device cool');
        changed();
        return true;
    }

    // Heavy CPU load reported by the browser (Chrome/Edge 125+; others skip this).
    // Only with a media room: elsewhere lowering the quality would change nothing.
    try {
        if (room && 'PressureObserver' in window) {
            const po = new PressureObserver(records => {
                const s = records[records.length - 1]?.state;
                if (s === 'serious' || s === 'critical') stepDown('cpu');
            });
            po.observe('cpu', { sampleInterval: 5000 }).catch(() => { });
        }
    } catch { }

    // Battery under 20% and not charging: drop straight to low
    try {
        if (room) navigator.getBattery?.().then(b => {
            let warned = false;
            const check = () => {
                if (b.charging || b.level >= .2 || warned) return;
                warned = true;
                if (choice !== 'auto' || autoLevel === 'low') return;
                autoLevel = 'low';
                write(sessionStorage, AUTO_KEY, autoLevel);
                toast('Battery low — video quality lowered. Tap Audio only to save more.');
                changed();
            };
            b.addEventListener('levelchange', check);
            b.addEventListener('chargingchange', check);
            check();
        }).catch(() => { });
    } catch { }

    function label() { return choice === 'auto' ? `Auto · ${NAMES[autoLevel].split(' ')[0]}` : NAMES[choice]; }

    // ── Menu ───────────────────────────────────────────────
    let menu = null, menuAnchor = null;
    function injectStyle() {
        if (document.getElementById('vqStyle')) return;
        const s = document.createElement('style');
        s.id = 'vqStyle';
        s.textContent = `
.vq-menu{position:fixed;z-index:10000;min-width:210px;background:rgba(26,26,36,.96);color:#fff;
  border:1px solid rgba(255,255,255,.12);border-radius:12px;padding:.35rem;box-shadow:0 8px 24px rgba(0,0,0,.45);
  font-size:.88rem}
.vq-menu .vq-title{padding:.35rem .6rem .25rem;font-size:.72rem;opacity:.65;text-transform:uppercase;letter-spacing:.04em}
.vq-menu button{display:flex;align-items:center;gap:.5rem;width:100%;background:none;border:none;color:inherit;
  text-align:left;padding:.5rem .6rem;border-radius:8px;cursor:pointer;font:inherit}
.vq-menu button:hover{background:rgba(255,255,255,.08)}
.vq-menu button .vq-check{width:1rem;color:#a5b4fc}
.vq-menu button small{opacity:.6;margin-left:auto;font-size:.72rem}
.vq-menu .vq-note{padding:.3rem .6rem .35rem;font-size:.72rem;opacity:.6;line-height:1.3}`;
        document.head.appendChild(s);
    }
    // A tap outside the menu (other than on its button, which toggles it) closes it
    function onOutside(e) {
        if (menu && !menu.contains(e.target) && !menuAnchor?.contains(e.target)) closeMenu();
    }
    function closeMenu() {
        menu?.remove(); menu = null;
        document.removeEventListener('pointerdown', onOutside, true);
    }
    function openMenu(anchor) {
        if (menu) { closeMenu(); return; }
        menuAnchor = anchor;
        injectStyle();
        menu = document.createElement('div');
        menu.className = 'vq-menu';
        const hint = { auto: isMobile ? 'Medium on phones' : 'Best for this device', high: 'Sharpest', medium: '360p', low: 'Coolest, least data' };
        menu.innerHTML = `<div class="vq-title">Video quality</div>` +
            ['auto', 'high', 'medium', 'low'].map(c =>
                `<button type="button" data-c="${c}"><span class="vq-check">${choice === c ? '✓' : ''}</span>${NAMES[c]}<small>${c === 'auto' && choice === 'auto' ? NAMES[autoLevel].split(' ')[0] : hint[c]}</small></button>`).join('') +
            `<div class="vq-note">Lower quality keeps your phone cooler.</div>`;
        menu.addEventListener('click', e => {
            const b = e.target.closest('button[data-c]');
            if (!b) return;
            set(b.dataset.c);
            toast(`Video quality: ${label()}`);
            closeMenu();
        });
        document.body.appendChild(menu);
        // Above the button if there's room, otherwise below; kept on screen
        const r = anchor.getBoundingClientRect(), m = menu.getBoundingClientRect();
        const top = r.top - m.height - 8 >= 8 ? r.top - m.height - 8 : Math.min(r.bottom + 8, innerHeight - m.height - 8);
        const left = Math.max(8, Math.min(r.left + r.width / 2 - m.width / 2, innerWidth - m.width - 8));
        menu.style.top = top + 'px'; menu.style.left = left + 'px';
        document.addEventListener('pointerdown', onOutside, true);
    }

    return {
        isMobile, apply, applyAll, set, stepDown, openMenu, label,
        get choice() { return choice; }, get level() { return level(); }
    };
}
