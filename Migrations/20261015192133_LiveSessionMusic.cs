using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchMinistry.Migrations
{
    /// <inheritdoc />
    public partial class LiveSessionMusic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MusicAnchorAt",
                table: "LiveSessions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MusicPausedAt",
                table: "LiveSessions",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "MusicPlaylistJson",
                table: "LiveSessions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "MusicShuffle",
                table: "LiveSessions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MusicState",
                table: "LiveSessions",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "stopped")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "MusicVolume",
                table: "LiveSessions",
                type: "int",
                nullable: false,
                defaultValue: 40);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MusicAnchorAt",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "MusicPausedAt",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "MusicPlaylistJson",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "MusicShuffle",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "MusicState",
                table: "LiveSessions");

            migrationBuilder.DropColumn(
                name: "MusicVolume",
                table: "LiveSessions");
        }
    }
}
