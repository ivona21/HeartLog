using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeartLog.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgreementEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgreementEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgreementEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PrivacyPolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JournalProcessingVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationIntents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgreementEvents_UserId_AgreementKey_OccurredAtUtc",
                table: "AgreementEvents",
                columns: new[] { "UserId", "AgreementKey", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgreementEvents_UserId_SubmissionId_AgreementKey",
                table: "AgreementEvents",
                columns: new[] { "UserId", "SubmissionId", "AgreementKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_ExpiresAtUtc",
                table: "RegistrationIntents",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_TokenHash",
                table: "RegistrationIntents",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgreementEvents");

            migrationBuilder.DropTable(
                name: "RegistrationIntents");
        }
    }
}
