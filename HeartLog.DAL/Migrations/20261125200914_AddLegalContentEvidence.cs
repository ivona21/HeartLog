using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeartLog.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalContentEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrivacyPolicyHash",
                table: "RegistrationIntents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsHash",
                table: "RegistrationIntents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentId",
                table: "AgreementEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "AgreementEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Rolling back would delete legal evidence. Use a reviewed forward migration instead.");
        }
    }
}
