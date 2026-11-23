using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class AddSermonEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The new one-note-per-member index starts with SermonId, so it can carry the foreign key.
            // MySQL won't drop the old index while the key still needs it, so create the new one first.
            migrationBuilder.CreateIndex(
                name: "IX_SermonNotes_SermonId_MemberId",
                table: "SermonNotes",
                columns: new[] { "SermonId", "MemberId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_SermonNotes_SermonId",
                table: "SermonNotes");

            // Existing sermons allow comments, the same as new ones
            migrationBuilder.AddColumn<bool>(
                name: "AllowComments",
                table: "Sermons",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SermonNotes",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SermonComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SermonId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsStaff = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StaffRole = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SermonComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SermonComments_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SermonComments_Sermons_SermonId",
                        column: x => x.SermonId,
                        principalTable: "Sermons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SermonReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SermonId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SermonReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SermonReactions_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SermonReactions_Sermons_SermonId",
                        column: x => x.SermonId,
                        principalTable: "Sermons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SermonComments_MemberId",
                table: "SermonComments",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_SermonComments_SermonId_CreatedAt",
                table: "SermonComments",
                columns: new[] { "SermonId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SermonReactions_MemberId",
                table: "SermonReactions",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_SermonReactions_SermonId_MemberId_Kind",
                table: "SermonReactions",
                columns: new[] { "SermonId", "MemberId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SermonComments");

            migrationBuilder.DropTable(
                name: "SermonReactions");

            migrationBuilder.CreateIndex(
                name: "IX_SermonNotes_SermonId",
                table: "SermonNotes",
                column: "SermonId");

            migrationBuilder.DropIndex(
                name: "IX_SermonNotes_SermonId_MemberId",
                table: "SermonNotes");

            migrationBuilder.DropColumn(
                name: "AllowComments",
                table: "Sermons");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SermonNotes");
        }
    }
}
