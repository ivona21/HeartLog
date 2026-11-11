using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeartLog.DAL.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyEntryStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Startup must never remove legacy storage before the operator's full
            // verification pass. New, empty databases need no backfill receipt.
            migrationBuilder.Sql("""
                SET LOCAL TIME ZONE 'UTC';
                LOCK TABLE "EmotionEntries", "EmotionEntryEmotions" IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "EmotionEntries" WHERE "EncryptedEmotions" IS NULL) THEN
                        RAISE EXCEPTION 'Entry encryption backfill is incomplete';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "EmotionEntries") THEN
                        IF to_regclass('"EntryEncryptionCutover"') IS NULL THEN
                            RAISE EXCEPTION 'Run the entry encryption verification tool before cutover';
                        END IF;
                        IF NOT EXISTS (
                            SELECT 1 FROM "EntryEncryptionCutover"
                            WHERE "Id" = 1 AND "Fingerprint" = md5(COALESCE((
                                SELECT string_agg(md5(jsonb_build_array("Id", "UserId", "OccurredAt", "CreatedAt", "UpdatedAt", "EncryptedNote", "EncryptedEmotions")::text), '' ORDER BY "Id")
                                FROM "EmotionEntries"
                            ), ''))
                        ) THEN
                            RAISE EXCEPTION 'Entries changed since verification; rerun verification';
                        END IF;
                    END IF;
                END $guard$;
                DROP TABLE IF EXISTS "EntryEncryptionCutover";
                """);

            migrationBuilder.DropTable(
                name: "EmotionEntryEmotions");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "EmotionEntries");

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedEmotions",
                table: "EmotionEntries",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Legacy entry plaintext cannot be recovered by rolling back this migration. Use the encrypted-compatible API release.");
        }
    }
}
