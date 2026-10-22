using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace ChurchMinistry.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IActivityService _activity;

        public ChatHub(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IActivityService activity)
        {
            _db = db;
            _userManager = userManager;
            _activity = activity;
        }

        public async Task JoinRoom(string room)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, room);
            await Clients.Group(room).SendAsync("SystemMessage", $"{Context.User?.Identity?.Name} joined {room}");
        }

        public async Task LeaveRoom(string room)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
        }

        public async Task SendMessage(string room, string message)
        {
            var user = await _userManager.GetUserAsync(Context.User!);
            if (user?.Member == null) return;

            var chat = new ChatMessage
            {
                MemberId = user.Member.Id,
                Message = message,
                Room = room,
                SentAt = DateTime.UtcNow
            };
            _db.ChatMessages.Add(chat);
            await _db.SaveChangesAsync();
            // Counted once per day: chatting is participation, not each message
            await _activity.LogAsync(user.Member.Id, MemberActivityType.CommunityChat,
                $"Chatted in #{room}", oncePerDay: true);

            await Clients.Group(room).SendAsync("ReceiveMessage", new
            {
                id = chat.Id,
                senderName = user.FullName,
                senderPhoto = user.Member.ProfilePhoto ?? "/images/avatar-default.png",
                message = chat.Message,
                sentAt = chat.SentAt.ToString("HH:mm"),
                isOwn = false
            });
        }

        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "general");
            await base.OnConnectedAsync();
        }
    }
}
