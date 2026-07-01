using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunScope.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddElevation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ElevationMeters",
                table: "RouteWaypoints",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ElevationGainMeters",
                table: "Routes",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElevationMeters",
                table: "RouteWaypoints");

            migrationBuilder.DropColumn(
                name: "ElevationGainMeters",
                table: "Routes");
        }
    }
}
