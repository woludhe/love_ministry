using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using ChurchMinistry.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace ChurchMinistry.Controllers
{
    // One-on-One meetings with Apostle Enock Makoko: members ask from their dashboard,
    // Admins and Pastors schedule them (online room or in person) or decline.
    [Authorize]
    public class OneOnOneController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notifications;
        private readonly IEmailService _email;
        private readonly IMailTemplateService _mailTemplates;
        private readonly IMeetingSeriesService _series;
        private readonly ILogger<OneOnOneController> _log;

        public OneOnOneController(ApplicationDbContext db, UserManager<ApplicationUser> um,
            INotificationService notifications, IEmailService email, IMailTemplateService mailTemplates,
            IMeetingSeriesService series, ILogger<OneOnOneController> log)
        {
            _db = db; _userManager = um; _notifications = notifications; _email = email;
            _mailTemplates = mailTemplates; _series = series; _log = log;
        }

        private async Task<Member?> CurrentMemberAsync()
        {
            var userId = _userManager.GetUserId(User);
            return userId == null ? null : await _db.Members.FirstOrDefaultAsync(m => m.UserId == userId);
        }

        // No #one-on-one here: that opens the pop-up (used by notification links); the alert says what happened
        private IActionResult BackToDashboard() => RedirectToAction("Dashboard", "Members");

        // ── Member: ask for a One-on-One ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask(OneOnOneRequestViewModel vm)
        {
            var member = await CurrentMemberAsync();
            if (member == null) return Forbid();

            var now = DateTime.UtcNow;
            if (await _db.OneOnOneRequests.Where(r => r.MemberId == member.Id).AnyAsync(OneOnOneRequest.IsOpen(now)))
            {
                TempData["Error"] = "You already have a One-on-One request open. You can ask again once it has taken place, or after you cancel it.";
                return BackToDashboard();
            }

            var times = new[] { vm.Time1, vm.Time2, vm.Time3 }
                .Where(t => t.HasValue).Select(t => t!.Value.ToUniversalTime())
                .Where(t => t > now).Distinct().OrderBy(t => t).ToList();
            if (!ModelState.IsValid || times.Count == 0)
            {
                TempData["Error"] = !ModelState.IsValid
                    ? string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage))
                    : "Pick at least one time in the future that suits you.";
                TempData["OneOnOneTopic"] = vm.Topic;
                return BackToDashboard();
            }

            var request = new OneOnOneRequest
            {
                MemberId = member.Id,
                Topic = vm.Topic.Trim(),
                PreferredTimes = string.Join("|", times.Select(t => t.ToString("o"))),
                PreferredFormat = vm.PreferredFormat,
            };
            _db.OneOnOneRequests.Add(request);
            await _db.SaveChangesAsync();

            // Staff see the real name (they also see the alias)
            foreach (var staffId in await StaffMemberIdsAsync())
                await _notifications.CreateAsync(staffId, "One-on-One Request",
                    $"{member.StaffName} would like a One-on-One with {OneOnOneRequest.ApostleName}.", "/OneOnOne");

            TempData["Success"] = "Your request has been sent. You'll be notified when a time is set.";
            return BackToDashboard();
        }

        // ── Member: cancel their own request ─────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var member = await CurrentMemberAsync();
            var request = await _db.OneOnOneRequests.Include(r => r.MeetingRoom)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (request == null || member == null || request.MemberId != member.Id) return NotFound();
            if (request.Status is OneOnOneStatus.Declined or OneOnOneStatus.Cancelled) return BackToDashboard();

            var wasScheduled = request.Status == OneOnOneStatus.Scheduled;
            request.Status = OneOnOneStatus.Cancelled;
            RemoveRoom(request);
            await _db.SaveChangesAsync();

            // Whoever set the time is told; a pending request just disappears from the queue
            if (wasScheduled && request.HandledByUserId != null)
            {
                var staffMemberId = await _db.Members.Where(m => m.UserId == request.HandledByUserId)
                    .Select(m => (int?)m.Id).FirstOrDefaultAsync();
                if (staffMemberId != null)
                    await _notifications.CreateAsync(staffMemberId.Value, "One-on-One Cancelled",
                        $"{member.StaffName} cancelled their One-on-One on {Local(request.ScheduledAt)}.", "/OneOnOne");
            }

            TempData["Success"] = "Your One-on-One request was cancelled.";
            return BackToDashboard();
        }

        // ── Staff: requests list ─────────────────────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Index(int pendingPage = 1, int page = 1)
        {
            var pendingPager = new PagerModel(pendingPage, "requests", "pendingPage");
            ViewBag.Pending = await _db.OneOnOneRequests.AsNoTracking().Include(r => r.Member)
                .Where(r => r.Status == OneOnOneStatus.Pending)
                .OrderBy(r => r.CreatedAt).ToPageAsync(pendingPager);

            var pager = new PagerModel(page, "requests");
            ViewBag.Others = await _db.OneOnOneRequests.AsNoTracking()
                .Include(r => r.Member).Include(r => r.MeetingRoom)
                .Where(r => r.Status != OneOnOneStatus.Pending)
                // Upcoming meetings first (soonest first), then the rest newest first
                .OrderByDescending(r => r.Status == OneOnOneStatus.Scheduled && r.ScheduledAt > DateTime.UtcNow)
                .ThenBy(r => r.Status == OneOnOneStatus.Scheduled && r.ScheduledAt > DateTime.UtcNow ? r.ScheduledAt : null)
                .ThenByDescending(r => r.HandledAt ?? r.CreatedAt)
                .ToPageAsync(pager);

            ViewBag.PendingPager = pendingPager;
            ViewBag.Pager = pager;
            return View();
        }

        // ── Staff: schedule or reschedule ────────────────────────
        [HttpGet, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Schedule(int id)
        {
            var request = await LoadAsync(id);
            if (request == null) return NotFound();

            var firstPreferred = request.PreferredTimesUtc.FirstOrDefault(t => t > DateTime.UtcNow);
            var vm = new OneOnOneScheduleViewModel
            {
                Id = id,
                Request = request,
                ScheduledAt = (request.ScheduledAt ?? (firstPreferred == default ? (DateTime?)null : firstPreferred))?.ToLocalTime(),
                DurationMinutes = request.DurationMinutes ?? 30,
                Format = request.Format ?? (request.PreferredFormat == OneOnOneFormat.InPerson ? OneOnOneFormat.InPerson : OneOnOneFormat.Online),
                Privacy = request.Privacy,
                Location = request.Location,
                OtherAttendees = request.OtherAttendees,
                StaffNote = request.StaffNote,
            };
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Schedule(OneOnOneScheduleViewModel vm)
        {
            var request = await LoadAsync(vm.Id);
            if (request == null) return NotFound();
            if (request.Status is OneOnOneStatus.Declined or OneOnOneStatus.Cancelled)
            {
                TempData["Error"] = $"This request was {request.Status.ToString().ToLower()} and can't be scheduled.";
                return RedirectToAction("Index");
            }

            if (vm.Format == OneOnOneFormat.Either) vm.Format = OneOnOneFormat.Online;
            if (vm.Format == OneOnOneFormat.InPerson && string.IsNullOrWhiteSpace(vm.Location))
                ModelState.AddModelError(nameof(vm.Location), "Enter where you will meet.");
            if (vm.ScheduledAt.HasValue && vm.ScheduledAt.Value.ToUniversalTime() <= DateTime.UtcNow)
                ModelState.AddModelError(nameof(vm.ScheduledAt), "Pick a time in the future.");
            if (!ModelState.IsValid)
            {
                vm.Request = request;
                return View(vm);
            }

            var rescheduled = request.Status == OneOnOneStatus.Scheduled;
            request.Status = OneOnOneStatus.Scheduled;
            request.ScheduledAt = vm.ScheduledAt!.Value.ToUniversalTime();
            request.DurationMinutes = vm.DurationMinutes;
            request.Format = vm.Format;
            request.Privacy = vm.Privacy;
            request.Location = vm.Format == OneOnOneFormat.InPerson ? vm.Location?.Trim() : null;
            request.OtherAttendees = vm.Format == OneOnOneFormat.InPerson && vm.Privacy == OneOnOnePrivacy.Private
                ? vm.OtherAttendees?.Trim() : null;
            request.StaffNote = string.IsNullOrWhiteSpace(vm.StaffNote) ? null : vm.StaffNote.Trim();
            request.HandledByUserId = _userManager.GetUserId(User);
            request.HandledAt = DateTime.UtcNow;

            if (vm.Format == OneOnOneFormat.Online) await EnsureRoomAsync(request);
            else RemoveRoom(request);
            await _db.SaveChangesAsync();

            await NotifyMemberScheduledAsync(request, rescheduled);

            // A private online meeting: go and invite the others
            if (request.MeetingRoom != null && request.Privacy == OneOnOnePrivacy.Private)
            {
                TempData["Success"] = $"Scheduled for {Local(request.ScheduledAt)} and {request.Member.FirstName} was told. Now invite anyone else who should join.";
                return RedirectToAction("Invite", "Meeting", new { id = request.MeetingRoom.Id });
            }
            TempData["Success"] = $"Scheduled for {Local(request.ScheduledAt)}. {request.Member.FirstName} has been notified.";
            return RedirectToAction("Index");
        }

        // ── Staff: decline ───────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Decline(int id, string? note)
        {
            var request = await LoadAsync(id);
            if (request == null) return NotFound();
            if (request.Status is OneOnOneStatus.Declined or OneOnOneStatus.Cancelled) return RedirectToAction("Index");

            request.Status = OneOnOneStatus.Declined;
            request.StaffNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 1000)];
            request.HandledByUserId = _userManager.GetUserId(User);
            request.HandledAt = DateTime.UtcNow;
            RemoveRoom(request);
            await _db.SaveChangesAsync();

            await _notifications.CreateAsync(request.MemberId, "One-on-One Request",
                $"Your One-on-One request with {OneOnOneRequest.ApostleName} could not be arranged this time."
                + (request.StaffNote == null ? "" : $" {request.StaffNote}"), "/Members/Dashboard#one-on-one");

            TempData["Success"] = $"Request from {request.Member.FirstName} declined.";
            return RedirectToAction("Index");
        }

        // ── Helpers ──────────────────────────────────────────────
        private Task<OneOnOneRequest?> LoadAsync(int id) => _db.OneOnOneRequests
            .Include(r => r.Member).Include(r => r.MeetingRoom)
            .FirstOrDefaultAsync(r => r.Id == id);

        private static string Local(DateTime? utc) =>
            utc?.ToLocalTime().ToString("ddd, dd MMM · h:mm tt") ?? "";

        // Members who are Admins or Pastors (same lookup as meeting approvals)
        private Task<List<int>> StaffMemberIdsAsync() => _db.Members
            .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .Where(x => _db.UserRoles.Any(ur => ur.UserId == x.u.Id &&
                _db.Roles.Any(r => r.Id == ur.RoleId && (r.Name == "Admin" || r.Name == "Pastor"))))
            .Select(x => x.m.Id).ToListAsync();

        // Private room for an online One-on-One; only the member is invited (staff add others for Private)
        private async Task EnsureRoomAsync(OneOnOneRequest request)
        {
            var room = request.MeetingRoom;
            if (room == null)
            {
                var userId = _userManager.GetUserId(User);
                var host = await CurrentMemberAsync();
                room = new MeetingRoom
                {
                    MeetingType = MeetingType.OneOnOne,
                    RoomCode = _series.GenerateRoomCode(MeetingType.OneOnOne),
                    HostUserId = userId,
                    HostMemberId = host?.Id,
                    Status = RoomStatus.Scheduled,
                    WaitingRoomEnabled = false,
                    ChatEnabled = true,
                    IsPublic = false,
                    IsApproved = true,
                    ApprovalStatus = ApprovalStatus.Approved,
                    ApprovedByUserId = userId,
                    ApprovedAt = DateTime.UtcNow,
                };
                room.Invitations.Add(new MeetingInvitation { MemberId = request.MemberId });
                _db.MeetingRooms.Add(room);
                request.MeetingRoom = room;
            }
            room.Title = request.MeetingTitle;
            room.Description = $"With {request.Member.FullName}";
            room.ScheduledAt = request.ScheduledAt;
            room.DurationMinutes = request.DurationMinutes;
            room.MaxParticipants = request.Privacy == OneOnOnePrivacy.Private ? 10 : 5;
            room.UpdatedAt = DateTime.UtcNow;
        }

        // Online → in person, declined or cancelled: the room goes (its invitations and chat with it)
        private void RemoveRoom(OneOnOneRequest request)
        {
            if (request.MeetingRoom == null) return;
            _db.MeetingRooms.Remove(request.MeetingRoom);
            request.MeetingRoom = null;
            request.MeetingRoomId = null;
        }

        private async Task NotifyMemberScheduledAsync(OneOnOneRequest request, bool rescheduled)
        {
            var when = Local(request.ScheduledAt);
            var where = request.MeetingRoom != null
                ? $"Online, room code {request.MeetingRoom.RoomCode}"
                : $"In person at {request.Location}";
            var title = rescheduled ? "One-on-One Updated" : "One-on-One Scheduled";
            await _notifications.CreateAsync(request.MemberId, title,
                $"Your {request.MeetingTitle.Replace($" with {OneOnOneRequest.ApostleName}", "")} with {OneOnOneRequest.ApostleName} is on {when}. {where}.",
                "/Members/Dashboard#one-on-one");

            var link = request.MeetingRoom != null
                ? Url.Action("Room", "Meeting", new { code = request.MeetingRoom.RoomCode }, Request.Scheme)
                : Url.Action("Dashboard", "Members", null, Request.Scheme);
            var enc = (string? s) => WebUtility.HtmlEncode(s ?? "");

            try
            {
                var (subject, body) = await _mailTemplates.RenderAsync("OneOnOneScheduled", new Dictionary<string, string>
                {
                    ["FIRSTNAME"] = enc(request.Member.FirstName),
                    ["MEETINGTITLE"] = enc(request.MeetingTitle),
                    ["WHEN"] = enc(when),
                    ["WHERE"] = enc(where),
                    ["DURATION"] = request.DurationMinutes.ToString(),
                    ["ATTENDEESROW"] = request.OtherAttendees == null ? ""
                        : $"<p style=\"margin:.4rem 0 0;color:#6b7280\">👥 Also attending: {enc(request.OtherAttendees)}</p>",
                    ["STAFFNOTEROW"] = request.StaffNote == null ? "" : $"<p style=\"color:#4b5563\">{enc(request.StaffNote)}</p>",
                    ["INTRO"] = rescheduled ? "Your meeting has been updated:" : "Your meeting has been arranged:",
                    ["ACTIONLINK"] = link ?? "",
                    ["ACTIONLABEL"] = request.MeetingRoom != null ? "Join Meeting" : "Open my dashboard"
                });
                await _email.SendEmailAsync(request.Member.Email, subject, body);
            }
            catch (Exception ex) { _log.LogWarning(ex, "One-on-One email to member {MemberId} failed", request.MemberId); }
        }
    }
}
