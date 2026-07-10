using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRatingsAndClinicAggregateRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AggregateRating",
                table: "clinics",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ratings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ratings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ratings_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_AppointmentId",
                table: "ratings",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ratings_ClinicId",
                table: "ratings",
                column: "ClinicId");

            migrationBuilder.Sql("ALTER TABLE ratings ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON ratings;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON ratings
                USING ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''))
                WITH CHECK ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON ratings;");
            migrationBuilder.Sql("ALTER TABLE ratings DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "ratings");

            migrationBuilder.DropColumn(
                name: "AggregateRating",
                table: "clinics");
        }
    }
}
