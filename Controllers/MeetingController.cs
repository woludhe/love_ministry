using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.ViewModels;
using ChurchMinistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    [Authorize]
    public class MeetingController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notifications;
        private readonly IEmailService _email;
        private readonly IMeetingAccessService _access;
        private readonly IMeetingSeriesService _series;

        public MeetingController(ApplicationDbContext db,
            UserManager<ApplicationUser> um,
            INotificationService notifications,
            IEmailService email,
            IMeetingAccessService access,
            IMeetingSeriesService series)
        {
            _db = db;
            _userManager = um;
            _notifications = notifications;
            _email = email;
            _access = access;
            _series = series;
        }

        private const string PrivateMeetingMessage = "This is a private meeting. Only invited people can join.";

        // Members (not the host) may start a scheduled meeting this long before its time
        private static readonly TimeSpan EarlyJoinWindow = TimeSpan.FromMinutes(15);
        // A live meeting empty for this long is treated as ended
        private static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(30);

        private async Task<Member?> GetMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        private bool IsAdminOrPastor()
            => User.IsInRole("Admin") || User.IsInRole("Pastor");

        // ── Lobby ───────────────────────────────────────────────
        public async Task<IActionResult> Index(int scheduledPage = 1)
        {
            var now = DateTime.UtcNow;

            // Meetings opened well before their time that nobody is in go back to Scheduled
            var startedEarly = await _db.MeetingRooms
                .Where(r => r.Status == RoomStatus.Live
                         && r.ScheduledAt > now + EarlyJoinWindow
                         && !r.Participants.Any(p => p.LeftAt == null)
                         && !_db.Sermons.Any(s => s.IsLive && s.MeetingRoomCode == r.RoomCode))
                .ToListAsync();
            if (startedEarly.Count > 0)
            {
                foreach (var r in startedEarly)
                {
                    r.Status = RoomStatus.Scheduled;
                    r.StartedAt = null;
                }
                await _db.SaveChangesAsync();
            }

            // Live meetings nobody has been in for a while were left without being ended
            var abandonedBefore = now - AbandonedAfter;
            var abandoned = await _db.MeetingRooms
                .Where(r => r.Status == RoomStatus.Live
                         && !r.Participants.Any(p => p.LeftAt == null)
                         && !_db.Sermons.Any(s => s.IsLive && s.MeetingRoomCode == r.RoomCode))
                .Select(r => new
                {
                    Room = r,
                    LastActivity = r.Participants.Max(p => p.LeftAt) ?? r.StartedAt ?? r.CreatedAt
                })
                .Where(x => x.LastActivity < abandonedBefore)
                .ToListAsync();
            if (abandoned.Count > 0)
            {
                foreach (var x in abandoned)
                {
                    x.Room.Status = RoomStatus.Ended;
                    x.Room.EndedAt = x.LastActivity;
                }
                await _db.SaveChangesAsync();
            }

            // Recurring series whose latest meeting ended or was missed get their next meeting
            await _series.CatchUpAsync();

            var member = await GetMemberAsync();
            var isPrivileged = IsAdminOrPastor();

            // ── Public approved meetings (visible to everyone) ───
            var liveQ = _db.MeetingRooms
                .Include(r => r.HostMember)
                .Include(r => r.Participants)
                .Where(r => r.Status == RoomStatus.Live && r.IsPublic && r.IsApproved);

            var scheduledQ = _db.MeetingRooms
                .Include(r => r.HostMember)
                .Where(r => r.Status == RoomStatus.Scheduled
                         && r.IsPublic
                         && r.ScheduledAt >= now
                         && r.IsApproved);

            var pastQ = _db.MeetingRooms
                .Include(r => r.HostMember)
                .Include(r => r.Participants)
                .Where(r => r.Status == RoomStatus.Ended && r.IsPublic && r.IsApproved);

            var scheduledPager = new PagerModel(scheduledPage, "meetings", "scheduledPage");
            var vm = new MeetingLobbyViewModel
            {
                LiveMeetings = await liveQ.OrderByDescending(r => r.StartedAt).ToListAsync(),
                ScheduledMeetings = await scheduledQ.OrderBy(r => r.ScheduledAt).ToPageAsync(scheduledPager),
                // The latest few only: the full history is in the Meetings Register (Admin / Pastor)
                PastMeetings = await pastQ.OrderByDescending(r => r.EndedAt).Take(MeetingLobbyViewModel.RecentMeetings).ToListAsync(),
                ScheduledPager = scheduledPager
            };

            // Sermons streamed through a meeting room; the live (else latest) one per room
            vm.LinkedSermons = (await _db.Sermons
                    .Where(s => s.IsOnlineMeeting && s.MeetingRoomCode != null)
                    .OrderByDescending(s => s.IsLive).ThenByDescending(s => s.SermonDate)
                    .Select(s => new { s.MeetingRoomCode, Sermon = new MeetingLinkedSermon(s.Id, s.Title, s.IsLive, s.IsPublished) })
                    .ToListAsync())
                .GroupBy(x => x.MeetingRoomCode!)
                .ToDictionary(g => g.Key, g => g.First().Sermon);

            // ── My meetings (host's own — public or private) ─────
            // Private meetings are only visible to the host and invitees
            if (member != null)
            {
                ViewBag.MyMeetings = await _db.MeetingRooms
                    .Include(r => r.HostMember)
                    .Where(r => r.HostMemberId == member.Id
                             && r.Status != RoomStatus.Ended
                             && r.IsApproved)
                    .OrderBy(r => r.ScheduledAt)
                    .ToListAsync();

                ViewBag.MyPendingMeetings = await _db.MeetingRooms
                    .Where(r => r.HostMemberId == member.Id
                             && r.ApprovalStatus == ApprovalStatus.Pending)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync();

                // Private meetings this member is invited to — directly, by email or through their group
                var email = member.Email.ToLower();
                var groupIds = await _db.MinistryGroupMembers.Where(gm => gm.MemberId == member.Id)
                    .Select(gm => gm.MinistryGroupId).ToListAsync();
                ViewBag.InvitedMeetings = await _db.MeetingRooms
                    .Include(r => r.HostMember)
                    .Where(r => !r.IsPublic
                             && r.IsApproved
                             && r.Status != RoomStatus.Ended
                             && r.HostMemberId != member.Id
                             && r.Invitations.Any(i => i.MemberId == member.Id
                                 || (i.MinistryGroupId != null && groupIds.Contains(i.MinistryGroupId.Value))
                                 || (i.Email != null && i.Email.ToLower() == email)))
                    .OrderBy(r => r.ScheduledAt)
                    .ToListAsync();
            }

            // Admins see all (including private) and pending count
            if (isPrivileged)
            {
                ViewBag.PendingApprovalCount = await _db.MeetingRooms
                    .CountAsync(r => r.ApprovalStatus == ApprovalStatus.Pending);

                // Admins also see all private approved meetings
                ViewBag.AdminPrivateMeetings = await _db.MeetingRooms
                    .Include(r => r.HostMember)
                    .Where(r => !r.IsPublic && r.IsApproved
                             && r.Status != RoomStatus.Ended)
                    .OrderBy(r => r.ScheduledAt)
                    .ToListAsync();

            }

            return View(vm);
        }

        // ── Create (schedule a meeting) ─────────────────────────
        [HttpGet]
        public IActionResult Create() => View(new CreateMeetingViewModel
        {
            ScheduledAt = DateTime.Now.AddHours(1)
        });

        [HttpPost]
        public async Task<IActionResult> Create(CreateMeetingViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var member = await GetMemberAsync();
            bool isPrivileged = IsAdminOrPastor();

            var code = GenerateRoomCode(vm.MeetingType);
            var room = new MeetingRoom
            {
                Title = vm.Title,
                Description = vm.Description,
                MeetingType = vm.MeetingType,
                RoomCode = code,
                HostUserId = _userManager.GetUserId(User),
                HostMemberId = member?.Id,
                ScheduledAt = vm.ScheduledAt?.ToUniversalTime(),
                DurationMinutes = vm.DurationMinutes,
                WaitingRoomEnabled = vm.WaitingRoomEnabled,
                ChatEnabled = vm.ChatEnabled,
                MaxParticipants = vm.MaxParticipants,
                IsPublic = vm.IsPublic,
                PassCode = vm.PassCode,
                Status = RoomStatus.Scheduled,

                // Admins/Pastors are auto-approved; members need approval
                IsApproved = isPrivileged,
                ApprovalStatus = isPrivileged
                    ? ApprovalStatus.Approved : ApprovalStatus.Pending,
                ApprovedByUserId = isPrivileged ? _userManager.GetUserId(User) : null,
                ApprovedAt = isPrivileged ? DateTime.UtcNow : null
            };

            ApplyRecurrence(room, vm);

            _db.MeetingRooms.Add(room);
            await _db.SaveChangesAsync();

            // The first meeting of a series gives the series its id
            if (room.IsRecurring)
            {
                room.SeriesId = room.Id;
                await _db.SaveChangesAsync();
            }

            if (isPrivileged)
            {
                if (!room.IsPublic)
                {
                    TempData["Success"] = $"Private meeting created (room code {code}). Now invite people or groups.";
                    return RedirectToAction("Invite", new { id = room.Id });
                }
                // Scheduling only schedules; the host starts it from the lobby
                var when = room.ScheduledAt?.ToLocalTime().ToString("ddd, dd MMM · h:mm tt");
                TempData["Success"] = when == null
                    ? $"Meeting scheduled. Room code: {code}"
                    : $"Meeting scheduled for {when}. Room code: {code}";
                return RedirectToAction("Index");
            }

            // Notify all admins that a meeting needs approval
            var adminMembers = await _db.Members
                .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
                .Where(x => _db.UserRoles.Any(ur => ur.UserId == x.u.Id &&
                    _db.Roles.Any(r => r.Id == ur.RoleId &&
                        (r.Name == "Admin" || r.Name == "Pastor"))))
                .Select(x => x.m.Id).ToListAsync();

            foreach (var adminId in adminMembers)
                await _notifications.CreateAsync(adminId,
                    "Meeting Approval Required",
                    $"{member?.FullName ?? "A member"} scheduled \"{room.Title}\" and needs approval.",
                    "/MeetingApprovals");

            TempData["Info"] = "Your meeting has been submitted and is awaiting admin approval. You will be notified once approved.";
            return RedirectToAction("Index");
        }


        // ── Edit meeting ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var room = await _db.MeetingRooms.FindAsync(id);
            if (room == null) return NotFound();

            // Only host or admin can edit
            var member = await GetMemberAsync();
            if (room.HostMemberId != member?.Id && !IsAdminOrPastor())
                return Forbid();

            var vm = new CreateMeetingViewModel
            {
                Title = room.Title,
                Description = room.Description,
                MeetingType = room.MeetingType,
                ScheduledAt = room.ScheduledAt?.ToLocalTime(),
                DurationMinutes = room.DurationMinutes,
                WaitingRoomEnabled = room.WaitingRoomEnabled,
                ChatEnabled = room.ChatEnabled,
                MaxParticipants = room.MaxParticipants,
                IsPublic = room.IsPublic,
                PassCode = room.PassCode,
                IsRecurring = room.IsRecurring,
            };
            if (room.ParseRecurrence() is { } rec)
            {
                vm.RecurringFrequency = rec.Day == null ? "Daily" : "Weekly";
                vm.RecurringDay  = rec.Day?.ToString();
                vm.RecurringTime = rec.Time.ToString(@"hh\:mm");
            }
            ViewBag.RoomId = id;
            ViewBag.RoomCode = room.RoomCode;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreateMeetingViewModel vm)
        {
            var room = await _db.MeetingRooms.FindAsync(id);
            if (room == null) return NotFound();

            var member = await GetMemberAsync();
            if (room.HostMemberId != member?.Id && !IsAdminOrPastor())
                return Forbid();

            if (!ModelState.IsValid) { ViewBag.RoomId = id; return View(vm); }

            room.Title = vm.Title;
            room.Description = vm.Description;
            room.MeetingType = vm.MeetingType;
            room.ScheduledAt = vm.ScheduledAt?.ToUniversalTime();
            room.DurationMinutes = vm.DurationMinutes;
            room.WaitingRoomEnabled = vm.WaitingRoomEnabled;
            room.ChatEnabled = vm.ChatEnabled;
            room.MaxParticipants = vm.MaxParticipants;
            room.IsPublic = vm.IsPublic;
            room.PassCode = vm.PassCode;
            // Unticking Recurring stops the series after this meeting; changes here
            // carry forward, since the next week's meeting is copied from this one
            ApplyRecurrence(room, vm);
            if (room.IsRecurring) room.SeriesId ??= room.Id;
            room.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Meeting updated successfully.";
            return RedirectToAction("Index");
        }

        // ── Invite — show invite form ─────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Invite(int id)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .Include(r => r.Invitations).ThenInclude(i => i.Member)
                .Include(r => r.Invitations).ThenInclude(i => i.MinistryGroup)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (room == null) return NotFound();

            var member = await GetMemberAsync();
            if (room.HostMemberId != member?.Id && !IsAdminOrPastor())
                return Forbid();

            // Load members for the dropdown
            ViewBag.AllMembers = await _db.Members
                .Where(m => m.Status == MemberStatus.Active)
                .OrderBy(m => m.FirstName)
                .ToListAsync();

            // Groups with their active member counts
            ViewBag.Groups = await _db.MinistryGroups
                .Where(g => g.IsActive)
                .OrderBy(g => g.Name)
                .Select(g => new MeetingInviteGroupOption(g.Id, g.Name,
                    g.Members.Count(m => m.Status == MemberStatus.Active)))
                .ToListAsync();

            return View(room);
        }

        // ── Send email invitations ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendInvites(int id, List<int> memberIds, List<int> groupIds, string? emailList)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .Include(r => r.Invitations)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (room == null) return NotFound();

            var member = await GetMemberAsync();
            if (room.HostMemberId != member?.Id && !IsAdminOrPastor())
                return Forbid();

            var sentCount = 0;
            var joinUrl = Url.Action("Room", "Meeting", new { code = room.RoomCode }, Request.Scheme)!;

            // ── Invite existing members by ID ──────────────────────
            foreach (var memberId in (memberIds ?? new List<int>()).Distinct())
            {
                if (room.Invitations.Any(i => i.MemberId == memberId)) continue;

                var invitee = await _db.Members.FirstOrDefaultAsync(m => m.Id == memberId);
                if (invitee == null) continue;

                var inv = new MeetingInvitation
                {
                    MeetingRoomId = room.Id,
                    MemberId = memberId,
                    InvitedAt = DateTime.UtcNow,
                    Token = Guid.NewGuid().ToString("N")
                };
                room.Invitations.Add(inv);

                await NotifyInviteeAsync(room, invitee, joinUrl, inv.Token);
                sentCount++;
            }

            // ── Invite ministry groups ─────────────────────────────
            // Access follows the group's membership at join time; the people
            // in it now are told about the meeting
            foreach (var groupId in (groupIds ?? new List<int>()).Distinct())
            {
                if (room.Invitations.Any(i => i.MinistryGroupId == groupId)) continue;

                var group = await _db.MinistryGroups
                    .Include(g => g.Members.Where(m => m.Status == MemberStatus.Active))
                    .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive);
                if (group == null) continue;

                var inv = new MeetingInvitation
                {
                    MeetingRoomId = room.Id,
                    MinistryGroupId = groupId,
                    InvitedAt = DateTime.UtcNow,
                    Token = Guid.NewGuid().ToString("N")
                };
                room.Invitations.Add(inv);

                foreach (var groupMember in group.Members)
                {
                    // Already told through an individual invite, and the host needs no invite
                    if (groupMember.Id == room.HostMemberId
                        || room.Invitations.Any(i => i.MemberId == groupMember.Id)) continue;
                    await NotifyInviteeAsync(room, groupMember, joinUrl, inv.Token);
                }
                sentCount++;
            }

            // ── Invite by raw email addresses ──────────────────────
            if (!string.IsNullOrWhiteSpace(emailList))
            {
                var emails = emailList.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(e => e.Trim())
                                      .Where(e => e.Contains('@'))
                                      .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var email in emails)
                {
                    if (room.Invitations.Any(i => string.Equals(i.Email, email, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Saved so the person gets in once they sign in with this email
                    var inv = new MeetingInvitation
                    {
                        MeetingRoomId = room.Id,
                        Email = email,
                        InvitedAt = DateTime.UtcNow,
                        Token = Guid.NewGuid().ToString("N")
                    };
                    room.Invitations.Add(inv);

                    await _email.SendEmailAsync(
                        email,
                        $"You're invited: {room.Title}",
                        BuildInviteEmail(room, email, joinUrl, inv.Token));

                    sentCount++;
                }
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = sentCount > 0
                ? $"{sentCount} invitation{(sentCount > 1 ? "s" : "")} sent successfully."
                : "No new invitations sent (may already be invited).";

            return RedirectToAction("Invite", new { id });
        }

        // ── Remove an invitation ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveInvite(int id, int invitationId)
        {
            var room = await _db.MeetingRooms.FindAsync(id);
            if (room == null) return NotFound();

            var member = await GetMemberAsync();
            if (room.HostMemberId != member?.Id && !IsAdminOrPastor())
                return Forbid();

            var inv = await _db.MeetingInvitations
                .FirstOrDefaultAsync(i => i.Id == invitationId && i.MeetingRoomId == id);
            if (inv != null)
            {
                _db.MeetingInvitations.Remove(inv);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Invitation removed.";
            }
            return RedirectToAction("Invite", new { id });
        }

        // ── Cancel — permanently deletes the meeting (Admin/Pastor only) ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Cancel(int id)
        {
            var room = await _db.MeetingRooms.FindAsync(id);
            if (room == null) return NotFound();

            // Cancelling a recurring meeting stops its series, otherwise the
            // series catch-up would schedule a replacement from an earlier meeting
            if (room.SeriesId != null)
            {
                var series = await _db.MeetingRooms
                    .Where(r => r.SeriesId == room.SeriesId && r.Id != room.Id && r.IsRecurring)
                    .ToListAsync();
                foreach (var r in series) r.IsRecurring = false;
            }

            // Participants, chat messages and invitations are removed with it (cascade)
            _db.MeetingRooms.Remove(room);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Meeting \"{room.Title}\" was cancelled and deleted.";
            return RedirectToAction("Index");
        }

        // In-app notification + email for one invited member
        private async Task NotifyInviteeAsync(MeetingRoom room, Member invitee, string joinUrl, string token)
        {
            var when = room.ScheduledAt.HasValue
                ? $" scheduled for {room.ScheduledAt.Value.ToLocalTime():ddd, dd MMM · h:mm tt}" : "";
            await _notifications.CreateAsync(invitee.Id,
                $"Meeting Invitation: {room.Title}",
                $"You have been invited to join {room.Title}{when}. Room code: {room.RoomCode}",
                $"/meeting/room/{room.RoomCode}");

            await _email.SendEmailAsync(
                invitee.Email,
                $"You're invited: {room.Title}",
                BuildInviteEmail(room, invitee.FullName, joinUrl, token));
        }

        // ── Email template ─────────────────────────────────────────
        private static string BuildInviteEmail(MeetingRoom room, string name, string joinUrl, string? token)
        {
            var scheduled = room.ScheduledAt?.ToLocalTime().ToString("dddd, dd MMMM yyyy 'at' h:mm tt") ?? "Soon";
            var description = string.IsNullOrEmpty(room.Description) ? ""
                : $"<p style=\"color:#4b5563\">{System.Net.WebUtility.HtmlEncode(room.Description)}</p>";
            return $"""
                <div style="font-family:sans-serif;max-width:520px;margin:0 auto">
                  <div style="background:#312e81;padding:1.5rem;border-radius:12px 12px 0 0;text-align:center">
                    <h1 style="color:white;font-size:1.4rem;margin:0">✝ You're Invited</h1>
                  </div>
                  <div style="background:#f9fafb;padding:1.5rem;border-radius:0 0 12px 12px;border:1px solid #e5e7eb">
                    <p>Hi <strong>{System.Net.WebUtility.HtmlEncode(name)}</strong>,</p>
                    <p>You have been invited to join an online meeting:</p>
                    <div style="background:white;border:1px solid #e5e7eb;border-radius:8px;padding:1rem;margin:1rem 0">
                      <h2 style="margin:0 0 .5rem;color:#1e1b4b">{System.Net.WebUtility.HtmlEncode(room.Title)}</h2>
                      <p style="margin:0;color:#6b7280">📅 {scheduled}</p>
                      <p style="margin:.3rem 0 0;color:#6b7280">🔑 Room code: <strong>{room.RoomCode}</strong></p>
                    </div>
                    {description}
                    <div style="text-align:center;margin:1.5rem 0">
                      <a href="{joinUrl}" style="background:#6366f1;color:white;padding:.75rem 2rem;
                         border-radius:8px;text-decoration:none;font-weight:700;display:inline-block">
                        Join Meeting
                      </a>
                    </div>
                    <p style="font-size:.8rem;color:#9ca3af;text-align:center">
                      Or use room code <strong>{room.RoomCode}</strong> at the meeting page.
                    </p>
                  </div>
                </div>
                """;
        }

        // ── Instant meeting — Admin/Pastor only ─────────────────
        [HttpPost]
        [Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> StartInstant()
        {
            var member = await GetMemberAsync();
            var code = GenerateRoomCode(MeetingType.Prayer);

            var room = new MeetingRoom
            {
                Title = "Instant Meeting",
                MeetingType = MeetingType.Prayer,
                RoomCode = code,
                HostUserId = _userManager.GetUserId(User),
                HostMemberId = member?.Id,
                Status = RoomStatus.Live,
                StartedAt = DateTime.UtcNow,
                WaitingRoomEnabled = false,
                ChatEnabled = true,
                IsPublic = true,
                IsApproved = true,
                ApprovalStatus = ApprovalStatus.Approved,
                ApprovedByUserId = _userManager.GetUserId(User),
                ApprovedAt = DateTime.UtcNow
            };

            _db.MeetingRooms.Add(room);
            await _db.SaveChangesAsync();
            return RedirectToAction("Room", new { code });
        }

        // ── Join ────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Join(string? code = null)
            => View(new JoinMeetingViewModel { RoomCode = code ?? "" });

        [HttpPost]
        public async Task<IActionResult> Join(JoinMeetingViewModel vm)
        {
            var room = await _db.MeetingRooms
                .FirstOrDefaultAsync(r => r.RoomCode == vm.RoomCode.ToUpper().Trim());

            if (room == null)
            {
                ModelState.AddModelError("RoomCode", "Room not found. Please check the code.");
                return View(vm);
            }
            if (!room.IsApproved)
            {
                ModelState.AddModelError("", "This meeting is pending admin approval and cannot be joined yet.");
                return View(vm);
            }
            if (room.Status == RoomStatus.Ended)
            {
                ModelState.AddModelError("", "This meeting has ended.");
                return View(vm);
            }
            if (!await _access.CanAccessAsync(room, User))
            {
                ModelState.AddModelError("", PrivateMeetingMessage);
                return View(vm);
            }
            if (!string.IsNullOrEmpty(room.PassCode) && room.PassCode != vm.PassCode)
            {
                ModelState.AddModelError("PassCode", "Incorrect passcode.");
                return View(vm);
            }

            return RedirectToAction("Room", new { code = room.RoomCode });
        }

        // ── Room ────────────────────────────────────────────────
        [HttpGet("meeting/room/{code}")]
        public async Task<IActionResult> Room(string code)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .Include(r => r.Messages.OrderByDescending(m => m.SentAt).Take(50))
                .Include(r => r.Participants.Where(p => p.LeftAt == null))
                .FirstOrDefaultAsync(r => r.RoomCode == code);

            if (room == null) return NotFound();

            // Block access if not approved (unless host or admin)
            var member = await GetMemberAsync();
            var isHost = room.HostMemberId == member?.Id
                      || room.HostUserId == _userManager.GetUserId(User);

            if (!room.IsApproved && !isHost && !IsAdminOrPastor())
            {
                TempData["Error"] = "This meeting is pending approval and is not yet accessible.";
                return RedirectToAction("Index");
            }

            if (!await _access.CanAccessAsync(room, User))
            {
                TempData["Error"] = PrivateMeetingMessage;
                return RedirectToAction("Index");
            }

            // A scheduled meeting goes live when the host (or an Admin/Pastor) starts it,
            // or when anyone arrives near its start time, so it still happens if the host is late
            if (room.Status == RoomStatus.Scheduled)
            {
                var canStart = isHost || IsAdminOrPastor()
                    || room.ScheduledAt == null
                    || room.ScheduledAt <= DateTime.UtcNow + EarlyJoinWindow;
                if (!canStart)
                {
                    TempData["Info"] = "This meeting hasn't started yet. It's scheduled for " +
                        $"{room.ScheduledAt!.Value.ToLocalTime():ddd, dd MMM · h:mm tt}.";
                    return RedirectToAction("Index");
                }

                room.Status = RoomStatus.Live;
                room.StartedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            // Mark the member's own invitation accepted the first time they come in
            if (!room.IsPublic && member != null)
            {
                var invitation = await _db.MeetingInvitations
                    .FirstOrDefaultAsync(i => i.MeetingRoomId == room.Id && i.MemberId == member.Id
                                           && i.AcceptedAt == null);
                if (invitation != null)
                {
                    invitation.AcceptedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            var user = await _userManager.GetUserAsync(User);
            // The admin login has no member profile: it shows as "Admin" with the site logo, not its email
            var isAdminLogin = member == null && User.IsInRole("Admin");
            var displayName = member?.FullName ?? (isAdminLogin ? MeetingAvatar.AdminName : user?.UserName) ?? "Guest";

            // Sermon streamed through this room (for verse overlays + attendance tracking)
            var linkedSermon = await _db.Sermons
                .Where(s => s.IsOnlineMeeting && s.MeetingRoomCode == code && s.IsPublished)
                .OrderByDescending(s => s.IsLive)
                .ThenByDescending(s => s.SermonDate)
                .FirstOrDefaultAsync();
            ViewBag.LiveSermonId = linkedSermon?.Id;
            ViewBag.SermonIsLive = linkedSermon?.IsLive == true;
            ViewBag.TrackAttendance = linkedSermon?.TrackAttendance == true;
            ViewBag.SermonPoster = linkedSermon?.ThumbnailUrl;   // shown in audio only mode

            return View(new MeetingRoomViewModel
            {
                Room = room,
                DisplayName = displayName,
                DisplayPhoto = MeetingAvatar.For(member?.Id, member?.ProfilePhoto, isHost || IsAdminOrPastor()),
                IsHost = isHost,
                CurrentMemberId = member?.Id
            });
        }

        // ── Recording: a 10 s piece of the meeting's audio from the host's page ──
        // A new part starts only while the room and its sermon are live; the last pieces of a
        // part still arrive after the sermon stops, until it's finalized (Services/RecordingService)
        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> RecordingChunk(string code, string part, int seq, string format,
            IFormFile? chunk, [FromServices] IRecordingService recordings)
        {
            if (chunk == null || chunk.Length == 0) return BadRequest();
            var room = await _db.MeetingRooms.FirstOrDefaultAsync(r => r.RoomCode == code);
            if (room == null) return NotFound();

            var member = await GetMemberAsync();
            var isHost = (member != null && room.HostMemberId == member.Id)
                      || room.HostUserId == _userManager.GetUserId(User);
            if (!isHost && !IsAdminOrPastor()) return Forbid();

            var sermon = await _db.Sermons
                .Where(s => s.IsOnlineMeeting && s.MeetingRoomCode == code)
                .OrderByDescending(s => s.IsLive).ThenByDescending(s => s.Id)
                .Select(s => new { s.Id, s.IsLive }).FirstOrDefaultAsync();
            if (sermon == null) return Conflict();
            var recording = room.Status == RoomStatus.Live && sermon.IsLive;
            if (!recording && !recordings.HasPart(sermon.Id, part, format)) return Conflict();

            await using var data = chunk.OpenReadStream();
            return await recordings.SaveChunkAsync(sermon.Id, part, seq, format, data)
                ? Ok() : BadRequest();
        }

        // ── Participants API ────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Participants(string code)
        {
            var room = await _db.MeetingRooms.FirstOrDefaultAsync(r => r.RoomCode == code);
            if (room == null) return NotFound();
            if (!await _access.CanAccessAsync(room, User)) return Forbid();

            var list = await _db.MeetingParticipants
                .Include(p => p.Member)
                .Where(p => p.MeetingRoomId == room.Id && p.LeftAt == null)
                .Select(p => new
                {
                    p.Id,
                    p.DisplayName,
                    p.IsHost,
                    p.IsCoHost,
                    p.IsMuted,
                    p.IsVideoOff,
                    p.IsHandRaised,
                    p.IsInWaitingRoom,
                    p.ConnectionId,
                    photo = p.Member != null
                        ? p.Member.ProfilePhoto ?? MeetingAvatar.Default
                        : p.IsHost ? MeetingAvatar.SiteLogo : MeetingAvatar.Default
                }).ToListAsync();

            return Json(list);
        }

        // ── Helpers ────────────────────────────────────────────
        private string GenerateRoomCode(MeetingType type) => _series.GenerateRoomCode(type);

        // Applies the form's recurrence to the room. A recurring meeting's date is moved to
        // the first occurrence on or after the chosen date, so date and schedule agree.
        private static void ApplyRecurrence(MeetingRoom room, CreateMeetingViewModel vm)
        {
            room.IsRecurring = vm.IsRecurring;
            if (!vm.IsRecurring) { room.RecurringPattern = null; return; }

            var time = string.IsNullOrWhiteSpace(vm.RecurringTime)
                ? (vm.ScheduledAt ?? DateTime.Now).ToString("HH:mm") : vm.RecurringTime;
            if (vm.RecurringFrequency == "Daily")
                room.RecurringPattern = $"daily:{time}";
            else
            {
                var day = string.IsNullOrWhiteSpace(vm.RecurringDay)
                    ? (vm.ScheduledAt ?? DateTime.Now).DayOfWeek.ToString() : vm.RecurringDay;
                room.RecurringPattern = $"weekly:{day}:{time}";
            }

            var from = vm.ScheduledAt?.ToUniversalTime() ?? DateTime.UtcNow;
            var first = room.NextOccurrenceUtc(from.AddTicks(-1));
            if (first == null) { room.IsRecurring = false; room.RecurringPattern = null; return; }
            room.ScheduledAt = first;
        }
    }
}
