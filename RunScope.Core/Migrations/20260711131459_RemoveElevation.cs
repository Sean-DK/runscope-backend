using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunScope.Core.Migrations
{
    /// <inheritdoc />
    public partial class RemoveElevation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElevationGainMeters",
                table: "Routes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ElevationGainMeters",
                table: "Routes",
                type: "float",
                nullable: true);
        }
    }
}
