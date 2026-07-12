using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCompanionsCountFromAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanionsCount",
                table: "appointments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanionsCount",
                table: "appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
