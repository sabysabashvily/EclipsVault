using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeEmailIdentity : Migration
    {
        private const string Refusal =
            "EclipsVault cannot upgrade: some email addresses would belong to more than one account. " +
            "Email addresses now compare ignoring Unicode letter case and surrounding white space, " +
            "and no username may be the email address of a different account. " +
            "No account was changed. Give all but one account in each group below a different email " +
            "address or username, then upgrade again.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse before changing anything: choosing which account keeps an address would hand one
            // person's sign-in to another. A username aliases another account's email when it is that
            // address as the username column compares it, or the same email identity. Diagnostics
            // name accounts by id only, because deployment logs need no email addresses.
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    collisions text;
                BEGIN
                    SELECT string_agg(accounts, '; ') INTO collisions FROM (
                        SELECT string_agg("Id"::text, ' and ' ORDER BY "Id") AS accounts
                        FROM "Users"
                        GROUP BY {EmailIdentitySql.NormalizePostgres("\"Email\"")}
                        HAVING count(*) > 1
                        UNION ALL
                        SELECT named."Id"::text || ' (username) and ' || addressed."Id"::text || ' (email)'
                        FROM "Users" AS named
                        JOIN "Users" AS addressed
                            ON named."Id" <> addressed."Id"
                            AND (named."Username" = addressed."Email"
                                OR {EmailIdentitySql.NormalizePostgres("named.\"Username\"")} = {EmailIdentitySql.NormalizePostgres("addressed.\"Email\"")})
                    ) AS shared;
                    IF collisions IS NOT NULL THEN
                        RAISE EXCEPTION '%', '{Refusal} Accounts by id: ' || collisions || '.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                collation: "C");

            migrationBuilder.Sql($"""
                UPDATE "Users" SET "NormalizedEmail" = {EmailIdentitySql.NormalizePostgres("\"Email\"")};
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }
    }
}
