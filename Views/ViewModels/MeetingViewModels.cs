using System.ComponentModel.DataAnnotations;
using ChurchMinistry.Models;

namespace ChurchMinistry.ViewModels
{
    public class CreateMeetingViewModel
    {
        [Required] public string Title { get; set; } = "";
        public string? Description { get; set; }
        public MeetingType MeetingType { get; set; } = MeetingType.Prayer;

        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:yyyy-MM-ddTHH:mm}")]   // the format datetime-local inputs read
        public DateTime? ScheduledAt { get; set; } = DateTime.Today;
        public int? DurationMinutes { get; set; } = 60;
        public bool WaitingRoomEnabled { get; set; } = true;
        public bool ChatEnabled { get; set; } = true;
        public int MaxParticipants { get; set; } = 50;
        public bool IsPublic { get; set; } = true;
        public string? PassCode { get; set; }
        public bool IsRecurring { get; set; }
        public string RecurringFrequency { get; set; } = "Weekly";   // Daily | Weekly
        public string? RecurringDay { get; set; }
        public string? RecurringTime { get; set; }

        // Choices for the Duration dropdown on Create/Edit Meeting
        public static readonly IReadOnlyList<(int? Minutes, string Label)> DurationOptions = new (int?, string)[]
        {
            (null, "Not set"), (15, "15 min"), (30, "30 min"), (45, "45 min"), (60, "1 hour"),
            (90, "1 h 30 min"), (120, "2 hours"), (150, "2 h 30 min"), (180, "3 hours"), (240, "4 hours")
        };
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
        public string DisplayPhoto { get; set; } = MeetingAvatar.Default;   // own tile's picture
        public bool IsHost { get; set; }
        public int? CurrentMemberId { get; set; }
    }

    // A ministry group offered on the meeting Invite page
    public record MeetingInviteGroupOption(int Id, string Name, int MemberCount);

    // A sermon streamed through a meeting room, shown on the lobby card
    public record MeetingLinkedSermon(int Id, string Title, bool IsLive, bool IsPublished);

    public class MeetingLobbyViewModel
    {
        public List<MeetingRoom> ScheduledMeetings { get; set; } = new();
        public List<MeetingRoom> LiveMeetings { get; set; } = new();
        public List<MeetingRoom> PastMeetings { get; set; } = new();
        public PagerModel ScheduledPager { get; set; } = new(1);
        public const int RecentMeetings = 6;   // ended meetings shown under "Recent Meetings"
        // Linked sermon per room code
        public Dictionary<string, MeetingLinkedSermon> LinkedSermons { get; set; } = new();
    }

    // ─── Meetings Register (Admin) ───

    // One person's time in a meeting; rejoining makes new participant rows, so they are merged per person
    public record MeetingAttendeeRow(int? MemberId, string Name, bool IsHost, bool IsCoHost,
        DateTime FirstJoined, DateTime? LastLeft, TimeSpan TimeInMeeting, int Joins);

    // One Live-page viewing of the meeting's linked sermon
    public record MeetingViewerRow(int? MemberId, string Name, bool IsGuest,
        DateTime JoinedAt, DateTime? LeftAt, TimeSpan Duration, bool Estimated);

    // Unique Live-page viewers: members counted once each; guests can't be told apart, so each guest connection counts
    public class MeetingViewerStats
    {
        public bool Tracked { get; set; }      // a linked sermon logs attendance
        public int Members { get; set; }
        public int Guests { get; set; }
        public bool Estimated { get; set; }    // includes rows logged before room/Live page was recorded
        public int Total => Members + Guests;
    }

    public class MeetingRegisterRow
    {
        public MeetingRoom Room { get; set; } = null!;
        public int Attendees { get; set; }
        public MeetingViewerStats? Viewers { get; set; }   // null when no sermon streams this meeting
    }

    public class MeetingRegisterViewModel
    {
        public List<MeetingRegisterRow> Rows { get; set; } = new();
        public PagerModel Pager { get; set; } = new(1);

        // Filters
        public string? Search { get; set; }
        public MeetingType? Type { get; set; }
        public string Status { get; set; } = "ended";     // ended | cancelled | all
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        // Totals for every meeting matching the filters
        public int MeetingCount { get; set; }
        public int TotalAttendees { get; set; }
        public int TotalViewers { get; set; }
        public double AverageAttendees => MeetingCount == 0 ? 0 : (double)TotalAttendees / MeetingCount;
    }

    public class MeetingRegisterDetailsViewModel
    {
        public MeetingRoom Room { get; set; } = null!;
        public Sermon? LinkedSermon { get; set; }
        public string? ApprovedBy { get; set; }

        public List<MeetingAttendeeRow> Attendees { get; set; } = new();        // everyone (CSV, totals)
        public List<MeetingAttendeeRow> PageAttendees { get; set; } = new();
        public PagerModel AttendeesPager { get; set; } = new(1);
        public List<string> NotAdmitted { get; set; } = new();                  // waited, never let in
        public int PeakAttendees { get; set; }

        public MeetingViewerStats? ViewerStats { get; set; }
        public List<MeetingViewerRow> Viewers { get; set; } = new();
        public List<MeetingViewerRow> PageViewers { get; set; } = new();
        public PagerModel ViewersPager { get; set; } = new(1);

        public int ChatMessages { get; set; }
        public int InvitedMembers { get; set; }
        public int InvitedGroups { get; set; }
        public int InvitedEmails { get; set; }
    }

    public static class MeetingTimes
    {
        // "1 h 05 min", "45 min", "under a minute"
        public static string Span(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = t.Negate();
            if (t.TotalMinutes < 1) return "under a minute";
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : $"{(int)t.TotalMinutes} min";
        }

        public static string Local(DateTime? utc, string format = "dd MMM yyyy, h:mm tt") =>
            utc?.ToLocalTime().ToString(format) ?? "—";
    }
}
