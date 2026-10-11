using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class BackfillAttendanceDeliveryMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows logged before DeliveryMode existed: take the sermon's current mode
            // (same rules as Sermon.DeliveryMode)
            migrationBuilder.Sql(@"
                UPDATE SermonAttendances a
                JOIN Sermons s ON s.Id = a.SermonId
                SET a.DeliveryMode = CASE
                    WHEN s.IsOnlineMeeting = 1 THEN 'meeting'
                    WHEN COALESCE(s.LiveStreamUrl, '') <> '' THEN 'live'
                    WHEN COALESCE(s.VideoUrl, '') <> '' THEN 'video'
                    WHEN COALESCE(s.AudioUrl, '') <> '' THEN 'audio'
                    ELSE 'video' END
                WHERE a.DeliveryMode = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
