// Background music for online meeting services.
// Live Settings controls it; Live page viewers hear it. Every browser works out the same
// track and position from the shared state:
//   position = (server now - anchor) wrapped round the playlist's total length

const LiveMusicMath = {
    totalDuration(playlist) {
        return (playlist || []).reduce((sum, t) => sum + (t.duration > 0 ? t.duration : 0), 0);
    },
    // Seconds into the playlist. skewMs = server clock - this device's clock.
    positionNow(state, nowMs, skewMs) {
        if (!state || state.state === 'stopped') return 0;
        const total = LiveMusicMath.totalDuration(state.playlist);
        if (total <= 0) return 0;
        if (state.state === 'paused') return Math.min(state.pausedAt || 0, total);
        if (state.anchorMs == null) return 0;
        const elapsed = (nowMs + skewMs - state.anchorMs) / 1000;
        return ((elapsed % total) + total) % total;
    },
    // Which track a playlist position falls in, and how far into it
    locate(playlist, position) {
        let p = position;
        for (let i = 0; i < (playlist || []).length; i++) {
            const d = playlist[i].duration > 0 ? playlist[i].duration : 0;
            if (p < d) return { index: i, offset: p };
            p -= d;
        }
        return { index: 0, offset: 0 };
    },
    // 0–1 playback volume: the set level, lowered while someone talks if ducking is on
    duckedVolume(volumePercent, duckOn, speaking, duckLevel = 0.2) {
        const base = Math.min(1, Math.max(0, (volumePercent ?? 40) / 100));
        return duckOn && speaking ? base * duckLevel : base;
    },
    formatTime(seconds) {
        const s = Math.max(0, Math.floor(seconds));
        const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
        return h ? `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}` : `${m}:${String(sec).padStart(2, '0')}`;
    }
};

if (typeof module !== 'undefined') module.exports = LiveMusicMath;

// ── Live page player (viewers) ──────────────────────────────
// Used from Live.cshtml: LiveMusicPlayer.start(liveConn, initialState)
const LiveMusicPlayer = (function () {
    if (typeof document === 'undefined') return null;

    const RESYNC_EVERY_MS = 15000, TOLERANCE_S = 2;
    const DUCK_RELEASE_MS = 1500, FADE_MS = 400;   // back up 1.5 s after the talking stops
    const audio = new Audio();
    audio.preload = 'auto';
    let state = null, skew = 0, currentUrl = null, blocked = false, muted = false;
    let speaking = false, releaseTimer = null, fadeTimer = null;
    let onBlocked = () => { }, chip = null, chipTitle = null, chipMute = null;

    const targetVolume = () => LiveMusicMath.duckedVolume(state?.volume, state?.duck, speaking);

    // Glide to a volume rather than jumping
    function fadeTo(v) {
        clearInterval(fadeTimer);
        const from = audio.volume, started = performance.now();
        if (Math.abs(from - v) < 0.01) { audio.volume = v; return; }
        fadeTimer = setInterval(() => {
            const k = Math.min(1, (performance.now() - started) / FADE_MS);
            audio.volume = from + (v - from) * k;
            if (k >= 1) clearInterval(fadeTimer);
        }, 40);
    }

    // From the meeting: is anyone talking right now?
    function setSpeaking(on) {
        if (on) {
            clearTimeout(releaseTimer); releaseTimer = null;
            if (!speaking) { speaking = true; fadeTo(targetVolume()); }
        } else if (speaking && !releaseTimer) {
            releaseTimer = setTimeout(() => { releaseTimer = null; speaking = false; fadeTo(targetVolume()); }, DUCK_RELEASE_MS);
        }
    }

    function setState(s) {
        if (!s) return;
        state = s;
        skew = (s.serverNow || Date.now()) - Date.now();
        apply();
    }

    function apply() {
        if (!state || state.state === 'stopped' || !state.playlist?.length) {
            audio.pause();
            currentUrl = null;
            showChip(null);
            return;
        }
        const pos = LiveMusicMath.positionNow(state, Date.now(), skew);
        const { index, offset } = LiveMusicMath.locate(state.playlist, pos);
        const track = state.playlist[index];
        fadeTo(targetVolume());
        audio.muted = muted;
        showChip(track);

        const seek = () => { try { audio.currentTime = offset; } catch { } };
        if (currentUrl !== track.url) {
            currentUrl = track.url;
            audio.src = track.url;
            audio.addEventListener('loadedmetadata', seek, { once: true });
        } else if (Math.abs(audio.currentTime - offset) > TOLERANCE_S) {
            seek();
        }

        if (state.state === 'paused') { audio.pause(); return; }
        audio.play().then(() => { blocked = false; }).catch(() => { blocked = true; onBlocked(); });
    }

    function showChip(track) {
        if (!chip) return;
        chip.style.display = track ? 'flex' : 'none';
        if (track) chipTitle.textContent = track.title;
    }

    // When a track ends, the next one is worked out from the clock
    audio.addEventListener('ended', () => { currentUrl = null; apply(); });

    return {
        start(liveConn, initial, options = {}) {
            onBlocked = options.onBlocked || onBlocked;
            chip = document.getElementById('musicChip');
            chipTitle = document.getElementById('musicChipTitle');
            chipMute = document.getElementById('musicChipMute');
            chipMute?.addEventListener('click', () => {
                muted = !muted;
                audio.muted = muted;
                chipMute.innerHTML = muted ? '<i class="fas fa-volume-xmark"></i>' : '<i class="fas fa-volume-high"></i>';
                chipMute.title = muted ? 'Unmute music' : 'Mute music';
            });
            liveConn.on('MusicChanged', setState);
            setState(initial);
            setInterval(() => { if (state?.state === 'playing') apply(); }, RESYNC_EVERY_MS);
        },
        // Call from a tap: browsers only allow sound after one
        unblock() { if (blocked) apply(); },
        setSpeaking

    };
})();
