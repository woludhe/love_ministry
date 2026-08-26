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
    public class PrayerController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public PrayerController(ApplicationDbContext db, UserManager<ApplicationUser> um)
        {
            _db = db; _userManager = um;
        }

        private async Task<Member?> GetMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        // ── Public Prayer Wall ──────────────────────────────────────
        public async Task<IActionResult> Index(PrayerCategory? category, bool? answered)
        {
            var q = _db.PrayerRequests
                .Include(p => p.Member)
                .Where(p => p.IsPublic)
                .AsQueryable();

            if (category.HasValue)
                q = q.Where(p => p.Category == category.Value);

            if (answered.HasValue)
                q = q.Where(p => p.IsAnswered == answered.Value);
            else
                q = q.Where(p => !p.IsAnswered);

            ViewBag.Requests      = await q.OrderByDescending(p => p.CreatedAt).ToListAsync();
            ViewBag.Category      = category;
            ViewBag.Answered      = answered;
            ViewBag.AnsweredCount = await _db.PrayerRequests.CountAsync(p => p.IsPublic && p.IsAnswered);
            ViewBag.ActiveCount   = await _db.PrayerRequests.CountAsync(p => p.IsPublic && !p.IsAnswered);

            // Build a HashSet of prayer IDs the current member has already prayed for
            var member = await GetMemberAsync();
            if (member != null)
            {
                var prayedIds = await _db.PrayerInteractions
                    .Where(i => i.MemberId == member.Id && i.Type == "prayed")
                    .Select(i => i.PrayerRequestId)
                    .ToListAsync();

                ViewBag.PrayedIds     = new HashSet<int>(prayedIds);
                ViewBag.CurrentMemberId = member.Id;
            }
            else
            {
                ViewBag.PrayedIds       = new HashSet<int>();
                ViewBag.CurrentMemberId = null;
            }

            return View();
        }

        // ── My Prayer Requests ──────────────────────────────────────
        public async Task<IActionResult> MyRequests(PrayerCategory? category, string? status)
        {
            var member = await GetMemberAsync();
            if (member == null) return RedirectToAction("Login", "Account");

            var q = _db.PrayerRequests
                .Where(p => p.MemberId == member.Id)
                .AsQueryable();

            if (category.HasValue)
                q = q.Where(p => p.Category == category.Value);

            if (status == "answered")
                q = q.Where(p => p.IsAnswered);
            else if (status == "active")
                q = q.Where(p => !p.IsAnswered);

            var requests = await q.OrderByDescending(p => p.CreatedAt).ToListAsync();

            ViewBag.Category = category;
            ViewBag.Status   = status;
            ViewBag.TotalCount    = requests.Count;
            ViewBag.AnsweredCount = requests.Count(p => p.IsAnswered);
            ViewBag.ActiveCount   = requests.Count(p => !p.IsAnswered);
            ViewBag.PrayerCount   = requests.Sum(p => p.PrayerCount);

            return View(requests);
        }

        // ── Submit ──────────────────────────────────────────────────
        [HttpGet] public IActionResult Submit() => View(new PrayerRequestViewModel());

        [HttpPost]
        public async Task<IActionResult> Submit(PrayerRequestViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var member = await GetMemberAsync();

            _db.PrayerRequests.Add(new PrayerRequest
            {
                Request       = vm.Request,
                Category      = vm.Category,
                IsAnonymous   = vm.IsAnonymous,
                IsPublic      = vm.IsPublic,
                MemberId      = vm.IsAnonymous ? null : member?.Id,
                AnonymousName = vm.IsAnonymous
                    ? (vm.AnonymousName ?? "Anonymous")
                    : member?.FullName ?? "Anonymous"
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Your prayer request has been submitted. We are praying for you!";
            return RedirectToAction("MyRequests");
        }

        // ── Pray (reaction) ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Pray([FromBody] int id)
        {
            var member = await GetMemberAsync();
            var prayer = await _db.PrayerRequests.FindAsync(id);
            if (prayer == null) return NotFound();

            // Prevent duplicate pray reactions per member
            bool alreadyPrayed = await _db.PrayerInteractions
                .AnyAsync(i => i.PrayerRequestId == id
                            && i.MemberId == member!.Id
                            && i.Type == "prayed");
            if (!alreadyPrayed)
            {
                prayer.PrayerCount++;
                _db.PrayerInteractions.Add(new PrayerInteraction
                {
                    PrayerRequestId = id,
                    MemberId        = member?.Id,
                    Type            = "prayed"
                });
                await _db.SaveChangesAsync();
            }

            return Json(new { count = prayer.PrayerCount, alreadyPrayed });
        }

        // ── Mark Answered ───────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> MarkAnswered(int id)
        {
            var member = await GetMemberAsync();
            var prayer = await _db.PrayerRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.MemberId == member!.Id);

            if (prayer == null) return NotFound();
            if (prayer.IsAnswered)
            {
                TempData["Info"] = "This prayer has already been marked as answered.";
                return RedirectToAction("MyRequests");
            }

            return View(prayer);
        }

        [HttpPost, ActionName("MarkAnswered")]
        public async Task<IActionResult> MarkAnsweredPost(int id, string testimony,
            bool shareInCommunity = false)
        {
            var member = await GetMemberAsync();
            if (member == null) return Unauthorized();

            var prayer = await _db.PrayerRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.MemberId == member.Id);
            if (prayer == null) return NotFound();

            prayer.IsAnswered       = true;
            prayer.AnswerTestimony  = testimony;
            prayer.AnsweredAt       = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Optionally share testimony as a forum post
            if (shareInCommunity && !string.IsNullOrWhiteSpace(testimony))
            {
                var post = new ForumPost
                {
                    Title     = $"Prayer Answered: {prayer.Category} 🙌",
                    Content   = $"**Prayer Request:**\n{prayer.Request}\n\n**Testimony:**\n{testimony}",
                    MemberId  = member.Id,
                    Category  = ForumCategory.Testimony,
                    IsApproved = true
                };
                _db.ForumPosts.Add(post);
                await _db.SaveChangesAsync();

                TempData["Success"] = "Praise God! Your testimony has been recorded and shared in the Community.";
                return RedirectToAction("Post", "Community", new { id = post.Id });
            }

            TempData["Success"] = "Praise God! Your prayer has been marked as answered.";
            return RedirectToAction("MyRequests");
        }

        // ── Testify in Community (standalone) ──────────────────────
        [HttpGet]
        public async Task<IActionResult> Testify(int id)
        {
            var member = await GetMemberAsync();
            var prayer = await _db.PrayerRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.MemberId == member!.Id);

            if (prayer == null) return NotFound();
            return View(prayer);
        }

        [HttpPost, ActionName("Testify")]
        public async Task<IActionResult> TestifyPost(int id, string testimony)
        {
            var member = await GetMemberAsync();
            if (member == null) return Unauthorized();

            var prayer = await _db.PrayerRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.MemberId == member.Id);
            if (prayer == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(testimony))
            {
                // Update testimony on prayer request too
                prayer.AnswerTestimony = testimony;
                if (!prayer.IsAnswered)
                {
                    prayer.IsAnswered  = true;
                    prayer.AnsweredAt  = DateTime.UtcNow;
                }

                // Create community forum post
                var post = new ForumPost
                {
                    Title    = $"Prayer Answered: {prayer.Category} 🙌",
                    Content  = $"**Prayer Request:**\n{prayer.Request}\n\n**My Testimony:**\n{testimony}",
                    MemberId = member.Id,
                    Category = ForumCategory.Testimony,
                    IsApproved = true
                };
                _db.ForumPosts.Add(post);
                await _db.SaveChangesAsync();

                TempData["Success"] = "Your testimony has been shared in the Community! 🎉";
                return RedirectToAction("Post", "Community", new { id = post.Id });
            }

            TempData["Error"] = "Please enter your testimony before sharing.";
            return View(prayer);
        }

        // ── Delete my own prayer request ────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var member = await GetMemberAsync();
            var prayer = await _db.PrayerRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.MemberId == member!.Id);

            if (prayer != null)
            {
                _db.PrayerRequests.Remove(prayer);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Prayer request deleted.";
            }

            return RedirectToAction("MyRequests");
        }

        // ── Toggle visibility ───────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> TogglePublic(int id)
        {
            var member = await GetMemberAsync();
            var prayer = await _db.PrayerRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.MemberId == member!.Id);

            if (prayer != null)
            {
                prayer.IsPublic = !prayer.IsPublic;
                await _db.SaveChangesAsync();
            }

            return Json(new { isPublic = prayer?.IsPublic });
        }
    }
}
