using System.ComponentModel.DataAnnotations;
using ChurchMinistry.Models;

namespace ChurchMinistry.ViewModels
{
    public class CreateMeetingViewModel
    {
        [Required] public string Title { get; set; } = "";
        public string? Description { get; set; }
        public MeetingType MeetingType { get; set; } = MeetingType.Prayer;

        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:mm/dd/yyyy hh:mm tt}")]
        public DateTime? ScheduledAt { get; set; } = DateTime.Today;
        public bool WaitingRoomEnabled { get; set; } = true;
        public bool ChatEnabled { get; set; } = true;
        public int MaxParticipants { get; set; } = 50;
        public bool IsPublic { get; set; } = true;
        public string? PassCode { get; set; }
        public bool IsRecurring { get; set; }
        public string? RecurringDay { get; set; }
        public string? RecurringTime { get; set; }
    }

    public class JoinMeetingViewModel
    {
        [Required] public string RoomCode { get; set; } = "";
        public string? PassCode { get; set; }
        public string? DisplayName { get; set; }
    }

    public class MeetingRoomViewModel
    {
        public MeetingRoom Room { get; set; } = null!;
        public string DisplayName { get; set; } = "";
        public bool IsHost { get; set; }
        public int? CurrentMemberId { get; set; }
    }

    public class MeetingLobbyViewModel
    {
        public List<MeetingRoom> ScheduledMeetings { get; set; } = new();
        public List<MeetingRoom> LiveMeetings { get; set; } = new();
        public List<MeetingRoom> PastMeetings { get; set; } = new();
    }
}
