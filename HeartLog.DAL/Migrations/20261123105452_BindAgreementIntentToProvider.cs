using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeartLog.DAL.Migrations
{
    /// <inheritdoc />
    public partial class BindAgreementIntentToProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProviderUserId",
                table: "RegistrationIntents",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderUserId",
                table: "RegistrationIntents");
        }
    }
}
