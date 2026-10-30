using System.Security.Claims;
using ChurchMinistry.Data;
using ChurchMinistry.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.ViewComponents
{
    // "New" tags on the member sidebar's Live Chat and Community links, and their shortcuts on the
    // phone "Dashboard Menu" bar: Members Chat messages and Community posts from others since the
    // member last opened each. The views are kept live by wwwroot/js/nav-unread.js over /chatHub.
    public class NavUnreadViewComponent : ViewComponent
    {
        private static readonly TimeSpan Lookback = TimeSpan.FromDays(7);   // never-visited members only see recent items as new

        private readonly ApplicationDbContext _db;
        private readonly CurrentMemberViewComponent _currentMember;

        public NavUnreadViewComponent(ApplicationDbContext db, CurrentMemberViewComponent currentMember)
        {
            _db = db; _currentMember = currentMember;
        }

        // place: "chat" or "community" (sidebar tag), "bar" (phone menu bar shortcuts)
        public async Task<IViewComponentResult> InvokeAsync(string place)
        {
            var memberId = (await _currentMember.GetModelAsync()).MemberId;
            if (memberId == null) return Content("");

            // Three places render on the same page, so count once per request
            if (HttpContext.Items[typeof(NavUnreadModel)] is not NavUnreadModel model)
            {
                var seen = await _db.Members.Where(m => m.Id == memberId)
                    .Select(m => new { m.ChatLastSeenAt, m.CommunityLastSeenAt }).FirstAsync();
                var floor = DateTime.UtcNow - Lookback;

                var chat = 0;
                var userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
                // Nothing is new while the chat is open (this page or another tab)
                if (!HttpContext.Request.Path.StartsWithSegments("/Community/Chat")
                    && (userId == null || !ChatHub.IsOnChatPage(userId)))
                {
                    var since = seen.ChatLastSeenAt > floor ? seen.ChatLastSeenAt.Value : floor;
                    chat = await _db.ChatMessages.CountAsync(c => c.SentAt > since && c.MemberId != memberId);
                }

                // Opening the feed (CommunityController.Index) moves CommunityLastSeenAt, so the feed itself shows 0
                var postsSince = seen.CommunityLastSeenAt > floor ? seen.CommunityLastSeenAt.Value : floor;
                var community = await _db.ForumPosts.CountAsync(p => p.CreatedAt > postsSince && p.IsApproved && p.MemberId != memberId);

                model = new NavUnreadModel(memberId.Value, chat, community);
                HttpContext.Items[typeof(NavUnreadModel)] = model;
            }

            return place switch
            {
                "bar" => View("Bar", model),
                "community" => View("Tag", new NavUnreadTag("communityUnread", "new community posts", model.CommunityCount)),
                _ => View("Tag", new NavUnreadTag("chatUnread", "new chat messages", model.ChatCount))
            };
        }
    }

    public record NavUnreadModel(int MemberId, int ChatCount, int CommunityCount);

    public record NavUnreadTag(string Id, string AriaLabel, int Count);

    public static class NavUnread
    {
        public static string Label(int count) => count > 99 ? "99+" : count.ToString();
    }
}
