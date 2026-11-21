using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AwaitingActivation",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccountInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SpentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PendingTotpSecret = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountInvitations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvitations_TokenHash",
                table: "AccountInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvitations_UserId",
                table: "AccountInvitations",
                column: "UserId",
                unique: true);

            // Never-used accounts (no enrolled TOTP, no passkey, and no LoginSucceeded=11 or PasskeyLogin=58
            // row in the audit trail) move to awaiting activation so an administrator-chosen password or
            // copied pending seed cannot be used to enroll them (ADR-0033). Never invite the last enabled
            // administrator (Clearance=4, IsDisabled=0): if all would be invited, keep vault-admin first,
            // otherwise the earliest created enabled administrator. The audit trail is read-only here.
            migrationBuilder.Sql("""
                WITH [Candidates] AS (
                    SELECT u.[Id], u.[Username], u.[Clearance], u.[IsDisabled], u.[CreatedAtUtc]
                    FROM [Users] AS u
                    WHERE u.[TotpEnabled] = CAST(0 AS bit)
                      AND NOT EXISTS (
                          SELECT 1 FROM [PasskeyCredentials] AS p
                          WHERE p.[UserId] = u.[Id]
                      )
                      AND NOT EXISTS (
                          SELECT 1 FROM [AuditLogs] AS a
                          WHERE a.[Action] IN (11, 58)
                            AND (a.[UserId] = u.[Id] OR a.[ResourceId] = u.[Id])
                      )
                ),
                [Preserved] AS (
                    SELECT TOP (1) c.[Id]
                    FROM [Candidates] AS c
                    WHERE c.[Clearance] = 4
                      AND c.[IsDisabled] = CAST(0 AS bit)
                      AND NOT EXISTS (
                          SELECT 1
                          FROM [Users] AS ea
                          WHERE ea.[Clearance] = 4
                            AND ea.[IsDisabled] = CAST(0 AS bit)
                            AND NOT EXISTS (
                                SELECT 1 FROM [Candidates] AS ec WHERE ec.[Id] = ea.[Id]
                            )
                      )
                    ORDER BY
                        CASE WHEN c.[Username] = N'vault-admin' THEN 0 ELSE 1 END,
                        c.[CreatedAtUtc] ASC,
                        c.[Id] ASC
                )
                UPDATE u
                SET u.[AwaitingActivation] = CAST(1 AS bit),
                    u.[CredentialGeneration] = u.[CredentialGeneration] + 1,
                    u.[TotpSecret] = NULL,
                    u.[ConcurrencyStamp] = NEWID()
                FROM [Users] AS u
                WHERE EXISTS (SELECT 1 FROM [Candidates] AS c WHERE c.[Id] = u.[Id])
                  AND NOT EXISTS (SELECT 1 FROM [Preserved] AS p WHERE p.[Id] = u.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountInvitations");

            migrationBuilder.DropColumn(
                name: "AwaitingActivation",
                table: "Users");
        }
    }
}
