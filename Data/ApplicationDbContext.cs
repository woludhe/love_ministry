using ChurchMinistry.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Member> Members => Set<Member>();
        public DbSet<MinistryGroup> MinistryGroups => Set<MinistryGroup>();
        public DbSet<ServiceSession> ServiceSessions => Set<ServiceSession>();
        public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
        public DbSet<Giving> Givings => Set<Giving>();
        public DbSet<Sermon> Sermons => Set<Sermon>();
        public DbSet<SermonNote> SermonNotes => Set<SermonNote>();
        public DbSet<ForumPost> ForumPosts => Set<ForumPost>();
        public DbSet<ForumReply> ForumReplies => Set<ForumReply>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<PrayerRequest> PrayerRequests => Set<PrayerRequest>();
        public DbSet<PrayerInteraction> PrayerInteractions => Set<PrayerInteraction>();
        public DbSet<Newsletter> Newsletters => Set<Newsletter>();
        public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();
        public DbSet<ChurchEvent> ChurchEvents => Set<ChurchEvent>();
        public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<MeetingRoom> MeetingRooms => Set<MeetingRoom>();
        public DbSet<MeetingParticipant> MeetingParticipants => Set<MeetingParticipant>();
        public DbSet<MeetingMessage> MeetingMessages => Set<MeetingMessage>();
        public DbSet<LiveSession> LiveSessions => Set<LiveSession>();
        public DbSet<LiveBibleVerse> LiveBibleVerses => Set<LiveBibleVerse>();
        public DbSet<LiveAnnouncement> LiveAnnouncements => Set<LiveAnnouncement>();
        public DbSet<EventPayment> EventPayments => Set<EventPayment>();
        public DbSet<PaymentMethodConfig> PaymentMethods => Set<PaymentMethodConfig>();
        public DbSet<MeetingInvitation> MeetingInvitations => Set<MeetingInvitation>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Member
            builder.Entity<Member>(e =>
            {
                e.HasIndex(m => m.Email).IsUnique();
                e.Property(m => m.Status).HasConversion<string>();
                e.Property(m => m.Gender).HasConversion<string>();
                e.Property(m => m.MaritalStatus).HasConversion<string>();
                e.Property(m => m.MembershipType).HasConversion<string>();
                e.HasOne(m => m.User).WithOne(u => u.Member)
                    .HasForeignKey<Member>(m => m.UserId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(m => m.MinistryGroup).WithMany(g => g.Members)
                    .HasForeignKey(m => m.MinistryGroupId).OnDelete(DeleteBehavior.SetNull);
            });

            // MinistryGroup self-ref leader
            builder.Entity<MinistryGroup>()
                .HasOne(g => g.Leader).WithMany()
                .HasForeignKey(g => g.LeaderId).OnDelete(DeleteBehavior.SetNull);

            // Attendance
            builder.Entity<AttendanceRecord>(e =>
            {
                e.HasIndex(a => new { a.MemberId, a.ServiceSessionId }).IsUnique();
                e.Property(a => a.CheckInMethod).HasConversion<string>();
                e.HasOne(a => a.Member).WithMany(m => m.AttendanceRecords)
                    .HasForeignKey(a => a.MemberId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(a => a.ServiceSession).WithMany(s => s.AttendanceRecords)
                    .HasForeignKey(a => a.ServiceSessionId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ServiceSession>()
                .Property(s => s.ServiceType).HasConversion<string>();

            // Giving
            builder.Entity<Giving>(e =>
            {
                e.Property(g => g.Amount).HasColumnType("decimal(18,2)");
                e.Property(g => g.GivingType).HasConversion<string>();
                e.Property(g => g.PaymentMethod).HasConversion<string>();
                e.Property(g => g.PaymentStatus).HasConversion<string>();
                e.HasOne(g => g.Member).WithMany(m => m.Givings)
                    .HasForeignKey(g => g.MemberId).OnDelete(DeleteBehavior.SetNull);
            });

            // Forum
            builder.Entity<ForumPost>(e =>
            {
                e.Property(f => f.Category).HasConversion<string>();
                e.HasOne(f => f.Member).WithMany()
                    .HasForeignKey(f => f.MemberId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ForumReply>(e =>
            {
                e.HasOne(r => r.ForumPost).WithMany(p => p.Replies)
                    .HasForeignKey(r => r.ForumPostId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(r => r.Member).WithMany()
                    .HasForeignKey(r => r.MemberId).OnDelete(DeleteBehavior.Cascade);
            });

            // Prayer
            builder.Entity<PrayerRequest>(e =>
            {
                e.Property(p => p.Category).HasConversion<string>();
                e.HasOne(p => p.Member).WithMany(m => m.PrayerRequests)
                    .HasForeignKey(p => p.MemberId).OnDelete(DeleteBehavior.SetNull);
            });

            // Newsletter subscriber unique email
            builder.Entity<NewsletterSubscriber>()
                .HasIndex(n => n.Email).IsUnique();

            // Meeting Rooms
            builder.Entity<MeetingRoom>(e =>
            {
                e.Property(m => m.MeetingType).HasConversion<string>();
                e.Property(m => m.Status).HasConversion<string>();
                e.HasOne(m => m.HostMember).WithMany()
                    .HasForeignKey(m => m.HostMemberId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(m => m.RoomCode).IsUnique();
                e.Property(m => m.ApprovalStatus).HasConversion<string>();
            });

            builder.Entity<MeetingParticipant>(e =>
            {
                e.HasOne(p => p.MeetingRoom).WithMany(r => r.Participants)
                    .HasForeignKey(p => p.MeetingRoomId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(p => p.Member).WithMany()
                    .HasForeignKey(p => p.MemberId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<MeetingMessage>(e =>
            {
                e.HasOne(m => m.MeetingRoom).WithMany(r => r.Messages)
                    .HasForeignKey(m => m.MeetingRoomId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(m => m.Member).WithMany()
                    .HasForeignKey(m => m.MemberId).OnDelete(DeleteBehavior.SetNull);
            });

            // Seed default roles
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityRole>().HasData(
                new Microsoft.AspNetCore.Identity.IdentityRole { Id = "1", Name = "Admin", NormalizedName = "ADMIN" },
                new Microsoft.AspNetCore.Identity.IdentityRole { Id = "2", Name = "Pastor", NormalizedName = "PASTOR" },
                new Microsoft.AspNetCore.Identity.IdentityRole { Id = "3", Name = "Member", NormalizedName = "MEMBER" }
            );

            // Live
            builder.Entity<LiveSession>(e =>
            {
                e.HasOne(s => s.Sermon).WithMany()
                    .HasForeignKey(s => s.SermonId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(s => s.ActiveVerse).WithMany()
                    .HasForeignKey(s => s.ActiveVerseId).OnDelete(DeleteBehavior.SetNull);
                e.Property(s => s.MarqueeSpeed).HasConversion<string>();
                e.Property(s => s.GivingPanelPosition).HasConversion<string>();
                e.HasIndex(s => s.SermonId).IsUnique(); // one LiveSession per sermon
            });

            builder.Entity<LiveBibleVerse>(e =>
            {
                e.HasOne(v => v.LiveSession).WithMany(s => s.BibleVerses)
                    .HasForeignKey(v => v.LiveSessionId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<LiveAnnouncement>(e =>
            {
                e.HasOne(a => a.LiveSession).WithMany(s => s.Announcements)
                    .HasForeignKey(a => a.LiveSessionId).OnDelete(DeleteBehavior.Cascade);
            });

                // Event Registration
            builder.Entity<EventRegistration>(e =>
            {
                e.Property(r => r.TotalAmount).HasColumnType("decimal(18,2)");
                e.Property(r => r.AmountPaid).HasColumnType("decimal(18,2)");
                e.Property(r => r.PaymentStatus).HasConversion<string>();
                // Ignore computed properties
                e.Ignore(r => r.BalanceDue);
                e.Ignore(r => r.IsFullyPaid);
            });

            builder.Entity<EventPayment>(e =>
            {
                e.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                e.Property(p => p.Status).HasConversion<string>();
                e.HasOne(p => p.Registration).WithMany(r => r.Payments)
                    .HasForeignKey(p => p.EventRegistrationId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ChurchEvent>(e =>
            {
                e.Property(ev => ev.TicketPrice).HasColumnType("decimal(18,2)");
                e.Property(ev => ev.MinPartialAmount).HasColumnType("decimal(18,2)");
            });
        }
    }
}
