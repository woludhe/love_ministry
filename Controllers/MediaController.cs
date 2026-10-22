// ═══════════════════════════════════════════════════════════
// Replace the Live() and Watch() actions in MediaController
// inside Controllers/Controllers.cs
// ═══════════════════════════════════════════════════════════

using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using ChurchMinistry.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class MediaController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IActivityService _activity;
    public MediaController(ApplicationDbContext db, UserManager<ApplicationUser> um, IActivityService activity) {
        _db = db; _userManager = um; _activity = activity;
    }

    // ── Sermon listing ──────────────────────────────────────
    public async Task<IActionResult> Index(
        string? search, string? speaker, string? series, int page = 1)
    {
        const int pageSize = PagerModel.DefaultPageSize;
        var q = _db.Sermons.Where(s => s.IsPublished).AsQueryable();

        if (!string.IsNullOrEmpty(search))
            q = q.Where(s => s.Title.Contains(search)
                          || (s.Description != null && s.Description.Contains(search)));
        if (!string.IsNullOrEmpty(speaker))
            q = q.Where(s => s.Speaker == speaker);
        if (!string.IsNullOrEmpty(series))
            q = q.Where(s => s.SeriesName == series);

        var total = await q.CountAsync();
        var sermons = await q.OrderByDescending(s => s.SermonDate)
                             .Skip((page - 1) * pageSize).Take(pageSize)
                             .ToListAsync();

        // Find the live sermon (standard or online meeting)
        var liveSermon = await _db.Sermons.FirstOrDefaultAsync(s => s.IsLive && s.IsPublished);

        return View(new SermonIndexViewModel
        {
            Sermons = sermons,
            LiveSermon = liveSermon,
            SearchTerm = search,
            Speaker = speaker,
            Series = series,
            Page = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    // ── Watch a recorded sermon ─────────────────────────────
    public async Task<IActionResult> Watch(int id)
    {
        var sermon = await _db.Sermons.FindAsync(id);
        if (sermon == null) return NotFound();

        // Inactive sermons are hidden from the public; admins/pastors can still preview them
        if (!sermon.IsPublished && !(User.IsInRole("Admin") || User.IsInRole("Pastor")))
            return NotFound();

        sermon.ViewCount++;
        await _db.SaveChangesAsync();

        // Upcoming sermons only show a poster, so opening one isn't counted as watching
        if (sermon.IsLive || sermon.SermonDate <= DateTime.UtcNow)
            await _activity.LogForUserAsync(_userManager.GetUserId(User),
                MemberActivityType.WatchSermon, sermon.Title, sermon.Id, oncePerDay: true);

        // Recommendations: same series → same speaker → latest
        var recommended = await _db.Sermons
            .Where(s => s.IsPublished && s.Id != id
                     && s.SeriesName != null
                     && s.SeriesName == sermon.SeriesName)
            .OrderByDescending(s => s.SermonDate).Take(3).ToListAsync();

        if (recommended.Count < 3)
        {
            var ids = recommended.Select(s => s.Id).Append(id).ToList();
            recommended.AddRange(await _db.Sermons
                .Where(s => s.IsPublished && !ids.Contains(s.Id)
                         && s.Speaker == sermon.Speaker)
                .OrderByDescending(s => s.SermonDate)
                .Take(3 - recommended.Count).ToListAsync());
        }

        if (recommended.Count < 3)
        {
            var ids = recommended.Select(s => s.Id).Append(id).ToList();
            recommended.AddRange(await _db.Sermons
                .Where(s => s.IsPublished && !ids.Contains(s.Id))
                .OrderByDescending(s => s.SermonDate)
                .Take(3 - recommended.Count).ToListAsync());
        }

        ViewBag.Recommended = recommended;
        return View(sermon);
    }

    // ── Live page ───────────────────────────────────────────
    public async Task<IActionResult> Live()
    {
        // Find the currently live sermon
        var liveSermon = await _db.Sermons
            .FirstOrDefaultAsync(s => s.IsLive && s.IsPublished);

        if (liveSermon == null)
        {
            // No live sermon — show offline page
            return View((Sermon?)null);
        }

        // If the sermon is linked to an online meeting room,
        // load the meeting room to verify it exists and is accessible
        if (liveSermon.IsOnlineMeeting && !string.IsNullOrEmpty(liveSermon.MeetingRoomCode))
        {
            var meetingRoom = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .Include(r => r.Participants.Where(p => p.LeftAt == null))
                .FirstOrDefaultAsync(r => r.RoomCode == liveSermon.MeetingRoomCode);

            if (meetingRoom != null)
            {
                // Auto-start the meeting room if it's still scheduled
                if (meetingRoom.Status == RoomStatus.Scheduled)
                {
                    meetingRoom.Status = RoomStatus.Live;
                    meetingRoom.StartedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }

                liveSermon.LinkedMeetingRoom = meetingRoom;
                ViewBag.MeetingRoom = meetingRoom;
                ViewBag.ParticipantCount = meetingRoom.Participants.Count;
            }
        }

        ViewBag.MemberName = "Guest";

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            var member = user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);

            ViewBag.MemberName = member?.FullName
           ?? user?.UserName
           ?? User.Identity?.Name
           ?? "Guest";
        }

        // Load the live session settings overlay config
        var liveSession = await _db.LiveSessions
            .Include(s => s.ActiveVerse)
            .Include(s => s.Announcements.Where(a => a.IsActive))
            .FirstOrDefaultAsync(s => s.SermonId == liveSermon.Id);

        ViewBag.LiveSession = liveSession;

        return View(liveSermon);
    }
}