using System.ComponentModel.DataAnnotations;
using ChurchMinistry.Models;

namespace ChurchMinistry.ViewModels
{
    // ─── Auth ───
    public class LoginViewModel
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
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
        public int? MinistryGroupId { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public IFormFile? ProfilePhotoFile { get; set; }
        public string? ExistingPhoto { get; set; }
    }

    public class MemberDashboardViewModel
    {
        public Member Member { get; set; } = null!;
        public int AttendanceThisMonth { get; set; }
        public decimal TotalGivingThisYear { get; set; }
        public int PrayerRequestsCount { get; set; }
        public List<Sermon> RecentSermons { get; set; } = new();
        public List<ChurchEvent> UpcomingEvents { get; set; } = new();
        public List<Notification> Notifications { get; set; } = new();
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

    public class GivingHistoryViewModel
    {
        public List<Giving> Givings { get; set; } = new();
        public decimal TotalThisYear { get; set; }
        public decimal TotalThisMonth { get; set; }
        public Dictionary<string, decimal> ByType { get; set; } = new();
    }

    // ─── Attendance ───
    public class AttendanceCheckInViewModel
    {
        public string QrCode { get; set; } = "";
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
}
