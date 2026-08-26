using ChurchMinistry.Data;
using ChurchMinistry.Models;
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

        public MeetingHub(ApplicationDbContext db, UserManager<ApplicationUser> um)
        {
            _db = db; _userManager = um;
        }

        // ── Join meeting room ──────────────────────────────────
        [Authorize]
        public async Task JoinRoom(string roomCode, string displayName)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.Participants)
                .FirstOrDefaultAsync(r => r.RoomCode == roomCode);
            if (room == null) return;

            var user = await _userManager.GetUserAsync(Context.User!);
            var member = user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);

            // Admin and Pastor always count as host — they bypass waiting room
            // and have full host controls regardless of who created the room
            var isAdminPastor = await IsAdminOrPastorAsync();
            var isHostMember  = room.HostMemberId == member?.Id
                             || room.HostUserId   == user?.Id;
            var isEffectiveHost = isHostMember || isAdminPastor;

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
            }
            else
            {
                participant.ConnectionId = Context.ConnectionId;
                participant.LeftAt       = null;
                // Fix IsHost if they re-joined and their role wasn't set correctly before
                if (isEffectiveHost) participant.IsHost = true;
                await _db.SaveChangesAsync();
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

            // Tell everyone else a new peer arrived
            await Clients.OthersInGroup(roomCode).SendAsync("PeerJoined", new
            {
                connectionId  = Context.ConnectionId,
                displayName   = participant.DisplayName,
                isHost        = participant.IsHost,
                isAdminPastor = participant.IsHost || isAdminPastor,
                memberId      = participant.MemberId,
                photo         = member?.ProfilePhoto ?? "/images/avatar-default.png"
            });

            // Send current participant list to the new joiner
            var activeParticipants = await _db.MeetingParticipants
                .Include(p => p.Member)
                .Where(p => p.MeetingRoomId == room.Id && p.LeftAt == null && p.IsAdmitted)
                .Select(p => new {
                    connectionId  = p.ConnectionId,
                    displayName   = p.DisplayName,
                    isHost        = p.IsHost,
                    isAdminPastor = p.IsHost,   // host flag doubles as admin indicator for clients
                    isMuted       = p.IsMuted,
                    isVideoOff    = p.IsVideoOff,
                    isHandRaised  = p.IsHandRaised,
                    photo         = p.Member != null
                        ? p.Member.ProfilePhoto ?? "/images/avatar-default.png"
                        : "/images/avatar-default.png"
                }).ToListAsync();

            await Clients.Caller.SendAsync("RoomJoined", new
            {
                roomCode,
                participants    = activeParticipants,
                isInWaitingRoom = participant.IsInWaitingRoom,
                chatEnabled     = room.ChatEnabled,
                isHost          = participant.IsHost,
                isAdminPastor   = participant.IsHost || isAdminPastor
            });

            // System message
            await Clients.Group(roomCode).SendAsync("SystemMessage",
                $"{participant.DisplayName} joined the meeting");

            // Notify host if waiting room
            if (participant.IsInWaitingRoom)
            {
                var hostParticipant = await _db.MeetingParticipants
                    .FirstOrDefaultAsync(p => p.MeetingRoomId == room.Id && p.IsHost && p.LeftAt == null);
                if (hostParticipant?.ConnectionId != null)
                    await Clients.Client(hostParticipant.ConnectionId).SendAsync("WaitingRoomRequest", new
                    {
                        connectionId  = Context.ConnectionId,
                        displayName   = participant.DisplayName,
                        participantId = participant.Id
                    });
            }

            // Tell this new participant to send video offers to any existing Live page viewers
            await PushOffersToExistingViewers(roomCode);

            // Update viewer count for everyone
            await BroadcastViewerCount(roomCode);
        }

        // ── WebRTC signalling ──────────────────────────────────
        [Authorize]
        public async Task SendOffer(string targetConnectionId, object offer)
            => await Clients.Client(targetConnectionId).SendAsync("ReceiveOffer",
                Context.ConnectionId, offer);

        //[Authorize]
        //public async Task SendAnswer(string targetConnectionId, object answer)
        //    => await Clients.Client(targetConnectionId).SendAsync("ReceiveAnswer",
        //        Context.ConnectionId, answer);

        //[AllowAnonymous]
        //public async Task SendIceCandidate(string targetConnectionId, object candidate)
        //    => await Clients.Client(targetConnectionId).SendAsync("ReceiveIceCandidate",
        //        Context.ConnectionId, candidate);

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
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId);
            if (p != null) { p.IsHandRaised = isRaised; await _db.SaveChangesAsync(); }
            await Clients.Group(roomCode).SendAsync("HandRaised",
                Context.ConnectionId, isRaised, p?.DisplayName ?? "Someone");
        }

        // ── Chat ───────────────────────────────────────────────
        [Authorize]
        public async Task SendMessage(string roomCode, string message, string type = "chat")
        {
            var p = await _db.MeetingParticipants
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId);
            if (p == null) return;

            var msg = new MeetingMessage
            {
                MeetingRoomId = p.MeetingRoomId,
                MemberId = p.MemberId,
                SenderName = p.DisplayName,
                Message = message,
                Type = type
            };
            _db.MeetingMessages.Add(msg);
            await _db.SaveChangesAsync();

            await Clients.Group(roomCode).SendAsync("ReceiveMessage", new
            {
                id = msg.Id,
                senderName = msg.SenderName,
                message = msg.Message,
                type = msg.Type,
                sentAt = msg.SentAt.ToString("HH:mm"),
                connectionId = Context.ConnectionId
            });
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
            var p = await _db.MeetingParticipants.FindAsync(participantId);
            if (p == null) return;
            p.IsInWaitingRoom = false;
            p.IsAdmitted = true;
            await _db.SaveChangesAsync();
            if (p.ConnectionId != null)
                await Clients.Client(p.ConnectionId).SendAsync("Admitted");
            await Clients.Group(roomCode).SendAsync("SystemMessage",
                $"{p.DisplayName} was admitted");
        }

        [Authorize]
        public async Task RemoveParticipant(string roomCode, string targetConnectionId)
        {
            await Clients.Client(targetConnectionId).SendAsync("Removed");
            await Clients.Group(roomCode).SendAsync("PeerLeft", targetConnectionId);
        }

        [Authorize]
        public async Task MuteAll(string roomCode)
            => await Clients.OthersInGroup(roomCode).SendAsync("ForceMuted");

        [Authorize]
        public async Task EndMeeting(string roomCode)
        {
            var room = await _db.MeetingRooms.FirstOrDefaultAsync(r => r.RoomCode == roomCode);
            if (room != null)
            {
                room.Status = RoomStatus.Ended;
                room.EndedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            await Clients.Group(roomCode).SendAsync("MeetingEnded");
        }

        // ── Admin/Pastor sets a participant in the spotlight ────────
        // Broadcasts to all participants in the room so everyone
        // sees the same person spotlighted automatically.
        [Authorize(Roles = "Admin,Pastor")]
        public async Task SetSpotlight(string roomCode, string connectionId)
        {
            var spotId = string.IsNullOrEmpty(connectionId) ? null : connectionId;

            // Broadcast to meeting participants in Room.cshtml
            await Clients.Group(roomCode).SendAsync("SpotlightChanged", spotId);

            // Also broadcast to Live.cshtml viewers watching this room
            await Clients.Group($"{roomCode}-viewers").SendAsync("SpotlightChanged", spotId);
        }

        public async Task ClearSpotlight(string roomCode)
        {
            await Clients.Group(roomCode).SendAsync("SpotlightChanged", null);
            await Clients.Group($"{roomCode}-viewers").SendAsync("SpotlightChanged", null);
        }


        // ── Leave ─────────────────────────────────────────────
        [Authorize]
        public async Task LeaveRoom(string roomCode)
        {
            await HandleDisconnect(roomCode);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);
        }

        [Authorize]
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var p = await _db.MeetingParticipants
                .Include(x => x.MeetingRoom)
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId && x.LeftAt == null);

            if (p != null)
                await HandleDisconnect(p.MeetingRoom.RoomCode);

            await base.OnDisconnectedAsync(exception);
        }

        [Authorize]
        private async Task HandleDisconnect(string roomCode)
        {
            var p = await _db.MeetingParticipants
                .FirstOrDefaultAsync(x => x.ConnectionId == Context.ConnectionId && x.LeftAt == null);
            if (p != null)
            {
                p.LeftAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                // Include isHost so Live page knows if host left and should show "Starting Soon"
                await Clients.OthersInGroup(roomCode).SendAsync("PeerLeft", Context.ConnectionId, p.IsHost);
                await Clients.Group(roomCode).SendAsync("SystemMessage",
                    $"{p.DisplayName} left the meeting");
                await BroadcastViewerCount(roomCode);
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

        private async Task<int> GetRoomViewerCount(string roomCode)
        {
            return await _db.MeetingParticipants
                .CountAsync(p => p.MeetingRoom.RoomCode == roomCode && p.LeftAt == null);
        }

        // ── In-memory viewer tracking (per room) ───────────────────
        // Stores viewer connectionIds so participants who join AFTER
        // a guest can still send them offers
        private static readonly ConcurrentDictionary<string, HashSet<string>> _roomViewers
            = new ConcurrentDictionary<string, HashSet<string>>();

        // ── Guest calls this to join as a viewer ───────────────────
        [AllowAnonymous]
        public async Task JoinAsViewer(string roomCode, string displayName)
        {
            // Add to separate viewers group — NOT the main room group
            // This prevents NewViewerJoined from looping back to the viewer
            await Groups.AddToGroupAsync(Context.ConnectionId, $"{roomCode}-viewers");

            // Track this viewer so late-joining participants can also send them offers
            var viewers = _roomViewers.GetOrAdd(roomCode, _ => new HashSet<string>());
            lock (viewers) { viewers.Add(Context.ConnectionId); }

            // Load current room and its active participants
            var room = await _db.MeetingRooms
                .Include(r => r.Participants.Where(p => p.LeftAt == null && !p.IsInWaitingRoom))
                .ThenInclude(p => p.Member)
                .FirstOrDefaultAsync(r => r.RoomCode == roomCode);

            if (room == null) return;

            // Tell all current PARTICIPANTS (main room group) to send video offers to this viewer
            // The viewer is in {roomCode}-viewers group, NOT roomCode, so they won't receive this
            await Clients.Group(roomCode).SendAsync("NewViewerJoined", new
            {
                viewerConnectionId = Context.ConnectionId,
                displayName        = displayName,
                isGuest            = !Context.User?.Identity?.IsAuthenticated ?? true
            });

            // Send viewer the current participant list including isHost flag
            // so the client knows whether the host is already present
            var participantList = room.Participants
                .Where(p => p.LeftAt == null && !p.IsInWaitingRoom)
                .Select(p => new {
                    connectionId  = p.ConnectionId ?? "",
                    displayName   = p.DisplayName,
                    photo         = p.Member?.ProfilePhoto ?? "/images/avatar-default.png",
                    isHost        = p.IsHost,
                    isAdminPastor = p.IsHost   // IsHost covers host + admin for client purposes
                }).ToList();

            await Clients.Caller.SendAsync("RoomJoined", new
            {
                participants    = participantList,
                isInWaitingRoom = false,
                viewerMode      = true
            });

            await BroadcastViewerCount(roomCode);
        }

        // ── Viewer leaves ──────────────────────────────────────────
        [AllowAnonymous]
        public async Task LeaveAsViewer(string roomCode)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"{roomCode}-viewers");

            if (_roomViewers.TryGetValue(roomCode, out var viewers))
                lock (viewers) { viewers.Remove(Context.ConnectionId); }

            await BroadcastViewerCount(roomCode);
        }

        // ── Push offers to existing viewers when a new participant joins
        // Add this call at the END of your JoinRoom success path:
        //   await PushOffersToExistingViewers(roomCode);
        public async Task PushOffersToExistingViewers(string roomCode)
        {
            if (!_roomViewers.TryGetValue(roomCode, out var viewers)) return;

            List<string> viewerList;
            lock (viewers) { viewerList = viewers.ToList(); }

            // Tell the newly joined participant to send offers to every current viewer
            foreach (var viewerConnId in viewerList)
            {
                await Clients.Caller.SendAsync("NewViewerJoined", new
                {
                    viewerConnectionId = viewerConnId,
                    displayName = "Viewer",
                    isGuest = true
                });
            }
        }

        // ── Guest retry — called if no video after timeout ─────────
        [AllowAnonymous]
        public async Task RequestVideoFromParticipants(string roomCode)
        {
            await Clients.Group(roomCode).SendAsync("NewViewerJoined", new
            {
                viewerConnectionId = Context.ConnectionId,
                displayName = "Viewer",
                isGuest = true
            });
        }

        // ── WebRTC answer/ICE must be anonymous so guests can complete handshake
        [AllowAnonymous]
        public async Task SendAnswer(string targetConnectionId, object answer)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveAnswer",
                Context.ConnectionId, answer);
        }

        [AllowAnonymous]
        public async Task SendIceCandidate(string targetConnectionId, object candidate)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveIceCandidate",
                Context.ConnectionId, candidate);
        }

        // ── Viewer count helper ────────────────────────────────────
        private async Task BroadcastViewerCount(string roomCode)
        {
            int participants = await _db.MeetingParticipants
                .CountAsync(p => p.MeetingRoom.RoomCode == roomCode && p.LeftAt == null);
            int viewers = _roomViewers.TryGetValue(roomCode, out var v) ? v.Count : 0;
            await Clients.Group(roomCode).SendAsync("ViewerCountUpdated", participants + viewers);
        }



    }
}
