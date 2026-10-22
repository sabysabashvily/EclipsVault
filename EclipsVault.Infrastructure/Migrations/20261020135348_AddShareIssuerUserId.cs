using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
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
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretGrants_IssuerUserId",
                table: "SecretGrants",
                column: "IssuerUserId");

            // A share's issuer is the account with its issuer's username that already existed when the
            // share was made. A later account given a deleted account's username is never the issuer.
            // Usernames compare by the column's collation, as sign-in compares them, so at most one
            // account matches. Every other share keeps no issuer.
            migrationBuilder.Sql("""
                UPDATE g
                SET IssuerUserId = u.Id
                FROM SecretGrants AS g
                JOIN Users AS u
                    ON u.Username = g.GrantedBy
                    AND u.CreatedAtUtc <= g.CreatedAtUtc;
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
