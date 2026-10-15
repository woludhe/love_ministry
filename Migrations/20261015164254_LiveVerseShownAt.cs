using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class LiveVerseShownAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastShownAt",
                table: "LiveBibleVerses",
                type: "datetime(6)",
                nullable: true);

            // Verses already in the list count as shown when they were added
            migrationBuilder.Sql("UPDATE LiveBibleVerses SET LastShownAt = CreatedAt WHERE LastShownAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastShownAt",
                table: "LiveBibleVerses");
        }
    }
}
