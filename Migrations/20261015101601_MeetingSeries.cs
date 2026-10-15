using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class MeetingSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "MeetingRooms",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeetingRooms_SeriesId",
                table: "MeetingRooms",
                column: "SeriesId");

            // Existing recurring meetings each start their own series
            migrationBuilder.Sql("UPDATE MeetingRooms SET SeriesId = Id WHERE IsRecurring = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MeetingRooms_SeriesId",
                table: "MeetingRooms");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "MeetingRooms");
        }
    }
}
