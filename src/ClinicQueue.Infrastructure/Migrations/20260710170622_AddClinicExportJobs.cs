using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clinic_export_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinic_export_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clinic_export_jobs_ClinicId_CreatedAt",
                table: "clinic_export_jobs",
                columns: new[] { "ClinicId", "CreatedAt" });

            // PostgreSQL RLS — clinic_id = current_setting('app.current_clinic') (constitution Principle I)
            migrationBuilder.Sql("ALTER TABLE clinic_export_jobs ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON clinic_export_jobs
                USING ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''))
                WITH CHECK ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON clinic_export_jobs;");
            migrationBuilder.Sql("ALTER TABLE clinic_export_jobs DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "clinic_export_jobs");
        }
    }
}
