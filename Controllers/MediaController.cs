// ═══════════════════════════════════════════════════════════
// Replace the Live() and Watch() actions in MediaController
// inside Controllers/Controllers.cs
// ═══════════════════════════════════════════════════════════

using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using ChurchMinistry.ViewModels;
using ChurchMinistry.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

public class MediaController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IActivityService _activity;
    private readonly INotificationService _notifications;
    private readonly IHubContext<ChatHub> _chatHub;
    public MediaController(ApplicationDbContext db, UserManager<ApplicationUser> um, IActivityService activity,
        INotificationService notifications, IHubContext<ChatHub> chatHub) {
        _db = db; _userManager = um; _activity = activity; _notifications = notifications; _chatHub = chatHub;
    }

    private async Task<Member?> CurrentMemberAsync()
    {
        var userId = _userManager.GetUserId(User);
        return userId == null ? null : await _db.Members.FirstOrDefaultAsync(m => m.UserId == userId);
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
        if (!sermon.IsUpcoming)
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
        ViewBag.Engagement = await LoadEngagementAsync(sermon);
        return View(sermon);
    }

    // ═══ Engagement on the Watch page: reactions, prayer, questions, comments, notes ═══

    private async Task<SermonEngagementViewModel> LoadEngagementAsync(Sermon sermon)
    {
        var me = await CurrentMemberAsync();
        var reactions = await _db.SermonReactions.Where(r => r.SermonId == sermon.Id)
            .GroupBy(r => r.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var mine = me == null ? new List<SermonReactionKind>()
            : await _db.SermonReactions.Where(r => r.SermonId == sermon.Id && r.MemberId == me.Id)
                .Select(r => r.Kind).ToListAsync();
        return new SermonEngagementViewModel
        {
            SermonId = sermon.Id,
            AllowComments = sermon.AllowComments,
            AmenCount = reactions.FirstOrDefault(r => r.Key == SermonReactionKind.Amen)?.Count ?? 0,
            BlessedCount = reactions.FirstOrDefault(r => r.Key == SermonReactionKind.Blessed)?.Count ?? 0,
            Amened = mine.Contains(SermonReactionKind.Amen),
            Blessed = mine.Contains(SermonReactionKind.Blessed),
            Me = me,
            MyNote = me == null ? null
                : await _db.SermonNotes.Where(n => n.SermonId == sermon.Id && n.MemberId == me.Id)
                    .Select(n => n.Note).FirstOrDefaultAsync(),
            Comments = !sermon.AllowComments ? new()
                : await _db.SermonComments.Include(c => c.Member)
                    .Where(c => c.SermonId == sermon.Id)
                    .OrderByDescending(c => c.CreatedAt).ToListAsync()
        };
    }

    // A sermon a viewer may engage with: published, or any sermon for staff previewing it
    private async Task<Sermon?> EngageableSermonAsync(int id)
    {
        var sermon = await _db.Sermons.FindAsync(id);
        return sermon != null && (sermon.IsPublished || User.IsStaff()) ? sermon : null;
    }

    // Amen / Blessed: tapping again takes it back
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> React(int id, SermonReactionKind kind)
    {
        var me = await CurrentMemberAsync();
        if (me == null) return Forbid();
        if (await EngageableSermonAsync(id) == null) return NotFound();

        var existing = await _db.SermonReactions
            .FirstOrDefaultAsync(r => r.SermonId == id && r.MemberId == me.Id && r.Kind == kind);
        if (existing != null) _db.SermonReactions.Remove(existing);
        else _db.SermonReactions.Add(new SermonReaction { SermonId = id, MemberId = me.Id, Kind = kind });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* a double tap raced itself: the unique index kept one */ }

        var counts = await _db.SermonReactions.Where(r => r.SermonId == id)
            .GroupBy(r => r.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var mine = await _db.SermonReactions.Where(r => r.SermonId == id && r.MemberId == me.Id)
            .Select(r => r.Kind).ToListAsync();
        return Json(new
        {
            amen = counts.FirstOrDefault(c => c.Key == SermonReactionKind.Amen)?.Count ?? 0,
            blessed = counts.FirstOrDefault(c => c.Key == SermonReactionKind.Blessed)?.Count ?? 0,
            mine = mine.Select(k => k.ToString())
        });
    }

    // A prayer request made while watching: the same as Prayer/Submit, with the sermon named
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Pray(int id, PrayerRequestViewModel vm)
    {
        var sermon = await EngageableSermonAsync(id);
        if (sermon == null) return NotFound();
        if (string.IsNullOrWhiteSpace(vm.Request)) return BadRequest(new { error = "Please write your prayer request." });
        var me = await CurrentMemberAsync();

        var request = new PrayerRequest
        {
            Request = $"{vm.Request.Trim()}\n\n— while watching \"{sermon.Title}\"",
            Category = vm.Category,
            IsAnonymous = vm.IsAnonymous,
            IsPublic = vm.IsPublic,
            MemberId = vm.IsAnonymous ? null : me?.Id,
            AnonymousName = vm.IsAnonymous ? "Anonymous" : me?.PublicName ?? "Anonymous"
        };
        _db.PrayerRequests.Add(request);
        await _db.SaveChangesAsync();
        if (me != null)
            await _activity.LogAsync(me.Id, MemberActivityType.PrayerRequest,
                vm.IsAnonymous ? "Submitted a prayer request" : $"Prayer request ({vm.Category})",
                vm.IsAnonymous ? null : request.Id);
        return Json(new { ok = true });
    }

    // A question for the Pastor: a Community "Pastor Q&A" post, so it's answered where others can learn too
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask(int id, string? question)
    {
        var sermon = await EngageableSermonAsync(id);
        if (sermon == null) return NotFound();
        if (string.IsNullOrWhiteSpace(question)) return BadRequest(new { error = "Please write your question." });
        var me = await CurrentMemberAsync();
        if (me == null) return BadRequest(new { error = "Questions need a member profile." });

        var title = $"Question on \"{sermon.Title}\"";
        if (title.Length > 200) title = title[..199] + "…";
        var post = new ForumPost
        {
            Title = title,
            Content = $"{question.Trim()}\n\nSermon: {Request.Scheme}://{Request.Host}/Media/Watch/{sermon.Id}",
            MemberId = me.Id,
            Category = ForumCategory.Questions,
            IsPastorQuestion = true
        };
        _db.ForumPosts.Add(post);
        await _db.SaveChangesAsync();
        await _activity.LogAsync(me.Id, MemberActivityType.ForumPost, post.Title, post.Id);
        await _chatHub.Clients.All.SendAsync("CommunityActivity", new { memberId = me.Id });

        // Admins and Pastors are told; they see the real name
        foreach (var staffId in await StaffMemberIdsAsync())
            if (staffId != me.Id)
                await _notifications.CreateAsync(staffId, "New question on a sermon",
                    $"{me.StaffName} asked about \"{sermon.Title}\"", $"/Community/Post/{post.Id}");
        return Json(new { ok = true, url = $"/Community/Post/{post.Id}" });
    }

    private Task<List<int>> StaffMemberIdsAsync() => _db.Members
        .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
        .Where(x => _db.UserRoles.Any(ur => ur.UserId == x.u.Id &&
            _db.Roles.Any(r => r.Id == ur.RoleId && (r.Name == "Admin" || r.Name == "Pastor"))))
        .Select(x => x.m.Id).ToListAsync();

    // Returns the new comment's HTML for the page to put at the top of the list
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Comment(int id, string? content)
    {
        var sermon = await EngageableSermonAsync(id);
        if (sermon == null) return NotFound();
        if (!sermon.AllowComments) return BadRequest(new { error = "Comments are turned off for this sermon." });
        content = content?.Trim();
        if (string.IsNullOrEmpty(content)) return BadRequest(new { error = "Please write a comment." });
        if (content.Length > SermonComment.MaxLength)
            return BadRequest(new { error = $"Comments can be up to {SermonComment.MaxLength} characters." });
        var me = await CurrentMemberAsync();
        if (me == null) return BadRequest(new { error = "Comments need a member profile." });

        var comment = new SermonComment
        {
            SermonId = id,
            MemberId = me.Id,
            Content = content,
            IsStaff = User.IsStaff(),
            StaffRole = User.IsInRole("Pastor") ? "Pastor" : User.IsInRole("Admin") ? "Admin" : null
        };
        _db.SermonComments.Add(comment);
        await _db.SaveChangesAsync();
        comment.Member = me;
        return PartialView("_SermonComment", new SermonCommentViewModel { Comment = comment, CanDelete = true });
    }

    // Admins and Pastors can remove any comment; members only their own
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComment(int commentId)
    {
        var comment = await _db.SermonComments.FindAsync(commentId);
        if (comment == null) return NotFound();
        if (!User.IsStaff() && (await CurrentMemberAsync())?.Id != comment.MemberId) return Forbid();
        _db.SermonComments.Remove(comment);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // The member's private note on this sermon; an empty note removes it
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNote(int id, string? note)
    {
        var me = await CurrentMemberAsync();
        if (me == null) return BadRequest(new { error = "Notes need a member profile." });
        if (await EngageableSermonAsync(id) == null) return NotFound();

        var existing = await _db.SermonNotes.FirstOrDefaultAsync(n => n.SermonId == id && n.MemberId == me.Id);
        if (string.IsNullOrWhiteSpace(note))
        {
            if (existing != null) _db.SermonNotes.Remove(existing);
        }
        else if (existing == null)
            _db.SermonNotes.Add(new SermonNote { SermonId = id, MemberId = me.Id, Note = note });
        else
        {
            existing.Note = note;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
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