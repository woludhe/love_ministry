using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsController(ApplicationDbContext db, UserManager<ApplicationUser> um)
        {
            _db = db; _userManager = um;
        }

        private async Task<Member?> GetMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        // Full notifications page
        public async Task<IActionResult> Index(int page = 1)
        {
            var member = await GetMemberAsync();
            if (member == null) return RedirectToAction("Login", "Account");

            var pager = new PagerModel(page, "notifications");
            var notifications = await _db.Notifications
                .Where(n => n.MemberId == member.Id)
                .OrderByDescending(n => n.CreatedAt)
                .ToPageAsync(pager);
            ViewBag.Pager = pager;

            // Mark all as read when page is viewed (not just this page)
            var unread = await _db.Notifications.Where(n => n.MemberId == member.Id && !n.IsRead).ToListAsync();
            unread.ForEach(n => n.IsRead = true);
            if (unread.Any()) await _db.SaveChangesAsync();

            return View(notifications);
        }

        // API: unread count + recent list for the navbar dropdown
        [HttpGet]
        public async Task<IActionResult> Recent()
        {
            var member = await GetMemberAsync();
            if (member == null) return Json(new { count = 0, items = Array.Empty<object>() });

            var items = await _db.Notifications
                .Where(n => n.MemberId == member.Id)
                .OrderByDescending(n => n.CreatedAt)
                .Take(8)
                .Select(n => new {
                    n.Id, n.Title, n.Message, n.Link, n.IsRead,
                    createdAt = n.CreatedAt.ToString("dd MMM, HH:mm")
                })
                .ToListAsync();

            int unreadCount = await _db.Notifications
                .CountAsync(n => n.MemberId == member.Id && !n.IsRead);

            return Json(new { count = unreadCount, items });
        }

        // Mark a single notification as read and redirect to its link.
        // GET because the navbar dropdown items are plain links.
        [HttpGet]
        public async Task<IActionResult> MarkRead(int id)
        {
            var member = await GetMemberAsync();
            if (member == null) return RedirectToAction(nameof(Index));

            var notification = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.MemberId == member.Id);

            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                await _db.SaveChangesAsync();
            }

            // Only follow links within the site; anything else lands on the notifications page
            var link = notification?.Link;
            return !string.IsNullOrEmpty(link) && Url.IsLocalUrl(link)
                ? LocalRedirect(link)
                : RedirectToAction(nameof(Index));
        }

        // Mark all as read (called from dropdown)
        [HttpPost]
        public async Task<IActionResult> MarkAllRead()
        {
            var member = await GetMemberAsync();
            if (member == null) return Unauthorized();

            await _db.Notifications
                .Where(n => n.MemberId == member.Id && !n.IsRead)
                .ForEachAsync(n => n.IsRead = true);

            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}
