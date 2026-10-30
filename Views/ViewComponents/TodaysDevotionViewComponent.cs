using ChurchMinistry.Data;
using ChurchMinistry.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.ViewComponents
{
    // "Today's Devotion" pop-up, shown in the layouts to a signed-in member who hasn't
    // read today's devotion yet. "Later" hides it for the day (a cookie, handled in the view).
    public class TodaysDevotionViewComponent : ViewComponent
    {
        public const string LaterCookie = "devotion-later";

        private readonly ApplicationDbContext _db;
        private readonly CurrentMemberViewComponent _currentMember;

        public TodaysDevotionViewComponent(ApplicationDbContext db, CurrentMemberViewComponent currentMember)
        {
            _db = db; _currentMember = currentMember;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (User.Identity?.IsAuthenticated != true) return Content("");
            // Not over the devotion page itself
            if (HttpContext.Request.Path.StartsWithSegments("/Devotionals")) return Content("");

            var today = DateTime.Today;
            var devotional = await _db.Devotionals.AsNoTracking().FirstOrDefaultAsync(d => d.Date == today);
            if (devotional == null) return Content("");
            if (Request.Cookies[LaterCookie] == devotional.Id.ToString()) return Content("");

            var memberId = (await _currentMember.GetModelAsync()).MemberId;
            if (memberId == null) return Content("");
            if (await _db.DevotionalReads.AnyAsync(r => r.DevotionalId == devotional.Id && r.MemberId == memberId))
                return Content("");

            return View(devotional);
        }
    }
}
