using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddShareIssuerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IssuerUserId",
                table: "SecretGrants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretGrants_IssuerUserId",
                table: "SecretGrants",
                column: "IssuerUserId");

            // A share's issuer is the account with its issuer's username that already existed when the
            // share was made. A later account given a deleted account's username is never the issuer.
            // Usernames compare case-sensitively, as sign-in compares them here, where "Alice" and
            // "alice" can be two accounts; so at most one account matches. Every other share keeps no issuer.
            migrationBuilder.Sql("""
                UPDATE "SecretGrants" AS g
                SET "IssuerUserId" = u."Id"
                FROM "Users" AS u
                WHERE u."Username" = g."GrantedBy"
                    AND u."CreatedAtUtc" <= g."CreatedAtUtc";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SecretGrants_IssuerUserId",
                table: "SecretGrants");

            migrationBuilder.DropColumn(
                name: "IssuerUserId",
                table: "SecretGrants");
        }
    }
}
