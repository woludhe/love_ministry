using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class SermonLiveStartedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LiveStartedAt",
                table: "Sermons",
                type: "datetime(6)",
                nullable: true);

            // A sermon that's live right now starts its broadcast clock from here
            migrationBuilder.Sql("UPDATE Sermons SET LiveStartedAt = UTC_TIMESTAMP() WHERE IsLive = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiveStartedAt",
                table: "Sermons");
        }
    }
}
