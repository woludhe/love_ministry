using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace ChurchMinistry.Hubs
{
    //[Authorize]
    public class MeetingHub : Hub
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILiveKitService _liveKit;
        private readonly IAttendanceService _attendance;
        private readonly IMeetingAccessService _access;
        private readonly IMeetingSeriesService _series;
        private readonly IActivityService _activity;
        private readonly IHubContext<LiveSessionHub> _liveHub;
        private readonly IRecordingService _recordings;

        public MeetingHub(ApplicationDbContext db, UserManager<ApplicationUser> um, ILiveKitService liveKit,
            IAttendanceService attendance, IMeetingAccessService access, IMeetingSeriesService series,
            IActivityService activity, IHubContext<LiveSessionHub> liveHub, IRecordingService recordings)
        {
            _db = db; _userManager = um; _liveKit = liveKit; _attendance = attendance; _access = access;
            _series = series; _activity = activity; _liveHub = liveHub; _recordings = recordings;
        }

        // ── Join meeting room ──────────────────────────────────
        [Authorize]
        public async Task JoinRoom(string roomCode, string displayName)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.Participants)
                .FirstOrDefaultAsync(r => r.RoomCode == roomCode);
            if (room == null) return;

            // Private meetings: invited people (directly or through a group) only
            if (!await _access.CanAccessAsync(room, Context.User!))
            {
                await Clients.Caller.SendAsync("AccessDenied", "This is a private meeting. Only invited people can join.");
                return;
            }

            var user = await _userManager.GetUserAsync(Context.User!);
            var member = user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);

            // Admin and Pastor always count as host — they bypass waiting room
            // and have full host controls regardless of who created the room
            var isAdminPastor = await IsAdminOrPastorAsync();
            var isHostMember  = room.HostMemberId == member?.Id
                             || room.HostUserId   == user?.Id;
            var isEffectiveHost = isHostMember || isAdminPastor;

            // The name comes from the server where we know it: a member's own name, and "Admin"
            // (not the email) for the admin login, which has no member profile. A member's saved
            // name is their public one ("@alias" if they have one); staff are sent real names.
            if (member != null) displayName = member.PublicName;
            else if (Context.User!.IsInRole("Admin")) displayName = MeetingAvatar.AdminName;

            // Upsert participant
            var participant = room.Participants
                .FirstOrDefault(p => p.ConnectionId == Context.ConnectionId
                               || (member != null && p.MemberId == member.Id && p.LeftAt == null));

            if (participant == null)
            {
                participant = new MeetingParticipant
                {
                    MeetingRoomId   = room.Id,
                    MemberId        = member?.Id,
                    DisplayName     = displayName,
                    ConnectionId    = Context.ConnectionId,
                    IsHost          = isEffectiveHost,
                    // Admin/Pastor always admitted, skip waiting room
                    IsInWaitingRoom = room.WaitingRoomEnabled && !isEffectiveHost,
                    IsAdmitted      = !room.WaitingRoomEnabled || isEffectiveHost,
                };
                _db.MeetingParticipants.Add(participant);
                await _db.SaveChangesAsync();
                if (member != null)
                    await _activity.LogAsync(member.Id, MemberActivityType.MeetingJoined,
                        room.Title, room.Id, oncePerDay: true);
            }
            else
            {
                // Same member re-joining (SignalR reconnect or a second tab) — the old
                // connection is dead or superseded, so drop it from the room for everyone
                var oldConnectionId = participant.ConnectionId;
                var superseded = !string.IsNullOrEmpty(oldConnectionId) && oldConnectionId != Context.ConnectionId;

                // Save the new connection first. The old connection's disconnect may be handled
                // meanwhile and set LeftAt on this row, so LeftAt is always written (an unchanged
                // null wouldn't be) or the person would count as having left.
                participant.ConnectionId = Context.ConnectionId;
                participant.DisplayName  = displayName;
                participant.LeftAt       = null;
                _db.Entry(participant).Property(p => p.LeftAt).IsModified = true;
                // Fix IsHost if they re-joined and their role wasn't set correctly before
                if (isEffectiveHost) participant.IsHost = true;
                await _db.SaveChangesAsync();

                if (superseded)
                {
                    await HubAudience.LeaveAsync(Groups, oldConnectionId!, roomCode);
                    await Clients.Group(roomCode).SendAsync("PeerLeft", oldConnectionId, participant.IsHost);
                    await Clients.Group(ViewersGroup(roomCode)).SendAsync("PeerLeft", oldConnectionId, participant.IsHost);
                    await _liveKit.RemoveParticipantAsync(_liveKit.RoomName(roomCode), oldConnectionId!);
                }
            }

            // Split into staff and members, who are sent names differently (HubAudience)
            await HubAudience.JoinAsync(Context, Groups, roomCode);
            var staffName = member?.StaffName ?? participant.DisplayName;

            // People in the waiting room are announced only once admitted
            if (!participant.IsInWaitingRoom)
                await AnnounceJoinAsync(roomCode, participant, staffName, member?.ProfilePhoto, isAdminPastor);

            var forceMuted = !participant.IsHost && _muteOnEntry.ContainsKey(roomCode);
            if (forceMuted) { participant.IsMuted = true; await _db.SaveChangesAsync(); }

            await Clients.Caller.SendAsync("RoomJoined", new
            {
                roomCode,
                participants    = participant.IsInWaitingRoom
                                    ? new List<ParticipantDto>()
                                    : await GetActiveParticipantsAsync(room.Id, staff: Context.User.IsStaff()),
                isInWaitingRoom = participant.IsInWaitingRoom,
                chatEnabled     = room.ChatEnabled,
                isHost          = participant.IsHost,
                isAdminPastor   = participant.IsHost || isAdminPastor,
                forceMuted,
                spotlightId     = _spotlight.TryGetValue(roomCode, out var spot) ? spot : null
            });

            // Notify every host present if this person is waiting
            if (participant.IsInWaitingRoom)
            {
                var hosts = await _db.MeetingParticipants
                    .Where(p => p.MeetingRoomId == room.Id && p.IsHost && p.LeftAt == null && p.ConnectionId != null)
                    .Select(p => new { p.ConnectionId, p.MemberId })
                    .ToListAsync();
                // A member who hosts sees the alias; Admin/Pastor hosts see the real name
                var staffIds = await StaffMemberIdsAsync(hosts.Select(h => h.MemberId));
                foreach (var staffHost in new[] { true, false })
                {
                    var connections = hosts.Where(h => IsStaffParticipant(h.MemberId, staffIds) == staffHost)
                        .Select(h => h.ConnectionId!).ToList();
                    if (connections.Count > 0)
                        await Clients.Clients(connections).SendAsync("WaitingRoomRequest", new
                        {
                            connectionId  = Context.ConnectionId,
                            displayName   = staffHost ? staffName : participant.DisplayName,
                            participantId = participant.Id
                        });
                }
            }

            // A host arriving late must still see people who were already waiting
            if (participant.IsHost && room.WaitingRoomEnabled)
            {
                var waiting = await _db.MeetingParticipants
                    .Include(p => p.Member)
                    .Where(p => p.MeetingRoomId == room.Id && p.IsInWaitingRoom && p.LeftAt == null && p.ConnectionId != null)
                    .ToListAsync();
                foreach (var w in waiting)
                    await Clients.Caller.SendAsync("WaitingRoomRequest", new
                    {
                        connectionId  = w.ConnectionId,
                        displayName   = w.Member?.NameFor(Context.User) ?? w.DisplayName,
                        participantId = w.Id
                    });
            }

            // Update viewer count for everyone
            await BroadcastViewerCount(roomCode);
        }

        // ── Media (LiveKit) ────────────────────────────────────
        // Join token for the room's audio/video, requested after RoomJoined/Admitted.
        // Only an admitted participant on this connection gets one, so the waiting
        // room applies to media too. The LiveKit identity is the connection id, which
        // is what clients already use to find tiles, spotlight and mute targets.
        [Authorize]
        public async Task<object?> GetMediaToken(string roomCode)
        {
            var room = await _db.MeetingRooms.FirstOrDefaultAsync(r => r.RoomCode == roomCode);
            if (room == null || room.Status == RoomStatus.Ended || room.Status == RoomStatus.Cancelled)
                return null;

            var participant = await _db.MeetingParticipants
                .Include(p => p.Member)
                .FirstOrDefaultAsync(p => p.MeetingRoomId == room.Id && p.ConnectionId == Context.ConnectionId
                                       && p.LeftAt == null && p.IsAdmitted);
            if (participant == null || (!room.IsApproved && !participant.IsHost)) return null;
            if (!await _access.CanAccessAsync(room, Context.User!)) return null;

            var metadata = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                memberId = participant.MemberId,
                isHost   = participant.IsHost,
                photo    = MeetingAvatar.For(participant.MemberId, participant.Member?.ProfilePhoto, participant.IsHost)
            });
            var token = _liveKit.CreateJoinToken(_liveKit.RoomName(roomCode), Context.ConnectionId,
                participant.DisplayName, metadata, canPublish: true);
            return new { url = _liveKit.Url, token };
        }

        // ── Shared state for late joiners (single server instance) ─
        // Rooms where the host pressed "Mute all" — later joiners start muted
        private static readonly ConcurrentDictionary<string, bool> _muteOnEntry = new();
        // Current spotlighted connection id per room
        private static readonly ConcurrentDictionary<string, string> _spotlight = new();

        public record ParticipantDto(string? connectionId, string displayName, bool isHost,
            bool isAdminPastor, bool isMuted, bool isVideoOff, bool isHandRaised, string photo);

        // Admitted, present participants other than the caller (or the given connection).
        // staff: whether the person receiving the list sees real names.
        private async Task<List<ParticipantDto>> GetActiveParticipantsAsync(int roomId, string? excludeConnectionId = null,
            bool staff = false)
        {
            var exclude = excludeConnectionId ?? Context.ConnectionId;
            var rows = await _db.MeetingParticipants
                .Where(p => p.MeetingRoomId == roomId && p.LeftAt == null && p.IsAdmitted
                         && p.ConnectionId != exclude)
                .Select(p => new
                {
                    p.ConnectionId, p.DisplayName, p.IsHost, p.IsMuted, p.IsVideoOff, p.IsHandRaised,
                    First = p.Member != null ? p.Member.FirstName : null,
                    Last  = p.Member != null ? p.Member.LastName : null,
                    Alias = p.Member != null ? p.Member.Alias : null,
                    Photo = p.Member != null
                        ? p.Member.ProfilePhoto ?? MeetingAvatar.Default
                        : p.IsHost ? MeetingAvatar.SiteLogo : MeetingAvatar.Default
                })
                .ToListAsync();
            return rows.Select(p => new ParticipantDto(
                    p.ConnectionId,
                    p.First == null ? p.DisplayName : MemberNames.For(p.First, p.Last, p.Alias, staff),
                    p.IsHost,
                    p.IsHost,   // host flag doubles as admin indicator for clients
                    p.IsMuted,
                    p.IsVideoOff,
                    p.IsHandRaised,
                    p.Photo))
                .ToList();
        }

        // Tells the room (and Live page viewers) someone came in. staffName is what Admins
        // and Pastors see; everyone else gets participant.DisplayName (the public name).
        private async Task AnnounceJoinAsync(string roomCode, MeetingParticipant participant, string staffName,
            string? photo, bool isAdminPastor, string? connectionId = null)
        {
            var cid = connectionId ?? Context.ConnectionId;
            object Joined(bool staff) => new
            {
                connectionId  = cid,
                displayName   = staff ? staffName : participant.DisplayName,
                isHost        = participant.IsHost,
                isAdminPastor = participant.IsHost || isAdminPastor,
                memberId      = participant.MemberId,
                isMuted       = participant.IsMuted,
                isVideoOff    = participant.IsVideoOff,
                photo         = photo ?? MeetingAvatar.For(participant.MemberId, null, participant.IsHost)
            };
            await HubAudience.SendAsync(Clients, roomCode, "PeerJoined", s => new[] { Joined(s) }, cid);
            await HubAudience.SendAsync(Clients, ViewersGroup(roomCode), "PeerJoined", s => new[] { Joined(s) });
            await HubAudience.SendAsync(Clients, roomCode, "SystemMessage",
                s => new object?[] { $"{(s ? staffName : participant.DisplayName)} joined the meeting" });
        }

        // Which of these members are Admins or Pastors
        private async Task<HashSet<int>> StaffMemberIdsAsync(IEnumerable<int?> memberIds)
        {
            var ids = memberIds.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            return (await _db.Members
                .Where(m => ids.Contains(m.Id) && m.UserId != null
                    && _db.UserRoles.Any(ur => ur.UserId == m.UserId
                        && _db.Roles.Any(r => r.Id == ur.RoleId && (r.Name == "Admin" || r.Name == "Pastor"))))
                .Select(m => m.Id).ToListAsync()).ToHashSet();
        }
        // A participant without a member profile who is in the meeting is the admin login
        private static bool IsStaffParticipant(int? memberId, HashSet<int> staffIds)
            => memberId == null || staffIds.Contains(memberId.Value);

        // Room host (creator), or any Admin/Pastor
        private async Task<bool> IsRoomHostAsync(string roomCode)
        {
            if (await IsAdminOrPastorAsync()) return true;
            var user = await _userManager.GetUserAsync(Context.User!);
            if (user == null) return false;
            var room = await _db.MeetingRooms.FirstOrDefaultAsync(r => r.RoomCode == roomCode);
            if (room == null) return false;
            if (room.HostUserId == user.Id) return true;
            var member = await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
            return member != null && room.HostMemberId == member.Id;
        }

        // ── Media state ────────────────────────────────────────
        [Authorize]
        public async Task ToggleMute(string roomCode, bool isMuted)
        {
            var p = await _db.MeetingParticipants
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId);
            if (p != null) { p.IsMuted = isMuted; await _db.SaveChangesAsync(); }
            await Clients.Group(roomCode).SendAsync("ParticipantMuted",
                Context.ConnectionId, isMuted);
        }

        [Authorize]
        public async Task ToggleVideo(string roomCode, bool isVideoOff)
        {
            var p = await _db.MeetingParticipants
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId);
            if (p != null) { p.IsVideoOff = isVideoOff; await _db.SaveChangesAsync(); }
            await Clients.Group(roomCode).SendAsync("ParticipantVideoToggled",
                Context.ConnectionId, isVideoOff);
        }

        [Authorize]
        public async Task RaiseHand(string roomCode, bool isRaised)
        {
            var p = await _db.MeetingParticipants
                .Include(x => x.Member)
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId);
            if (p != null) { p.IsHandRaised = isRaised; await _db.SaveChangesAsync(); }
            await HubAudience.SendAsync(Clients, roomCode, "HandRaised", staff => new object?[] {
                Context.ConnectionId, isRaised,
                (staff ? p?.Member?.StaffName : null) ?? p?.DisplayName ?? "Someone" });
        }

        // ── Chat ───────────────────────────────────────────────
        [Authorize]
        public async Task SendMessage(string roomCode, string message, string type = "chat")
        {
            var p = await _db.MeetingParticipants
                .Include(x => x.MeetingRoom)
                .Include(x => x.Member)
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId);
            if (p == null || p.IsInWaitingRoom || string.IsNullOrWhiteSpace(message)) return;
            if (type != "prayer") type = "chat";
            // The room this connection is actually in, not whatever code the client sent
            roomCode = p.MeetingRoom.RoomCode;

            var msg = new MeetingMessage
            {
                MeetingRoomId = p.MeetingRoomId,
                MemberId = p.MemberId,
                SenderName = p.DisplayName,
                Message = message.Trim(),
                Type = type
            };
            _db.MeetingMessages.Add(msg);
            await _db.SaveChangesAsync();

            var photo = p.MemberId == null ? (p.IsHost ? MeetingAvatar.SiteLogo : null) : p.Member?.ProfilePhoto;
            var staffName = p.Member?.StaffName ?? msg.SenderName;
            var item = new SermonChat.Item(msg.SenderName, staffName, msg.Message, msg.Type, msg.SentAt,
                photo, SermonChat.FromMeeting, null, msg.MemberId);

            // Room members find their own message by connectionId; staff see the real name
            await HubAudience.SendAsync(Clients, roomCode, "ReceiveMessage", staff => new object?[] { new
            {
                id = msg.Id,
                senderName = staff ? staffName : msg.SenderName,
                message = msg.Message,
                type = msg.Type,
                sentAt = DateTime.SpecifyKind(msg.SentAt, DateTimeKind.Utc).ToString("o"),
                source = SermonChat.FromMeeting,
                connectionId = Context.ConnectionId
            } });

            // Same conversation on the Live page of the sermon this room belongs to
            if (await SermonChat.SermonForRoomAsync(_db, roomCode) is int sermonId)
                await SermonChat.SendAsync(_liveHub.Clients, $"livechat-{sermonId}", "LiveChatMessage", item);
        }

        // Chat so far, for someone entering the room: the room's messages, plus the Live page's
        // when the room belongs to a sermon. Admitted participants only.
        [Authorize]
        public async Task<object> GetChat(string roomCode)
        {
            var p = await _db.MeetingParticipants
                .Include(x => x.MeetingRoom)
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId && x.LeftAt == null);
            if (p == null || p.IsInWaitingRoom || p.MeetingRoom.RoomCode != roomCode) return Array.Empty<object>();

            var userId = _userManager.GetUserId(Context.User!);
            var memberId = p.MemberId ?? await _db.Members.Where(m => m.UserId == userId)
                .Select(m => (int?)m.Id).FirstOrDefaultAsync();
            var sermonId = await SermonChat.SermonForRoomAsync(_db, roomCode);
            var items = await SermonChat.LoadAsync(_db, sermonId, p.MeetingRoomId);
            return items.Select(i => SermonChat.Payload(i, i.IsFrom(userId, memberId), Context.User.IsStaff()));
        }

        // ── Reactions ──────────────────────────────────────────
        [Authorize]
        public async Task SendReaction(string roomCode, string emoji)
            => await Clients.Group(roomCode).SendAsync("Reaction", Context.ConnectionId, emoji);

        // ── Screen share ───────────────────────────────────────
        [Authorize]
        public async Task StartScreenShare(string roomCode)
            => await Clients.OthersInGroup(roomCode).SendAsync("ScreenShareStarted", Context.ConnectionId);

        [Authorize]
        public async Task StopScreenShare(string roomCode)
            => await Clients.OthersInGroup(roomCode).SendAsync("ScreenShareStopped", Context.ConnectionId);

        // ── Host controls ──────────────────────────────────────
        [Authorize]
        public async Task AdmitParticipant(string roomCode, int participantId)
        {
            if (!await IsRoomHostAsync(roomCode)) return;
            var p = await _db.MeetingParticipants
                .Include(x => x.Member)
                .Include(x => x.MeetingRoom)
                .FirstOrDefaultAsync(x => x.Id == participantId);
            if (p == null || p.MeetingRoom.RoomCode != roomCode) return;
            p.IsInWaitingRoom = false;
            p.IsAdmitted = true;
            if (_muteOnEntry.ContainsKey(roomCode)) p.IsMuted = true;
            await _db.SaveChangesAsync();
            if (p.ConnectionId == null) return;

            // Announce to the room now that they're in, then give the admitted
            // person the participant list so they can connect to everyone
            await AnnounceJoinAsync(roomCode, p, p.Member?.StaffName ?? p.DisplayName, p.Member?.ProfilePhoto, false, p.ConnectionId);
            // The list goes to the admitted person: real names only if they are staff
            var admittedIsStaff = IsStaffParticipant(p.MemberId, await StaffMemberIdsAsync(new[] { p.MemberId }));
            var others = await GetActiveParticipantsAsync(p.MeetingRoomId, p.ConnectionId, admittedIsStaff);
            await Clients.Client(p.ConnectionId).SendAsync("Admitted", new
            {
                participants = others,
                forceMuted   = _muteOnEntry.ContainsKey(roomCode),
                spotlightId  = _spotlight.TryGetValue(roomCode, out var spot) ? spot : null
            });
            await BroadcastViewerCount(roomCode);
        }

        [Authorize]
        public async Task RemoveParticipant(string roomCode, string targetConnectionId)
        {
            if (!await IsRoomHostAsync(roomCode)) return;
            var p = await _db.MeetingParticipants
                .FirstOrDefaultAsync(x => x.ConnectionId == targetConnectionId && x.LeftAt == null);
            if (p != null) { p.LeftAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
            await Clients.Client(targetConnectionId).SendAsync("Removed");
            // Cut their audio/video on the media server too, whatever their page does
            await _liveKit.RemoveParticipantAsync(_liveKit.RoomName(roomCode), targetConnectionId);
            await HubAudience.LeaveAsync(Groups, targetConnectionId, roomCode);
            await Clients.Group(roomCode).SendAsync("PeerLeft", targetConnectionId, p?.IsHost ?? false);
            await Clients.Group(ViewersGroup(roomCode)).SendAsync("PeerLeft", targetConnectionId, p?.IsHost ?? false);
            await ClearSpotlightIfAsync(roomCode, targetConnectionId);
            await BroadcastViewerCount(roomCode);
        }

        // Mutes every non-host present, and everyone who joins afterwards.
        // Participants can still unmute themselves.
        [Authorize]
        public async Task MuteAll(string roomCode)
        {
            if (!await IsRoomHostAsync(roomCode)) return;
            var targets = await _db.MeetingParticipants
                .Where(p => p.MeetingRoom.RoomCode == roomCode && p.LeftAt == null
                         && !p.IsHost && p.ConnectionId != null && p.ConnectionId != Context.ConnectionId)
                .ToListAsync();
            foreach (var t in targets) t.IsMuted = true;
            await _db.SaveChangesAsync();
            _muteOnEntry[roomCode] = true;
            if (targets.Count > 0)
                await Clients.Clients(targets.Select(t => t.ConnectionId!).ToList()).SendAsync("ForceMuted");
            // The media server mutes them as well, so it happens even if a page ignores the message
            await Task.WhenAll(targets.Select(t =>
                _liveKit.MuteMicrophoneAsync(_liveKit.RoomName(roomCode), t.ConnectionId!)));
        }

        [Authorize]
        public async Task ForceMute(string roomCode, string targetConnectionId)
        {
            if (!await IsRoomHostAsync(roomCode)) return;
            await Clients.Client(targetConnectionId).SendAsync("ForceMuted");
            await _liveKit.MuteMicrophoneAsync(_liveKit.RoomName(roomCode), targetConnectionId);
        }

        [Authorize]
        public async Task EndMeeting(string roomCode)
        {
            if (!await IsRoomHostAsync(roomCode)) return;
            var room = await _db.MeetingRooms.FirstOrDefaultAsync(r => r.RoomCode == roomCode);
            if (room != null)
            {
                room.Status = RoomStatus.Ended;
                room.EndedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // End the attendance of everyone still in the meeting or watching it live
                var sermonIds = await _db.Sermons
                    .Where(s => s.MeetingRoomCode == roomCode)
                    .Select(s => s.Id).ToListAsync();
                await _attendance.CloseSermonAttendanceAsync(sermonIds);

                // What was recorded becomes the meeting's recording (and its sermon's)
                _recordings.QueueFinalize(room.Id);

                // Recurring: next week's meeting (its own room code) is created now
                await _series.CreateNextAsync(room);
            }
            _recorders.TryRemove(roomCode, out _);
            _muteOnEntry.TryRemove(roomCode, out _);
            _spotlight.TryRemove(roomCode, out _);
            await Clients.Group(roomCode).SendAsync("MeetingEnded");
            await Clients.Group(ViewersGroup(roomCode)).SendAsync("MeetingEnded");
            // Closing the media room disconnects everyone's audio/video
            await _liveKit.DeleteRoomAsync(_liveKit.RoomName(roomCode));
        }

        // ── Host sets a participant in the spotlight ────────
        // Broadcasts to all participants in the room so everyone
        // sees the same person spotlighted automatically.
        [Authorize]
        public async Task SetSpotlight(string roomCode, string connectionId)
        {
            if (!await IsRoomHostAsync(roomCode)) return;
            var spotId = string.IsNullOrEmpty(connectionId) ? null : connectionId;
            if (spotId == null) _spotlight.TryRemove(roomCode, out _);
            else _spotlight[roomCode] = spotId;

            // Broadcast to meeting participants in Room.cshtml
            await Clients.Group(roomCode).SendAsync("SpotlightChanged", spotId);

            // Also broadcast to Live.cshtml viewers watching this room
            await Clients.Group(ViewersGroup(roomCode)).SendAsync("SpotlightChanged", spotId);
        }

        [Authorize]
        public async Task ClearSpotlight(string roomCode)
            => await SetSpotlight(roomCode, "");

        // Clears the spotlight when the spotlighted connection leaves
        private async Task ClearSpotlightIfAsync(string roomCode, string connectionId)
        {
            if (!_spotlight.TryRemove(new KeyValuePair<string, string>(roomCode, connectionId))) return;
            await Clients.Group(roomCode).SendAsync("SpotlightChanged", null);
            await Clients.Group(ViewersGroup(roomCode)).SendAsync("SpotlightChanged", null);
        }


        // ── Leave ─────────────────────────────────────────────
        [Authorize]
        public async Task LeaveRoom(string roomCode)
        {
            var wasRecording = _recorders.TryGetValue(roomCode, out var holder) && holder == Context.ConnectionId;
            await HandleDisconnect(roomCode);
            await HubAudience.LeaveAsync(Context, Groups, roomCode);

            // The recording host left: save what was recorded, unless a host has taken over
            // (or come back) by then. A dropped connection doesn't do this: that page may
            // reconnect and carry on recording.
            if (wasRecording)
            {
                var roomId = await _db.MeetingRooms.Where(r => r.RoomCode == roomCode)
                    .Select(r => (int?)r.Id).FirstOrDefaultAsync();
                if (roomId != null)
                    _recordings.QueueFinalize(roomId.Value, () => _recorders.ContainsKey(roomCode));
            }
        }

        [Authorize]
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var p = await _db.MeetingParticipants
                .Include(x => x.MeetingRoom)
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId && x.LeftAt == null);

            if (p != null)
                await HandleDisconnect(p.MeetingRoom.RoomCode);

            // A Live page viewer who closed the tab without LeaveAsViewer
            foreach (var roomCode in _roomViewers.Keys)
                if (RemoveViewer(roomCode, Context.ConnectionId)) await BroadcastViewerCount(roomCode);

            // A recording host whose participant row was already closed
            foreach (var (roomCode, holder) in _recorders)
                if (holder == Context.ConnectionId) await ReleaseRecorderAsync(roomCode);

            await base.OnDisconnectedAsync(exception);
        }

        // ── Recording ─────────────────────────────────────────
        // One host's page records the meeting's audio when the meeting is set to record
        // (several hosts may be in the room: Admins and Pastors count as hosts too).
        // Room code → the recording connection.
        private static readonly ConcurrentDictionary<string, string> _recorders = new();

        // True when this connection may record: nobody else is recording the room
        [Authorize]
        public async Task<bool> ClaimRecorder(string roomCode)
        {
            if (!await IsRoomHostAsync(roomCode)) return false;
            var holder = _recorders.GetOrAdd(roomCode, Context.ConnectionId);
            return holder == Context.ConnectionId;
        }

        // The recording host left: another host's page can take over
        private async Task ReleaseRecorderAsync(string roomCode)
        {
            if (_recorders.TryRemove(new KeyValuePair<string, string>(roomCode, Context.ConnectionId)))
                await Clients.OthersInGroup(roomCode).SendAsync("RecorderFree");
        }

        [Authorize]
        private async Task HandleDisconnect(string roomCode)
        {
            await ReleaseRecorderAsync(roomCode);
            var p = await _db.MeetingParticipants.AsNoTracking()
                .Include(x => x.Member)
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId && x.LeftAt == null);
            // Only if the row still belongs to this connection: the same person may have just
            // rejoined on a new connection, which takes the row over
            var left = p == null ? 0 : await _db.MeetingParticipants
                .Where(x => x.Id == p.Id && x.ConnectionId == Context.ConnectionId && x.LeftAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeftAt, DateTime.UtcNow));
            if (p != null && left > 0)
            {
                // Include isHost so Live page knows if host left and should show "Starting Soon"
                await Clients.OthersInGroup(roomCode).SendAsync("PeerLeft", Context.ConnectionId, p.IsHost);
                await Clients.Group(ViewersGroup(roomCode)).SendAsync("PeerLeft", Context.ConnectionId, p.IsHost);
                await HubAudience.SendAsync(Clients, roomCode, "SystemMessage", staff => new object?[] {
                    $"{(staff ? p.Member?.StaffName : null) ?? p.DisplayName} left the meeting" });
                await ClearSpotlightIfAsync(roomCode, Context.ConnectionId);
                await BroadcastViewerCount(roomCode);
                // The media room is left alone here: this also runs for a connection that is
                // only reconnecting, and a server-side removal would stop that person's camera
                // and microphone. A closed tab leaves the media room itself; a crashed one is
                // dropped by LiveKit after its timeout (its tile is already gone via PeerLeft).
            }
        }

        // ── Helper: check if current user is Admin or Pastor ───────
        private async Task<bool> IsAdminOrPastorAsync()
        {
            var user = await _userManager.GetUserAsync(Context.User!);
            if (user == null) return false;
            var roles = await _userManager.GetRolesAsync(user);
            return roles.Contains("Admin") || roles.Contains("Pastor");
        }

        // ── Live page viewers ──────────────────────────────────────
        // Viewer connection ids per room, for the viewer count
        private static readonly ConcurrentDictionary<string, HashSet<string>> _roomViewers
            = new ConcurrentDictionary<string, HashSet<string>>();

        // Viewers have their own group so meeting-only messages (chat, signalling of
        // host controls) don't reach them; who joins/leaves and the spotlight do.
        private static string ViewersGroup(string roomCode) => $"{roomCode}-viewers";

        // Live page (guests too) joins to watch. The viewer gets a watch-only, hidden
        // LiveKit token, so the media server sends them the meeting's audio/video
        // without them appearing in, or adding traffic to, the meeting itself.
        [AllowAnonymous]
        public async Task JoinAsViewer(string roomCode, string displayName)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.Participants.Where(p => p.LeftAt == null && !p.IsInWaitingRoom))
                .ThenInclude(p => p.Member)
                .FirstOrDefaultAsync(r => r.RoomCode == roomCode);
            if (room == null || room.Status == RoomStatus.Ended || room.Status == RoomStatus.Cancelled) return;

            // A private meeting can be watched only when an admin has linked it to a
            // published sermon (that is how the Live page streams it)
            if (!room.IsPublic && !await _db.Sermons.AnyAsync(s =>
                    s.IsOnlineMeeting && s.MeetingRoomCode == roomCode && s.IsPublished))
                return;

            // A separate group from the meeting's, so meeting-only messages don't reach viewers
            // (and its staff/members half: see HubAudience)
            await HubAudience.JoinAsync(Context, Groups, ViewersGroup(roomCode));
            var viewers = _roomViewers.GetOrAdd(roomCode, _ => new HashSet<string>());
            lock (viewers) { viewers.Add(Context.ConnectionId); }

            // Current participants, with isHost so the page knows whether the host is already here
            var participantList = room.Participants
                .Where(p => p.IsAdmitted)
                .Select(p => new {
                    connectionId  = p.ConnectionId ?? "",
                    displayName   = p.Member?.NameFor(Context.User) ?? p.DisplayName,
                    photo         = MeetingAvatar.For(p.MemberId, p.Member?.ProfilePhoto, p.IsHost),
                    isHost        = p.IsHost,
                    isAdminPastor = p.IsHost   // IsHost covers host + admin for client purposes
                }).ToList();

            var token = _liveKit.CreateJoinToken(_liveKit.RoomName(roomCode), $"viewer-{Context.ConnectionId}",
                string.IsNullOrWhiteSpace(displayName) ? "Viewer" : displayName, null,
                canPublish: false, hidden: true);

            await Clients.Caller.SendAsync("RoomJoined", new
            {
                participants    = participantList,
                isInWaitingRoom = false,
                viewerMode      = true,
                spotlightId     = _spotlight.TryGetValue(roomCode, out var spot) ? spot : null,
                media           = new { url = _liveKit.Url, token }
            });

            await BroadcastViewerCount(roomCode);
        }

        [AllowAnonymous]
        public async Task LeaveAsViewer(string roomCode)
        {
            await HubAudience.LeaveAsync(Context, Groups, ViewersGroup(roomCode));
            if (RemoveViewer(roomCode, Context.ConnectionId)) await BroadcastViewerCount(roomCode);
        }

        private static bool RemoveViewer(string roomCode, string connectionId)
        {
            if (!_roomViewers.TryGetValue(roomCode, out var viewers)) return false;
            lock (viewers) { return viewers.Remove(connectionId); }
        }

        // ── Viewer count helper ────────────────────────────────────
        private async Task BroadcastViewerCount(string roomCode)
        {
            int participants = await _db.MeetingParticipants
                .CountAsync(p => p.MeetingRoom.RoomCode == roomCode && p.LeftAt == null);
            int viewers = _roomViewers.TryGetValue(roomCode, out var v) ? v.Count : 0;
            // Live page viewers show the count too ("· N watching")
            await Clients.Groups(roomCode, ViewersGroup(roomCode)).SendAsync("ViewerCountUpdated", participants + viewers);
        }



    }
}
