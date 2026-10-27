using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CredentialGeneration",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CredentialGeneration",
                table: "Users");
        }
    }
}
