using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClearanceCeiling",
                table: "ApiKeys",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MetadataOnly",
                table: "ApiKeys",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProjectScope",
                table: "ApiKeys",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClearanceCeiling",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "MetadataOnly",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "ProjectScope",
                table: "ApiKeys");
        }
    }
}
