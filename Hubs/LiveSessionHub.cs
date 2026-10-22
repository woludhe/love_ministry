using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChurchMinistry.Hubs
{
    /// <summary>
    /// Pushes live overlay changes from admin → all viewers in real time.
    /// Admin calls push methods; viewers receive them without page refresh.
    /// </summary>
    public class LiveSessionHub : Hub
    {
        private readonly ApplicationDbContext _db;
        private readonly IActivityService _activity;
        private readonly IHubContext<MeetingHub> _meetingHub;
        public LiveSessionHub(ApplicationDbContext db, IActivityService activity, IHubContext<MeetingHub> meetingHub)
        {
            _db = db; _activity = activity; _meetingHub = meetingHub;
        }

        // ── Viewers join the room for a sermon ─────────────────
        // Live page viewers call JoinLiveRoom; people in the sermon's meeting room call JoinLiveRoomFromMeeting
        public Task JoinLiveRoom(int sermonId) => JoinAsync(sermonId, inMeeting: false);
        public Task JoinLiveRoomFromMeeting(int sermonId) => JoinAsync(sermonId, inMeeting: true);

        private async Task JoinAsync(int sermonId, bool inMeeting)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"live-{sermonId}");
            // Live page viewers get chat from the start, so the unread badge counts while the panel is closed
            if (!inMeeting) await Groups.AddToGroupAsync(Context.ConnectionId, $"livechat-{sermonId}");
            await LogAttendanceAsync(sermonId, inMeeting);
            await LogActivityAsync(sermonId);
            LiveAudience.Join(Context.ConnectionId, sermonId, inMeeting);
            await BroadcastAudienceAsync(sermonId);
        }

        public async Task LeaveLiveRoom(int sermonId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"live-{sermonId}");
            await CloseAttendanceAsync();
            if (LiveAudience.Leave(Context.ConnectionId) is int left) await BroadcastAudienceAsync(left);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await CloseAttendanceAsync();
            if (LiveAudience.Leave(Context.ConnectionId) is int left) await BroadcastAudienceAsync(left);
            await base.OnDisconnectedAsync(exception);
        }

        // ── Audience count for admins (Live Settings) ──────────
        // Joins the admin to the count's updates and returns the current numbers
        [Authorize(Roles = "Admin,Pastor")]
        public async Task<object> WatchAudience(int sermonId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"live-audience-{sermonId}");
            return LiveAudience.Summary(sermonId);
        }

        private Task BroadcastAudienceAsync(int sermonId) =>
            Clients.Group($"live-audience-{sermonId}").SendAsync("AudienceChanged", LiveAudience.Summary(sermonId));

        // ── Attendance tracking ────────────────────────────────
        // Start a log row for this connection if the sermon tracks attendance
        // Member activity log: signed-in viewers of a live sermon, once a day per sermon
        // (regardless of TrackAttendance, which only drives the attendance report)
        private async Task LogActivityAsync(int sermonId)
        {
            if (Context.User?.Identity?.IsAuthenticated != true) return;
            var sermon = await _db.Sermons.Where(s => s.Id == sermonId && s.IsLive)
                .Select(s => new { s.Title }).FirstOrDefaultAsync();
            if (sermon == null) return;
            await _activity.LogForUserAsync(Context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                MemberActivityType.WatchLive, sermon.Title, sermonId, oncePerDay: true);
        }

        private async Task LogAttendanceAsync(int sermonId, bool inMeeting)
        {
            var sermon = await _db.Sermons
                .FirstOrDefaultAsync(s => s.Id == sermonId && s.TrackAttendance);
            if (sermon == null) return;

            // Nothing to attend once it's over: a viewer whose browser reconnects after the
            // stream stopped or the meeting ended mustn't open a new row
            if (!string.IsNullOrEmpty(sermon.MeetingRoomCode))
            {
                if (await _db.MeetingRooms.AnyAsync(r => r.RoomCode == sermon.MeetingRoomCode
                        && (r.Status == RoomStatus.Ended || r.Status == RoomStatus.Cancelled)))
                    return;
            }
            else if (!sermon.IsLive) return;

            var connId = Context.ConnectionId;
            if (await _db.SermonAttendances.AnyAsync(a =>
                    a.SermonId == sermonId && a.ConnectionId == connId && a.LeftAt == null))
                return;

            var entry = new SermonAttendance
            {
                SermonId = sermonId,
                ConnectionId = connId,
                DeliveryMode = sermon.DeliveryMode,
                InMeeting = inMeeting,
                IsGuest = true,
                DisplayName = "Guest"
            };

            if (Context.User?.Identity?.IsAuthenticated == true)
            {
                var userId = Context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var member = userId == null ? null
                    : await _db.Members.FirstOrDefaultAsync(m => m.UserId == userId);
                entry.IsGuest = false;
                entry.MemberId = member?.Id;
                entry.DisplayName = member?.FullName ?? Context.User.Identity.Name ?? "Member";
            }

            _db.SermonAttendances.Add(entry);
            await _db.SaveChangesAsync();
        }

        // Viewer's browser reports its location (only if they allowed it)
        public async Task ReportLocation(int sermonId, double latitude, double longitude)
        {
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180) return;

            var connId = Context.ConnectionId;
            var entry = await _db.SermonAttendances.FirstOrDefaultAsync(a =>
                a.SermonId == sermonId && a.ConnectionId == connId && a.LeftAt == null);
            if (entry == null) return;

            entry.Latitude = latitude;
            entry.Longitude = longitude;
            await _db.SaveChangesAsync();
        }

        private async Task CloseAttendanceAsync()
        {
            var connId = Context.ConnectionId;
            var open = await _db.SermonAttendances
                .Where(a => a.ConnectionId == connId && a.LeftAt == null)
                .ToListAsync();
            if (open.Count == 0) return;

            foreach (var a in open) a.LeftAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // ── Admin pushes verse change ──────────────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task PushVerse(int sermonId, string reference, string verseText, string translation)
        {
            await Clients.Group($"live-{sermonId}").SendAsync("VerseChanged", new
            {
                reference,
                verseText,
                translation
            });
        }

        // ── Admin clears verse ─────────────────────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task ClearVerse(int sermonId)
        {
            await Clients.Group($"live-{sermonId}").SendAsync("VerseCleared");
        }

        // ── Admin pushes announcement update ──────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task PushAnnouncement(int sermonId, string text)
        {
            await Clients.Group($"live-{sermonId}").SendAsync("AnnouncementChanged", text);
        }

        // ── Admin toggles overlay visibility ──────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task ToggleOverlay(int sermonId, string overlay, bool visible)
        {
            // overlay = "verse" | "marquee" | "giving" | "prayer"
            await Clients.Group($"live-{sermonId}").SendAsync("OverlayToggled", overlay, visible);
        }

        // ── Admin updates giving CTA text ─────────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task PushGivingCta(int sermonId, string cta)
        {
            await Clients.Group($"live-{sermonId}").SendAsync("GivingCtaChanged", cta);
        }

        // ── Admin updates marquee speed ────────────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task SetMarqueeSpeed(int sermonId, string speed)
        {
            await Clients.Group($"live-{sermonId}").SendAsync("MarqueeSpeedChanged", speed);
        }

        // ── Live chat ───────────────────────────────────────────────
        private bool IsHost => Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("Pastor") == true;

        // Kept for older pages: JoinLiveRoom now puts Live page viewers in the chat group
        public async Task JoinLiveChatRoom(int sermonId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"livechat-{sermonId}");
        }

        // The chat so far — Live page and meeting room messages merged — and whether it's
        // paused. Called when the page loads and after a reconnect.
        [Authorize]
        public async Task<object> GetLiveChat(int sermonId)
        {
            var userId = Context.User!.FindFirstValue(ClaimTypes.NameIdentifier);
            var memberId = await _db.Members.Where(m => m.UserId == userId).Select(m => (int?)m.Id).FirstOrDefaultAsync();
            var paused = await _db.LiveSessions.AnyAsync(s => s.SermonId == sermonId && s.ChatPaused);
            var items = await SermonChat.LoadAsync(_db, sermonId, null);

            return new
            {
                paused,
                messages = items.Select(i => SermonChat.Payload(i, i.IsFrom(userId, memberId)))
            };
        }

        // ── Send a live chat message ────────────────────────────────
        [Authorize]
        public async Task SendLiveChat(int sermonId, string message, string type = "chat")
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (type != "prayer") type = "chat";

            // Sanitise — strip HTML tags
            message = System.Text.RegularExpressions.Regex.Replace(message, "<[^>]+>", "").Trim();
            if (message.Length == 0) return;
            if (message.Length > 400) message = message.Substring(0, 400);

            var sermon = await _db.Sermons.Where(s => s.Id == sermonId)
                .Select(s => new { s.MeetingRoomCode }).FirstOrDefaultAsync();
            if (sermon == null) return;
            // Paused chat: only the hosts can still post
            if (!IsHost && await _db.LiveSessions.AnyAsync(s => s.SermonId == sermonId && s.ChatPaused))
            {
                await Clients.Caller.SendAsync("LiveChatPaused", true);
                return;
            }

            var userId = Context.User!.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var member = await _db.Members.FirstOrDefaultAsync(m => m.UserId == userId);

            var chat = new LiveChatMessage
            {
                SermonId = sermonId,
                UserId = userId,
                MemberId = member?.Id,
                SenderName = member?.FullName ?? Context.User.Identity?.Name ?? "Member",
                Message = message,
                Type = type
            };
            _db.LiveChatMessages.Add(chat);
            await _db.SaveChangesAsync();

            // Everyone else in the group, then the sender's own copy
            var item = new SermonChat.Item(chat.SenderName, chat.Message, chat.Type, chat.SentAt,
                member?.ProfilePhoto, SermonChat.FromLive, userId, member?.Id);
            await Clients.OthersInGroup($"livechat-{sermonId}").SendAsync("LiveChatMessage", SermonChat.Payload(item, isOwn: false));
            await Clients.Caller.SendAsync("LiveChatMessage", SermonChat.Payload(item, isOwn: true));

            // Same conversation in the sermon's meeting room
            if (!string.IsNullOrEmpty(sermon.MeetingRoomCode))
                await _meetingHub.Clients.Group(sermon.MeetingRoomCode)
                    .SendAsync("ReceiveMessage", SermonChat.Payload(item, isOwn: false));
        }

        // ── Admin: pause/unpause chat ───────────────────────────────
        // Saved on the sermon's LiveSession so it holds for people who join later
        [Authorize(Roles = "Admin,Pastor")]
        public async Task PauseLiveChat(int sermonId, bool paused)
        {
            if (!await _db.Sermons.AnyAsync(s => s.Id == sermonId)) return;
            var session = await _db.LiveSessions.FirstOrDefaultAsync(s => s.SermonId == sermonId);
            if (session == null)
            {
                session = new LiveSession { SermonId = sermonId };
                _db.LiveSessions.Add(session);
            }
            session.ChatPaused = paused;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await Clients.Group($"livechat-{sermonId}").SendAsync("LiveChatPaused", paused);
        }

        // ── Admin: clear all chat messages ─────────────────────────
        // Hides what was said so far (Live page and meeting room); the rows stay in the database
        [Authorize(Roles = "Admin,Pastor")]
        public async Task ClearLiveChat(int sermonId)
        {
            var sermon = await _db.Sermons.Where(s => s.Id == sermonId)
                .Select(s => new { s.MeetingRoomCode }).FirstOrDefaultAsync();
            if (sermon == null) return;
            var session = await _db.LiveSessions.FirstOrDefaultAsync(s => s.SermonId == sermonId);
            if (session == null)
            {
                session = new LiveSession { SermonId = sermonId };
                _db.LiveSessions.Add(session);
            }
            session.ChatClearedAt = session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await Clients.Group($"livechat-{sermonId}").SendAsync("LiveChatCleared");
            if (!string.IsNullOrEmpty(sermon.MeetingRoomCode))
                await _meetingHub.Clients.Group(sermon.MeetingRoomCode).SendAsync("ChatCleared");
        }

        /// <summary>
        /// Broadcasts a reaction emoji to all viewers watching a sermon live.
        /// Called from Live.cshtml → sendReaction(emoji)
        /// </summary>
        [AllowAnonymous]   // guests can also react (if you want to restrict to logged-in only, use [Authorize])
        public async Task SendLiveReaction(int sermonId, string emoji)
        {
            // Validate emoji is a single grapheme cluster (not arbitrary text)
            if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 8) return;

            // Broadcast to everyone watching this sermon's live room
            await Clients.Group($"live-{sermonId}").SendAsync("LiveReaction", new
            {
                emoji,
                // Optionally include sender name if logged in
                senderName = Context.User?.Identity?.IsAuthenticated == true
                    ? Context.User.Identity.Name
                    : "Guest"
            });
        }
    }

    /// <summary>
    /// One conversation per sermon: messages sent on the Live page (LiveChatMessages) and in
    /// the sermon's meeting room (MeetingMessages) are merged by time, each tagged with where
    /// it came from. Used by LiveSessionHub (Live page) and MeetingHub (meeting room).
    /// </summary>
    public static class SermonChat
    {
        public const string FromLive = "live", FromMeeting = "meeting";
        private const int HistorySize = 100;

        public record Item(string SenderName, string Message, string Type, DateTime SentAt,
            string? Photo, string Source, string? UserId, int? MemberId)
        {
            // Live messages know the sender's login; meeting messages only their member record
            public bool IsFrom(string? userId, int? memberId) =>
                Source == FromLive ? UserId != null && UserId == userId
                                   : MemberId != null && MemberId == memberId;
        }

        // The sermon whose conversation a meeting room shares (latest, if several use the room)
        public static Task<int?> SermonForRoomAsync(ApplicationDbContext db, string roomCode) =>
            db.Sermons.Where(s => s.MeetingRoomCode == roomCode)
                .OrderByDescending(s => s.Id).Select(s => (int?)s.Id).FirstOrDefaultAsync();

        // Latest messages, oldest first. Pass the sermon (its meeting room is found from it),
        // or just a meeting room id for a meeting that isn't linked to a sermon.
        public static async Task<List<Item>> LoadAsync(ApplicationDbContext db, int? sermonId, int? meetingRoomId)
        {
            DateTime? clearedAt = null;
            if (sermonId is int sid)
            {
                var roomCode = await db.Sermons.Where(s => s.Id == sid).Select(s => s.MeetingRoomCode).FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(roomCode))
                    meetingRoomId = await db.MeetingRooms.Where(r => r.RoomCode == roomCode).Select(r => (int?)r.Id).FirstOrDefaultAsync();
                clearedAt = await db.LiveSessions.Where(s => s.SermonId == sid).Select(s => s.ChatClearedAt).FirstOrDefaultAsync();
            }

            var items = new List<Item>();
            if (sermonId is int liveId)
                items.AddRange(await db.LiveChatMessages
                    .Where(m => m.SermonId == liveId && (clearedAt == null || m.SentAt > clearedAt))
                    .OrderByDescending(m => m.SentAt).Take(HistorySize)
                    .Select(m => new Item(m.SenderName, m.Message, m.Type, m.SentAt,
                        m.Member != null ? m.Member.ProfilePhoto : null, FromLive, m.UserId, m.MemberId))
                    .ToListAsync());
            if (meetingRoomId is int roomId)
                items.AddRange(await db.MeetingMessages
                    .Where(m => m.MeetingRoomId == roomId && m.Type != "system" && (clearedAt == null || m.SentAt > clearedAt))
                    .OrderByDescending(m => m.SentAt).Take(HistorySize)
                    .Select(m => new Item(m.SenderName, m.Message, m.Type, m.SentAt,
                        m.Member != null ? m.Member.ProfilePhoto : null, FromMeeting, null, m.MemberId))
                    .ToListAsync());

            return items.OrderBy(i => i.SentAt).TakeLast(HistorySize).ToList();
        }

        // What both pages receive. sentAt is UTC ISO time; each browser shows it in local time.
        public static object Payload(Item i, bool isOwn) => new
        {
            senderName = i.SenderName,
            message = i.Message,
            type = i.Type,
            sentAt = DateTime.SpecifyKind(i.SentAt, DateTimeKind.Utc).ToString("o"),
            photo = i.Photo ?? "/images/avatar-default.png",
            source = i.Source,
            isOwn
        };
    }

    /// <summary>
    /// Who is connected to each live sermon right now: people watching on the Live page and
    /// people inside the sermon's meeting room. Counts open connections, so a person with
    /// two tabs open counts twice. Held in memory (one web server).
    /// </summary>
    public static class LiveAudience
    {
        private record Entry(int SermonId, bool InMeeting);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Entry> _connections = new();

        public static void Join(string connectionId, int sermonId, bool inMeeting) =>
            _connections[connectionId] = new Entry(sermonId, inMeeting);

        // The sermon the connection was watching, if any
        public static int? Leave(string connectionId) =>
            _connections.TryRemove(connectionId, out var e) ? e.SermonId : null;

        public static (int OnLivePage, int InMeeting) Count(int sermonId)
        {
            int live = 0, meeting = 0;
            foreach (var e in _connections.Values)
                if (e.SermonId == sermonId) { if (e.InMeeting) meeting++; else live++; }
            return (live, meeting);
        }

        public static object Summary(int sermonId)
        {
            var (live, meeting) = Count(sermonId);
            return new { total = live + meeting, onLivePage = live, inMeeting = meeting };
        }
    }
}
