using System.ComponentModel.DataAnnotations;

namespace ChurchMinistry.Models
{
    // ── Live Session Settings ─────────────────────────────────
    // One row per sermon - stores all display config for the live page
    public class LiveSession
    {
        public int Id { get; set; }

        [Required] public int SermonId { get; set; }
        public Sermon Sermon { get; set; } = null!;

        // Verse overlay
        public bool ShowVerseOverlay     { get; set; } = true;
        public int? ActiveVerseId        { get; set; }
        public LiveBibleVerse? ActiveVerse { get; set; }
        public bool AutoRotateVerses     { get; set; } = false;
        public int  VerseRotateSeconds   { get; set; } = 60;

        // Announcement marquee
        public bool ShowMarquee          { get; set; } = true;
        public MarqueeSpeed MarqueeSpeed { get; set; } = MarqueeSpeed.Normal;

        // Giving panel
        public bool ShowGivingPanel      { get; set; } = true;
        public string GivingCta          { get; set; } = "Support this ministry - give today!";
        public GivingPanelPosition GivingPanelPosition { get; set; } = GivingPanelPosition.Sidebar;
        public bool ShowMtn              { get; set; } = true;
        public bool ShowAirtel           { get; set; } = true;
        public bool ShowMpesa            { get; set; } = true;
        public bool ShowVisa             { get; set; } = false;

        // Prayer FAB
        public bool ShowPrayerButton     { get; set; } = false;

        public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt        { get; set; } = DateTime.UtcNow;

        public ICollection<LiveBibleVerse> BibleVerses { get; set; } = new List<LiveBibleVerse>();
        public ICollection<LiveAnnouncement> Announcements { get; set; } = new List<LiveAnnouncement>();
    }

    public enum MarqueeSpeed    { Slow, Normal, Fast }
    public enum GivingPanelPosition { Sidebar, BottomBar, Floating }

    // ── Bible Verse ───────────────────────────────────────────
    public class LiveBibleVerse
    {
        public int Id { get; set; }
        public int LiveSessionId { get; set; }
        public LiveSession LiveSession { get; set; } = null!;

        [Required, MaxLength(50)]  public string Reference { get; set; } = "";   // e.g. "John 3:16"
        [Required]                 public string VerseText { get; set; } = "";
        [MaxLength(10)]            public string Translation { get; set; } = "KJV";
        public bool IsActive       { get; set; } = false;
        public int  SortOrder      { get; set; } = 0;
        public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    }

    // ── Announcement ──────────────────────────────────────────
    public class LiveAnnouncement
    {
        public int Id { get; set; }
        public int LiveSessionId { get; set; }
        public LiveSession LiveSession { get; set; } = null!;

        [Required] public string Text    { get; set; } = "";
        public bool IsActive             { get; set; } = false;
        public int  SortOrder            { get; set; } = 0;
        public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    }

    // ── Attendance log ────────────────────────────────────────
    // One row per viewer connection to a sermon with TrackAttendance on
    // (Live page or the sermon's linked meeting room)
    public class SermonAttendance
    {
        public int Id { get; set; }
        public int SermonId { get; set; }
        public Sermon Sermon { get; set; } = null!;

        public int? MemberId             { get; set; }   // null for guests
        public Member? Member            { get; set; }
        [MaxLength(200)] public string DisplayName { get; set; } = "Guest";
        public bool IsGuest              { get; set; }
        [MaxLength(100)] public string ConnectionId { get; set; } = "";

        // Sermon's delivery mode when the viewer joined: video | audio | live | meeting
        [MaxLength(20)] public string DeliveryMode { get; set; } = "";

        // Browser geolocation (only if the viewer allows it)
        public double? Latitude          { get; set; }
        public double? Longitude         { get; set; }

        public DateTime JoinedAt         { get; set; } = DateTime.UtcNow;
        public DateTime? LeftAt          { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public TimeSpan? Duration => LeftAt - JoinedAt;
    }
}
