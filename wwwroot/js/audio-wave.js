// Waveform for audio-only screens (Live page, meeting room, sermon audio): bars scroll
// right to left with how loud the sound is.
// Meeting audio (LiveKit tracks) is read through an analyser. An <audio> element is read
// through captureStream() where the browser has it and the file is ours — never through
// createMediaElementSource, which would send the sound itself through Web Audio and cut
// it off when a phone locks. Anything that can't be read gets a gentle made-up wave
// while it plays, so the screen still shows that sound is coming.
function AudioWave(canvas) {
    const g = canvas.getContext('2d');
    const levels = new Array(64).fill(0);
    const sources = new Map();   // id -> MediaStreamAudioSourceNode
    let ctx = null, analyser = null, samples = null, media = null;
    let running = false, last = 0;

    function audio() {
        if (!ctx) {
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return null;
            ctx = new Ctx();
            analyser = ctx.createAnalyser();
            analyser.fftSize = 512;
            samples = new Uint8Array(analyser.fftSize);
        }
        // Browsers keep audio contexts paused until the viewer taps something
        if (ctx.state === 'suspended') ctx.resume().catch(() => { });
        return ctx;
    }
    document.addEventListener('click', () => { if (ctx) audio(); });

    function addTrack(id, mediaStreamTrack) {
        if (!mediaStreamTrack || !audio()) return;
        removeTrack(id);
        const src = ctx.createMediaStreamSource(new MediaStream([mediaStreamTrack]));
        src.connect(analyser);
        sources.set(id, src);
    }
    function removeTrack(id) {
        sources.get(id)?.disconnect();
        sources.delete(id);
    }

    function isOurs(src) {
        if (!src) return false;
        if (src.startsWith('blob:')) return true;   // HLS through Media Source
        try { return new URL(src, location.href).origin === location.origin; } catch { return false; }
    }
    function addMedia(el) {
        media = el;
        el.addEventListener('playing', () => {
            if (typeof el.captureStream !== 'function' || !isOurs(el.currentSrc)) return;
            try {
                const t = el.captureStream().getAudioTracks()[0];
                if (t) addTrack('media', t);
            } catch { }
        });
    }

    function level(t) {
        if (sources.size && ctx?.state === 'running') {
            analyser.getByteTimeDomainData(samples);
            let sum = 0;
            for (const s of samples) sum += ((s - 128) / 128) ** 2;
            return Math.min(1, Math.sqrt(sum / samples.length) * 4);
        }
        if (media && !media.paused && !media.ended)
            return .2 + .35 * Math.abs(Math.sin(t / 260)) * (.5 + Math.random() * .5);
        return 0;
    }

    function draw(t) {
        if (!running) return;
        requestAnimationFrame(draw);
        if (t - last < 50) return;   // ~20 steps a second
        last = t;
        levels.shift(); levels.push(level(t));
        const w = canvas.width, h = canvas.height, bw = w / levels.length;
        g.clearRect(0, 0, w, h);
        g.fillStyle = '#a5b4fc';
        levels.forEach((v, i) => {
            const bh = Math.max(3, v * h);
            g.fillRect(i * bw + 1, (h - bh) / 2, bw - 2, bh);
        });
    }

    return {
        addTrack, removeTrack, addMedia,
        unlock: audio,
        start() { if (running) return; running = true; requestAnimationFrame(draw); },
        stop() { running = false; }
    };
}
