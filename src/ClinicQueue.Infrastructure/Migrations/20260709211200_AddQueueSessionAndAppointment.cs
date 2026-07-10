using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueSessionAndAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "queue_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PauseReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PausedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    NormalSinceLastUrgent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_queue_sessions_clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    DependentId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PossibleUrgentFlag = table.Column<bool>(type: "boolean", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    TemporaryNumber = table.Column<int>(type: "integer", nullable: false),
                    RealNumber = table.Column<int>(type: "integer", nullable: true),
                    CompanionsCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsInstantEntry = table.Column<bool>(type: "boolean", nullable: false),
                    InstantEntryReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appointments_queue_sessions_QueueSessionId",
                        column: x => x.QueueSessionId,
                        principalTable: "queue_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_ClinicId_QueueSessionId_Status",
                table: "appointments",
                columns: new[] { "ClinicId", "QueueSessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_QueueSessionId_RealNumber",
                table: "appointments",
                columns: new[] { "QueueSessionId", "RealNumber" },
                unique: true,
                filter: "\"RealNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_QueueSessionId_TemporaryNumber",
                table: "appointments",
                columns: new[] { "QueueSessionId", "TemporaryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_queue_sessions_ClinicId_OperatingDate",
                table: "queue_sessions",
                columns: new[] { "ClinicId", "OperatingDate" },
                unique: true);

            // PostgreSQL RLS — clinic_id = current_setting('app.current_clinic') (constitution Principle I)
            EnableRls(migrationBuilder, "queue_sessions");
            EnableRls(migrationBuilder, "appointments");
        }

        private static void EnableRls(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"""
                CREATE POLICY tenant_isolation ON {table}
                USING ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''))
                WITH CHECK ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "appointments", "queue_sessions" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "queue_sessions");
        }
    }
}
