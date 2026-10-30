using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeEmailIdentity : Migration
    {
        private const string Refusal =
            "EclipsVault cannot upgrade: some email addresses would belong to more than one account. " +
            "Email addresses now compare ignoring the case of letters A to Z and surrounding white space, " +
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
            // THROW accepts a message of at most 2048 characters.
            migrationBuilder.Sql($"""
                DECLARE @collisions nvarchar(max) = (
                    SELECT STRING_AGG([Accounts], N'; ') FROM (
                        SELECT STRING_AGG(CONVERT(nvarchar(max), [Id]), N' and ') WITHIN GROUP (ORDER BY [Id]) AS [Accounts]
                        FROM [Users]
                        GROUP BY {EmailIdentitySql.NormalizeSqlServer("[Email]")}
                        HAVING COUNT(*) > 1
                        UNION ALL
                        SELECT CONVERT(nvarchar(36), [named].[Id]) + N' (username) and ' + CONVERT(nvarchar(36), [addressed].[Id]) + N' (email)'
                        FROM [Users] AS [named]
                        JOIN [Users] AS [addressed]
                            ON [named].[Id] <> [addressed].[Id]
                            AND ([named].[Username] = [addressed].[Email]
                                OR {EmailIdentitySql.NormalizeSqlServer("[named].[Username]")} = {EmailIdentitySql.NormalizeSqlServer("[addressed].[Email]")})
                    ) AS [Shared]);
                IF @collisions IS NOT NULL
                BEGIN
                    DECLARE @message nvarchar(2048) = LEFT(N'{Refusal} Accounts by id: ' + @collisions + N'.', 2048);
                    THROW 50000, @message, 1;
                END
                """);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.Sql($"""
                UPDATE [Users] SET [NormalizedEmail] = {EmailIdentitySql.NormalizeSqlServer("[Email]")};
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldCollation: "Latin1_General_100_BIN2");

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
