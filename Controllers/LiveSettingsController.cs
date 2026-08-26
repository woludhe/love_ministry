using ChurchMinistry.Data;
using ChurchMinistry.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ChurchMinistry.Hubs;

namespace ChurchMinistry.Controllers
{
    [Authorize(Roles = "Admin,Pastor")]
    public class LiveSettingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<LiveSessionHub> _hub;

        public LiveSettingsController(ApplicationDbContext db, IHubContext<LiveSessionHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        // ── Settings page for a given sermon ──────────────────
        [HttpGet("Admin/LiveSettings/{sermonId}")]
        public async Task<IActionResult> Index(int sermonId)
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
            session.AutoRotateVerses = form.AutoRotateVerses;
            session.VerseRotateSeconds = form.VerseRotateSeconds;
            session.ShowMarquee = form.ShowMarquee;
            session.MarqueeSpeed = form.MarqueeSpeed;
            session.ShowGivingPanel = form.ShowGivingPanel;
            session.GivingCta = form.GivingCta;
            session.GivingPanelPosition = form.GivingPanelPosition;
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

        //// ── Add Bible verse ────────────────────────────────────
        //[HttpPost("Admin/LiveSettings/{sermonId}/AddVerse")]
        //public async Task<IActionResult> AddVerse(int sermonId,
        //    string reference, string verseText, string translation = "KJV")
        //{
        //    if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(verseText))
        //    {
        //        TempData["Error"] = "Reference and verse text are required.";
        //        return RedirectToAction("Index", new { sermonId });
        //    }

        //    var session = await GetOrCreateSessionAsync(sermonId);
        //    var verse = new LiveBibleVerse
        //    {
        //        LiveSessionId = session.Id,
        //        Reference = reference.Trim(),
        //        VerseText = verseText.Trim(),
        //        Translation = translation,
        //        SortOrder = session.BibleVerses.Count
        //    };
        //    _db.LiveBibleVerses.Add(verse);
        //    await _db.SaveChangesAsync();

        //    TempData["Success"] = $"Verse {reference} added.";
        //    return RedirectToAction("Index", new { sermonId });
        //}

        //// ── Activate a verse (push to viewers) ────────────────
        //[HttpPost("Admin/LiveSettings/{sermonId}/ActivateVerse/{verseId}")]
        //public async Task<IActionResult> ActivateVerse(int sermonId, int verseId)
        //{
        //    var session = await _db.LiveSessions
        //        .Include(s => s.BibleVerses)
        //        .FirstOrDefaultAsync(s => s.SermonId == sermonId);
        //    if (session == null) return NotFound();

        //    // Deactivate all
        //    session.BibleVerses.ToList().ForEach(v => v.IsActive = false);

        //    var verse = session.BibleVerses.FirstOrDefault(v => v.Id == verseId);
        //    if (verse == null) return NotFound();

        //    verse.IsActive = true;
        //    session.ActiveVerseId = verseId;
        //    session.UpdatedAt = DateTime.UtcNow;
        //    await _db.SaveChangesAsync();

        //    // Push to viewers in real time
        //    await _hub.Clients.Group($"live-{sermonId}")
        //        .SendAsync("VerseChanged", new
        //        {
        //            reference = verse.Reference,
        //            verseText = verse.VerseText,
        //            translation = verse.Translation
        //        });

        //    return Json(new { success = true, reference = verse.Reference });
        //}

        [HttpPost]
        [Route("Admin/LiveSettings/{sermonId}/AddVerse")]
        public async Task<IActionResult> AddVerse(int sermonId, string reference,
    string verseText, string translation)
        {
            // ── get or create the live session ──────────────────────
            // (keep your existing session lookup logic here — this is just the wrapper)
            var session = await _db.LiveSessions
                .Include(s => s.BibleVerses)
                .FirstOrDefaultAsync(s => s.SermonId == sermonId);

            if (session == null)
            {
                session = new LiveSession { SermonId = sermonId };
                _db.LiveSessions.Add(session);
                await _db.SaveChangesAsync();
            }

            // ── save the verse ───────────────────────────────────────
            var verse = new LiveBibleVerse
            {
                LiveSessionId = session.Id,
                Reference = reference?.Trim() ?? "",
                VerseText = verseText?.Trim() ?? "",
                Translation = translation?.Trim() ?? "KJV",
                SortOrder = session.BibleVerses.Count
            };
            _db.LiveBibleVerses.Add(verse);
            await _db.SaveChangesAsync();

            // ── return JSON so JS can get the new verse ID ───────────
            // (previously this returned RedirectToAction or location.reload)
            return Json(new { success = true, id = verse.Id, reference = verse.Reference });
        }

        // ═══════════════════════════════════════════════════════════
        // ActivateVerse should also return JSON (it likely already does,
        // but confirm it returns { success, reference }):
        // ═══════════════════════════════════════════════════════════

        [HttpPost]
        [Route("Admin/LiveSettings/{sermonId}/ActivateVerse/{id}")]
        public async Task<IActionResult> ActivateVerse(int sermonId, int id)
        {
            var verse = await _db.LiveBibleVerses
                .Include(v => v.LiveSession)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (verse == null) return NotFound();

            // Deactivate all verses in this session
            await _db.LiveBibleVerses
                .Where(v => v.LiveSessionId == verse.LiveSessionId)
                .ForEachAsync(v => v.IsActive = false);

            verse.IsActive = true;
            await _db.SaveChangesAsync();

            // Push via SignalR to all live viewers
            await _hub.Clients
                .Group($"live-{verse.LiveSession.SermonId}")
                .SendAsync("VerseChanged", new
                {
                    reference = verse.Reference,
                    verseText = verse.VerseText,
                    translation = verse.Translation
                });

            return Json(new { success = true, reference = verse.Reference });
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

        // ── Delete verse ───────────────────────────────────────
        [HttpPost("Admin/LiveSettings/{sermonId}/DeleteVerse/{verseId}")]
        public async Task<IActionResult> DeleteVerse(int sermonId, int verseId)
        {
            var verse = await _db.LiveBibleVerses.FindAsync(verseId);
            if (verse != null) { _db.LiveBibleVerses.Remove(verse); await _db.SaveChangesAsync(); }
            return Json(new { success = true });
        }

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
                givingPanelPos = session.GivingPanelPosition.ToString().ToLower(),
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
                activeAnnouncement = session.Announcements.FirstOrDefault()?.Text
            });
        }

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
