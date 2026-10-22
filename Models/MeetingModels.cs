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
        public bool RecordingEnabled { get; set; }
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
}
