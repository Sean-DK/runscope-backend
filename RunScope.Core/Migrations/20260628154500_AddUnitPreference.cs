using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RunScope.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnitPreference",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitPreference",
                table: "Users");
        }
    }
}
