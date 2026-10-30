using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using ChurchMinistry.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    // Daily devotionals: a pastor writes one per date; members read them, see which
    // ones they haven't read yet, and keep private notes. "Today" is the server's date.
    [Authorize]
    public class DevotionalsController : Controller
    {
        private const int ListPageSize = 30;

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IActivityService _activity;

        public DevotionalsController(ApplicationDbContext db, UserManager<ApplicationUser> um, IActivityService activity)
        {
            _db = db; _userManager = um; _activity = activity;
        }

        private async Task<Member?> GetMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        private bool IsPastor => User.IsInRole("Admin") || User.IsInRole("Pastor");

        // ── Members ─────────────────────────────────────────────────

        // Today's devotion, or the latest one before today. With unread: the newest one
        // this member hasn't read (from the dashboard's "Devotions Unread" card).
        public async Task<IActionResult> Index(bool unread = false)
        {
            var today = DateTime.Today;
            var published = _db.Devotionals.Where(d => d.Date <= today).OrderByDescending(d => d.Date);
            int? id = null;
            if (unread && await GetMemberAsync() is { } member)
                id = await published.Where(d => !d.Reads.Any(r => r.MemberId == member.Id))
                    .Select(d => (int?)d.Id).FirstOrDefaultAsync();
            id ??= await published.Select(d => (int?)d.Id).FirstOrDefaultAsync();
            if (id != null) return RedirectToAction(nameof(Read), new { id, unread = unread ? true : (bool?)null });
            return View("Read", new DevotionalReadViewModel());
        }

        public async Task<IActionResult> Read(int id, int page = 1, bool unread = false)
        {
            var today = DateTime.Today;
            var devotional = await _db.Devotionals.FindAsync(id);
            // A future devotion stays hidden until its day (pastors can preview it)
            if (devotional == null || (devotional.Date > today && !IsPastor)) return NotFound();

            var member = await GetMemberAsync();
            var vm = new DevotionalReadViewModel { Devotional = devotional };

            if (member != null)
            {
                if (!await _db.DevotionalReads.AnyAsync(r => r.DevotionalId == id && r.MemberId == member.Id))
                {
                    _db.DevotionalReads.Add(new DevotionalRead { DevotionalId = id, MemberId = member.Id });
                    try { await _db.SaveChangesAsync(); }
                    catch (DbUpdateException) { /* opened twice at once: already marked read */ }
                    await _activity.LogAsync(member.Id, MemberActivityType.DevotionalRead, devotional.Title, devotional.Id, oncePerDay: true);
                }
                vm.Note = await _db.DevotionalNotes
                    .Where(n => n.DevotionalId == id && n.MemberId == member.Id)
                    .Select(n => n.Text).FirstOrDefaultAsync();
            }

            var memberId = member?.Id ?? 0;
            var published = _db.Devotionals.Where(d => d.Date <= today);
            vm.UnreadOnly = unread;
            vm.UnreadCount = await published.CountAsync(d => !d.Reads.Any(r => r.MemberId == memberId));
            vm.Pager = new PagerModel(page, unread ? "unread devotions" : "devotions") { PageSize = ListPageSize };
            // "Unread only" leaves out everything already read, the open devotion included
            vm.Others = await published
                .Where(d => !unread || !d.Reads.Any(r => r.MemberId == memberId))
                .OrderByDescending(d => d.Date)
                .Select(d => new DevotionalListItem(d.Id, d.Date, d.Title,
                    d.Reads.Any(r => r.MemberId == memberId)))
                .ToPageAsync(vm.Pager);
            return View(vm);
        }

        // Autosaved from the notes box; an empty note is removed
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveNote(int id, string? text)
        {
            var member = await GetMemberAsync();
            if (member == null) return BadRequest(new { saved = false, error = "Only members can keep notes." });
            if (!await _db.Devotionals.AnyAsync(d => d.Id == id)) return NotFound();

            text = text?.Trim() ?? "";
            if (text.Length > 4000) text = text[..4000];

            var note = await _db.DevotionalNotes.FirstOrDefaultAsync(n => n.DevotionalId == id && n.MemberId == member.Id);
            if (text.Length == 0)
            {
                if (note != null) _db.DevotionalNotes.Remove(note);
            }
            else if (note == null)
                _db.DevotionalNotes.Add(new DevotionalNote { DevotionalId = id, MemberId = member.Id, Text = text });
            else
            {
                note.Text = text;
                note.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            return Json(new { saved = true });
        }

        // ── Pastors ─────────────────────────────────────────────────

        [Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Manage(int page = 1)
        {
            var pager = new PagerModel(page, "devotions");
            ViewBag.Pager = pager;
            ViewBag.MemberCount = await _db.Members.CountAsync();
            var rows = await _db.Devotionals.OrderByDescending(d => d.Date)
                .Select(d => new { d.Id, d.Date, d.Title, Reads = d.Reads.Count })
                .ToPageAsync(pager);
            return View(rows.Select(r => (r.Id, r.Date, r.Title, r.Reads)).ToList());
        }

        [Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Create()
        {
            // Suggest the first day from today that has no devotion yet
            var date = DateTime.Today;
            var taken = await _db.Devotionals.Where(d => d.Date >= date).Select(d => d.Date).ToListAsync();
            while (taken.Contains(date)) date = date.AddDays(1);
            return View("Edit", new DevotionalFormViewModel { Date = date });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Create(DevotionalFormViewModel vm)
        {
            vm.Id = 0;
            if (!await ValidateAsync(vm)) return View("Edit", vm);

            var user = await _userManager.GetUserAsync(User);
            var author = await _db.Members.Where(m => m.UserId == user!.Id).Select(m => m.FirstName + " " + m.LastName).FirstOrDefaultAsync();
            var devotional = new Devotional { CreatedBy = author ?? user?.UserName };
            Apply(vm, devotional);
            _db.Devotionals.Add(devotional);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Devotion for {vm.Date:dddd d MMM yyyy} saved.";
            return RedirectToAction(nameof(Manage));
        }

        [Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Edit(int id)
        {
            var d = await _db.Devotionals.FindAsync(id);
            if (d == null) return NotFound();
            return View(new DevotionalFormViewModel
            {
                Id = d.Id, Title = d.Title, Date = d.Date, Scripture = d.Scripture,
                Content = d.Content, Prayer = d.Prayer, StudyScriptures = d.StudyScriptures
            });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Edit(int id, DevotionalFormViewModel vm)
        {
            var devotional = await _db.Devotionals.FindAsync(id);
            if (devotional == null) return NotFound();
            vm.Id = id;
            if (!await ValidateAsync(vm)) return View(vm);

            Apply(vm, devotional);
            devotional.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Devotion updated.";
            return RedirectToAction(nameof(Manage));
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> Delete(int id)
        {
            var devotional = await _db.Devotionals.FindAsync(id);
            if (devotional != null)
            {
                _db.Devotionals.Remove(devotional);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Deleted \"{devotional.Title}\".";
            }
            return RedirectToAction(nameof(Manage));
        }

        private async Task<bool> ValidateAsync(DevotionalFormViewModel vm)
        {
            vm.Date = vm.Date.Date;
            if (await _db.Devotionals.AnyAsync(d => d.Date == vm.Date && d.Id != vm.Id))
                ModelState.AddModelError(nameof(vm.Date), $"There's already a devotion for {vm.Date:d MMM yyyy}.");
            return ModelState.IsValid;
        }

        private static void Apply(DevotionalFormViewModel vm, Devotional d)
        {
            d.Title = vm.Title.Trim();
            d.Date = vm.Date.Date;
            d.Scripture = vm.Scripture.Trim();
            d.Content = vm.Content.Trim();
            d.Prayer = string.IsNullOrWhiteSpace(vm.Prayer) ? null : vm.Prayer.Trim();
            d.StudyScriptures = string.IsNullOrWhiteSpace(vm.StudyScriptures) ? null : vm.StudyScriptures.Trim();
        }
    }
}
