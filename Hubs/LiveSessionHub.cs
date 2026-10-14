using ChurchMinistry.Data;
using ChurchMinistry.Models;
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
        public LiveSessionHub(ApplicationDbContext db) { _db = db; }

        // ── Viewers join the room for a sermon ─────────────────
        public async Task JoinLiveRoom(int sermonId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"live-{sermonId}");
            await LogAttendanceAsync(sermonId);
        }

        public async Task LeaveLiveRoom(int sermonId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"live-{sermonId}");
            await CloseAttendanceAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await CloseAttendanceAsync();
            await base.OnDisconnectedAsync(exception);
        }

        // ── Attendance tracking ────────────────────────────────
        // Start a log row for this connection if the sermon tracks attendance
        private async Task LogAttendanceAsync(int sermonId)
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

        // ── Join the live chat room for a sermon ────────────────────
        public async Task JoinLiveChatRoom(int sermonId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"livechat-{sermonId}");
        }

        // ── Send a live chat message ────────────────────────────────
        [Authorize]
        public async Task SendLiveChat(int sermonId, string message, string type = "chat")
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Sanitise — strip HTML tags
            message = System.Text.RegularExpressions.Regex.Replace(message, "<[^>]+>", "").Trim();
            if (message.Length > 400) message = message.Substring(0, 400);

            // Get sender details from DB
            var db = Context.GetHttpContext()!.RequestServices
                                .GetRequiredService<ApplicationDbContext>();
            var userMgr = Context.GetHttpContext()!.RequestServices
                                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userMgr.GetUserAsync(Context.GetHttpContext()!.User);
            var member = user == null ? null
                           : await db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);

            var senderName = member?.FullName ?? user?.UserName ?? "Member";
            var photo = member?.ProfilePhoto ?? "/images/avatar-default.png";
            var sentAt = DateTime.UtcNow.ToString("h:mm tt");
            var connId = Context.ConnectionId;

            // Broadcast to everyone in the group
            await Clients.Group($"livechat-{sermonId}").SendAsync("LiveChatMessage", new
            {
                senderName,
                message,
                type,
                sentAt,
                photo,
                connectionId = connId,
                isOwn = false    // receiver-side always false; JS checks connectionId
            });

            // Send back to sender with isOwn=true
            await Clients.Caller.SendAsync("LiveChatMessage", new
            {
                senderName,
                message,
                type,
                sentAt,
                photo,
                connectionId = connId,
                isOwn = true
            });
        }

        // ── Admin: pause/unpause chat ───────────────────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task PauseLiveChat(int sermonId, bool paused)
        {
            await Clients.Group($"livechat-{sermonId}").SendAsync("LiveChatPaused", paused);
        }

        // ── Admin: clear all chat messages ─────────────────────────
        [Authorize(Roles = "Admin,Pastor")]
        public async Task ClearLiveChat(int sermonId)
        {
            await Clients.Group($"livechat-{sermonId}").SendAsync("LiveChatCleared");
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
}
