using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeartLog.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddEmotionTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "Emotions");

            migrationBuilder.CreateTable(
                name: "EmotionTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmotionTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmotionTranslations_Emotions_EmotionId",
                        column: x => x.EmotionId,
                        principalTable: "Emotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmotionTranslations_EmotionId_Locale",
                table: "EmotionTranslations",
                columns: new[] { "EmotionId", "Locale" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmotionTranslations");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "Emotions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
