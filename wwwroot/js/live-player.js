// Live page player for sermons that aren't online meetings (see Views/Media/_LivePlayer.cshtml).
// Live stream links play at the live edge; video/audio sermons play as a premiere —
// everyone is at the same moment, counted from when Go Live was clicked.

const LivePlayerMath = {
    // Seconds since Go Live on the server's clock. skewMs = server time - this device's time.
    elapsedSeconds(startedMs, nowMs, skewMs) {
        if (!startedMs) return 0;
        return Math.max(0, (nowMs + skewMs - startedMs) / 1000);
    },
    // A premiere that's further off than this is pulled back into line
    needsResync(currentSeconds, expectedSeconds, toleranceSeconds = 3) {
        return Math.abs(currentSeconds - expectedSeconds) > toleranceSeconds;
    },
    hasEnded(elapsed, durationSeconds) {
        return Number.isFinite(durationSeconds) && durationSeconds > 0 && elapsed >= durationSeconds - 0.5;
    }
};

if (typeof module !== 'undefined') module.exports = LivePlayerMath;

(function () {
    if (typeof document === 'undefined') return;
    const root = document.getElementById('livePlayer');
    if (!root) return;

    const d = root.dataset;
    const kind = d.kind;
    const premiere = d.premiere === 'true';
    const started = d.started ? Number(d.started) : null;
    const skew = Number(d.serverNow) - Date.now();
    const SYNC_EVERY_MS = 20000;

    const stage = document.getElementById('lpStage');
    const artwork = document.getElementById('lpArtwork');
    const tap = document.getElementById('lpTap');
    const tapLabel = document.getElementById('lpTapLabel');
    const waiting = document.getElementById('lpWaiting');
    const ended = document.getElementById('lpEnded');

    let finished = false;
    const elapsed = () => LivePlayerMath.elapsedSeconds(started, Date.now(), skew);
    const show = (el, on, display = 'flex') => { if (el) el.style.display = on ? display : 'none'; };
    const ready = () => show(waiting, false);

    function showTap(label, onTap) {
        tapLabel.textContent = label;
        show(tap, true);
        tap.onclick = () => { show(tap, false); onTap(); };
    }

    function finish() {
        if (finished) return;
        finished = true;
        show(tap, false);
        show(waiting, false);
        show(ended, true);
        stage.innerHTML = '';
    }

    function loadScript(src) {
        return new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = src; s.async = true; s.onload = resolve; s.onerror = reject;
            document.head.appendChild(s);
        });
    }

    // Keeps a premiere on the shared clock: re-seeks when it drifts and resumes if paused
    function startSync(getTime, seekTo, isPaused, resume) {
        setInterval(() => {
            if (finished) return;
            const expected = elapsed();
            if (isPaused()) resume();
            else if (LivePlayerMath.needsResync(getTime(), expected)) seekTo(expected);
        }, SYNC_EVERY_MS);
    }

    // ── YouTube ─────────────────────────────────────────────
    function playYouTubeChannel() {
        const f = document.createElement('iframe');
        f.src = `https://www.youtube.com/embed/live_stream?channel=${encodeURIComponent(d.youtubeChannel)}&autoplay=1&mute=1&rel=0&playsinline=1`;
        f.allow = 'autoplay; encrypted-media; picture-in-picture; fullscreen';
        f.allowFullscreen = true;
        stage.appendChild(f);
        ready();
    }

    async function playYouTube() {
        if (!window.YT || !window.YT.Player) {
            const apiReady = new Promise(r => { window.onYouTubeIframeAPIReady = r; });
            await loadScript('https://www.youtube.com/iframe_api');
            await apiReady;
        }
        const holder = document.createElement('div');
        stage.appendChild(holder);

        const player = new YT.Player(holder, {
            videoId: d.youtubeId,
            playerVars: premiere
                ? { autoplay: 1, mute: 1, controls: 0, disablekb: 1, rel: 0, modestbranding: 1, playsinline: 1, start: Math.floor(elapsed()) }
                : { autoplay: 1, mute: 1, rel: 0, modestbranding: 1, playsinline: 1 },
            events: {
                onReady: () => {
                    ready();
                    if (premiere) {
                        if (LivePlayerMath.hasEnded(elapsed(), player.getDuration())) return finish();
                        player.seekTo(elapsed(), true);
                        startSync(() => player.getCurrentTime(), t => player.seekTo(t, true),
                            () => player.getPlayerState() === YT.PlayerState.PAUSED, () => player.playVideo());
                    }
                    player.playVideo();
                    showTap('Tap for sound', () => { player.unMute(); player.setVolume(100); });
                },
                onStateChange: e => {
                    if (premiere && e.data === YT.PlayerState.ENDED) finish();
                }
            }
        });
    }

    // ── Facebook ────────────────────────────────────────────
    async function playFacebook() {
        const box = document.createElement('div');
        box.className = 'fb-video';
        box.id = 'lpFbVideo';
        box.dataset.href = d.src;
        box.dataset.width = String(Math.max(320, Math.round(stage.clientWidth)));
        box.dataset.autoplay = 'true';
        box.dataset.allowfullscreen = 'true';
        box.dataset.showText = 'false';
        box.dataset.controls = premiere ? 'false' : 'true';
        stage.classList.add('lp-stage-fb');
        stage.appendChild(box);

        if (!document.getElementById('fb-root')) {
            const fbRoot = document.createElement('div');
            fbRoot.id = 'fb-root';
            document.body.prepend(fbRoot);
        }

        window.fbAsyncInit = () => {
            FB.Event.subscribe('xfbml.ready', msg => {
                if (msg.type !== 'video' || msg.id !== 'lpFbVideo') return;
                const player = msg.instance;
                ready();
                if (premiere) {
                    player.subscribe('finishedPlaying', finish);
                    let paused = false;
                    player.subscribe('paused', () => { paused = true; });
                    player.subscribe('startedPlaying', () => { paused = false; });
                    if (LivePlayerMath.hasEnded(elapsed(), player.getDuration())) return finish();
                    player.seek(elapsed());
                    startSync(() => player.getCurrentPosition(), t => player.seek(t),
                        () => paused, () => player.play());
                }
                player.play();
                showTap('Tap for sound', () => player.unmute());
            });
            FB.init({ xfbml: true, version: 'v19.0' });
        };
        await loadScript('https://connect.facebook.net/en_US/sdk.js');
    }

    // ── HLS / video file / audio ────────────────────────────
    async function playMedia({ audio = false, hls = false } = {}) {
        const media = document.createElement(audio ? 'audio' : 'video');
        media.playsInline = true;
        media.preload = 'auto';
        if (!audio) { media.muted = true; media.autoplay = true; media.controls = !premiere; }
        stage.appendChild(media);

        if (audio) show(artwork, true);

        if (hls && !media.canPlayType('application/vnd.apple.mpegurl')) {
            await loadScript('https://cdnjs.cloudflare.com/ajax/libs/hls.js/1.5.13/hls.min.js');
            if (!window.Hls || !Hls.isSupported()) { waiting.querySelector('p').textContent = 'This browser can\'t play the stream.'; return; }
            const h = new Hls();
            h.loadSource(d.src);
            h.attachMedia(media);
        } else {
            media.src = d.src;
        }

        // A finite length means a recording (premiere); an endless one is a live stream
        const isRecording = () => Number.isFinite(media.duration) && media.duration > 0;
        const catchUp = () => {
            if (!premiere || !isRecording()) return true;
            if (LivePlayerMath.hasEnded(elapsed(), media.duration)) { finish(); return false; }
            media.currentTime = elapsed();
            return true;
        };

        media.addEventListener('loadedmetadata', () => {
            ready();
            // A stream with no picture is shown as audio
            if (!audio && media.videoWidth === 0) show(artwork, true);
            if (!audio && catchUp()) media.play().catch(() => { });
        }, { once: true });

        media.addEventListener('ended', () => { if (premiere && isRecording()) finish(); });
        media.addEventListener('error', () => {
            show(waiting, true);
            waiting.querySelector('p').textContent = 'The stream isn\'t available right now.';
        });

        let audioWaitingForTap = audio;   // audio can't start until the viewer taps
        if (premiere) {
            startSync(() => media.currentTime, t => { if (isRecording()) media.currentTime = t; },
                () => media.paused && !audioWaitingForTap, () => media.play().catch(() => { }));
        }

        if (audio) {
            ready();
            showTap(premiere ? 'Tap to listen' : 'Tap to listen live', () => {
                audioWaitingForTap = false;
                const go = () => { if (catchUp()) media.play().catch(() => { }); };
                if (media.readyState >= 1) go(); else media.addEventListener('loadedmetadata', go, { once: true });
            });
        } else {
            showTap('Tap for sound', () => { media.muted = false; media.play().catch(() => { }); });
        }
    }

    switch (kind) {
        case 'youtube': playYouTube(); break;
        case 'youtube-channel': playYouTubeChannel(); break;
        case 'facebook': playFacebook(); break;
        case 'hls': playMedia({ hls: true }); break;
        case 'hls-audio': playMedia({ hls: true, audio: true }); break;
        case 'audio': playMedia({ audio: true }); break;
        case 'file': playMedia(); break;
        default: break;   // nothing playable: the "starting soon" message stays
    }
})();
