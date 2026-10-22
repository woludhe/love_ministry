using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class CommunityFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForumReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ForumPostId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumReactions_ForumPosts_ForumPostId",
                        column: x => x.ForumPostId,
                        principalTable: "ForumPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ForumReactions_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ForumReactions_ForumPostId_MemberId",
                table: "ForumReactions",
                columns: new[] { "ForumPostId", "MemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForumReactions_MemberId",
                table: "ForumReactions",
                column: "MemberId");

            // Answered prayers shared to the community were filed as Testimony; they get their own category
            migrationBuilder.Sql(
                "UPDATE ForumPosts SET Category = 'PrayerAnswered' " +
                "WHERE Category = 'Testimony' AND Title LIKE 'Prayer Answered:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE ForumPosts SET Category = 'Testimony' WHERE Category = 'PrayerAnswered';");

            migrationBuilder.DropTable(
                name: "ForumReactions");
        }
    }
}
