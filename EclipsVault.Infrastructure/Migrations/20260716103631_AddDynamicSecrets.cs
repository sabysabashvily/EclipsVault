using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DynamicSecretLeases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CredentialIdentity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RevocationError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicSecretLeases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DynamicSecretRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProjectKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    Sensitivity = table.Column<int>(type: "int", nullable: false),
                    Backend = table.Column<int>(type: "int", nullable: false),
                    CreationStatements = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RevocationStatements = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DefaultTtlMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxTtlMinutes = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicSecretRoles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicSecretLeases_CredentialIdentity",
                table: "DynamicSecretLeases",
                column: "CredentialIdentity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DynamicSecretLeases_Status_ExpiresAtUtc",
                table: "DynamicSecretLeases",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicSecretLeases_UserId",
                table: "DynamicSecretLeases",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DynamicSecretRoles_Name",
                table: "DynamicSecretRoles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DynamicSecretLeases");

            migrationBuilder.DropTable(
                name: "DynamicSecretRoles");
        }
    }
}
