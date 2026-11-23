/* Service worker for the Home Screen app.
   - Pages always come from the network (they're per-user and live); if the network is down, show offline.html.
   - Static files (/css, /js, /lib, /images, icons) are cached after their first load.
   - Nothing else is touched: logins, POSTs, SignalR hubs, payments, uploads and media all bypass the worker.
   Bump VERSION to drop old caches after a big static-file change. */
const VERSION = "v2";
const STATIC_CACHE = "static-" + VERSION;
const OFFLINE_URL = "/offline.html";
const PRECACHE = [OFFLINE_URL, "/apple-touch-icon.png", "/android-chrome-192x192.png"];
const STATIC_PREFIXES = ["/css/", "/js/", "/lib/", "/images/", "/splash/"];
const STATIC_FILES = ["/favicon.ico", "/apple-touch-icon.png", "/android-chrome-192x192.png", "/android-chrome-512x512.png"];

self.addEventListener("install", (event) => {
    event.waitUntil(caches.open(STATIC_CACHE).then((c) => c.addAll(PRECACHE)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(keys.filter((k) => k !== STATIC_CACHE).map((k) => caches.delete(k))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", (event) => {
    const req = event.request;
    if (req.method !== "GET") return;
    const url = new URL(req.url);
    if (url.origin !== self.location.origin) return;

    // Page loads: network only, offline page as the fallback
    if (req.mode === "navigate") {
        event.respondWith(fetch(req).catch(() => caches.match(OFFLINE_URL)));
        return;
    }

    // Static files. Versioned ones (?v= from asp-append-version) never change, so cache first.
    // Unversioned ones are served from cache but refreshed in the background, so edits still reach phones.
    const isStatic = STATIC_PREFIXES.some((p) => url.pathname.startsWith(p)) || STATIC_FILES.includes(url.pathname);
    if (!isStatic) return;
    const versioned = url.searchParams.has("v");
    event.respondWith(
        caches.open(STATIC_CACHE).then((cache) =>
            cache.match(req).then((hit) => {
                const network = fetch(req).then((res) => {
                    if (res.ok && res.type === "basic") cache.put(req, res.clone());
                    return res;
                });
                if (hit && !versioned) event.waitUntil(network.catch(() => { }));
                return hit || network;
            })
        )
    );
});

// ── Push alerts (sent by PushSenderService for every new Notification) ──
self.addEventListener("push", (event) => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; }
    catch (e) { data = { body: event.data ? event.data.text() : "" }; }

    const jobs = [
        self.registration.showNotification(data.title || "Apostle Enock Makoko", {
            body: data.body || "",
            icon: "/android-chrome-192x192.png",
            badge: "/android-chrome-192x192.png",
            tag: data.tag,
            data: { url: data.url || "/Notifications" }
        })
    ];
    // Unread count on the app icon (Home Screen app on iPhone, installed app on desktop/Android)
    if (typeof data.badge === "number" && self.navigator.setAppBadge) {
        jobs.push((data.badge > 0 ? self.navigator.setAppBadge(data.badge) : self.navigator.clearAppBadge()).catch(() => { }));
    }
    event.waitUntil(Promise.all(jobs));
});

self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    const target = new URL((event.notification.data && event.notification.data.url) || "/Notifications", self.location.origin);
    if (target.origin !== self.location.origin) return;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
        for (const client of windows) {
            if (new URL(client.url).origin !== self.location.origin) continue;
            try {
                await client.focus();
                await client.navigate(target.href);
                return;
            } catch (e) { /* fall through to a new window */ }
        }
        await self.clients.openWindow(target.href);
    })());
});
