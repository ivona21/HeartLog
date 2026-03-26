using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeartLog.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddEmotionEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmotionEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmotionEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmotionEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmotionEntryEmotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmotionEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmotionEntryEmotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmotionEntryEmotions_EmotionEntries_EmotionEntryId",
                        column: x => x.EmotionEntryId,
                        principalTable: "EmotionEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmotionEntryEmotions_Emotions_EmotionId",
                        column: x => x.EmotionId,
                        principalTable: "Emotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmotionEntries_UserId_OccurredAt",
                table: "EmotionEntries",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmotionEntryEmotions_EmotionEntryId",
                table: "EmotionEntryEmotions",
                column: "EmotionEntryId",
                unique: true,
                filter: "\"IsPrimary\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EmotionEntryEmotions_EmotionEntryId_EmotionId",
                table: "EmotionEntryEmotions",
                columns: new[] { "EmotionEntryId", "EmotionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmotionEntryEmotions_EmotionId",
                table: "EmotionEntryEmotions",
                column: "EmotionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmotionEntryEmotions");

            migrationBuilder.DropTable(
                name: "EmotionEntries");
        }
    }
}
