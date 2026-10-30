using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "MailboxCodeHash",
                table: "ProfileOperations",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MailboxDelivered",
                table: "ProfileOperations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MailboxExpiresAtUtc",
                table: "ProfileOperations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MailboxFailures",
                table: "ProfileOperations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TargetEmail",
                table: "ProfileOperations",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailVerificationSends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationSends", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileOperations_UserId",
                table: "ProfileOperations",
                column: "UserId",
                unique: true,
                filter: "\"Kind\" = 6");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationSends_AccountId_SentAtUtc",
                table: "EmailVerificationSends",
                columns: new[] { "AccountId", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationSends_SourceHash_SentAtUtc",
                table: "EmailVerificationSends",
                columns: new[] { "SourceHash", "SentAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailVerificationSends");

            migrationBuilder.DropIndex(
                name: "IX_ProfileOperations_UserId",
                table: "ProfileOperations");

            migrationBuilder.DropColumn(
                name: "MailboxCodeHash",
                table: "ProfileOperations");

            migrationBuilder.DropColumn(
                name: "MailboxDelivered",
                table: "ProfileOperations");

            migrationBuilder.DropColumn(
                name: "MailboxExpiresAtUtc",
                table: "ProfileOperations");

            migrationBuilder.DropColumn(
                name: "MailboxFailures",
                table: "ProfileOperations");

            migrationBuilder.DropColumn(
                name: "TargetEmail",
                table: "ProfileOperations");
        }
    }
}
