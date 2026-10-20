using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RotationBackend",
                table: "Secrets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RotationPrincipal",
                table: "Secrets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RotationBackend",
                table: "Secrets");

            migrationBuilder.DropColumn(
                name: "RotationPrincipal",
                table: "Secrets");
        }
    }
}
