using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace ChurchMinistry.Models
{
    // ─────────────────────────────────────────────
    // Identity / Auth
    // ─────────────────────────────────────────────
    public class ApplicationUser : IdentityUser
    {
        [MaxLength(100)] public string FirstName { get; set; } = "";
        [MaxLength(100)] public string LastName { get; set; } = "";
        public string FullName => $"{FirstName} {LastName}";
        public string? ProfilePhoto { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        // Navigation
        public Member? Member { get; set; }
    }

    // ─────────────────────────────────────────────
    // Member
    // ─────────────────────────────────────────────
    public class Member
    {
        public int Id { get; set; }
        [Required, MaxLength(100)] public string FirstName { get; set; } = "";
        [Required, MaxLength(100)] public string LastName { get; set; } = "";
        public string FullName => $"{FirstName} {LastName}";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Phone] public string? Phone { get; set; }
        public string? AlternatePhone { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public Gender Gender { get; set; }
        public MaritalStatus MaritalStatus { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; } = "Uganda";
        public string? Occupation { get; set; }
        public string? ProfilePhoto { get; set; }
        public string? QrCode { get; set; }
        public DateTime JoinDate { get; set; } = DateTime.UtcNow;
        public DateTime? BaptismDate { get; set; }
        public MemberStatus Status { get; set; } = MemberStatus.Active;
        public MembershipType MembershipType { get; set; } = MembershipType.Regular;
        public string? Notes { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }

        // FK
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
        public int? MinistryGroupId { get; set; }
        public MinistryGroup? MinistryGroup { get; set; }

        // Navigation
        public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
        public ICollection<Giving> Givings { get; set; } = new List<Giving>();
        public ICollection<PrayerRequest> PrayerRequests { get; set; } = new List<PrayerRequest>();
    }

    public enum Gender { Male, Female, Other }
    public enum MaritalStatus { Single, Married, Divorced, Widowed }
    public enum MemberStatus { Active, Inactive, Visitor, Transferred }
    public enum MembershipType { Regular, Youth, Children, Senior, Associate }

    // ─────────────────────────────────────────────
    // Ministry Groups / Cell
    // ─────────────────────────────────────────────
    public class MinistryGroup
    {
        public int Id { get; set; }
        [Required, MaxLength(150)] public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? MeetingDay { get; set; }
        public string? MeetingTime { get; set; }
        public string? Location { get; set; }
        public int? LeaderId { get; set; }
        public Member? Leader { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<Member> Members { get; set; } = new List<Member>();
    }

    // ─────────────────────────────────────────────
    // Attendance
    // ─────────────────────────────────────────────
    public class ServiceSession
    {
        public int Id { get; set; }
        [Required, MaxLength(150)] public string Title { get; set; } = "";
        public ServiceType ServiceType { get; set; }
        public DateTime ServiceDate { get; set; }
        public string? Speaker { get; set; }
        public string? SermonTitle { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    }

    public enum ServiceType { Sunday, Midweek, Prayer, Youth, Special, Online }

    public class AttendanceRecord
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public int ServiceSessionId { get; set; }
        public ServiceSession ServiceSession { get; set; } = null!;
        public DateTime CheckInTime { get; set; } = DateTime.UtcNow;
        public CheckInMethod CheckInMethod { get; set; } = CheckInMethod.QR;
        public string? Notes { get; set; }
    }

    public enum CheckInMethod { QR, Manual, Online }

    // ─────────────────────────────────────────────
    // Giving / Offerings
    // ─────────────────────────────────────────────
    public class Giving
    {
        public int Id { get; set; }
        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string? AnonymousName { get; set; }
        [Required] public decimal Amount { get; set; }
        public string Currency { get; set; } = "UGX";
        public GivingType GivingType { get; set; } = GivingType.Tithe;
        public PaymentMethod PaymentMethod { get; set; }
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
        public string? TransactionId { get; set; }
        public string? FlutterwaveRef { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Notes { get; set; }
        public bool IsRecurring { get; set; }
        public RecurringInterval? RecurringInterval { get; set; }
        public DateTime GivingDate { get; set; } = DateTime.UtcNow;
        public DateTime? NextGivingDate { get; set; }
    }

    public enum GivingType { Tithe, Offering, Pledge, BuildingFund, Missions, Seed, Welfare, Special, Other }
    public enum PaymentMethod { MTNMobileMoney, AirtelMoney, MPesa, Visa, Mastercard, Cash, Bank }
    public enum PaymentStatus { Free, Pending, Successful, Failed, PendingVerification, Refunded }
    public enum RecurringInterval { Weekly, Monthly, Quarterly, Annually }

    // ─────────────────────────────────────────────
    // Media / Sermons
    // ─────────────────────────────────────────────
    public class Sermon
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? Speaker { get; set; }
        public string? SeriesName { get; set; }
        public DateTime SermonDate { get; set; }
        public string? VideoUrl { get; set; }
        public string? AudioUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? ScriptureReference { get; set; }
        public string? Tags { get; set; }
        public bool IsLive { get; set; }
        public string? LiveStreamUrl { get; set; }
        public bool IsPublished { get; set; } = true;
        public int ViewCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<SermonNote> SermonNotes { get; set; } = new List<SermonNote>();
        public string? MeetingRoomCode { get; set; }   // links to MeetingRoom.RoomCode
        public bool IsOnlineMeeting { get; set; } = false;
        // Navigation (optional — load meeting room details when needed)
        [NotMapped]
        public MeetingRoom? LinkedMeetingRoom { get; set; }
    }

    public class SermonNote
    {
        public int Id { get; set; }
        public int SermonId { get; set; }
        public Sermon Sermon { get; set; } = null!;
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public string Note { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ─────────────────────────────────────────────
    // Community / Forum
    // ─────────────────────────────────────────────
    public class ForumPost
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; } = "";
        [Required] public string Content { get; set; } = "";
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public ForumCategory Category { get; set; } = ForumCategory.General;
        public bool IsPastorQuestion { get; set; }
        public bool IsPinned { get; set; }
        public bool IsApproved { get; set; } = true;
        public int ViewCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<ForumReply> Replies { get; set; } = new List<ForumReply>();
    }

    public class ForumReply
    {
        public int Id { get; set; }
        public int ForumPostId { get; set; }
        public ForumPost ForumPost { get; set; } = null!;
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public string Content { get; set; } = "";
        public bool IsPastorReply { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ForumCategory { General, Testimony, BibleStudy, Questions, Announcements }

    // ─────────────────────────────────────────────
    // Chat Messages (SignalR)
    // ─────────────────────────────────────────────
    public class ChatMessage
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public string Message { get; set; } = "";
        public string? Room { get; set; } = "general";
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }

    // ─────────────────────────────────────────────
    // Prayer
    // ─────────────────────────────────────────────
    public class PrayerRequest
    {
        public int Id { get; set; }
        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string AnonymousName { get; set; } = "";
        [Required] public string Request { get; set; } = "";
        public bool IsAnonymous { get; set; }
        public bool IsPublic { get; set; } = true;
        public bool IsAnswered { get; set; }
        public string? AnswerTestimony { get; set; }
        public PrayerCategory Category { get; set; } = PrayerCategory.General;
        public int PrayerCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AnsweredAt { get; set; }
        public ICollection<PrayerInteraction> Interactions { get; set; } = new List<PrayerInteraction>();
    }

    public class PrayerInteraction
    {
        public int Id { get; set; }
        public int PrayerRequestId { get; set; }
        public PrayerRequest PrayerRequest { get; set; } = null!;
        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string Type { get; set; } = "prayed"; // prayed, comment
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum PrayerCategory { General, Health, Family, Financial, Salvation, Guidance, Thanksgiving }

    // ─────────────────────────────────────────────
    // Newsletter
    // ─────────────────────────────────────────────
    public class Newsletter
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Subject { get; set; } = "";
        [Required] public string Body { get; set; } = "";
        public string? PreviewText { get; set; }
        public NewsletterStatus Status { get; set; } = NewsletterStatus.Draft;
        public DateTime? SentAt { get; set; }
        public int RecipientsCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum NewsletterStatus { Draft, Scheduled, Sent }

    public class NewsletterSubscriber
    {
        public int Id { get; set; }
        [Required, EmailAddress] public string Email { get; set; } = "";
        public string? Name { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
        public string? UnsubscribeToken { get; set; }
    }

    // ─────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────
    public class ChurchEvent
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; } = "";
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? Location { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsPublic { get; set; } = true;
        public bool RequiresRegistration { get; set; }
        public int? MaxCapacity { get; set; }
        public decimal? TicketPrice { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<EventRegistration> Registrations { get; set; } = new List<EventRegistration>();
        public bool IsAllDay { get; set; } = false;
        public bool AllowPartialPayment { get; set; } = false;
        public decimal? MinPartialAmount { get; set; }          // minimum first payment
        public string? RichDescription { get; set; }          // HTML from TinyMCE
                                                              // Gallery: comma-separated image paths stored as JSON array string
        public string? GalleryJson { get; set; }
        // Helper to get gallery list (not mapped to DB column)
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<string> Gallery =>
            string.IsNullOrEmpty(GalleryJson)
                ? new List<string>()
                : System.Text.Json.JsonSerializer
                    .Deserialize<List<string>>(GalleryJson) ?? new List<string>();
    }

    public class EventRegistration
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public ChurchEvent Event { get; set; } = null!;
        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public bool IsConfirmed { get; set; } = true;
        // Payment
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Free;
        public string? TransactionId { get; set; }
        public string? FlutterwaveRef { get; set; }
        public decimal AmountPaid { get; set; } = 0;
        public DateTime? PaidAt { get; set; }
        public decimal TotalAmount { get; set; } = 0;   // full ticket price at time of reg
        public decimal BalanceDue => TotalAmount - AmountPaid;
        public bool IsFullyPaid => TotalAmount == 0 || AmountPaid >= TotalAmount;
        // Registering on behalf of others
        public bool IsBehalf { get; set; } = false;
        public string? BehalfName { get; set; }        // name of the person being registered for
        public string? BehalfEmail { get; set; }
        public string? BehalfPhone { get; set; }
        public string? BehalfNotes { get; set; }

        // Payment history list (child table - see EventPayment below)
        public ICollection<EventPayment> Payments { get; set; } = new List<EventPayment>();
    }

    // ── EventPayment - new model for payment instalment tracking ─
    public class EventPayment
    {
        public int Id { get; set; }
        public int EventRegistrationId { get; set; }
        public EventRegistration Registration { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? TransactionId { get; set; }
        public string? FlutterwaveRef { get; set; }
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public string? PaymentMethod { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; set; }
        public string? Notes { get; set; }
    }

    // ─────────────────────────────────────────────
    // Notifications
    // ─────────────────────────────────────────────
    public class Notification
    {
        public int Id { get; set; }
        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Link { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
