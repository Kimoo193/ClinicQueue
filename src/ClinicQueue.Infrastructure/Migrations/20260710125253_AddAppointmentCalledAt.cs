using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentCalledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CalledAt",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalledAt",
                table: "appointments");
        }
    }
}
