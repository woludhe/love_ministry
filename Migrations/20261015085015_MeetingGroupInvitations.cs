using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class MeetingGroupInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinistryGroupId",
                table: "MeetingInvitations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeetingInvitations_MinistryGroupId",
                table: "MeetingInvitations",
                column: "MinistryGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_MeetingInvitations_MinistryGroups_MinistryGroupId",
                table: "MeetingInvitations",
                column: "MinistryGroupId",
                principalTable: "MinistryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MeetingInvitations_MinistryGroups_MinistryGroupId",
                table: "MeetingInvitations");

            migrationBuilder.DropIndex(
                name: "IX_MeetingInvitations_MinistryGroupId",
                table: "MeetingInvitations");

            migrationBuilder.DropColumn(
                name: "MinistryGroupId",
                table: "MeetingInvitations");
        }
    }
}
