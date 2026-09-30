using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations.Manga
{
    /// <inheritdoc />
    public partial class AddChapterLastDownloadAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastDownloadAttempt",
                table: "MangaConnectorToManga",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDownloadAttempt",
                table: "MangaConnectorToChapter",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastDownloadAttempt",
                table: "MangaConnectorToManga");

            migrationBuilder.DropColumn(
                name: "LastDownloadAttempt",
                table: "MangaConnectorToChapter");
        }
    }
}
