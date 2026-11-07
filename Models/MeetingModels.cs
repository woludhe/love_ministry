using System.ComponentModel.DataAnnotations;

namespace ChurchMinistry.Models
{
    public class MeetingRoom
    {
        public int Id { get; set; }
        [Required, MaxLength(200)] public string Title { get; set; } = "";
        public string? Description { get; set; }
        public MeetingType MeetingType { get; set; } = MeetingType.Prayer;
        public string RoomCode { get; set; } = "";           // short join code e.g. "PRAY-4821"
        public string? HostUserId { get; set; }
        public ApplicationUser? Host { get; set; }
        public int? HostMemberId { get; set; }
        public Member? HostMember { get; set; }
        // Host's member name, else their account name (admins/pastors may have no member profile)
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? HostName => HostMember?.FullName
            ?? (Host == null ? null : string.IsNullOrWhiteSpace(Host.FullName) ? Host.Email : Host.FullName);
        public DateTime? ScheduledAt { get; set; }
        // Planned length; with ScheduledAt it gives the scheduled end
        public int? DurationMinutes { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public DateTime? ScheduledEndAt => ScheduledAt.HasValue && DurationMinutes.HasValue
            ? ScheduledAt.Value.AddMinutes(DurationMinutes.Value) : null;
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public RoomStatus Status { get; set; } = RoomStatus.Scheduled;
        public bool IsRecurring { get; set; }
        public string? RecurringPattern { get; set; }        // "weekly:Sunday:08:00"
        // Id of the first meeting of a recurring series; every weekly meeting shares it
        public int? SeriesId { get; set; }
        public bool WaitingRoomEnabled { get; set; } = true;
        public bool ChatEnabled { get; set; } = true;
        // Ticked, or a sermon linked to the room: the host's page records the meeting's audio
        public bool RecordingEnabled { get; set; }
        // This meeting's recording in the audio library (uploads/audio/meetings/…)
        [MaxLength(300)] public string? RecordingUrl { get; set; }
        public int MaxParticipants { get; set; } = 50;
        public bool IsPublic { get; set; } = true;
        public string? PassCode { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        //Approval
        public bool IsApproved { get; set; } = false;
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;
        public string? ApprovedByUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<MeetingParticipant> Participants { get; set; } = new List<MeetingParticipant>();
        public ICollection<MeetingMessage> Messages { get; set; } = new List<MeetingMessage>();
        public ICollection<MeetingInvitation> Invitations { get; set; } = new List<MeetingInvitation>();

        // ── Recurrence ("daily:08:00" or "weekly:Sunday:08:00", server local time) ──
        // Day is null for a daily meeting
        public (DayOfWeek? Day, TimeSpan Time)? ParseRecurrence()
        {
            if (!IsRecurring || string.IsNullOrEmpty(RecurringPattern)) return null;
            if (RecurringPattern.StartsWith("daily:"))
                return TimeSpan.TryParse(RecurringPattern["daily:".Length..], out var dailyTime)
                    ? (null, dailyTime) : null;

            var parts = RecurringPattern.Split(':', 3);
            if (parts.Length != 3 || parts[0] != "weekly") return null;
            if (!Enum.TryParse<DayOfWeek>(parts[1], true, out var day)) return null;
            if (!TimeSpan.TryParse(parts[2], out var time)) return null;
            return (day, time);
        }

        // First occurrence strictly after the given moment, or null when not recurring
        public DateTime? NextOccurrenceUtc(DateTime afterUtc)
        {
            if (ParseRecurrence() is not { } rec) return null;
            var after = afterUtc.ToLocalTime();
            DateTime candidate;
            if (rec.Day is { } day)
            {
                candidate = after.Date.AddDays(((int)day - (int)after.DayOfWeek + 7) % 7) + rec.Time;
                if (candidate <= after) candidate = candidate.AddDays(7);
            }
            else
            {
                candidate = after.Date + rec.Time;
                if (candidate <= after) candidate = candidate.AddDays(1);
            }
            return DateTime.SpecifyKind(candidate, DateTimeKind.Local).ToUniversalTime();
        }

        public string? RecurrenceLabel => ParseRecurrence() is { } rec
            ? $"Every {(rec.Day?.ToString() ?? "day")} · {DateTime.Today.Add(rec.Time):h:mm tt}" : null;
    }

    public enum MeetingType { Prayer, BibleStudy, Worship, YouthService, GeneralService, CellGroup, OneOnOne }
    public enum RoomStatus  { Scheduled, Live, Ended, Cancelled }
    public enum ApprovalStatus { Pending, Approved, Rejected }

    // Picture shown for a meeting participant. A host with no member profile is the admin (or a
    // pastor) login, which appears with the site logo rather than the blank default avatar.
    public static class MeetingAvatar
    {
        public const string Default = "/images/avatar-default.png";
        public const string SiteLogo = "/images/logo-avatar.png";
        public const string AdminName = "Admin";   // display name of the admin login, not its email

        public static string For(int? memberId, string? memberPhoto, bool isHost) =>
            memberId != null ? memberPhoto ?? Default : isHost ? SiteLogo : Default;
    }

    public class MeetingParticipant
    {
        public int Id { get; set; }
        public int MeetingRoomId { get; set; }
        public MeetingRoom MeetingRoom { get; set; } = null!;
        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string DisplayName { get; set; } = "";
        public string? ConnectionId { get; set; }   // SignalR connection
        public bool IsHost { get; set; }
        public bool IsCoHost { get; set; }
        public bool IsMuted { get; set; }
        public bool IsVideoOff { get; set; }
        public bool IsHandRaised { get; set; }
        public bool IsInWaitingRoom { get; set; }
        public bool IsAdmitted { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LeftAt { get; set; }
    }

    public class MeetingMessage
    {
        public int Id { get; set; }
        public int MeetingRoomId { get; set; }
        public MeetingRoom MeetingRoom { get; set; } = null!;
        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string SenderName { get; set; } = "";
        public string Message { get; set; } = "";
        public string Type { get; set; } = "chat";  // chat | system | prayer
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }

    public class MeetingInvitation
    {
        public int Id { get; set; }

        public int MeetingRoomId { get; set; }
        public MeetingRoom MeetingRoom { get; set; } = null!;

        // Null if invited by email (not a registered member)
        public int? MemberId { get; set; }
        public Member? Member { get; set; }

        // Email for external guests
        public string? Email { get; set; }

        // Set for a group invite — access follows the group's current membership
        public int? MinistryGroupId { get; set; }
        public MinistryGroup? MinistryGroup { get; set; }

        public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

        public DateTime? AcceptedAt { get; set; }

        // Unique token for the invitation link
        public string Token { get; set; } = Guid.NewGuid().ToString("N");
    }

    // ── One-on-One with Apostle Enock Makoko ───────────────────────
    // A member asks from their dashboard; an Admin/Pastor schedules it (online or in person) or declines.
    public enum OneOnOneStatus { Pending, Scheduled, Declined, Cancelled }
    public enum OneOnOneFormat { Either, Online, InPerson }
    // OneOnOne: only the member. Private: staff may invite others too (e.g. family).
    public enum OneOnOnePrivacy { OneOnOne, Private }

    public class OneOnOneRequest
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public Member Member { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // From the member. The topic is only shown to Admins and Pastors.
        [Required, MaxLength(1000)] public string Topic { get; set; } = "";
        // Up to 3 preferred times, UTC, as ISO strings separated by "|"
        [MaxLength(200)] public string? PreferredTimes { get; set; }
        public OneOnOneFormat PreferredFormat { get; set; } = OneOnOneFormat.Either;

        public OneOnOneStatus Status { get; set; } = OneOnOneStatus.Pending;

        // Set by staff
        public DateTime? ScheduledAt { get; set; }
        public int? DurationMinutes { get; set; }
        public OneOnOneFormat? Format { get; set; }
        public OneOnOnePrivacy Privacy { get; set; } = OneOnOnePrivacy.OneOnOne;
        [MaxLength(300)] public string? Location { get; set; }
        // In-person private meeting: who else is coming, shown to the member
        [MaxLength(300)] public string? OtherAttendees { get; set; }
        public int? MeetingRoomId { get; set; }
        public MeetingRoom? MeetingRoom { get; set; }
        [MaxLength(1000)] public string? StaffNote { get; set; }
        public string? HandledByUserId { get; set; }
        public DateTime? HandledAt { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<DateTime> PreferredTimesUtc => (PreferredTimes ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : (DateTime?)null)
            .Where(d => d != null).Select(d => d!.Value).ToList();

        // Still needs attention or still ahead: the member can't ask again until it's closed
        public static System.Linq.Expressions.Expression<Func<OneOnOneRequest, bool>> IsOpen(DateTime nowUtc) =>
            r => r.Status == OneOnOneStatus.Pending
              || (r.Status == OneOnOneStatus.Scheduled && r.ScheduledAt != null
                  && r.ScheduledAt.Value.AddMinutes(r.DurationMinutes ?? 30) > nowUtc);

        public const string ApostleName = "Apostle Enock Makoko";
        public string MeetingTitle => Privacy == OneOnOnePrivacy.Private
            ? $"Private meeting with {ApostleName}" : $"One-on-One with {ApostleName}";
    }
}
