using ChurchMinistry.Data;
using ChurchMinistry.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChurchMinistry.ViewComponents
{
    public class CurrentMemberViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentMemberViewComponent(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
        }

        // Called when used as @await Component.InvokeAsync("CurrentMember")
        public async Task<IViewComponentResult> InvokeAsync()
        {
            return View(await GetModelAsync());
        }

        // Called directly from _Layout.cshtml via @inject
        public async Task<CurrentMemberViewModel> GetModelAsync()
        {
            var claimsPrincipal = _httpContextAccessor.HttpContext?.User;

            if (claimsPrincipal?.Identity?.IsAuthenticated != true)
                return new CurrentMemberViewModel();

            var user = await _userManager.GetUserAsync(claimsPrincipal);
            if (user == null)
                return new CurrentMemberViewModel();

            var member = await _db.Members
                .AsNoTracking()
                .Include(m => m.MinistryGroups)
                .FirstOrDefaultAsync(m => m.UserId == user.Id);

            return new CurrentMemberViewModel
            {
                IsAuthenticated = true,
                FullName = member?.FullName ?? user.UserName ?? "",
                ProfilePhoto = member?.ProfilePhoto ?? "/images/avatar-default.png",
                MembershipType = member?.MembershipType ?? MembershipType.Regular,
                MemberStatus = member?.Status ?? MemberStatus.Active,
                MinistryGroup = member == null || member.MinistryGroups.Count == 0 ? null
                    : string.Join(", ", member.MinistryGroups.OrderBy(g => g.Name).Select(g => g.Name)),
                Groups = member?.MinistryGroups.OrderBy(g => g.Name).ToList() ?? new(),
                MemberId = member?.Id,
                Email = user.Email ?? "",
                IsAdmin = claimsPrincipal.IsInRole("Admin"),
                IsPastor = claimsPrincipal.IsInRole("Pastor"),
            };
        }
    }

    public class CurrentMemberViewModel
    {
        public bool IsAuthenticated { get; set; }
        public string FullName { get; set; } = "";
        public string ProfilePhoto { get; set; } = "/images/avatar-default.png";
        public MembershipType MembershipType { get; set; } = MembershipType.Regular;
        public MemberStatus MemberStatus { get; set; } = MemberStatus.Active;
        public string? MinistryGroup { get; set; }
        public List<MinistryGroup> Groups { get; set; } = new();
        public int? MemberId { get; set; }
        public string Email { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool IsPastor { get; set; }
    }
}