using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    // Record of meetings that have taken place: who hosted and attended, Live-page viewers,
    // and scheduled against actual times
    [Authorize(Roles = "Admin,Pastor")]
    public class MeetingRegisterController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public MeetingRegisterController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db; _userManager = userManager;
        }

        // Participant / attendance fields the register needs, without loading whole entities
        private record ParticipantInfo(int RoomId, int? MemberId, string DisplayName, bool IsHost, bool IsCoHost,
            bool Admitted, DateTime JoinedAt, DateTime? LeftAt);
        private record ViewingInfo(int SermonId, int? MemberId, string DisplayName, bool IsGuest, bool? InMeeting,
            DateTime JoinedAt, DateTime? LeftAt);

        // ── Register list ───────────────────────────────────────
        public async Task<IActionResult> Index(string? search, MeetingType? type, string? status,
            DateTime? from, DateTime? to, int page = 1)
        {
            var vm = new MeetingRegisterViewModel
            {
                Search = search, Type = type, From = from, To = to,
                Status = status is "cancelled" or "all" ? status : "ended",
                Pager = new PagerModel(page, "meetings")
            };

            var q = _db.MeetingRooms.Include(r => r.HostMember).AsQueryable();
            q = vm.Status switch
            {
                "cancelled" => q.Where(r => r.Status == RoomStatus.Cancelled),
                "all" => q.Where(r => r.Status == RoomStatus.Ended || r.Status == RoomStatus.Cancelled),
                _ => q.Where(r => r.Status == RoomStatus.Ended)
            };
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(r => r.Title.Contains(search) || r.RoomCode.Contains(search)
                              || (r.HostMember != null && (r.HostMember.FirstName + " " + r.HostMember.LastName).Contains(search))
                              || _db.Users.Any(u => u.Id == r.HostUserId && (u.FirstName + " " + u.LastName).Contains(search)));
            if (type.HasValue) q = q.Where(r => r.MeetingType == type);
            // Dates are picked in local time; meeting times are stored in UTC
            if (from.HasValue)
            {
                var f = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Local).ToUniversalTime();
                q = q.Where(r => (r.StartedAt ?? r.ScheduledAt ?? r.CreatedAt) >= f);
            }
            if (to.HasValue)
            {
                var t = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
                q = q.Where(r => (r.StartedAt ?? r.ScheduledAt ?? r.CreatedAt) < t);
            }

            // Counts for every matching meeting (for the totals), then one page of rows
            var all = await q.Select(r => new { r.Id, r.RoomCode }).ToListAsync();
            var attendees = await LoadAttendeeCountsAsync(all.Select(r => r.Id).ToList());
            var viewers = await LoadViewerStatsAsync(all.ToDictionary(r => r.Id, r => r.RoomCode));

            vm.MeetingCount = all.Count;
            vm.TotalAttendees = attendees.Values.Sum(a => a.Count);
            vm.TotalViewers = viewers.Values.Sum(v => v.Total);

            var rooms = await q.OrderByDescending(r => r.EndedAt ?? r.ScheduledAt ?? r.CreatedAt).ToPageAsync(vm.Pager);
            await AttachHostAccountsAsync(rooms);
            vm.Rows = rooms.Select(r => new MeetingRegisterRow
            {
                Room = r,
                Attendees = attendees.TryGetValue(r.Id, out var a) ? a.Count : 0,
                Viewers = viewers.GetValueOrDefault(r.Id)
            }).ToList();

            return View(vm);
        }

        // ── One meeting ─────────────────────────────────────────
        public async Task<IActionResult> Details(int id, int page = 1, int viewersPage = 1)
        {
            var room = await _db.MeetingRooms.Include(r => r.HostMember).FirstOrDefaultAsync(r => r.Id == id);
            if (room == null) return NotFound();
            await AttachHostAccountsAsync(new List<MeetingRoom> { room });

            var participants = await ParticipantsOf(new List<int> { id }).ToListAsync();
            var end = room.EndedAt ?? DateTime.UtcNow;
            var attendees = MergeAttendees(participants.Where(p => p.Admitted), end);
            var attendeeMemberIds = attendees.Where(a => a.MemberId != null).Select(a => a.MemberId!.Value).ToHashSet();

            var vm = new MeetingRegisterDetailsViewModel
            {
                Room = room,
                Attendees = attendees,
                AttendeesPager = new PagerModel(page, "attendees"),
                ViewersPager = new PagerModel(viewersPage, "viewings", "viewersPage"),
                NotAdmitted = participants.Where(p => !p.Admitted)
                    .Select(p => p.DisplayName).Distinct().OrderBy(n => n).ToList(),
                PeakAttendees = PeakAtOnce(participants.Where(p => p.Admitted), end),
                ChatMessages = await _db.MeetingMessages.CountAsync(m => m.MeetingRoomId == id && m.Type != "system"),
                InvitedMembers = await _db.MeetingInvitations.CountAsync(i => i.MeetingRoomId == id && i.MemberId != null),
                InvitedGroups = await _db.MeetingInvitations.CountAsync(i => i.MeetingRoomId == id && i.MinistryGroupId != null),
                InvitedEmails = await _db.MeetingInvitations.CountAsync(i => i.MeetingRoomId == id && i.MemberId == null && i.MinistryGroupId == null && i.Email != null)
            };
            vm.PageAttendees = attendees.ToPage(vm.AttendeesPager);

            if (room.ApprovedByUserId != null && await _userManager.FindByIdAsync(room.ApprovedByUserId) is { } approver)
                vm.ApprovedBy = string.IsNullOrWhiteSpace(approver.FullName) ? approver.Email : approver.FullName;

            // Live-page viewers through the sermon streamed from this meeting (live one first, else latest)
            vm.LinkedSermon = await _db.Sermons.Where(s => s.MeetingRoomCode == room.RoomCode)
                .OrderByDescending(s => s.TrackAttendance).ThenByDescending(s => s.SermonDate).FirstOrDefaultAsync();
            if (vm.LinkedSermon != null)
            {
                var sermonIds = await _db.Sermons.Where(s => s.MeetingRoomCode == room.RoomCode).Select(s => s.Id).ToListAsync();
                var tracked = await _db.Sermons.AnyAsync(s => s.MeetingRoomCode == room.RoomCode && s.TrackAttendance);
                var viewings = await ViewingsOf(sermonIds).ToListAsync();
                var liveViewings = LivePageOnly(viewings, attendeeMemberIds).ToList();

                vm.ViewerStats = Stats(liveViewings, tracked);
                vm.Viewers = liveViewings
                    .OrderBy(v => v.JoinedAt)
                    .Select(v => new MeetingViewerRow(v.MemberId, v.IsGuest ? "Guest" : v.DisplayName, v.IsGuest,
                        v.JoinedAt, v.LeftAt, (v.LeftAt ?? end) - v.JoinedAt, v.InMeeting == null))
                    .ToList();
                vm.PageViewers = vm.Viewers.ToPage(vm.ViewersPager);
            }

            return View(vm);
        }

        // ── Helpers ─────────────────────────────────────────────

        // Host accounts by HostUserId (the Host navigation is mapped to a different column),
        // so HostName can fall back to the account for hosts without a member profile.
        // Read-only: nothing here is saved.
        private async Task AttachHostAccountsAsync(List<MeetingRoom> rooms)
        {
            var ids = rooms.Where(r => r.HostMember == null && r.HostUserId != null)
                .Select(r => r.HostUserId!).Distinct().ToList();
            if (ids.Count == 0) return;
            var users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
            foreach (var r in rooms)
                if (r.HostUserId != null && users.TryGetValue(r.HostUserId, out var u)) r.Host = u;
        }

        // Filters go before the projection: EF can't translate conditions on the projected record
        private IQueryable<ParticipantInfo> ParticipantsOf(List<int> roomIds, bool admittedOnly = false, bool membersOnly = false) =>
            _db.MeetingParticipants
                .Where(p => roomIds.Contains(p.MeetingRoomId))
                .Where(p => !admittedOnly || p.IsAdmitted || p.IsHost)
                .Where(p => !membersOnly || p.MemberId != null)
                .Select(p => new ParticipantInfo(p.MeetingRoomId, p.MemberId,
                    p.Member != null ? p.Member.FirstName + " " + p.Member.LastName : p.DisplayName,
                    p.IsHost, p.IsCoHost, p.IsAdmitted || p.IsHost, p.JoinedAt, p.LeftAt));

        private IQueryable<ViewingInfo> ViewingsOf(List<int> sermonIds) =>
            _db.SermonAttendances
                .Where(a => sermonIds.Contains(a.SermonId))
                .Select(a => new ViewingInfo(a.SermonId, a.MemberId, a.DisplayName, a.IsGuest, a.InMeeting, a.JoinedAt, a.LeftAt));

        // Same person = same member, else same display name (guests)
        private static string PersonKey(int? memberId, string name) =>
            memberId?.ToString() ?? "guest:" + name.Trim().ToLowerInvariant();

        // A person's rows overlap (reconnects, second tabs, rows never closed), so their time is the
        // union of their intervals, capped at the meeting's end
        private static List<(DateTime From, DateTime To)> Union(IEnumerable<ParticipantInfo> rows, DateTime end)
        {
            var merged = new List<(DateTime From, DateTime To)>();
            foreach (var (from, to) in rows
                         .Select(p => (From: p.JoinedAt, To: p.LeftAt is { } l && l < end ? l : end))
                         .Where(i => i.To > i.From)
                         .OrderBy(i => i.From))
            {
                if (merged.Count > 0 && from <= merged[^1].To)
                    merged[^1] = (merged[^1].From, to > merged[^1].To ? to : merged[^1].To);
                else
                    merged.Add((from, to));
            }
            return merged;
        }

        private static List<MeetingAttendeeRow> MergeAttendees(IEnumerable<ParticipantInfo> rows, DateTime end) =>
            rows.GroupBy(p => PersonKey(p.MemberId, p.DisplayName))
                .Select(g =>
                {
                    var time = Union(g, end);
                    var lastLeft = time.Count > 0 ? time[^1].To : g.Max(p => p.LeftAt ?? end);
                    return new MeetingAttendeeRow(
                        g.First().MemberId,
                        g.First().DisplayName,
                        g.Any(p => p.IsHost), g.Any(p => p.IsCoHost),
                        g.Min(p => p.JoinedAt),
                        lastLeft >= end ? null : lastLeft,          // null = stayed until the end
                        TimeSpan.FromTicks(time.Sum(i => (i.To - i.From).Ticks)),
                        Math.Max(1, time.Count));                   // separate stretches in the room
                })
                .OrderByDescending(a => a.IsHost).ThenBy(a => a.FirstJoined)
                .ToList();

        // Most different people in the room at the same moment
        private static int PeakAtOnce(IEnumerable<ParticipantInfo> rows, DateTime end)
        {
            int current = 0, peak = 0;
            var events = rows.GroupBy(p => PersonKey(p.MemberId, p.DisplayName))
                .SelectMany(g => Union(g, end))
                .SelectMany(i => new[] { (At: i.From, Change: 1), (At: i.To, Change: -1) })
                .OrderBy(e => e.At).ThenBy(e => e.Change);   // leave before join at the same instant
            foreach (var e in events) { current += e.Change; peak = Math.Max(peak, current); }
            return peak;
        }

        // Viewings on the Live page. Rows logged before InMeeting was recorded are kept unless the
        // member was in the room (an estimate: guests in the room can't be told apart)
        private static IEnumerable<ViewingInfo> LivePageOnly(IEnumerable<ViewingInfo> rows, HashSet<int> attendeeMemberIds) =>
            rows.Where(v => v.InMeeting == false
                         || (v.InMeeting == null && !(v.MemberId.HasValue && attendeeMemberIds.Contains(v.MemberId.Value))));

        private static MeetingViewerStats Stats(List<ViewingInfo> liveViewings, bool tracked) => new()
        {
            Tracked = tracked,
            Members = liveViewings.Where(v => !v.IsGuest).Select(v => PersonKey(v.MemberId, v.DisplayName)).Distinct().Count(),
            Guests = liveViewings.Count(v => v.IsGuest),
            Estimated = liveViewings.Any(v => v.InMeeting == null)
        };

        // Unique admitted attendees per meeting
        private async Task<Dictionary<int, HashSet<string>>> LoadAttendeeCountsAsync(List<int> roomIds)
        {
            var rows = await ParticipantsOf(roomIds, admittedOnly: true).ToListAsync();
            return rows.GroupBy(p => p.RoomId)
                .ToDictionary(g => g.Key, g => g.Select(p => PersonKey(p.MemberId, p.DisplayName)).ToHashSet());
        }

        // Live-page viewer numbers per meeting that has a linked sermon
        private async Task<Dictionary<int, MeetingViewerStats>> LoadViewerStatsAsync(Dictionary<int, string> roomCodes)
        {
            var codes = roomCodes.Values.ToList();
            var sermons = await _db.Sermons
                .Where(s => s.MeetingRoomCode != null && codes.Contains(s.MeetingRoomCode))
                .Select(s => new { s.Id, s.MeetingRoomCode, s.TrackAttendance })
                .ToListAsync();
            if (sermons.Count == 0) return new();

            var roomIds = roomCodes.Where(kv => sermons.Any(s => s.MeetingRoomCode == kv.Value)).Select(kv => kv.Key).ToList();
            var attendeeMembers = (await ParticipantsOf(roomIds, admittedOnly: true, membersOnly: true).ToListAsync())
                .GroupBy(p => p.RoomId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.MemberId!.Value).ToHashSet());
            var viewings = (await ViewingsOf(sermons.Select(s => s.Id).ToList()).ToListAsync())
                .GroupBy(v => v.SermonId).ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<int, MeetingViewerStats>();
            foreach (var roomId in roomIds)
            {
                var linked = sermons.Where(s => s.MeetingRoomCode == roomCodes[roomId]).ToList();
                var rows = linked.SelectMany(s => viewings.GetValueOrDefault(s.Id) ?? new List<ViewingInfo>());
                var live = LivePageOnly(rows, attendeeMembers.GetValueOrDefault(roomId) ?? new HashSet<int>()).ToList();
                result[roomId] = Stats(live, linked.Any(s => s.TrackAttendance));
            }
            return result;
        }
    }
}
