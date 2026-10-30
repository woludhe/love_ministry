using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class ForumReactionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every reaction so far was an Amen
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ForumReactions",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Amen")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ForumReactions_ForumPostId_MemberId_Kind",
                table: "ForumReactions",
                columns: new[] { "ForumPostId", "MemberId", "Kind" },
                unique: true);

            // Dropped only after the new index exists: MySQL needs an index on ForumPostId for its foreign key
            migrationBuilder.DropIndex(
                name: "IX_ForumReactions_ForumPostId_MemberId",
                table: "ForumReactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Likes don't exist before this migration; removing them keeps (post, member) unique again
            migrationBuilder.Sql("DELETE FROM ForumReactions WHERE Kind = 'Like'");

            migrationBuilder.CreateIndex(
                name: "IX_ForumReactions_ForumPostId_MemberId",
                table: "ForumReactions",
                columns: new[] { "ForumPostId", "MemberId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_ForumReactions_ForumPostId_MemberId_Kind",
                table: "ForumReactions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ForumReactions");
        }
    }
}
