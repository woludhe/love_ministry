using ChurchMinistry.Data;
using ChurchMinistry.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ChurchMinistry.Hubs;
using ChurchMinistry.Services;

namespace ChurchMinistry.Controllers
{
    [Authorize(Roles = "Admin,Pastor")]
    public class LiveSettingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<LiveSessionHub> _hub;
        private readonly IBibleService _bible;

        public LiveSettingsController(ApplicationDbContext db, IHubContext<LiveSessionHub> hub, IBibleService bible)
        {
            _db = db;
            _hub = hub;
            _bible = bible;
        }

        // ── Settings page for a given sermon ──────────────────
        [HttpGet("Admin/LiveSettings/{sermonId}")]
        public async Task<IActionResult> Index(int sermonId, [FromServices] IWebHostEnvironment env)
        {
            var sermon = await _db.Sermons.FindAsync(sermonId);
            if (sermon == null) return NotFound();
            if (!sermon.IsLive)
            {
                TempData["Error"] = "This sermon is not currently live.";
                return RedirectToAction("Sermons", "Admin");
            }

            var session = await GetOrCreateSessionAsync(sermonId);
            session.Sermon = sermon;
            ViewBag.Sermon = sermon;
            ViewBag.BibleTranslations = _bible.GetTranslations();
            ViewBag.DefaultTranslation = _bible.DefaultCode();
            ViewBag.BibleBooks = _bible.GetBooks();

            // Background music is offered for online meeting services only
            if (sermon.IsOnlineMeeting)
            {
                ViewBag.MusicFiles = AudioStore.ListFiles(env);
                ViewBag.MusicTypes = AudioStore.ListTypes(env);
                ViewBag.MusicState = MusicJson(session);
            }

            return View(session);
        }

        // ── Save general settings ──────────────────────────────
        [HttpPost("Admin/LiveSettings/{sermonId}/Save")]
        public async Task<IActionResult> Save(int sermonId, LiveSession form)
        {
            ModelState.Remove("Sermon");
            if (!ModelState.IsValid)
                return View(form);

            var session = await GetOrCreateSessionAsync(sermonId);

            session.ShowVerseOverlay = form.ShowVerseOverlay;
            session.ShowMarquee = form.ShowMarquee;
            session.MarqueeSpeed = form.MarqueeSpeed;
            session.ShowGivingPanel = form.ShowGivingPanel;
            session.GivingCta = form.GivingCta;
            session.ShowMtn = form.ShowMtn;
            session.ShowAirtel = form.ShowAirtel;
            session.ShowMpesa = form.ShowMpesa;
            session.ShowVisa = form.ShowVisa;
            session.ShowPrayerButton = form.ShowPrayerButton;
            session.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Push visibility changes to all live viewers instantly
            await _hub.Clients.Group($"live-{sermonId}")
                .SendAsync("SettingsChanged", new
                {
                    showVerseOverlay = session.ShowVerseOverlay,
                    showMarquee = session.ShowMarquee,
                    showGivingPanel = session.ShowGivingPanel,
                    givingCta = session.GivingCta,
                    marqueeSpeed = session.MarqueeSpeed.ToString().ToLower(),
                    showPrayerButton = session.ShowPrayerButton
                });

            TempData["Success"] = "Settings saved and pushed to viewers!";
            return RedirectToAction("Index", new { sermonId });
        }

        // ── Bible: load a whole chapter ("John 1", "Ps 23", "John 1:5") ──
        [HttpGet("Admin/LiveSettings/{sermonId}/Chapter")]
        public async Task<IActionResult> Chapter(int sermonId, string? q, string? translation, int? book, int? chapter)
        {
            int? highlight = null;
            if (book == null || chapter == null)
            {
                var (reference, parseError) = _bible.ParseReference(q);
                if (reference == null) return Json(new { error = parseError });
                book = reference.Book; chapter = reference.Chapter; highlight = reference.Verse;
            }

            var (result, error) = await _bible.GetChapterAsync(translation, book.Value, chapter.Value);
            if (result == null) return Json(new { error });

            return Json(new
            {
                reference = $"{RefBook(result)} {result.Chapter}",
                book = result.Book,
                bookName = RefBook(result),
                chapter = result.Chapter,
                chapterCount = result.ChapterCount,
                code = result.Code,
                translation = result.Abbrev,
                highlight = highlight != null && result.Verses.Any(v => v.Number == highlight) ? highlight : null,
                verses = result.Verses.Select(v => new { n = v.Number, label = v.Label, text = v.Text })
            });
        }

        // ── Bible: put one verse on screen and log it ──────────
        // The text is read from the Bible file here, not taken from the browser
        [HttpPost("Admin/LiveSettings/{sermonId}/ShowVerse")]
        public async Task<IActionResult> ShowVerse(int sermonId, string translation, int book, int chapter, int verse)
        {
            var (result, error) = await _bible.GetChapterAsync(translation, book, chapter);
            var text = result?.Verses.FirstOrDefault(v => v.Number == verse);
            if (result == null || text == null)
                return Json(new { success = false, message = error ?? "That verse isn't in this chapter." });

            var session = await GetOrCreateSessionAsync(sermonId);
            var reference = $"{RefBook(result)} {chapter}:{text.Label}";

            // One log row per verse: showing it again moves it back to the top
            var entry = session.BibleVerses.FirstOrDefault(v => v.Reference == reference && v.Translation == result.Abbrev);
            if (entry == null)
            {
                entry = new LiveBibleVerse
                {
                    LiveSessionId = session.Id,
                    Reference = reference,
                    VerseText = text.Text,
                    Translation = result.Abbrev,
                    SortOrder = session.BibleVerses.Count
                };
                _db.LiveBibleVerses.Add(entry);
            }

            await PutOnScreenAsync(session, entry);
            return Json(VerseJson(entry));
        }

        // ── Show a verse from the log again ───────────────────
        [HttpPost("Admin/LiveSettings/{sermonId}/ActivateVerse/{id}")]
        public async Task<IActionResult> ActivateVerse(int sermonId, int id)
        {
            var session = await GetOrCreateSessionAsync(sermonId);
            var entry = session.BibleVerses.FirstOrDefault(v => v.Id == id);
            if (entry == null) return Json(new { success = false, message = "That verse is no longer in the log." });

            await PutOnScreenAsync(session, entry);
            return Json(VerseJson(entry));
        }

        // ── Clear verse overlay ────────────────────────────────
        [HttpPost("Admin/LiveSettings/{sermonId}/ClearVerse")]
        public async Task<IActionResult> ClearVerse(int sermonId)
        {
            var session = await _db.LiveSessions
                .Include(s => s.BibleVerses)
                .FirstOrDefaultAsync(s => s.SermonId == sermonId);
            if (session != null)
            {
                session.BibleVerses.ToList().ForEach(v => v.IsActive = false);
                session.ActiveVerseId = null;
                await _db.SaveChangesAsync();
            }
            await _hub.Clients.Group($"live-{sermonId}").SendAsync("VerseCleared");
            return Json(new { success = true });
        }

        // ── Delete verse from the log ──────────────────────────
        [HttpPost("Admin/LiveSettings/{sermonId}/DeleteVerse/{verseId}")]
        public async Task<IActionResult> DeleteVerse(int sermonId, int verseId)
        {
            var verse = await _db.LiveBibleVerses.Include(v => v.LiveSession)
                .FirstOrDefaultAsync(v => v.Id == verseId && v.LiveSession.SermonId == sermonId);
            if (verse == null) return Json(new { success = true });

            var wasOnScreen = verse.IsActive;
            if (verse.LiveSession.ActiveVerseId == verse.Id) verse.LiveSession.ActiveVerseId = null;
            _db.LiveBibleVerses.Remove(verse);
            await _db.SaveChangesAsync();

            if (wasOnScreen) await _hub.Clients.Group($"live-{sermonId}").SendAsync("VerseCleared");
            return Json(new { success = true });
        }

        // Saves first, then tells the live page and meeting rooms; only one verse is ever active
        private async Task PutOnScreenAsync(LiveSession session, LiveBibleVerse entry)
        {
            foreach (var v in session.BibleVerses) v.IsActive = false;
            entry.IsActive = true;
            entry.LastShownAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();          // gives a new entry its Id

            session.ActiveVerseId = entry.Id;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"live-{session.SermonId}").SendAsync("VerseChanged", new
            {
                reference = entry.Reference,
                verseText = entry.VerseText,
                translation = entry.Translation
            });
        }

        // "Psalm 23:1", not "Psalms 23:1"
        private static string RefBook(BibleChapter c) => c.BookName == "Psalms" ? "Psalm" : c.BookName;

        private static object VerseJson(LiveBibleVerse v) => new
        {
            success = true,
            id = v.Id,
            reference = v.Reference,
            translation = v.Translation,
            verseText = v.VerseText,
            shownAt = (v.LastShownAt ?? v.CreatedAt).ToLocalTime().ToString("h:mm tt")
        };

        // ── Add announcement ───────────────────────────────────
        [HttpPost("Admin/LiveSettings/{sermonId}/AddAnnouncement")]
        public async Task<IActionResult> AddAnnouncement(int sermonId, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["Error"] = "Announcement text is required.";
                return RedirectToAction("Index", new { sermonId });
            }

            var session = await GetOrCreateSessionAsync(sermonId);
            _db.LiveAnnouncements.Add(new LiveAnnouncement
            {
                LiveSessionId = session.Id,
                Text = text.Trim(),
                SortOrder = session.Announcements.Count
            });
            await _db.SaveChangesAsync();
            return RedirectToAction("Index", new { sermonId });
        }

        // ── Activate an announcement (push to viewers) ─────────
        [HttpPost("Admin/LiveSettings/{sermonId}/ActivateAnnouncement/{annId}")]
        public async Task<IActionResult> ActivateAnnouncement(int sermonId, int annId)
        {
            var session = await _db.LiveSessions
                .Include(s => s.Announcements)
                .FirstOrDefaultAsync(s => s.SermonId == sermonId);
            if (session == null) return NotFound();

            session.Announcements.ToList().ForEach(a => a.IsActive = false);
            var ann = session.Announcements.FirstOrDefault(a => a.Id == annId);
            if (ann == null) return NotFound();

            ann.IsActive = true;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"live-{sermonId}")
                .SendAsync("AnnouncementChanged", ann.Text);

            return Json(new { success = true, text = ann.Text });
        }

        // ── Delete announcement ────────────────────────────────
        [HttpPost("Admin/LiveSettings/{sermonId}/DeleteAnnouncement/{annId}")]
        public async Task<IActionResult> DeleteAnnouncement(int sermonId, int annId)
        {
            var ann = await _db.LiveAnnouncements.FindAsync(annId);
            if (ann != null) { _db.LiveAnnouncements.Remove(ann); await _db.SaveChangesAsync(); }
            return Json(new { success = true });
        }

        // ── API: current live state (polled by viewers) ────────
        [AllowAnonymous]
        [HttpGet("api/LiveSettings/{sermonId}/State")]
        public async Task<IActionResult> State(int sermonId)
        {
            var session = await _db.LiveSessions
                .Include(s => s.ActiveVerse)
                .Include(s => s.Announcements.Where(a => a.IsActive))
                .FirstOrDefaultAsync(s => s.SermonId == sermonId);

            if (session == null) return Ok(new { });

            return Ok(new
            {
                showVerseOverlay = session.ShowVerseOverlay,
                showMarquee = session.ShowMarquee,
                showGivingPanel = session.ShowGivingPanel,
                showPrayerButton = session.ShowPrayerButton,
                givingCta = session.GivingCta,
                marqueeSpeed = session.MarqueeSpeed.ToString().ToLower(),
                showMtn = session.ShowMtn,
                showAirtel = session.ShowAirtel,
                showMpesa = session.ShowMpesa,
                showVisa = session.ShowVisa,
                activeVerse = session.ActiveVerse == null ? null : new
                {
                    reference = session.ActiveVerse.Reference,
                    verseText = session.ActiveVerse.VerseText,
                    translation = session.ActiveVerse.Translation
                },
                activeAnnouncement = session.Announcements.FirstOrDefault()?.Text,
                music = MusicJson(session)
            });
        }

        // ── How many are connected right now (admin dashboard polls this) ──
        [HttpGet("Admin/LiveSettings/{sermonId}/Audience")]
        public IActionResult Audience(int sermonId) => Json(LiveAudience.Summary(sermonId));

        // ── Background music (meeting-type sermons) ────────────
        // action: play (playlist + index) | pause | resume | next | prev | jump (index) | stop | clear | volume | duck
        [HttpPost("Admin/LiveSettings/{sermonId}/Music")]
        public async Task<IActionResult> Music(int sermonId, string action, string? playlist, int? index,
                                               int? volume, bool? shuffle, bool? duck, [FromServices] IWebHostEnvironment env)
        {
            var sermon = await _db.Sermons.FindAsync(sermonId);
            if (sermon == null) return NotFound();
            if (!sermon.IsOnlineMeeting)
                return Json(new { success = false, message = "Background music is only for online meeting services." });

            var session = await GetOrCreateSessionAsync(sermonId);
            var tracks = LiveSession.ReadPlaylist(session.MusicPlaylistJson);
            var now = DateTime.UtcNow;

            switch (action)
            {
                case "play":
                {
                    // Only files from the audio library, with a known length
                    var chosen = LiveSession.ReadPlaylist(playlist)
                        .Where(t => t.Duration > 0 && AudioStore.IsLibraryUrl(env, t.Url))
                        .Take(200).ToList();
                    if (chosen.Count == 0)
                        return Json(new { success = false, message = "Choose at least one track from the Audio Library." });

                    var start = Math.Clamp(index ?? 0, 0, chosen.Count - 1);
                    if (shuffle == true)
                    {
                        // The chosen track first, the rest in a random order that everyone shares
                        var first = chosen[start];
                        chosen = new[] { first }.Concat(chosen.Where((_, i) => i != start).OrderBy(_ => Random.Shared.Next())).ToList();
                        start = 0;
                    }
                    tracks = chosen;
                    session.MusicPlaylistJson = System.Text.Json.JsonSerializer.Serialize(tracks,
                        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                    session.MusicShuffle = shuffle == true;
                    session.MusicState = "playing";
                    session.MusicAnchorAt = now.AddSeconds(-StartOfTrack(tracks, start));
                    break;
                }
                case "pause" when session.MusicState == "playing":
                    session.MusicPausedAt = MusicPosition(session, tracks, now);
                    session.MusicState = "paused";
                    break;
                case "resume" when session.MusicState == "paused" && tracks.Count > 0:
                    session.MusicAnchorAt = now.AddSeconds(-session.MusicPausedAt);
                    session.MusicState = "playing";
                    break;
                case "next" or "prev" or "jump" when session.MusicState != "stopped" && tracks.Count > 0:
                {
                    var position = MusicPosition(session, tracks, now);
                    var (current, offset) = LocateTrack(tracks, position);
                    var target = action switch
                    {
                        "next" => (current + 1) % tracks.Count,
                        // Back to the start of this track, or the one before if it's only just begun
                        "prev" => offset > 3 ? current : (current - 1 + tracks.Count) % tracks.Count,
                        _ => Math.Clamp(index ?? current, 0, tracks.Count - 1)
                    };
                    var targetStart = StartOfTrack(tracks, target);
                    if (session.MusicState == "playing") session.MusicAnchorAt = now.AddSeconds(-targetStart);
                    else session.MusicPausedAt = targetStart;
                    break;
                }
                case "stop":
                case "clear":   // stop and empty the playlist (frees its files for deleting)
                    session.MusicState = "stopped";
                    session.MusicAnchorAt = null;
                    session.MusicPausedAt = 0;
                    if (action == "clear") session.MusicPlaylistJson = null;
                    break;
                case "volume" when volume != null:
                    session.MusicVolume = Math.Clamp(volume.Value, 0, 100);
                    break;
                case "duck" when duck != null:
                    session.MusicDuckOnSpeech = duck.Value;
                    break;
                default:
                    return Json(new { success = false, message = "Nothing to do: start the music first." });
            }

            session.UpdatedAt = now;
            await _db.SaveChangesAsync();

            var state = MusicJson(session);
            await _hub.Clients.Group($"live-{sermonId}").SendAsync("MusicChanged", state);
            return Json(new { success = true, music = state });
        }

        // Seconds into the playlist right now (wrapping round when it loops)
        private static double MusicPosition(LiveSession s, List<MusicTrack> tracks, DateTime now)
        {
            var total = tracks.Sum(t => t.Duration);
            if (total <= 0) return 0;
            if (s.MusicState == "paused") return s.MusicPausedAt;
            if (s.MusicAnchorAt == null) return 0;
            var elapsed = (now - s.MusicAnchorAt.Value).TotalSeconds;
            return ((elapsed % total) + total) % total;
        }

        private static double StartOfTrack(List<MusicTrack> tracks, int index) => tracks.Take(index).Sum(t => t.Duration);

        private static (int Index, double Offset) LocateTrack(List<MusicTrack> tracks, double position)
        {
            for (var i = 0; i < tracks.Count; i++)
            {
                if (position < tracks[i].Duration) return (i, position);
                position -= tracks[i].Duration;
            }
            return (0, 0);
        }

        private static object MusicJson(LiveSession s) => new
        {
            state = s.MusicState,
            playlist = LiveSession.ReadPlaylist(s.MusicPlaylistJson)
                .Select(t => new { url = t.Url, title = t.Title, duration = t.Duration }),
            anchorMs = s.MusicAnchorAt.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(s.MusicAnchorAt.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
                : (long?)null,
            pausedAt = s.MusicPausedAt,
            volume = s.MusicVolume,
            shuffle = s.MusicShuffle,
            duck = s.MusicDuckOnSpeech,
            serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // ── Helper ─────────────────────────────────────────────
        private async Task<LiveSession> GetOrCreateSessionAsync(int sermonId)
        {
            var session = await _db.LiveSessions
                .Include(s => s.BibleVerses.OrderBy(v => v.SortOrder))
                .Include(s => s.Announcements.OrderBy(a => a.SortOrder))
                .Include(s => s.ActiveVerse)
                .FirstOrDefaultAsync(s => s.SermonId == sermonId);

            if (session != null) return session;

            session = new LiveSession { SermonId = sermonId };
            _db.LiveSessions.Add(session);
            await _db.SaveChangesAsync();
            return session;
        }
    }
}
