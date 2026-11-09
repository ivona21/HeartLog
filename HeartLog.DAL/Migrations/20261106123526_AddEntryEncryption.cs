using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeartLog.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedEmotions",
                table: "EmotionEntries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedNote",
                table: "EmotionEntries",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedEmotions",
                table: "EmotionEntries");

            migrationBuilder.DropColumn(
                name: "EncryptedNote",
                table: "EmotionEntries");
        }
    }
}
