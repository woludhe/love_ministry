using System.ComponentModel.DataAnnotations;
using ChurchMinistry.Models;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.ViewModels
{
    // ─── Paging (rendered by Views/Shared/_Pager.cshtml) ───
    public class PagerModel
    {
        public const int DefaultPageSize = 15;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = DefaultPageSize;
        public int Total { get; set; }
        // Query-string key, so a page with two lists can page each one separately
        public string PageParam { get; set; } = "page";
        public string ItemName { get; set; } = "records";

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Total / PageSize));
        public int First => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
        public int Last => Math.Min(Page * PageSize, Total);

        public PagerModel(int page, string itemName = "records", string pageParam = "page")
        {
            Page = page; ItemName = itemName; PageParam = pageParam;
        }

        internal void SetTotal(int total)
        {
            Total = total;
            Page = Math.Clamp(Page, 1, TotalPages);
        }
    }

    public static class PagingExtensions
    {
        public static async Task<List<T>> ToPageAsync<T>(this IQueryable<T> query, PagerModel pager)
        {
            pager.SetTotal(await query.CountAsync());
            return await query.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToListAsync();
        }

        public static List<T> ToPage<T>(this IEnumerable<T> items, PagerModel pager)
        {
            var list = items as IReadOnlyCollection<T> ?? items.ToList();
            pager.SetTotal(list.Count);
            return list.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToList();
        }
    }

    // ─── Auth ───
    public class LoginViewModel
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required, DataType(DataType.Password), Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = "";

        [Required, DataType(DataType.Password), MinLength(8, ErrorMessage = "The new password must be at least 8 characters.")]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = "";

        [Required, DataType(DataType.Password), Display(Name = "Confirm New Password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The new passwords don't match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public class ResetPasswordViewModel
    {
        [Required] public string Code { get; set; } = "";    // Base64Url reset token from the email link
        [Required, EmailAddress] public string Email { get; set; } = "";

        [Required, DataType(DataType.Password), MinLength(8, ErrorMessage = "The new password must be at least 8 characters.")]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = "";

        [Required, DataType(DataType.Password), Display(Name = "Confirm New Password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The new passwords don't match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    // ─── Two-step verification ───
    // Second sign-in step: the code sent to the user's email, phone or recovery email
    public class LoginWith2faViewModel
    {
        [Required(ErrorMessage = "Enter the code we sent you."), Display(Name = "Verification Code")]
        [RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "The code is the 6 digits we sent you.")]
        public string Code { get; set; } = "";

        // "Email", "Phone" or "Recovery"
        [Required] public string Channel { get; set; } = "Email";
        public bool RememberMe { get; set; }
        [Display(Name = "Trust this device and don't ask for a code here again")]
        public bool RememberDevice { get; set; }
        public string? ReturnUrl { get; set; }

        // Channel → masked destination, e.g. "Email" → "ad•••@church.org"
        public Dictionary<string, string> Channels { get; set; } = new();
    }

    // Account Security page (Account/Security)
    public class SecuritySettingsViewModel
    {
        public string Email { get; set; } = "";
        public bool TwoFactorEnabled { get; set; }
        public string? TwoFactorMethod { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneConfirmed { get; set; }
        public string? RecoveryEmail { get; set; }
        public bool RecoveryEmailConfirmed { get; set; }

        // A code is on its way and this step is waiting for it
        public string? PendingPhone { get; set; }
        public string? PendingRecoveryEmail { get; set; }
        public string? PendingMethod { get; set; }
    }

    public class RegisterViewModel
    {
        [Required] public string FirstName { get; set; } = "";
        [Required] public string LastName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Phone] public string? Phone { get; set; }
        [Required, DataType(DataType.Password), MinLength(8)]
        public string Password { get; set; } = "";
        [Compare("Password")] public string ConfirmPassword { get; set; } = "";
        public bool AgreeToTerms { get; set; }
    }

    // ─── Member ───
    public class MemberProfileViewModel
    {
        public int Id { get; set; }
        public MemberStatus Status { get; set; }   // shown to Admin/Pastor; not edited by the form
        [Required] public string FirstName { get; set; } = "";
        [Required] public string LastName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Phone] public string? Phone { get; set; }
        public string? AlternatePhone { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public Gender Gender { get; set; }
        public MaritalStatus MaritalStatus { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? Occupation { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public IFormFile? ProfilePhotoFile { get; set; }
        public string? ExistingPhoto { get; set; }
        public List<MinistryGroup> Groups { get; set; } = new();   // shown only; managed under Admin > Groups
        public int ProfilePercent { get; set; }                     // display only
        public List<string> ProfileMissing { get; set; } = new();   // display only: form field names to highlight
    }

    // Admin/Pastor panel on a member's profile
    public class MemberActivitySummary
    {
        public int ProfilePercent { get; set; }
        public List<string> ProfileMissing { get; set; } = new();
        public int TopRank { get; set; }                 // 1–10 when a top participant, else 0
        public int Points30 { get; set; }
        public DateTime? LastActive { get; set; }
        public Dictionary<MemberActivityType, (int Recent, int All)> Counts { get; set; } = new();
        public List<MemberActivity> Recent { get; set; } = new();
    }

    // Admin > Member Activity page
    public class MemberActivityPageViewModel
    {
        public List<(Member Member, int Points)> TopParticipants { get; set; } = new();
        public List<MemberActivity> Activities { get; set; } = new();
        public string? Search { get; set; }
        public MemberActivityType? Type { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class MemberDashboardViewModel
    {
        public Member Member { get; set; } = null!;
        public int AttendanceThisMonth { get; set; }          // physical + virtual
        public int PhysicalAttendanceThisMonth { get; set; }  // checked in at church
        public int VirtualAttendanceThisMonth { get; set; }   // services joined online
        public decimal TotalGivingThisYear { get; set; }
        public int PrayerRequestsCount { get; set; }
        public List<Sermon> RecentSermons { get; set; } = new();
        public List<ChurchEvent> UpcomingEvents { get; set; } = new();
        public List<Notification> Notifications { get; set; } = new();
        public Sermon? LiveSermon { get; set; }                       // published sermon streaming now
        public List<MeetingRoom> LiveMeetings { get; set; } = new();  // public + invited private meetings in progress
        public int ProfilePercent { get; set; }
        public bool IsTopParticipant { get; set; }
    }

    // ─── Giving ───
    public class GivingViewModel
    {
        [Required] public decimal Amount { get; set; }
        public string Currency { get; set; } = "UGX";
        public GivingType GivingType { get; set; } = GivingType.Tithe;
        public PaymentMethod PaymentMethod { get; set; }
        public string? PhoneNumber { get; set; }
        public bool IsRecurring { get; set; }
        public RecurringInterval? RecurringInterval { get; set; }
        public string? Notes { get; set; }
        public bool IsAnonymous { get; set; }
        public string MethodCode { get; set; } = "";   // matches PaymentMethodConfig.Code
    }

    // ─── Attendance ───
    public class AttendanceCheckInViewModel
    {
        public string QrCode { get; set; } = "";
        public int ServiceSessionId { get; set; }
    }

    public class ManualCheckInViewModel
    {
        public int MemberId { get; set; }
        public int ServiceSessionId { get; set; }
    }

    // ─── Media ───
    public class SermonIndexViewModel
    {
        public List<Sermon> Sermons { get; set; } = new();
        public Sermon? LiveSermon { get; set; }
        public string? SearchTerm { get; set; }
        public string? Speaker { get; set; }
        public string? Series { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
    }

    // ─── Forum ───
    public class ForumPostViewModel
    {
        [Required, MaxLength(200)] public string Title { get; set; } = "";
        [Required] public string Content { get; set; } = "";
        public ForumCategory Category { get; set; }
        public bool IsPastorQuestion { get; set; }
    }

    // One post card on the community feed / post page (Views/Community/_PostCard.cshtml)
    public class ForumPostCardViewModel
    {
        public const int FeedReplies = 3;   // newest replies shown on the feed before "View all"

        public ForumPost Post { get; set; } = null!;
        public bool ShowAllReplies { get; set; }
        public int AmenCount { get; set; }
        public bool Amened { get; set; }
        public Member? Me { get; set; }
        public string ReturnUrl { get; set; } = "";   // where a comment posted from this card returns to
    }

    // ─── Prayer ───
    public class PrayerRequestViewModel
    {
        [Required] public string Request { get; set; } = "";
        public PrayerCategory Category { get; set; }
        public bool IsAnonymous { get; set; }
        public bool IsPublic { get; set; } = true;
        public string? AnonymousName { get; set; }
    }

    // ─── Newsletter ───
    public class NewsletterSubscribeViewModel
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        public string? Name { get; set; }
    }

    public class NewsletterComposeViewModel
    {
        [Required] public string Subject { get; set; } = "";
        [Required] public string Body { get; set; } = "";
        public string? PreviewText { get; set; }
        public bool SendNow { get; set; }
    }

    // ─── Admin ───
    public class AdminDashboardViewModel
    {
        public int TotalMembers { get; set; }
        public int ActiveMembers { get; set; }
        public int NewMembersThisMonth { get; set; }
        public decimal TotalGivingThisMonth { get; set; }
        public decimal TotalGivingThisYear { get; set; }
        public int AttendanceLastService { get; set; }
        public double AttendanceGrowthPercent { get; set; }
        public int PrayerRequestsPending { get; set; }
        public int UpcomingEvents { get; set; }
        public List<Member> RecentMembers { get; set; } = new();
        public List<Giving> RecentGivings { get; set; } = new();
        public List<ServiceSession> RecentServices { get; set; } = new();
        public Dictionary<string, decimal> MonthlyGiving { get; set; } = new();
        public Dictionary<string, int> MonthlyAttendance { get; set; } = new();
        public Dictionary<string, decimal> GivingByType { get; set; } = new();
        public Dictionary<string, int> MembersByGroup { get; set; } = new();
    }

    // ─── Groups ───
    public class MinistryGroupFormViewModel
    {
        public int Id { get; set; }
        [Required, MaxLength(150)] public string Name { get; set; } = "";
        public string? Description { get; set; }
        [Display(Name = "Meeting Day")] public string? MeetingDay { get; set; }
        [Display(Name = "Meeting Time")] public string? MeetingTime { get; set; }
        public string? Location { get; set; }
        public string? Icon { get; set; }
        [Display(Name = "Leader")] public int? LeaderId { get; set; }
        [Display(Name = "Active")] public bool IsActive { get; set; } = true;
    }

    // Bootstrap Icons offered for groups; shown in the member list in place of the group name
    public static class MinistryGroupIcons
    {
        public const string Default = "bi-people-fill";

        public static readonly (string Css, string Label)[] All =
        {
            ("bi-people-fill", "People"), ("bi-person-hearts", "Fellowship"), ("bi-heart-fill", "Heart"),
            ("bi-music-note-beamed", "Music"), ("bi-mic-fill", "Microphone"), ("bi-headphones", "Media"),
            ("bi-book-fill", "Bible"), ("bi-journal-bookmark-fill", "Study"), ("bi-bookmark-heart-fill", "Devotion"),
            ("bi-fire", "Fire"), ("bi-lightning-fill", "Power"), ("bi-brightness-high-fill", "Light"),
            ("bi-sun-fill", "Sun"), ("bi-moon-stars-fill", "Night"), ("bi-star-fill", "Star"),
            ("bi-gem", "Gem"), ("bi-feather", "Feather"), ("bi-droplet-fill", "Water"),
            ("bi-flower1", "Flower"), ("bi-tree-fill", "Tree"), ("bi-house-heart-fill", "Home"),
            ("bi-person-raised-hand", "Volunteers"), ("bi-person-arms-up", "Praise"), ("bi-hand-thumbs-up-fill", "Support"),
            ("bi-chat-heart-fill", "Counselling"), ("bi-megaphone-fill", "Outreach"), ("bi-broadcast", "Broadcast"),
            ("bi-camera-video-fill", "Video"), ("bi-palette-fill", "Creative"), ("bi-translate", "Languages"),
            ("bi-globe-americas", "Missions"), ("bi-airplane-fill", "Travel"), ("bi-car-front-fill", "Transport"),
            ("bi-shield-fill", "Protocol"), ("bi-flag-fill", "Flag"), ("bi-trophy-fill", "Sports"),
            ("bi-mortarboard-fill", "Students"), ("bi-briefcase-fill", "Professionals"), ("bi-cash-coin", "Finance"),
            ("bi-basket-fill", "Hospitality"), ("bi-cup-hot-fill", "Refreshments"), ("bi-gift-fill", "Gifts"),
            ("bi-balloon-heart-fill", "Children"), ("bi-emoji-smile-fill", "Youth"), ("bi-gender-male", "Men"),
            ("bi-gender-female", "Women"), ("bi-heart-pulse-fill", "Health"), ("bi-hospital", "Care"),
            ("bi-tools", "Maintenance"), ("bi-calendar-heart-fill", "Events"), ("bi-bell-fill", "Ushers"),
        };

        public static bool IsValid(string? css) => css != null && All.Any(i => i.Css == css);
        public static string For(MinistryGroup g) => IsValid(g.Icon) ? g.Icon! : Default;
    }

    // ─── Event ───
    public class EventRegisterViewModel
    {
        public int EventId { get; set; }
        [Required] public string Name { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Phone] public string? Phone { get; set; }
    }

    // ─── Payment callback ───
    public class FlutterwaveCallback
    {
        public string status { get; set; } = "";
        public string tx_ref { get; set; } = "";
        public string transaction_id { get; set; } = "";
    }

    // ─── Reports: giving ───
    public class MonthlyGivingByMethodRow
    {
        public int Month { get; set; }
        public PaymentMethod Method { get; set; }
        public decimal Total { get; set; }
    }

    // ─── Reports: sermon attendance ───
    public class SermonAttendanceReportViewModel
    {
        // Filters
        public string Period { get; set; } = "all";      // all | range | month | year
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Month { get; set; }               // "yyyy-MM"
        public int? Year { get; set; }
        public int? SermonId { get; set; }
        public string? Mode { get; set; }                // video | audio | live | meeting
        public string? Attendee { get; set; }            // member | guest
        public string? PeriodLabel { get; set; }

        // Filter options
        public List<Sermon> Sermons { get; set; } = new();
        public List<int> Years { get; set; } = new();

        // Totals
        public int Sessions { get; set; }
        public int UniqueMembers { get; set; }
        public int GuestSessions { get; set; }
        public TimeSpan TotalWatchTime { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public Dictionary<string, int> SessionsByMode { get; set; } = new();
        public List<SermonAttendanceSummaryRow> BySermon { get; set; } = new();
    }

    public class SermonAttendanceSummaryRow
    {
        public int SermonId { get; set; }
        public string Title { get; set; } = "";
        public DateTime SermonDate { get; set; }
        public string Mode { get; set; } = "";
        public int Sessions { get; set; }
        public int Members { get; set; }
        public int Guests { get; set; }
        public TimeSpan AverageDuration { get; set; }
    }
}
