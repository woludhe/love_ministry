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

        public MeetingController(ApplicationDbContext db,
            UserManager<ApplicationUser> um,
            INotificationService notifications,
            IEmailService email)
        {
            _db = db;
            _userManager = um;
            _notifications = notifications;
            _email = email;
        }

        private async Task<Member?> GetMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        private bool IsAdminOrPastor()
            => User.IsInRole("Admin") || User.IsInRole("Pastor");

        // ── Lobby ───────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
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

            var vm = new MeetingLobbyViewModel
            {
                LiveMeetings = await liveQ.OrderByDescending(r => r.StartedAt).ToListAsync(),
                ScheduledMeetings = await scheduledQ.OrderBy(r => r.ScheduledAt).Take(10).ToListAsync(),
                PastMeetings = await pastQ.OrderByDescending(r => r.EndedAt).Take(6).ToListAsync()
            };

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

                // Also include private meetings where this member is an invitee
                ViewBag.InvitedMeetings = await _db.MeetingInvitations
                    .Include(i => i.MeetingRoom).ThenInclude(r => r.HostMember)
                    .Where(i => i.MemberId == member.Id
                             && i.MeetingRoom.Status != RoomStatus.Ended
                             && i.MeetingRoom.IsApproved)
                    .Select(i => i.MeetingRoom)
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

                return View(vm);
            }
            ViewBag.PendingApprovalCount = await _db.MeetingRooms
                        .CountAsync(r => r.ApprovalStatus == ApprovalStatus.Pending);

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
                WaitingRoomEnabled = vm.WaitingRoomEnabled,
                ChatEnabled = vm.ChatEnabled,
                MaxParticipants = vm.MaxParticipants,
                IsPublic = vm.IsPublic,
                PassCode = vm.PassCode,
                IsRecurring = vm.IsRecurring,
                RecurringPattern = vm.IsRecurring
                    ? $"weekly:{vm.RecurringDay}:{vm.RecurringTime}" : null,
                Status = RoomStatus.Scheduled,

                // Admins/Pastors are auto-approved; members need approval
                IsApproved = isPrivileged,
                ApprovalStatus = isPrivileged
                    ? ApprovalStatus.Approved : ApprovalStatus.Pending,
                ApprovedByUserId = isPrivileged ? _userManager.GetUserId(User) : null,
                ApprovedAt = isPrivileged ? DateTime.UtcNow : null
            };

            _db.MeetingRooms.Add(room);
            await _db.SaveChangesAsync();

            if (isPrivileged)
            {
                TempData["Success"] = $"Meeting created! Room code: {code}";
                return RedirectToAction("Room", new { code });
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
                    "/Admin/MeetingApprovals");

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
                WaitingRoomEnabled = room.WaitingRoomEnabled,
                ChatEnabled = room.ChatEnabled,
                MaxParticipants = room.MaxParticipants,
                IsPublic = room.IsPublic,
                PassCode = room.PassCode,
                IsRecurring = room.IsRecurring,
            };
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
            room.WaitingRoomEnabled = vm.WaitingRoomEnabled;
            room.ChatEnabled = vm.ChatEnabled;
            room.MaxParticipants = vm.MaxParticipants;
            room.IsPublic = vm.IsPublic;
            room.PassCode = vm.PassCode;
            room.IsRecurring = vm.IsRecurring;
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

            return View(room);
        }

        // ── Send email invitations ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendInvites(int id, List<int> memberIds, string? emailList)
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

            // ── Invite existing members by ID ──────────────────────
            foreach (var memberId in memberIds ?? new List<int>())
            {
                if (room.Invitations.Any(i => i.MemberId == memberId)) continue;

                var invitee = await _db.Members
                    .Include(m => m.User)
                    .FirstOrDefaultAsync(m => m.Id == memberId);
                if (invitee == null) continue;

                var inv = new MeetingInvitation
                {
                    MeetingRoomId = room.Id,
                    MemberId = memberId,
                    InvitedAt = DateTime.UtcNow,
                    Token = Guid.NewGuid().ToString("N")
                };
                _db.MeetingInvitations.Add(inv);

                // In-app notification
                await _notifications.CreateAsync(memberId,
                    $"Meeting Invitation: {room.Title}",
                    $"You have been invited to join { room.Title}" +
                    $"scheduled for {room.ScheduledAt?.ToLocalTime():ddd, dd MMM · h:mm tt}. " +
                    $"Room code: {room.RoomCode}",
                    $"/Meeting/Room/{room.RoomCode}");

                // Email
                var joinUrl = Url.Action("Room", "Meeting",
                    new { code = room.RoomCode }, Request.Scheme)!;

                await _email.SendEmailAsync(
                    invitee.Email,
                    $"You're invited: {room.Title}",
                    BuildInviteEmail(room, invitee.FullName, joinUrl, inv.Token));

                sentCount++;
            }

            // ── Invite by raw email addresses ──────────────────────
            if (!string.IsNullOrWhiteSpace(emailList))
            {
                var emails = emailList.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries) //, ''
                                      .Select(e => e.Trim())
                                      .Where(e => e.Contains('@'));

                foreach (var email in emails)
                {
                    var joinUrl = Url.Action("Room", "Meeting",
                        new { code = room.RoomCode }, Request.Scheme)!;

                    await _email.SendEmailAsync(
                        email,
                        $"You're invited: {room.Title}",
                        BuildInviteEmail(room, email, joinUrl, null));

                    sentCount++;
                }
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = sentCount > 0
                ? $"{sentCount} invitation{(sentCount > 1 ? "s" : "")} sent successfully."
                : "No new invitations sent (may already be invited).";

            return RedirectToAction("Invite", new { id });
        }

        // ── Email template ─────────────────────────────────────────
        private static string BuildInviteEmail(MeetingRoom room, string name, string joinUrl, string? token)
        {
            var scheduled = room.ScheduledAt?.ToLocalTime().ToString("dddd, dd MMMM yyyy 'at' h:mm tt") ?? "Soon";
            return $"""
                <div style="font-family:sans-serif;max-width:520px;margin:0 auto">
                  <div style="background:#312e81;padding:1.5rem;border-radius:12px 12px 0 0;text-align:center">
                    <h1 style="color:white;font-size:1.4rem;margin:0">✝ You're Invited</h1>
                  </div>
                  <div style="background:#f9fafb;padding:1.5rem;border-radius:0 0 12px 12px;border:1px solid #e5e7eb">
                    <p>Hi <strong>{name}</strong>,</p>
                    <p>You have been invited to join an online meeting:</p>
                    <div style="background:white;border:1px solid #e5e7eb;border-radius:8px;padding:1rem;margin:1rem 0">
                      <h2 style="margin:0 0 .5rem;color:#1e1b4b">{room.Title}</h2>
                      <p style="margin:0;color:#6b7280">📅 {scheduled}</p>
                      <p style="margin:.3rem 0 0;color:#6b7280">🔑 Room code: <strong>{room.RoomCode}</strong></p>
                    </div>
                    {string.IsNullOrEmpty(room.Description)} ? "" : $"<p style="color:#4b5563">{room.Description}</p>")
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
                IsPublic = false,
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

            var user = await _userManager.GetUserAsync(User);
            var displayName = member?.FullName ?? user?.UserName ?? "Guest";

            if (room.Status == RoomStatus.Scheduled)
            {
                room.Status = RoomStatus.Live;
                room.StartedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return View(new MeetingRoomViewModel
            {
                Room = room,
                DisplayName = displayName,
                IsHost = isHost,
                CurrentMemberId = member?.Id
            });
        }

        // ── Participants API ────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Participants(string code)
        {
            var room = await _db.MeetingRooms.FirstOrDefaultAsync(r => r.RoomCode == code);
            if (room == null) return NotFound();

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
                        ? p.Member.ProfilePhoto ?? "/images/avatar-default.png"
                        : "/images/avatar-default.png"
                }).ToListAsync();

            return Json(list);
        }

        // ── Helper ─────────────────────────────────────────────
        private static string GenerateRoomCode(MeetingType type)
        {
            var prefix = type switch
            {
                MeetingType.Prayer => "PRAY",
                MeetingType.BibleStudy => "BSDY",
                MeetingType.Worship => "WRSP",
                MeetingType.YouthService => "YWTH",
                MeetingType.CellGroup => "CELL",
                _ => "MEET"
            };
            var rand = new Random().Next(1000, 9999);
            return $"{prefix}-{rand}";
        }
    }
}
