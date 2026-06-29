using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunScope.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPrTimeSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrTimeSeconds",
                table: "Events",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrTimeSeconds",
                table: "Events");
        }
    }
}
