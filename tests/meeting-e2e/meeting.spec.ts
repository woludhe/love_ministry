import { test, expect, chromium, Browser, BrowserContext, Page } from '@playwright/test';

// Meeting test: 1 host (seeded admin) + PEOPLE-1 seeded members (PEOPLE=10 by default,
// at least 10 since the steps use participants P1..P9; the app seeds 19 members).
// Start the app with Seed__MeetingTestUsers=true so the members exist.

// Globals from Room.cshtml's inline script (resolved inside page.evaluate)
declare const conn: any, ROOM_CODE: string;

const BASE = process.env.BASE_URL ?? 'https://localhost:53826';
const HOST = { email: 'admin@church.org', password: 'Admin@123456' };
const MEMBER_PASSWORD = 'MeetTest@123';
const PEOPLE = Math.max(10, Number(process.env.PEOPLE ?? 10));

type Person = {
  label: string;
  email: string;
  password: string;
  ctx: BrowserContext;
  page: Page;
  connId: string;
  errors: string[];
  log: string[];
};

let host: Person;
const members: Person[] = [];   // members[0] = participant 1 … members[PEOPLE-2] = the last
let roomUrl = '';
const results: string[] = [];

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));
const everyone = () => [host, ...members];
// People currently in the meeting (page open on the room URL)
const present = () => everyone().filter(p => p && !p.page.isClosed() && /\/meeting\/room\//i.test(p.page.url()));
const others = (me: Person) => present().filter(p => p !== me);

function watch(p: Person) {
  p.page.on('pageerror', e => p.errors.push(`pageerror: ${e.message}`));
  p.page.on('console', m => {
    const t = m.text();
    if (m.type() === 'error' && /HubException|Media connection failed|Failed to invoke/i.test(t)) p.errors.push(t);
    if (/Media room disconnected|Media connection failed|publish|dimensions|track muted|camera|Camera toggle failed|Mic toggle failed|\[diag\]/i.test(t)) p.log.push(`${new Date().toISOString().slice(11,23)} ${t}`);
  });
}

// Per-peer connection state on every screen, printed when the test fails
async function dumpState() {
  for (const p of present()) {
    const st = await p.page.evaluate(() => {
      const m = (window as any).__meet;
      return { me: m.myConnId, media: m.room.state, tiles: Array.from(document.querySelectorAll('.video-tile')).map(t => t.id),
        remote: Array.from(m.room.remoteParticipants.values()).map((rp: any) =>
          `${rp.identity.slice(0,6)}:${Array.from(rp.trackPublications.values()).map((t: any) => `${t.kind}${t.isSubscribed ? '' : '(unsub)'}${t.isMuted ? '(muted)' : ''}`).join('+')}`) };
    }).catch(e => ({ error: String(e) }));
    console.log(`[${p.label} ${p.connId.slice(0,6)}]`, JSON.stringify(st));
    if (p.log.length) console.log('   log:', p.log.join('\n        '));
    if (p.errors.length) console.log('   errors:', p.errors.join('\n        '));
  }
  console.log('who is who:', everyone().filter(Boolean).map(p => `${p.label}=${p.connId.slice(0,6)}`).join(' '));
}

async function newPerson(label: string, email: string, password: string): Promise<Person> {
  const ctx = await (await getBrowser()).newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1280, height: 720 },
    permissions: ['camera', 'microphone'],
  });
  // Each person's "camera" is an animated canvas and their "microphone" a tone.
  // Chrome's built-in fake camera is shared by every window on the machine and
  // breaks when one of them closes it (LiveKit opens and closes it briefly while
  // listing devices), which real people on their own devices never hit. The video
  // is small (320x180, 15 fps) because all ten test people share this machine's
  // internet connection: at full size they download over 40 Mbps while joining.
  await ctx.addInitScript(() => {
    const md = navigator.mediaDevices;
    const real = md.getUserMedia.bind(md);
    const hue = Math.floor(Math.random() * 360);
    md.getUserMedia = async (c?: MediaStreamConstraints) => {
      if (!c?.video && !c?.audio) return real(c);
      const tracks: MediaStreamTrack[] = [];
      if (c.video) {
        const canvas = document.createElement('canvas');
        canvas.width = 320; canvas.height = 180;
        const g = canvas.getContext('2d')!;
        let n = 0;
        setInterval(() => {
          g.fillStyle = `hsl(${hue},60%,40%)`; g.fillRect(0, 0, 320, 180);
          g.fillStyle = '#fff'; g.fillRect((n += 4) % 320, 70, 30, 30);
        }, 1000 / 15);
        const t = canvas.captureStream(15).getVideoTracks()[0];
        Object.defineProperty(t, 'label', { value: 'test camera' });
        tracks.push(t);
      }
      if (c.audio) {
        const ac = new AudioContext();
        const osc = ac.createOscillator();
        osc.frequency.value = 220 + hue;
        const dest = ac.createMediaStreamDestination();
        osc.connect(dest); osc.start();
        const t = dest.stream.getAudioTracks()[0];
        Object.defineProperty(t, 'label', { value: 'test microphone' });
        tracks.push(t);
      }
      return new MediaStream(tracks);
    };
  });
  const page = await ctx.newPage();
  const p: Person = { label, email, password, ctx, page, connId: '', errors: [], log: [] };
  watch(p);
  await page.goto(`${BASE}/Account/Login`);
  await page.fill('#Email', email);
  await page.fill('#Password', password);
  await Promise.all([page.waitForURL(u => !/Account\/Login/i.test(u.pathname)), page.click('button[type=submit]')]);
  return p;
}

// Someone watching on the Live page (not in the meeting); a guest when no login is given
const viewers: Person[] = [];
let liveSermonId = 0;
async function newViewer(label: string, login?: { email: string; password: string }): Promise<Person> {
  const ctx = await (await getBrowser()).newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 720 } });
  const page = await ctx.newPage();
  const p: Person = { label, email: login?.email ?? '', password: login?.password ?? '', ctx, page, connId: '', errors: [], log: [] };
  watch(p);
  if (login) {
    await page.goto(`${BASE}/Account/Login`);
    await page.fill('#Email', login.email);
    await page.fill('#Password', login.password);
    await Promise.all([page.waitForURL(u => !/Account\/Login/i.test(u.pathname)), page.click('button[type=submit]')]);
  }
  await page.goto(`${BASE}/Media/Live`);
  viewers.push(p);
  return p;
}
const viewerTiles = (v: Person) => v.page.evaluate(() =>
  Array.from(document.querySelectorAll('#guestVideoGrid .video-tile')).map(t => t.id.slice(5)).sort());

async function enterRoom(p: Person) {
  if (p.page.isClosed()) { p.page = await p.ctx.newPage(); watch(p); }
  await p.page.goto(roomUrl);
  await expect.poll(() => p.page.evaluate(() => (window as any).__meet?.myConnId ?? ''), { timeout: 30_000 }).not.toBe('');
  p.connId = await p.page.evaluate(() => (window as any).__meet.myConnId);
}

// ── Page probes ────────────────────────────────────────────
const tileCount = (p: Person) => p.page.evaluate(() => document.querySelectorAll('.video-tile').length);
// Remote participants in the LiveKit room whose tracks we've subscribed to
const connectedPeers = (p: Person) => p.page.evaluate(() => {
  const room = (window as any).__meet.room;
  if (room.state !== 'connected') return 0;
  return Array.from(room.remoteParticipants.values()).filter((rp: any) =>
    Array.from(rp.trackPublications.values()).some((t: any) => t.isSubscribed)).length;
});

async function tileIds(p: Person): Promise<string[]> {
  return p.page.evaluate(() => Array.from(document.querySelectorAll('.video-tile')).map(t => t.id));
}

// Inbound media counters per remote connection id (= LiveKit identity)
async function mediaStats(p: Person): Promise<Record<string, { frames: number; energy: number; audioBytes: number }>> {
  return p.page.evaluate(async () => {
    const out: Record<string, any> = {};
    const room = (window as any).__meet?.room ?? (window as any).__live.mediaRoom;   // meeting or Live page
    for (const [cid, rp] of room.remoteParticipants as Map<string, any>) {
      const s = { frames: 0, energy: 0, audioBytes: 0 };
      for (const pub of rp.trackPublications.values()) {
        const receiver = pub.track?.receiver;
        if (!receiver) continue;
        (await receiver.getStats()).forEach((r: any) => {
          if (r.type !== 'inbound-rtp') return;
          if (r.kind === 'video') s.frames += r.framesDecoded ?? 0;
          if (r.kind === 'audio') { s.energy += r.totalAudioEnergy ?? 0; s.audioBytes += r.bytesReceived ?? 0; }
        });
      }
      out[cid] = s;
    }
    return out;
  });
}

// Media delta over a window, keyed by viewer label → sender connId
async function mediaDelta(viewers: Person[], ms = 3000) {
  const before = await Promise.all(viewers.map(mediaStats));
  await sleep(ms);
  const after = await Promise.all(viewers.map(mediaStats));
  return viewers.map((v, i) => {
    const d: Record<string, { frames: number; energy: number; audioBytes: number }> = {};
    for (const cid of Object.keys(after[i])) {
      const b = before[i][cid] ?? { frames: 0, energy: 0, audioBytes: 0 };
      d[cid] = { frames: after[i][cid].frames - b.frames, energy: after[i][cid].energy - b.energy,
                 audioBytes: after[i][cid].audioBytes - b.audioBytes };
    }
    return d;
  });
}

async function expectAllConnected(label: string) {
  const people = present();
  const n = people.length;
  for (const p of people) {
    await expect.poll(() => tileCount(p), { message: `${p.label} tiles (${label})` }).toBe(n);
    await expect.poll(() => connectedPeers(p), { message: `${p.label} connected peers (${label})`, timeout: 60_000 }).toBe(n - 1);
    // no duplicates, and every remote tile belongs to someone actually present
    const ids = await tileIds(p);
    expect(new Set(ids).size, `${p.label} duplicate tiles`).toBe(ids.length);
    const expected = new Set(['tile-local', ...others(p).map(o => `tile-${o.connId}`)]);
    expect(ids.filter(id => !expected.has(id)), `${p.label} ghost tiles`).toEqual([]);
  }
}

async function expectNoClipping(p: Person) {
  const clipped = await p.page.evaluate(() => {
    const g = document.getElementById('videoGrid')!.getBoundingClientRect();
    return Array.from(document.querySelectorAll('#videoGrid .video-tile')).filter(t => {
      const r = t.getBoundingClientRect();
      return r.top < g.top - 2 || r.bottom > g.bottom + 2 || r.left < g.left - 2 || r.right > g.right + 2 || r.height < 40;
    }).map(t => t.id);
  });
  expect(clipped, `${p.label} clipped tiles`).toEqual([]);
}

// Every pair must show video and audio in at least one of several 3 s windows. The
// media server briefly pauses video to a viewer whose download is congested (all
// test viewers share one internet connection), so a single window is too strict.
async function expectAllHearAndSee(label: string) {
  const people = present();
  const pending = new Set(people.flatMap(v => others(v).map(s => `${v.label}|${s.label}`)));
  const why: Record<string, string> = {};
  // Muted people send no audio, so only their video is expected
  const muted = new Set<string>();
  for (const p of people) if (await p.page.evaluate(() => (window as any).__meet.isMuted)) muted.add(p.label);
  for (let round = 0; round < 10 && pending.size; round++) {
    const deltas = await mediaDelta(people);
    people.forEach((viewer, i) => {
      for (const sender of others(viewer)) {
        const key = `${viewer.label}|${sender.label}`, d = deltas[i][sender.connId];
        if (!pending.has(key)) continue;
        if (d && d.frames > 0 && (muted.has(sender.label) || (d.audioBytes > 0 && d.energy > 0))) pending.delete(key);
        else why[key] = d ? `frames ${d.frames}, audio bytes ${d.audioBytes}, energy ${d.energy.toFixed(3)}` : 'not in media room';
      }
    });
  }
  if (pending.size) {
    for (const key of pending) {
      const [v, s] = key.split('|');
      const viewer = people.find(p => p.label === v)!, sender = people.find(p => p.label === s)!;
      const state = await viewer.page.evaluate(id => {
        const rp = (window as any).__meet.room.remoteParticipants.get(id);
        return rp ? Array.from(rp.trackPublications.values()).map((t: any) => `${t.kind}:${t.track?.streamState ?? 'no track'}`).join(' ') : 'absent';
      }, sender.connId);
      why[key] += ` [${state}]`;
    }
  }
  expect([...pending].map(k => `${k.replace('|', ' ← ')}: ${why[k]}`), `pairs without media (${label})`).toEqual([]);
}

test.describe.configure({ mode: 'serial' });

let browser: Browser | undefined;
const getBrowser = async () => browser ??= await chromium.launch({
  channel: process.env.PW_CHANNEL ?? 'chrome',
  headless: process.env.HEADED ? false : true,
  args: [
    '--use-fake-ui-for-media-stream',
    '--autoplay-policy=no-user-gesture-required',
    // ten windows open for several minutes: keep their timers (and so the test
    // cameras) running at full speed, as each would on a person's own screen
    '--disable-background-timer-throttling',
    '--disable-backgrounding-occluded-windows',
    '--disable-renderer-backgrounding',
  ],
});

test.afterEach(async ({}, info) => { if (info.status !== 'passed') await dumpState(); });

test.afterAll(async () => {
  // Remove the test sermon created for the Live page step
  if (liveSermonId)
    await host.ctx.request.post(`${BASE}/Admin/DeleteSermon/${liveSermonId}`)
      .catch(e => console.log(`Could not delete test sermon ${liveSermonId}: ${e}`));
  console.log('\n──────── Meeting test summary ────────\n' + results.join('\n'));
  await browser?.close();
});

test(`${PEOPLE}-person meeting`, async () => {
  await test.step('1. Host starts an instant meeting', async () => {
    host = await newPerson('Host', HOST.email, HOST.password);
    await host.page.goto(`${BASE}/Meeting`);
    await Promise.all([
      host.page.waitForURL(/\/meeting\/room\//i),
      host.page.click('button:has-text("Start Instant Meeting")'),
    ]);
    roomUrl = host.page.url();
    await expect.poll(() => host.page.evaluate(() => (window as any).__meet?.myConnId ?? ''), { timeout: 30_000 }).not.toBe('');
    host.connId = await host.page.evaluate(() => (window as any).__meet.myConnId);
    for (let i = 1; i < PEOPLE; i++)
      members.push(await newPerson(`P${i}`, `meettest${i}@church.test`, MEMBER_PASSWORD));
    results.push(`PASS  Setup: room ${roomUrl}`);
  });

  await test.step('2. Participants join; tiles appear on every screen', async () => {
    for (const m of members) {
      await enterRoom(m);
      await sleep(1500);
      for (const p of present())
        await expect.poll(() => tileCount(p), { message: `${p.label} tiles after ${m.label} joined` }).toBe(present().length);
    }
    await expectAllConnected('all joined');
    for (const p of present()) await expectNoClipping(p);
    results.push(`PASS  Join: ${PEOPLE} tiles on all ${PEOPLE} screens, no duplicates/ghosts, nothing clipped`);
  });

  await test.step('3. Everyone sees and hears everyone', async () => {
    await expectAllHearAndSee('initial');
    results.push(`PASS  Audio/video: all ${PEOPLE * (PEOPLE - 1)} streams decoding video frames and carrying audio`);
  });

  await test.step('4. Camera off / on', async () => {
    const [p3, p7, p9] = [members[2], members[6], members[8]];
    for (const p of [p3, p7]) await p.page.click('#btnCam');
    for (const viewer of present()) for (const p of [p3, p7]) {
      const id = viewer === p ? 'tile-local' : `tile-${p.connId}`;
      await expect(viewer.page.locator(`#${id}`)).toHaveClass(/video-off/);
    }
    // late joiner sees the existing camera-off state straight away
    await p9.page.close();
    await enterRoom(p9);
    for (const p of [p3, p7])
      await expect(p9.page.locator(`#tile-${p.connId}`)).toHaveClass(/video-off/);
    await expectAllConnected('after P9 rejoin');
    for (const p of [p3, p7]) await p.page.click('#btnCam');
    for (const viewer of present()) for (const p of [p3, p7]) {
      const id = viewer === p ? 'tile-local' : `tile-${p.connId}`;
      await expect(viewer.page.locator(`#${id}`)).toHaveClass(/video-on/);
    }
    for (const p of [p3, p7]) {
      const t0 = Date.now();
      await expect.poll(async () => (await mediaDelta([host], 1000))[0][p.connId]?.frames ?? 0,
        { message: `host sees ${p.label} after camera back on`, timeout: 20_000 }).toBeGreaterThan(0);
      console.log(`${p.label} video resumed at host after ${Date.now() - t0} ms`);
    }
    results.push('PASS  Camera: off/on reflected on every screen, late joiner sees camera-off state, video resumes');
  });

  await test.step('5. Host mutes everyone', async () => {
    const [p1, p5, p6] = [members[0], members[4], members[5]];
    await host.page.click('button:has-text("Mute All")');
    for (const m of members) {
      await expect.poll(() => m.page.evaluate(() => (window as any).__meet.isMuted), { message: `${m.label} muted` }).toBe(true);
      await expect(m.page.locator('#btnMic')).toHaveClass(/ctrl-danger/);
    }
    for (const viewer of present()) for (const m of members)
      await expect(viewer.page.locator(`#badge-mute-${m.connId}`), `${viewer.label} shows mute badge for ${m.label}`).toHaveCount(1);
    expect(await host.page.evaluate(() => (window as any).__meet.isMuted), 'host stays unmuted').toBe(false);

    await sleep(1500);   // let in-flight audio drain
    const muted = await mediaDelta([members[1]]);
    const hostEnergy = muted[0][host.connId].energy;
    for (const m of members.filter(x => x !== members[1]))
      expect(muted[0][m.connId].energy, `${m.label} silent after mute all`).toBeLessThan(Math.max(hostEnergy * 0.05, 1e-6));

    // participants may unmute themselves
    await p5.page.click('#btnMic');
    await expect.poll(() => p5.page.evaluate(() => (window as any).__meet.isMuted)).toBe(false);
    await sleep(1500);
    const un = await mediaDelta([host]);
    expect(un[0][p5.connId].energy, 'P5 audible after unmuting').toBeGreaterThan(0);

    // someone who joins after mute-all starts muted
    await p6.page.close();
    await enterRoom(p6);
    await expect.poll(() => p6.page.evaluate(() => (window as any).__meet.isMuted), { message: 'P6 muted on rejoin' }).toBe(true);
    await expectAllConnected('after P6 rejoin');

    // a non-host cannot mute everyone
    await p1.page.evaluate(() => conn.invoke('MuteAll', ROOM_CODE));
    await sleep(2000);
    expect(await p5.page.evaluate(() => (window as any).__meet.isMuted), 'P5 unaffected by non-host MuteAll').toBe(false);
    results.push(`PASS  Mute all: all ${PEOPLE - 1} muted (state, button, badges, silent audio); unmute works; late joiner starts muted; non-host blocked`);
  });

  await test.step('6. Spotlight', async () => {
    const [p2, p4, p8] = [members[1], members[3], members[7]];
    await host.page.click('#btnSpotlight');
    await host.page.click(`#tile-${p4.connId}`);
    const expectSpot = async (viewer: Person, target: Person) => {
      const id = viewer === target ? 'tile-local' : `tile-${target.connId}`;
      await expect(viewer.page.locator('#videoGrid'), `${viewer.label} spotlight mode`).toHaveClass(/spotlight-mode/);
      await expect(viewer.page.locator('#videoGrid .video-tile'), `${viewer.label} one tile on stage`).toHaveCount(1);
      await expect(viewer.page.locator(`#videoGrid #${id}`), `${viewer.label} shows ${target.label}`).toHaveCount(1);
    };
    for (const v of present()) await expectSpot(v, p4);

    // late joiner sees the spotlight
    await p8.page.close();
    await enterRoom(p8);
    await expectSpot(p8, p4);
    // spotlighted video keeps playing for viewers
    const d = await mediaDelta([p8]);
    expect(d[0][p4.connId].frames, 'P8 sees spotlighted P4 video').toBeGreaterThan(0);

    // spotlighted person leaves → spotlight cleared everywhere
    await p4.page.close();
    for (const v of present()) {
      await expect(v.page.locator('#videoGrid'), `${v.label} spotlight cleared`).not.toHaveClass(/spotlight-mode/);
      await expect.poll(() => tileCount(v)).toBe(present().length);
    }
    await enterRoom(p4);

    // spotlight someone else, then clear it
    await host.page.evaluate(cid => (window as any).setSpotlight(cid), p2.connId);
    for (const v of present()) await expectSpot(v, p2);
    await host.page.evaluate(() => (window as any).clearSpotlight());
    for (const v of present()) {
      await expect(v.page.locator('#videoGrid')).not.toHaveClass(/spotlight-mode/);
      await expect.poll(() => v.page.locator('#videoGrid .video-tile').count()).toBe(10);
    }
    await expectAllConnected('after spotlight');
    results.push(`PASS  Spotlight: shown on all ${PEOPLE} screens, synced to late joiner, cleared when spotlighted person leaves, clear restores grid`);
  });

  await test.step('7. Leave / rejoin churn and reconnect', async () => {
    const [p1, p2, p6, p9] = [members[0], members[1], members[5], members[8]];
    await p2.page.close();                                                // tab closed
    await p6.page.click('button:has-text("Leave")');                     // Leave button
    await p9.page.goto(`${BASE}/Meeting`);                                // navigated away
    await p9.page.close();
    if (!p6.page.isClosed()) await p6.page.close();
    for (const v of present())
      await expect.poll(() => tileCount(v), { message: `${v.label} after 3 left`, timeout: 10_000 }).toBe(PEOPLE - 3);
    for (const p of [p2, p6, p9]) { await enterRoom(p); await sleep(1500); }
    await expectAllConnected('after churn');

    // drop P1's SignalR socket; the automatic reconnect must rejoin the media room
    // (the minified SignalR build has no stable property name for the socket)
    await p1.page.evaluate(() => (Object.values((conn as any).connection.transport)
      .find(v => v instanceof WebSocket) as WebSocket).close(4000));
    await expect.poll(() => p1.page.evaluate(() => (window as any).__meet.myConnId), { timeout: 30_000 }).not.toBe(p1.connId);
    p1.connId = await p1.page.evaluate(() => (window as any).__meet.myConnId);
    await expectAllConnected('after P1 reconnect');
    await expectAllHearAndSee('after churn and reconnect');
    for (const p of present()) await expectNoClipping(p);
    results.push('PASS  Join/leave: tiles removed on close/Leave/navigate, restored on rejoin; SignalR reconnect rejoins media');
  });

  await test.step('8. Live page viewers watch through the media server', async () => {
    // A live sermon linked to this meeting (removed again in afterAll)
    const title = `E2E live test ${Date.now()}`;
    const req = host.ctx.request;
    const add = await req.post(`${BASE}/Admin/AddSermon`, { form: {
      Title: title, Speaker: 'E2E test', SermonDate: new Date().toISOString().slice(0, 10), IsPublished: 'true',
      deliveryMode: 'meeting', MeetingRoomCode: roomUrl.split('/').pop()! } });
    expect(add.ok(), 'test sermon created').toBeTruthy();
    const list = await (await req.get(`${BASE}/Admin/Sermons?search=${encodeURIComponent(title)}`)).text();
    liveSermonId = Number(list.match(/id="row-(\d+)"/)?.[1] ?? 0);
    expect(liveSermonId, 'test sermon id').toBeGreaterThan(0);
    expect((await req.post(`${BASE}/Admin/ToggleLive/${liveSermonId}`)).ok(), 'sermon live').toBeTruthy();

    const guest = await newViewer('Guest viewer');
    const member = await newViewer('Member viewer', { email: 'meettest9@church.test', password: MEMBER_PASSWORD });
    const inMeeting = () => present().map(p => p.connId).sort();
    for (const v of [guest, member]) {
      await expect.poll(() => viewerTiles(v), { message: `${v.label} tiles`, timeout: 30_000 }).toEqual(inMeeting());
      await v.page.evaluate(() => (window as any).__live.mediaRoom.startAudio());
      const clipped = await v.page.evaluate(() => {
        const g = document.getElementById('guestVideoGrid')!.getBoundingClientRect();
        return Array.from(document.querySelectorAll('#guestVideoGrid .video-tile')).filter(t => {
          const r = t.getBoundingClientRect();
          return r.top < g.top - 2 || r.bottom > g.bottom + 2 || r.left < g.left - 2 || r.right > g.right + 2 || r.height < 40;
        }).map(t => t.id);
      });
      expect(clipped, `${v.label} clipped tiles`).toEqual([]);
    }

    // Viewers see and hear every participant (audio only from people who aren't muted)
    const muted = new Set<string>();
    for (const p of present()) if (await p.page.evaluate(() => (window as any).__meet.isMuted)) muted.add(p.connId);
    const pending = new Set([guest, member].flatMap(v => present().map(p => `${v.label}|${p.label}`)));
    for (let round = 0; round < 10 && pending.size; round++) {
      const deltas = await mediaDelta([guest, member]);
      [guest, member].forEach((v, i) => {
        for (const p of present()) {
          const d = deltas[i][p.connId];
          if (d && d.frames > 0 && (muted.has(p.connId) || (d.audioBytes > 0 && d.energy > 0))) pending.delete(`${v.label}|${p.label}`);
        }
      });
    }
    expect([...pending], 'viewer ← participant pairs without media').toEqual([]);

    // Viewers are hidden from the meeting: no extra tiles, not in anyone's media room
    for (const p of present()) {
      expect(await tileCount(p), `${p.label} tiles with viewers watching`).toBe(present().length);
      expect(await p.page.evaluate(() => Array.from((window as any).__meet.room.remoteParticipants.keys())
        .filter((id: any) => id.startsWith('viewer-')).length), `${p.label} sees no viewers`).toBe(0);
    }

    // Viewers follow people leaving and joining
    const p7 = members[6];
    await p7.page.close();
    for (const v of [guest, member])
      await expect.poll(() => viewerTiles(v), { message: `${v.label} after P7 left` }).toEqual(inMeeting());
    await enterRoom(p7);
    for (const v of [guest, member])
      await expect.poll(() => viewerTiles(v), { message: `${v.label} after P7 rejoined`, timeout: 30_000 }).toEqual(inMeeting());
    await expect.poll(async () => (await mediaDelta([guest], 3000))[0][p7.connId]?.frames ?? 0,
      { message: 'guest sees P7 after rejoin', timeout: 30_000 }).toBeGreaterThan(0);
    results.push('PASS  Live page: guest and member viewers see/hear everyone via the media server, stay hidden from the meeting, follow leave/rejoin');
  });

  await test.step('9. Host controls enforced by the media server', async () => {
    // P3 and P4 play a tampered page that ignores the host's hub messages
    const [p3, p4] = [members[2], members[3]];
    for (const p of [p3, p4])
      await p.page.evaluate(() => { for (const m of ['ForceMuted', 'Removed', 'MeetingEnded']) (conn as any).off(m); });
    if (await p3.page.evaluate(() => (window as any).__meet.isMuted)) await p3.page.click('#btnMic');
    const micMutedOnHost = (cid: string) => host.page.evaluate(id => {
      const rp = (window as any).__meet.room.remoteParticipants.get(id);
      const mic = rp && Array.from(rp.trackPublications.values()).find((t: any) => t.source === 'microphone') as any;
      return mic ? mic.isMuted : null;
    }, cid);
    await expect.poll(() => micMutedOnHost(p3.connId), { message: 'P3 unmuted before the test' }).toBe(false);

    await host.page.evaluate(id => (conn as any).invoke('ForceMute', ROOM_CODE, id), p3.connId);
    await expect.poll(() => micMutedOnHost(p3.connId), { message: 'P3 muted by the server' }).toBe(true);
    await expect.poll(() => p3.page.evaluate(() => (window as any).__meet.isMuted), { message: "P3's mic button follows" }).toBe(true);

    await host.page.evaluate(id => (conn as any).invoke('RemoveParticipant', ROOM_CODE, id), p3.connId);
    // P3's page may reconnect on its own; what matters is that it gets and sends nothing
    await expect.poll(() => p3.page.evaluate(() => {
      const r = (window as any).__meet.room;
      if (r.state === 'disconnected') return 0;
      return Array.from(r.remoteParticipants.values()).flatMap((rp: any) => Array.from(rp.trackPublications.values()))
        .filter((t: any) => t.isSubscribed).length;
    }), { message: 'P3 receives no media', timeout: 30_000 }).toBe(0);
    await expect.poll(() => host.page.evaluate(id => {
      const rp = (window as any).__meet.room.remoteParticipants.get(id);
      return rp ? rp.trackPublications.size : 0;
    }, p3.connId), { message: 'nobody receives media from P3', timeout: 30_000 }).toBe(0);
    await sleep(8000);   // and it stays that way after any automatic reconnect
    expect(await host.page.evaluate(id => (window as any).__meet.room.remoteParticipants.get(id)?.trackPublications.size ?? 0, p3.connId),
      'P3 still sends nothing after reconnecting').toBe(0);

    await host.page.evaluate(() => (conn as any).invoke('EndMeeting', ROOM_CODE));
    for (const p of present().filter(p => p !== p3 && p !== p4)) {
      await expect.poll(() => p.page.evaluate(() => (window as any).__meet.room.state), { message: `${p.label} media closed` }).toBe('disconnected');
      await expect(p.page.locator('#endedOverlay'), `${p.label} sees Meeting Ended`).toBeVisible();
    }
    // P4 ignores the hub and its page may reconnect by itself, into an empty room
    // where it can't send or receive anything
    await expect.poll(() => p4.page.evaluate(() => {
      const r = (window as any).__meet.room;
      return r.state === 'disconnected' ? 0 : r.remoteParticipants.size
        + (r.localParticipant.permissions?.canPublish === false ? 0 : r.localParticipant.trackPublications.size);
    }), { message: 'P4 cut off after the meeting ended', timeout: 30_000 }).toBe(0);
    for (const v of viewers) {
      await expect.poll(() => v.page.evaluate(() => (window as any).__live.mediaRoom.state), { message: `${v.label} media closed` }).toBe('disconnected');
      expect(await viewerTiles(v), `${v.label} tiles cleared`).toEqual([]);
      await expect(v.page.locator('#startingSoonOverlay h2'), `${v.label} told the meeting ended`).toHaveText('This meeting has ended');
    }
    results.push('PASS  Server enforcement: mute, remove and end meeting work even on a page that ignores the host');
  });

  await test.step('10. No hub or page errors', async () => {
    const errs = [...everyone(), ...viewers].flatMap(p => p.errors.map(e => `${p.label}: ${e}`));
    expect(errs).toEqual([]);
    results.push('PASS  No hub exceptions or page errors on any screen');
  });
});
