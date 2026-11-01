using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EclipsVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClearPendingDenialReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A pending request's denial text came from a form field its requester could rewrite, and a
            // forged row cannot be told from a genuine one. Status 0 is Pending; decided requests keep
            // the text their reviewer saw.
            migrationBuilder.Sql("""
                UPDATE AccessRequests
                SET DeniedReasons = NULL
                WHERE Status = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The cleared text is gone; there is nothing to put back.
        }
    }
}
