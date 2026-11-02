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
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string? ProfilePhoto { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        // Two-step verification. Identity's own TwoFactorEnabled switches it on;
        // this is the channel the sign-in code goes to first ("Email" or "Phone").
        [MaxLength(10)] public string? TwoFactorMethod { get; set; }
        // A second inbox for sign-in codes and password resets when the main one can't be reached
        [MaxLength(256)] public string? RecoveryEmail { get; set; }
        public bool RecoveryEmailConfirmed { get; set; }

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
        public DateTime? ChatLastSeenAt { get; set; }   // UTC; Members Chat messages after this count as new
        public DateTime? CommunityLastSeenAt { get; set; }   // UTC; Community posts after this count as new

        // FK
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        // Navigation
        public ICollection<MinistryGroup> MinistryGroups { get; set; } = new List<MinistryGroup>();
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
        [MaxLength(60)] public string? Icon { get; set; }   // Bootstrap Icons class, e.g. "bi-music-note-beamed"
        public int? LeaderId { get; set; }
        public Member? Leader { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<Member> Members { get; set; } = new List<Member>();
    }

    // A member's place in a group; a member can be in several groups
    public class MinistryGroupMember
    {
        public int MinistryGroupId { get; set; }
        public MinistryGroup MinistryGroup { get; set; } = null!;
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
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

        // Which PaymentMethodConfig was used, and whether it was a manual one (mobile money paid
        // outside the site, cash). Manual gifts stay pending until an admin reconciles them.
        [MaxLength(40)] public string? MethodCode { get; set; }
        public bool IsManualPayment { get; set; }
        public DateTime? ReconciledAt { get; set; }
        [MaxLength(150)] public string? ReconciledBy { get; set; }
        [MaxLength(100)] public string? ReceivedReference { get; set; }   // MoMo transaction ID / cash receipt no.
        [MaxLength(500)] public string? ReconciliationNote { get; set; }

        public bool NeedsReconciling => IsManualPayment
            && PaymentStatus is PaymentStatus.Pending or PaymentStatus.PendingVerification;
    }

    public enum GivingType { Tithe, Offering, Partnership, BuildingFund, Missions, Seed, Welfare, Special, Other }
    public enum PaymentMethod { MTNMobileMoney, AirtelMoney, MPesa, Visa, Mastercard, Cash, Bank }
    public enum PaymentStatus { Free, Pending, Successful, Failed, PendingVerification, Refunded }
    public enum RecurringInterval { Weekly, Monthly, Quarterly, Annually }

    // A member's "remind me to give this regularly" request. Nothing is charged automatically:
    // when it's due the member gets a notification + email with a pre-filled Give link.
    // Created inactive with the gift; switched on once that gift is paid or confirmed.
    public class RecurringGift
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public int? SourceGivingId { get; set; }          // the gift that set it up
        public decimal Amount { get; set; }
        [MaxLength(10)] public string Currency { get; set; } = "UGX";
        public GivingType GivingType { get; set; }
        [MaxLength(50)] public string? MethodCode { get; set; }   // PaymentMethodConfig.Code, pre-selected in the link
        public RecurringInterval Interval { get; set; }
        public DateTime? NextDueAt { get; set; }
        public DateTime? LastRemindedAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StoppedAt { get; set; }

        public static DateTime Advance(DateTime from, RecurringInterval interval) => interval switch
        {
            RecurringInterval.Weekly => from.AddDays(7),
            RecurringInterval.Quarterly => from.AddMonths(3),
            RecurringInterval.Annually => from.AddYears(1),
            _ => from.AddMonths(1)
        };
    }

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
        public DateTime? LiveStartedAt { get; set; }   // when Go Live was clicked; premieres play from here
        public string? LiveStreamUrl { get; set; }
        public bool IsPublished { get; set; } = true;
        public int ViewCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<SermonNote> SermonNotes { get; set; } = new List<SermonNote>();
        public string? MeetingRoomCode { get; set; }   // links to MeetingRoom.RoomCode
        public bool IsOnlineMeeting { get; set; } = false;
        public bool TrackAttendance { get; set; } = false;
        public string? RecordingUrl { get; set; }   // audio recorded from its meeting room while live

        // Delivery mode derived from which delivery field is set: video | audio | live | meeting
        [NotMapped]
        public string DeliveryMode =>
            IsOnlineMeeting ? "meeting"
            : !string.IsNullOrEmpty(LiveStreamUrl) ? "live"
            : !string.IsNullOrEmpty(VideoUrl) ? "video"
            : !string.IsNullOrEmpty(AudioUrl) ? "audio"
            : "video";

        // Scheduled for later and not live yet, so only its poster shows. A recording means it
        // has already happened, whatever date it was given.
        [NotMapped]
        public bool IsUpcoming => !IsLive && SermonDate > DateTime.UtcNow && string.IsNullOrEmpty(RecordingUrl);

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
        public ICollection<ForumReaction> Reactions { get; set; } = new List<ForumReaction>();
        public ICollection<ForumPostImage> Images { get; set; } = new List<ForumPostImage>();
    }

    // A photo attached to a community post (up to MaxPerPost, shown in SortOrder)
    public class ForumPostImage
    {
        public const int MaxPerPost = 4;
        public int Id { get; set; }
        public int ForumPostId { get; set; }
        public ForumPost ForumPost { get; set; } = null!;
        [Required, MaxLength(300)] public string Url { get; set; } = "";
        public int SortOrder { get; set; }
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

    // A member's "Amen" or "Like" on a community post — one of each per member per post, can be taken back
    public class ForumReaction
    {
        public int Id { get; set; }
        public int ForumPostId { get; set; }
        public ForumPost ForumPost { get; set; } = null!;
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public ForumReactionKind Kind { get; set; } = ForumReactionKind.Amen;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ForumReactionKind { Amen, Like }

    public enum ForumCategory { General, Testimony, BibleStudy, Questions, Announcements, PrayerAnswered }

    public static class ForumCategories
    {
        public static string Label(ForumCategory c) => c switch
        {
            ForumCategory.BibleStudy => "Bible Study",
            ForumCategory.PrayerAnswered => "Prayer Answered",
            _ => c.ToString()
        };
    }

    public static class ForumText
    {
        private static readonly System.Text.RegularExpressions.Regex Bold =
            new(@"\*\*(.+?)\*\*", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Post and reply text as safe HTML: encoded, **bold** kept, line breaks kept
        public static string Format(string? text)
        {
            var html = System.Net.WebUtility.HtmlEncode(text ?? "").Replace("\r\n", "\n");
            html = Bold.Replace(html, "<strong>$1</strong>");
            return html.Replace("\n", "<br>");
        }
    }

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

    // ─────────────────────────────────────────────
    // Member activity (behind-the-scenes usage log, seen by Admin/Pastor)
    // ─────────────────────────────────────────────
    public class MemberActivity
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public MemberActivityType Type { get; set; }
        [MaxLength(300)] public string? Description { get; set; }  // e.g. sermon title, "UGX 50,000 Tithe"
        public int? ReferenceId { get; set; }                       // sermon / post / room / giving id
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum MemberActivityType
    {
        Login, WatchLive, WatchSermon, PrayerRequest, PrayedFor, MeetingJoined,
        ForumPost, ForumReply, CommunityChat, Giving, EventRegistration, ProfileUpdated,
        DevotionalRead
    }

    // ─────────────────────────────────────────────
    // Daily devotionals (written by a pastor, one per date; members read and keep notes)
    // ─────────────────────────────────────────────
    public class Devotional
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; } = "";
        public DateTime Date { get; set; }                       // a calendar date; members see it from this day on
        [Required, MaxLength(500)] public string Scripture { get; set; } = "";
        [Required] public string Content { get; set; } = "";    // plain text, blank lines between paragraphs
        public string? Prayer { get; set; }
        public string? StudyScriptures { get; set; }             // one scripture per line
        [MaxLength(150)] public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<DevotionalRead> Reads { get; set; } = new List<DevotionalRead>();

        [NotMapped]
        public IEnumerable<string> StudyList => (StudyScriptures ?? "")
            .Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0);

        // Paragraphs of the content: a blank line starts a new one
        [NotMapped]
        public IEnumerable<string> Paragraphs => Content.Replace("\r\n", "\n")
            .Split("\n\n").Select(p => p.Trim()).Where(p => p.Length > 0);

        // The start of the content, cut at a word, for the "Today's Devotion" pop-up
        public string Summary(int length = 200)
        {
            var text = string.Join(' ', Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length <= length) return text;
            var cut = text.LastIndexOf(' ', length);
            return text[..(cut > length / 2 ? cut : length)].TrimEnd(',', '.', ';', ':') + "…";
        }
    }

    public class DevotionalRead
    {
        public int Id { get; set; }
        public int DevotionalId { get; set; }
        public Devotional Devotional { get; set; } = null!;
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    }

    // A member's own notes on a devotion; only that member sees them
    public class DevotionalNote
    {
        public int Id { get; set; }
        public int DevotionalId { get; set; }
        public Devotional Devotional { get; set; } = null!;
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        [MaxLength(4000)] public string Text { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
