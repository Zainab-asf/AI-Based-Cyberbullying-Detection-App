using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KidSafe.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRollNumberAndProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RollNumber",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RollNumber", table: "Users");
            migrationBuilder.DropColumn(name: "Phone",      table: "Users");
        }
    }
}
