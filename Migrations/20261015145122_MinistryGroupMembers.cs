using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class MinistryGroupMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MinistryGroupMembers",
                columns: table => new
                {
                    MinistryGroupId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinistryGroupMembers", x => new { x.MinistryGroupId, x.MemberId });
                    table.ForeignKey(
                        name: "FK_MinistryGroupMembers_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinistryGroupMembers_MinistryGroups_MinistryGroupId",
                        column: x => x.MinistryGroupId,
                        principalTable: "MinistryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MinistryGroupMembers_MemberId",
                table: "MinistryGroupMembers",
                column: "MemberId");

            // Keep everyone in the group they were in before
            migrationBuilder.Sql(
                "INSERT INTO MinistryGroupMembers (MinistryGroupId, MemberId, JoinedAt) " +
                "SELECT MinistryGroupId, Id, UTC_TIMESTAMP() FROM Members WHERE MinistryGroupId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Members_MinistryGroups_MinistryGroupId",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_MinistryGroupId",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "MinistryGroupId",
                table: "Members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinistryGroupId",
                table: "Members",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_MinistryGroupId",
                table: "Members",
                column: "MinistryGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Members_MinistryGroups_MinistryGroupId",
                table: "Members",
                column: "MinistryGroupId",
                principalTable: "MinistryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // One group per member again: keep the earliest one they joined
            migrationBuilder.Sql(
                "UPDATE Members m SET MinistryGroupId = (SELECT gm.MinistryGroupId FROM MinistryGroupMembers gm " +
                "WHERE gm.MemberId = m.Id ORDER BY gm.JoinedAt, gm.MinistryGroupId LIMIT 1);");

            migrationBuilder.DropTable(
                name: "MinistryGroupMembers");
        }
    }
}
