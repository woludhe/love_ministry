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
        public DateTime? ScheduledAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public RoomStatus Status { get; set; } = RoomStatus.Scheduled;
        public bool IsRecurring { get; set; }
        public string? RecurringPattern { get; set; }        // "weekly:Sunday:08:00"
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

        public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

        public DateTime? AcceptedAt { get; set; }

        // Unique token for the invitation link
        public string Token { get; set; } = Guid.NewGuid().ToString("N");
    }
}
