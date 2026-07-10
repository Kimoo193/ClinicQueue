using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicQueue.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPushSubscriptionsAndActiveBookingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Auth = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_active_dependent",
                table: "appointments",
                columns: new[] { "ClinicId", "QueueSessionId", "DependentId" },
                unique: true,
                filter: "\"DependentId\" IS NOT NULL AND \"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_active_membership",
                table: "appointments",
                columns: new[] { "ClinicId", "QueueSessionId", "PatientMembershipId" },
                unique: true,
                filter: "\"PatientMembershipId\" IS NOT NULL AND \"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_ClinicId_UserId",
                table: "push_subscriptions",
                columns: new[] { "ClinicId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_Endpoint",
                table: "push_subscriptions",
                column: "Endpoint",
                unique: true);

            // PostgreSQL RLS — clinic_id = current_setting('app.current_clinic') (constitution Principle I)
            migrationBuilder.Sql("ALTER TABLE push_subscriptions ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY tenant_isolation ON push_subscriptions
                USING ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''))
                WITH CHECK ("ClinicId"::text = NULLIF(current_setting('app.current_clinic', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON push_subscriptions;");
            migrationBuilder.Sql("ALTER TABLE push_subscriptions DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_appointments_active_dependent",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_active_membership",
                table: "appointments");
        }
    }
}
