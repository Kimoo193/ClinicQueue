using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositFieldsToAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DepositConfirmedAt",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositProofUrl",
                table: "appointments",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointments_QueueSessionId_Method_Status",
                table: "appointments",
                columns: new[] { "QueueSessionId", "Method", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_appointments_QueueSessionId_Method_Status",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositConfirmedAt",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "DepositProofUrl",
                table: "appointments");
        }
    }
}
