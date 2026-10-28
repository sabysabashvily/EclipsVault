using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialGeneration = table.Column<long>(type: "bigint", nullable: false),
                    RevocationVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CandidatePasswordHash = table.Column<byte[]>(type: "varbinary(64)", maxLength: 64, nullable: true),
                    CandidatePasswordSalt = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: true),
                    PasskeyNickname = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TargetPasskeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProofMethod = table.Column<int>(type: "int", nullable: true),
                    Challenge = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ChallengePurpose = table.Column<int>(type: "int", nullable: true),
                    ChallengeExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileOperations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileOperations_UserId_SessionId",
                table: "ProfileOperations",
                columns: new[] { "UserId", "SessionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileOperations");
        }
    }
}
