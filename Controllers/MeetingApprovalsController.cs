using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    [Authorize(Roles = "Admin,Pastor")]
    public class MeetingApprovalsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notifications;

        public MeetingApprovalsController(ApplicationDbContext db,
            UserManager<ApplicationUser> um,
            INotificationService notifications)
        {
            _db = db;
            _userManager = um;
            _notifications = notifications;
        }

        // ── Approval queue ──────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var pending = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .Where(r => r.ApprovalStatus == ApprovalStatus.Pending)
                .OrderBy(r => r.ScheduledAt)
                .ToListAsync();

            var recent = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .Where(r => r.ApprovalStatus != ApprovalStatus.Pending)
                .OrderByDescending(r => r.ApprovedAt)
                .Take(10)
                .ToListAsync();

            ViewBag.Pending = pending;
            ViewBag.Recent  = recent;
            return View();
        }

        // ── Approve ─────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Approve(int id)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (room == null) return NotFound();

            room.IsApproved       = true;
            room.ApprovalStatus   = ApprovalStatus.Approved;
            room.ApprovedByUserId = _userManager.GetUserId(User);
            room.ApprovedAt       = DateTime.UtcNow;
            room.RejectionReason  = null;
            await _db.SaveChangesAsync();

            // Notify the host
            if (room.HostMemberId.HasValue)
                await _notifications.CreateAsync(
                    room.HostMemberId.Value,
                    "Meeting Approved ✅",
                    $"Your meeting \"{room.Title}\" has been approved and is now visible to members.",
                    $"/meeting/room/{room.RoomCode}");

            TempData["Success"] = $"\"{room.Title}\" approved and published.";
            return RedirectToAction("Index");
        }

        // ── Reject ──────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Reject(int id, string reason)
        {
            var room = await _db.MeetingRooms
                .Include(r => r.HostMember)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (room == null) return NotFound();

            room.IsApproved       = false;
            room.ApprovalStatus   = ApprovalStatus.Rejected;
            room.ApprovedByUserId = _userManager.GetUserId(User);
            room.ApprovedAt       = DateTime.UtcNow;
            room.RejectionReason  = reason;
            await _db.SaveChangesAsync();

            // Notify the host
            if (room.HostMemberId.HasValue)
                await _notifications.CreateAsync(
                    room.HostMemberId.Value,
                    "Meeting Not Approved",
                    $"Your meeting \"{room.Title}\" was not approved. Reason: {reason}",
                    "/Meeting/Create");

            TempData["Info"] = $"\"{room.Title}\" rejected.";
            return RedirectToAction("Index");
        }

        // ── API: pending count for navbar badge ─────────────────
        [HttpGet]
        public async Task<IActionResult> PendingCount()
        {
            var count = await _db.MeetingRooms
                .CountAsync(r => r.ApprovalStatus == ApprovalStatus.Pending);
            return Json(new { count });
        }
    }
}
