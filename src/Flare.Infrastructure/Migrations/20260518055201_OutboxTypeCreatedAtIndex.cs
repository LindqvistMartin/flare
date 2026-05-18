using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flare.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OutboxTypeCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Type_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "Type", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Type_CreatedAt",
                table: "OutboxMessages");
        }
    }
}
