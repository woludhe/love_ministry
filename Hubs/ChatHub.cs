using System.Collections.Concurrent;
using System.Security.Claims;
using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Hubs
{
    // Members Chat (/Community/Chat): four fixed rooms, messages saved so a room shows its recent history on join.
    // Member-layout pages also connect (without joining a room) to hear "ChatActivity" for the Live Chat menu tag.
    [Authorize]
    public class ChatHub : Hub
    {
        public static readonly string[] Rooms = { "general", "prayer", "youth", "announcements" };
        private const int HistorySize = 50;
        private const int MaxMessageLength = 1000;
        private const string ChatPageGroup = "chat-page";   // connections with the chat page open

        // Connection id → user id, for connections on the chat page (single server, so kept in memory)
        private static readonly ConcurrentDictionary<string, string> OnChatPage = new();

        public static bool IsOnChatPage(string userId) => OnChatPage.Values.Contains(userId);
        private static int OnlineCount => OnChatPage.Values.Distinct().Count();

        private readonly ApplicationDbContext _db;
        private readonly IActivityService _activity;

        public ChatHub(ApplicationDbContext db, IActivityService activity)
        {
            _db = db;
            _activity = activity;
        }

        private string? UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        // Joins the room and returns its last messages, oldest first
        public async Task<List<object>> JoinRoom(string room)
        {
            if (!Rooms.Contains(room)) throw new HubException("Unknown room.");
            await Groups.AddToGroupAsync(Context.ConnectionId, room);

            var userId = UserId;
            if (userId != null && OnChatPage.TryAdd(Context.ConnectionId, userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ChatPageGroup);
                await Clients.Group(ChatPageGroup).SendAsync("OnlineCount", OnlineCount);
            }
            await MarkSeenAsync(userId);

            var myId = await CurrentMemberIdAsync();
            var recent = await _db.ChatMessages.AsNoTracking()
                .Where(c => c.Room == room)
                .OrderByDescending(c => c.SentAt).ThenByDescending(c => c.Id)
                .Take(HistorySize)
                .Select(c => new { c.Id, c.MemberId, c.Member.FirstName, c.Member.LastName, c.Member.ProfilePhoto, c.Message, c.SentAt })
                .ToListAsync();

            return recent.AsEnumerable().Reverse()
                .Select(c => Payload(c.Id, $"{c.FirstName} {c.LastName}", c.ProfilePhoto, c.Message, c.SentAt, c.MemberId == myId))
                .ToList();
        }

        public async Task LeaveRoom(string room)
        {
            if (Rooms.Contains(room))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
        }

        public async Task SendMessage(string room, string message)
        {
            if (!Rooms.Contains(room)) throw new HubException("Unknown room.");
            message = (message ?? "").Trim();
            if (message.Length == 0) return;
            if (message.Length > MaxMessageLength) message = message[..MaxMessageLength];

            var userId = UserId;
            var member = userId == null ? null : await _db.Members.AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => new { m.Id, m.FirstName, m.LastName, m.ProfilePhoto })
                .FirstOrDefaultAsync();
            // Messages belong to a member profile; logins without one (e.g. the admin) can read but not post
            if (member == null) throw new HubException("Only members with a profile can post in the chat.");

            var chat = new ChatMessage
            {
                MemberId = member.Id,
                Message = message,
                Room = room,
                SentAt = DateTime.UtcNow
            };
            _db.ChatMessages.Add(chat);
            await _db.SaveChangesAsync();
            // Counted once per day: chatting is participation, not each message
            await _activity.LogAsync(member.Id, MemberActivityType.CommunityChat,
                $"Chatted in #{room}", oncePerDay: true);

            var name = $"{member.FirstName} {member.LastName}";
            await Clients.OthersInGroup(room).SendAsync("ReceiveMessage",
                Payload(chat.Id, name, member.ProfilePhoto, chat.Message, chat.SentAt, false));
            await Clients.Caller.SendAsync("ReceiveMessage",
                Payload(chat.Id, name, member.ProfilePhoto, chat.Message, chat.SentAt, true));
            // Live Chat menu tag on other pages, and unread dots on the other rooms' buttons
            await Clients.All.SendAsync("ChatActivity", new { room, memberId = member.Id });
        }

        // Admin and Pastor can remove any message; members only their own
        public async Task DeleteMessage(int id)
        {
            var chat = await _db.ChatMessages.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.Room, c.MemberId })
                .FirstOrDefaultAsync();
            if (chat == null) return;   // already removed

            var moderator = Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("Pastor") == true;
            if (!moderator && await CurrentMemberIdAsync() != chat.MemberId)
                throw new HubException("You can only delete your own messages.");

            await _db.ChatMessages.Where(c => c.Id == id).ExecuteDeleteAsync();
            if (chat.Room != null)
                await Clients.Group(chat.Room).SendAsync("MessageDeleted", id);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (OnChatPage.TryRemove(Context.ConnectionId, out var userId))
            {
                // Everything shown while the page was open has been seen
                await MarkSeenAsync(userId);
                await Clients.Group(ChatPageGroup).SendAsync("OnlineCount", OnlineCount);
            }
            await base.OnDisconnectedAsync(exception);
        }

        private Task MarkSeenAsync(string? userId) => userId == null ? Task.CompletedTask
            : _db.Members.Where(m => m.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChatLastSeenAt, DateTime.UtcNow));

        private async Task<int?> CurrentMemberIdAsync()
        {
            var userId = UserId;
            if (userId == null) return null;
            return await _db.Members.Where(m => m.UserId == userId).Select(m => (int?)m.Id).FirstOrDefaultAsync();
        }

        // sentAt is UTC ISO 8601 so the browser shows it in local time
        private static object Payload(int id, string senderName, string? photo, string message, DateTime sentAt, bool isOwn) => new
        {
            id,
            senderName,
            senderPhoto = photo ?? "/images/avatar-default.png",
            message,
            sentAt = DateTime.SpecifyKind(sentAt, DateTimeKind.Utc).ToString("o"),
            isOwn
        };
    }
}
